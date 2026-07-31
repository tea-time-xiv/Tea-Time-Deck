using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeaTimeDeck.Api;

/// <summary>Wire protocol version. Bumped when a change breaks older clients.</summary>
public static class Protocol
{
    public const int Version = 1;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>A request from the Stream Deck plugin.</summary>
public sealed class Envelope
{
    /// <summary>Correlation id. Echoed back on the response. Null for fire-and-forget.</summary>
    public string? Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public JsonElement? Payload { get; set; }
}

/// <summary>A response or a server-pushed event.</summary>
public sealed class Message
{
    public string? Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public bool Ok { get; set; } = true;

    public object? Payload { get; set; }

    public string? Error { get; set; }

    public static Message Reply(string? id, string type, object? payload) =>
        new() { Id = id, Type = type, Ok = true, Payload = payload };

    public static Message Failure(string? id, string error) =>
        new() { Id = id, Type = "error", Ok = false, Error = error };

    public static Message Event(string type, object? payload) =>
        new() { Type = type, Ok = true, Payload = payload };

    public string Serialize() => JsonSerializer.Serialize(this, Protocol.Json);
}
