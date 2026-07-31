using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;

namespace TeaTimeDeck.Game;

/// <summary>
/// Turns a game icon id into a PNG the Stream Deck can draw.
///
/// Icons are held in memory rather than written to disk. There are only a few hundred of
/// them at a few KB each, and a game patch can change the artwork behind an id -- a disk
/// cache would need invalidating on patch day, memory does not.
/// </summary>
internal sealed class IconService
{
    /// <summary>
    /// Cap on a single batch request. The largest deck is 32 keys, so this covers a full
    /// page with room to spare while stopping a client asking for the entire sheet at once.
    /// </summary>
    public const int MaxBatchSize = 64;

    private readonly ConcurrentDictionary<uint, string> cache = new();
    private readonly Lazy<Guid> pngContainer = new(FindPngContainer);

    /// <summary>Returns the icon as base64-encoded PNG, without a data URI prefix.</summary>
    public async Task<string> GetPngAsync(uint iconId)
    {
        if (cache.TryGetValue(iconId, out var cached))
            return cached;

        var lookup = new GameIconLookup(iconId);
        if (!Plugin.TextureProvider.TryGetFromGameIcon(lookup, out var shared) || shared is null)
            throw new ArgumentException($"no icon with id {iconId}");

        using var wrap = await shared.RentAsync().ConfigureAwait(false);
        using var stream = new MemoryStream();

        await Plugin.TextureReadback
            .SaveToStreamAsync(wrap, pngContainer.Value, stream, leaveWrapOpen: true, leaveStreamOpen: true)
            .ConfigureAwait(false);

        var encoded = Convert.ToBase64String(stream.GetBuffer(), 0, (int)stream.Length);

        cache[iconId] = encoded;
        return encoded;
    }

    public void Clear() => cache.Clear();

    private static Guid FindPngContainer()
    {
        var png = Plugin.TextureReadback
            .GetSupportedImageEncoderInfos()
            .FirstOrDefault(codec => codec.MimeTypes.Contains("image/png", StringComparer.OrdinalIgnoreCase));

        if (png is null)
            throw new InvalidOperationException("this system has no PNG encoder registered");

        return png.ContainerGuid;
    }
}
