using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumina.Excel;

namespace TeaTimeDeck.Game;

/// <summary>
/// Owns the catalog providers and caches what they build. Entries only change when the
/// player unlocks something, so building on every request would be wasted work.
/// </summary>
internal sealed class CatalogRegistry : IDisposable
{
    /// <summary>
    /// Logging in replays every unlock as an event. Without this, one login means
    /// hundreds of broadcasts telling clients to refetch the same list.
    /// </summary>
    private static readonly TimeSpan InvalidateBurstWindow = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, ICatalogProvider> providers;
    private readonly ConcurrentDictionary<string, IReadOnlyList<CatalogEntry>> cache = new();

    private bool noticeScheduled;
    private bool disposed;

    /// <summary>Raised when cached entries were dropped and clients should refetch.</summary>
    public event Action? Invalidated;

    public CatalogRegistry(Configuration config)
    {
        providers = new ICatalogProvider[] { new EmoteCatalog(config), new MountCatalog(), new MinionCatalog() }
            .ToDictionary(p => p.Kind, StringComparer.OrdinalIgnoreCase);

        Plugin.UnlockState.Unlock += OnUnlock;
        Plugin.ClientState.Login += OnLogin;
    }

    public IEnumerable<object> DescribeKinds() =>
        providers.Values.Select(p => new { kind = p.Kind, displayName = p.DisplayName });

    public async Task<IReadOnlyList<CatalogEntry>> GetAsync(string kind)
    {
        if (!providers.TryGetValue(kind, out var provider))
            throw new ArgumentException($"unknown catalog kind '{kind}'");

        if (cache.TryGetValue(provider.Kind, out var cached))
            return cached;

        // Sheet and unlock reads are framework-thread only.
        var built = await Plugin.Framework.RunOnFrameworkThread(provider.Build).ConfigureAwait(false);
        cache[provider.Kind] = built;

        Plugin.Log.Debug("Built {Kind} catalog: {Count} entries.", provider.Kind, built.Count);
        return built;
    }

    public void Invalidate()
    {
        // Notify even when nothing was cached here: clients keep their own copies, and an
        // empty server cache says nothing about what a Stream Deck is currently showing.
        cache.Clear();
        NotifyInvalidated();
    }

    private void OnLogin()
    {
        // Unlock state belongs to the character, not the client.
        cache.Clear();
        NotifyInvalidated();
    }

    private void OnUnlock(RowRef rowRef)
    {
        // The event does not say which catalog a row belongs to, and resolving that is
        // more work than rebuilding two lists. Drop everything and let clients refetch.
        cache.Clear();
        NotifyInvalidated();
    }

    /// <summary>
    /// Trailing-edge debounce: a burst produces exactly one notice, fired after the burst
    /// ends. A leading-edge throttle would swallow the final unlock and leave clients stale.
    /// </summary>
    private void NotifyInvalidated()
    {
        if (noticeScheduled || disposed)
            return;

        noticeScheduled = true;

        _ = Plugin.Framework.RunOnTick(
            () =>
            {
                noticeScheduled = false;
                if (!disposed)
                    Invalidated?.Invoke();
            },
            delay: InvalidateBurstWindow);
    }

    public void Dispose()
    {
        disposed = true;
        Plugin.UnlockState.Unlock -= OnUnlock;
        Plugin.ClientState.Login -= OnLogin;
        Invalidated = null;
    }
}
