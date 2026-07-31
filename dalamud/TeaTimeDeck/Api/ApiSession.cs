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

    public Guid Id { get; } = Guid.NewGuid();

    public ApiSession(WebSocket socket, RequestRouter router)
    {
        this.socket = socket;
        this.router = router;
    }

    /// <summary>Queues a message. Returns false once the session is finished.</summary>
    public bool Send(Message message) => outbound.Writer.TryWrite(message.Serialize());

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
