using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TeaTimeDeck.Api;

/// <summary>One connected Stream Deck client.</summary>
internal sealed class ApiSession
{
    /// <summary>A request larger than this is a client bug or an attack; drop the connection.</summary>
    private const int MaxMessageBytes = 512 * 1024;

    private const int ReceiveChunkBytes = 16 * 1024;

    private readonly WebSocket socket;
    private readonly RequestRouter router;

    /// <summary>
    /// Serialises writes: <see cref="WebSocket.SendAsync"/> may not be called concurrently.
    /// Bounded and drop-oldest so a wedged client can never apply backpressure to the game.
    /// </summary>
    private readonly Channel<string> outbound = Channel.CreateBounded<string>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });

    /// <summary>
    /// Completes when the session has finished unwinding, so a shutdown can wait for the
    /// close handshake instead of cancelling out from under it.
    /// </summary>
    private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private volatile bool closeRequested;

    public Guid Id { get; } = Guid.NewGuid();

    public Task Completion => finished.Task;

    public ApiSession(WebSocket socket, RequestRouter router)
    {
        this.socket = socket;
        this.router = router;
    }

    /// <summary>Queues a message. Returns false once the session is finished.</summary>
    public bool Send(Message message) => outbound.Writer.TryWrite(message.Serialize());

    /// <summary>
    /// Asks for a clean shutdown: the write loop drains what is queued, sends a close frame
    /// and stops, and the read loop unwinds on the client's reply. Waiting on
    /// <see cref="Completion"/> afterwards is what makes a deliberate stop look deliberate --
    /// cancelling instead leaves the client with a 1006 abnormal closure.
    ///
    /// The frame goes through the write loop rather than straight to the socket because
    /// a send may be in flight, and two concurrent sends on one WebSocket are not allowed.
    /// </summary>
    public void RequestClose()
    {
        closeRequested = true;
        outbound.Writer.TryComplete();
    }

    public async Task RunAsync(CancellationToken outerToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);

        var writer = WriteLoopAsync(cts.Token);
        try
        {
            await ReadLoopAsync(cts.Token).ConfigureAwait(false);
        }
        finally
        {
            outbound.Writer.TryComplete();
            cts.Cancel();
            try
            {
                await writer.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }

            finished.TrySetResult();
        }
    }

    private async Task ReadLoopAsync(CancellationToken token)
    {
        var buffer = new byte[ReceiveChunkBytes];
        using var assembled = new MemoryStream();

        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            assembled.SetLength(0);
            WebSocketReceiveResult result;

            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", token).ConfigureAwait(false);
                    return;
                }

                if (assembled.Length + result.Count > MaxMessageBytes)
                {
                    await CloseAsync(WebSocketCloseStatus.MessageTooBig, "message too large", token)
                        .ConfigureAwait(false);
                    return;
                }

                assembled.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
                continue;

            var text = Encoding.UTF8.GetString(assembled.GetBuffer(), 0, (int)assembled.Length);
            await HandleTextAsync(text).ConfigureAwait(false);
        }
    }

    private async Task HandleTextAsync(string text)
    {
        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(text, Protocol.Json);
        }
        catch (JsonException ex)
        {
            Send(Message.Failure(null, $"malformed json: {ex.Message}"));
            return;
        }

        if (envelope is null)
        {
            Send(Message.Failure(null, "empty request"));
            return;
        }

        // Requests are handled one at a time so responses keep client-visible ordering.
        var response = await router.DispatchAsync(envelope).ConfigureAwait(false);
        Send(response);
    }

    private async Task WriteLoopAsync(CancellationToken token)
    {
        await foreach (var text in outbound.Reader.ReadAllAsync(token).ConfigureAwait(false))
        {
            if (socket.State != WebSocketState.Open)
                return;

            var bytes = Encoding.UTF8.GetBytes(text);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, token).ConfigureAwait(false);
        }

        // Reached the end of the queue after RequestClose rather than by cancellation.
        // CloseOutputAsync and not CloseAsync: the read loop owns the receive side and is
        // sitting in ReceiveAsync right now, where it will see the client's close reply.
        if (closeRequested && socket.State == WebSocketState.Open)
        {
            try
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "server stopping", token)
                    .ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                // Client vanished first. The read loop will end on its own.
            }
        }
    }

    private async Task CloseAsync(WebSocketCloseStatus status, string reason, CancellationToken token)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await socket.CloseAsync(status, reason, token).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                // Client vanished mid-close. Nothing useful to do.
            }
        }
    }
}
