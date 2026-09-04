using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TeaTimeDeck.Game;

namespace TeaTimeDeck.Api;

/// <summary>
/// Loopback HTTP listener that upgrades requests to a WebSocket.
///
/// Binding to localhost is not on its own an authorisation boundary: every process on
/// the machine can reach the port, and so can a web page the user happens to have open.
/// The web page is the case that is actually defended: any request carrying an Origin
/// header is refused, and browsers always send one while native clients never do.
///
/// Local processes are not gated. A shared secret used to, but it was stored in this
/// plugin's own config file, so any process able to read a file could present it -
/// it cost the user a copy-paste per install and bought no protection it did not
/// already have.
/// </summary>
internal sealed class ApiServer : IDisposable
{
    private const int MaxSessions = 8;

    private readonly Configuration config;
    private readonly RequestRouter router;
    private readonly ConcurrentDictionary<Guid, ApiSession> sessions = new();

    private HttpListener? listener;
    private CancellationTokenSource? cts;
    private Task? acceptLoop;

    public ApiServer(Configuration config, CatalogRegistry catalogs, HotbarExecutor executor,
        GlamourerExecutor designs, IconService icons)
    {
        this.config = config;
        this.router = new RequestRouter(catalogs, executor, designs, icons);
    }

    /// <summary>
    /// Wired after construction: the status service needs the server to broadcast through,
    /// and the router needs the status service to answer requests.
    /// </summary>
    public void AttachStatus(StatusService status) => router.AttachStatus(status);

    public bool IsRunning => listener?.IsListening == true;

    public int SessionCount => sessions.Count;

    /// <summary>Last startup failure, for display in the config window. Null when healthy.</summary>
    public string? LastError { get; private set; }

    public void Start()
    {
        if (IsRunning)
            return;

        Stop();

        try
        {
            var prefix = $"http://localhost:{config.ApiPort}/";
            listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();

            cts = new CancellationTokenSource();
            acceptLoop = Task.Run(() => AcceptLoopAsync(cts.Token));

            LastError = null;
            Plugin.Log.Information("API listening on {Prefix}", prefix);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Plugin.Log.Error(ex, "Could not listen on port {Port}.", config.ApiPort);
            Stop();
        }
    }

    public void Stop()
    {
        cts?.Cancel();

        try
        {
            listener?.Stop();
            listener?.Close();
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Ignoring error while stopping listener.");
        }

        listener = null;
        sessions.Clear();

        cts?.Dispose();
        cts = null;
        acceptLoop = null;
    }

    public void Restart()
    {
        Stop();
        if (config.ServerEnabled)
            Start();
    }

    /// <summary>Pushes an unsolicited event to every connected client.</summary>
    public void Broadcast(Message message)
    {
        foreach (var session in sessions.Values)
            session.Send(message);
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener is { IsListening: true })
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException ex)
            {
                Plugin.Log.Debug(ex, "Listener stopped accepting.");
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => HandleContextAsync(context, token), token);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken token)
    {
        try
        {
            // A browser page can be induced to hit a loopback port, but it cannot suppress
            // its Origin header. Native clients do not send one, so this is a clean split.
            if (!string.IsNullOrEmpty(context.Request.Headers["Origin"]))
            {
                await RejectAsync(context, 403, "origin not allowed").ConfigureAwait(false);
                return;
            }

            if (context.Request.Url?.AbsolutePath is "/health" && !context.Request.IsWebSocketRequest)
            {
                // Deliberately says nothing about the character or the session.
                await WriteJsonAsync(context, 200, new
                {
                    server = "TeaTimeDeck",
                    protocolVersion = Protocol.Version,
                }).ConfigureAwait(false);
                return;
            }

            if (!context.Request.IsWebSocketRequest)
            {
                await RejectAsync(context, 426, "websocket upgrade required").ConfigureAwait(false);
                return;
            }

            if (sessions.Count >= MaxSessions)
            {
                await RejectAsync(context, 503, "too many sessions").ConfigureAwait(false);
                return;
            }

            await RunSessionAsync(context, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Unhandled error serving a request.");
        }
    }

    private async Task RunSessionAsync(HttpListenerContext context, CancellationToken token)
    {
        var wsContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
        var session = new ApiSession(wsContext.WebSocket, router);
        sessions[session.Id] = session;

        Plugin.Log.Information("Client connected ({Count} active).", sessions.Count);

        try
        {
            await session.RunAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Session ended with an error.");
        }
        finally
        {
            sessions.TryRemove(session.Id, out _);
            wsContext.WebSocket.Dispose();
            Plugin.Log.Information("Client disconnected ({Count} active).", sessions.Count);
        }
    }

    private static async Task RejectAsync(HttpListenerContext context, int status, string reason)
    {
        await WriteJsonAsync(context, status, new { error = reason }).ConfigureAwait(false);
    }

    private static async Task WriteJsonAsync(HttpListenerContext context, int status, object body)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(body, Protocol.Json);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Could not write response.");
        }
        finally
        {
            context.Response.Close();
        }
    }

    public void Dispose() => Stop();
}
