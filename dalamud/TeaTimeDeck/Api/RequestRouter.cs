using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using TeaTimeDeck.Game;

namespace TeaTimeDeck.Api;

/// <summary>
/// Maps a request type to a handler. Handlers that touch game state must hop to the
/// framework thread themselves; the socket read loop runs on a thread pool thread.
/// </summary>
internal sealed class RequestRouter
{
    private static readonly string PluginVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

    private readonly Dictionary<string, Func<JsonElement?, Task<object?>>> handlers;
    private readonly CatalogRegistry catalogs;
    private readonly HotbarExecutor executor;
    private readonly IconService icons;

    public RequestRouter(CatalogRegistry catalogs, HotbarExecutor executor, IconService icons)
    {
        this.catalogs = catalogs;
        this.executor = executor;
        this.icons = icons;

        handlers = new Dictionary<string, Func<JsonElement?, Task<object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ping"] = HandlePing,
            ["hello"] = HandleHello,
            ["catalog.kinds"] = HandleCatalogKinds,
            ["catalog.list"] = HandleCatalogList,
            ["execute"] = HandleExecute,
            ["icon.get"] = HandleIconGet,
            ["icon.getMany"] = HandleIconGetMany,
            ["status.get"] = HandleStatusGet,
            ["status.cooldownSources"] = HandleCooldownSources,
        };
    }

    private StatusService? status;

    public void AttachStatus(StatusService service) => status = service;

    private Task<object?> HandleStatusGet(JsonElement? _) =>
        Task.FromResult<object?>(status?.Current);

    private static Task<object?> HandleCooldownSources(JsonElement? _) =>
        Plugin.Framework.RunOnFrameworkThread(
            () => (object?)new { sources = StatusService.DescribeCooldownSources() });

    public async Task<Message> DispatchAsync(Envelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.Type))
            return Message.Failure(envelope.Id, "missing request type");

        if (!handlers.TryGetValue(envelope.Type, out var handler))
            return Message.Failure(envelope.Id, $"unknown request type '{envelope.Type}'");

        try
        {
            var payload = await handler(envelope.Payload).ConfigureAwait(false);
            return Message.Reply(envelope.Id, envelope.Type, payload);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Handler for '{Type}' threw.", envelope.Type);
            return Message.Failure(envelope.Id, ex.Message);
        }
    }

    private static Task<object?> HandlePing(JsonElement? _) =>
        Task.FromResult<object?>(new
        {
            pong = true,
            serverTime = DateTimeOffset.UtcNow,
        });

    /// <summary>Handshake payload: what the client needs to decide whether it can talk to us.</summary>
    private static async Task<object?> HandleHello(JsonElement? _)
    {
        // Player state is only safe to touch on the framework thread.
        var (loggedIn, characterName) = await Plugin.Framework.RunOnFrameworkThread(() =>
        {
            var loaded = Plugin.PlayerState.IsLoaded;
            return (loaded, loaded ? Plugin.PlayerState.CharacterName : null);
        }).ConfigureAwait(false);

        return new
        {
            server = "TeaTimeDeck",
            pluginVersion = PluginVersion,
            protocolVersion = Protocol.Version,
            language = Plugin.ClientState.ClientLanguage.ToString(),
            loggedIn,
            characterName,
        };
    }

    private Task<object?> HandleCatalogKinds(JsonElement? _) =>
        Task.FromResult<object?>(new { kinds = catalogs.DescribeKinds() });

    private async Task<object?> HandleCatalogList(JsonElement? payload)
    {
        var kind = RequireString(payload, "kind");
        var entries = await catalogs.GetAsync(kind).ConfigureAwait(false);

        return new
        {
            kind,
            count = entries.Count,
            entries,
        };
    }

    private async Task<object?> HandleExecute(JsonElement? payload)
    {
        var kind = RequireString(payload, "kind");
        var id = RequireUInt32(payload, "id");

        return await executor.ExecuteAsync(kind, id).ConfigureAwait(false);
    }

    private async Task<object?> HandleIconGet(JsonElement? payload)
    {
        var iconId = RequireUInt32(payload, "iconId");

        return new
        {
            iconId,
            mimeType = "image/png",
            data = await icons.GetPngAsync(iconId).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// Batched because a browser page needs a whole deck's worth of icons at once, and a
    /// request per key would mean dozens of round trips before the page finished drawing.
    /// </summary>
    private async Task<object?> HandleIconGetMany(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } obj
            || !obj.TryGetProperty("iconIds", out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("payload needs an array 'iconIds'");
        }

        var iconIds = new List<uint>();
        foreach (var element in array.EnumerateArray())
        {
            if (!element.TryGetUInt32(out var iconId))
                throw new ArgumentException("'iconIds' must contain unsigned integers");

            iconIds.Add(iconId);
        }

        if (iconIds.Count > IconService.MaxBatchSize)
            throw new ArgumentException($"at most {IconService.MaxBatchSize} icons per request");

        var results = new List<object>(iconIds.Count);
        foreach (var iconId in iconIds.Distinct())
        {
            try
            {
                results.Add(new
                {
                    iconId,
                    data = await icons.GetPngAsync(iconId).ConfigureAwait(false),
                });
            }
            catch (Exception ex)
            {
                // One bad id must not cost the client the whole page.
                Plugin.Log.Debug(ex, "Skipping icon {IconId}.", iconId);
            }
        }

        return new { mimeType = "image/png", icons = results };
    }

    private static uint RequireUInt32(JsonElement? payload, string name)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } obj)
            throw new ArgumentException($"request needs a payload object with '{name}'");

        if (!obj.TryGetProperty(name, out var value) || !value.TryGetUInt32(out var number))
            throw new ArgumentException($"payload needs an unsigned integer '{name}'");

        return number;
    }

    private static string RequireString(JsonElement? payload, string name)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } obj)
            throw new ArgumentException($"request needs a payload object with '{name}'");

        if (!obj.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new ArgumentException($"payload needs a string '{name}'");

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException($"payload '{name}' must not be empty");

        return text;
    }
}
