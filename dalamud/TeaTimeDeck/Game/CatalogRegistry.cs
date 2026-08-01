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

    /// <summary>
    /// Kinds accumulated during the current burst window. Guarded because cached lists are
    /// read from socket threads, so an invalidation can arrive from one too.
    /// </summary>
    private readonly HashSet<string> pendingKinds = new(StringComparer.OrdinalIgnoreCase);
    private readonly object noticeLock = new();

    private bool pendingEveryKind;
    private bool noticeScheduled;
    private bool disposed;

    /// <summary>
    /// Raised when cached entries were dropped and clients should refetch. Carries the
    /// kinds affected, or null when every kind was.
    /// </summary>
    public event Action<IReadOnlyCollection<string>?>? Invalidated;

    public CatalogRegistry(Configuration config)
    {
        providers = new ICatalogProvider[]
            {
                new EmoteCatalog(config), new MountCatalog(), new MinionCatalog(), new GearSetCatalog(),
                new ActionCatalog(),
            }
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

    /// <summary>
    /// Drops cached entries and tells clients to refetch. Naming no kinds means every kind.
    ///
    /// Scoping this matters for the kinds that change often: a job change rebuilds that
    /// job's actions, and throwing away the emote list at the same time would mean a full
    /// sheet scan for nothing.
    /// </summary>
    public void Invalidate(params string[] kinds)
    {
        if (kinds.Length == 0)
        {
            // Notify even when nothing was cached here: clients keep their own copies, and an
            // empty server cache says nothing about what a Stream Deck is currently showing.
            cache.Clear();
            NotifyInvalidated(null);
            return;
        }

        // Resolve through the providers so what goes on the wire is the canonical spelling.
        // The client matches these against its own cache keys, which are exact.
        var resolved = new List<string>(kinds.Length);

        foreach (var kind in kinds)
        {
            if (!providers.TryGetValue(kind, out var provider))
            {
                Plugin.Log.Warning("Ignoring invalidation of unknown catalog kind '{Kind}'.", kind);
                continue;
            }

            cache.TryRemove(provider.Kind, out _);
            resolved.Add(provider.Kind);
        }

        if (resolved.Count > 0)
            NotifyInvalidated(resolved);
    }

    private void OnLogin()
    {
        // Unlock state belongs to the character, not the client.
        Invalidate();
    }

    private void OnUnlock(RowRef rowRef)
    {
        // The event does not say which catalog a row belongs to, and resolving that is
        // more work than rebuilding two lists. Drop everything and let clients refetch.
        Invalidate();
    }

    /// <summary>
    /// Trailing-edge debounce: a burst produces exactly one notice, fired after the burst
    /// ends. A leading-edge throttle would swallow the final unlock and leave clients stale.
    ///
    /// Kinds accumulate across the burst, and one unscoped invalidation swallows the rest --
    /// a login landing on top of a job change already means "refetch everything".
    /// </summary>
    private void NotifyInvalidated(IReadOnlyCollection<string>? kinds)
    {
        lock (noticeLock)
        {
            if (kinds is null)
                pendingEveryKind = true;
            else if (!pendingEveryKind)
                pendingKinds.UnionWith(kinds);

            if (noticeScheduled || disposed)
                return;

            noticeScheduled = true;
        }

        _ = Plugin.Framework.RunOnTick(
            () =>
            {
                IReadOnlyCollection<string>? affected;

                lock (noticeLock)
                {
                    noticeScheduled = false;
                    affected = pendingEveryKind ? null : pendingKinds.ToArray();
                    pendingEveryKind = false;
                    pendingKinds.Clear();
                }

                if (!disposed)
                    Invalidated?.Invoke(affected);
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
