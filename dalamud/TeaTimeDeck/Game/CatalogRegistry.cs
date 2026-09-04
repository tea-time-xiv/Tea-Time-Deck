using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    /// Bumped by every invalidation. A build dispatched before the bump must not write its
    /// result back afterwards -- see <see cref="GetAsync"/>.
    /// </summary>
    private int generation;

    /// <summary>
    /// Raised when cached entries were dropped and clients should refetch. Carries the
    /// kinds affected, or null when every kind was.
    /// </summary>
    public event Action<IReadOnlyCollection<string>?>? Invalidated;

    public CatalogRegistry(Configuration config, GlamourerIpc glamourer)
    {
        providers = new ICatalogProvider[]
            {
                new EmoteCatalog(config), new MountCatalog(), new MinionCatalog(), new GearSetCatalog(),
                new ActionCatalog(), new GlamourerCatalog(glamourer),
            }
            .ToDictionary(p => p.Kind, StringComparer.OrdinalIgnoreCase);

        Plugin.UnlockState.Unlock += OnUnlock;
        Plugin.ClientState.Login += OnLogin;
    }

    public IEnumerable<object> DescribeKinds() =>
        providers.Values.Select(p => new
        {
            kind = p.Kind,
            displayName = p.DisplayName,
            addressing = Describe(p.Addressing),
        });

    /// <summary>
    /// Which field an <c>execute</c> of this kind has to carry. Asked by the router rather
    /// than decided there, so adding a key-addressed kind is still one provider and nothing else.
    /// </summary>
    public CatalogAddressing AddressingOf(string kind) =>
        providers.TryGetValue(kind, out var provider)
            ? provider.Addressing
            : throw new ArgumentException($"unknown catalog kind '{kind}'");

    private static string Describe(CatalogAddressing addressing) =>
        addressing == CatalogAddressing.Key ? "key" : "id";

    public async Task<IReadOnlyList<CatalogEntry>> GetAsync(string kind)
    {
        if (!providers.TryGetValue(kind, out var provider))
            throw new ArgumentException($"unknown catalog kind '{kind}'");

        if (cache.TryGetValue(provider.Kind, out var cached))
            return cached;

        var generationAtDispatch = Volatile.Read(ref generation);

        // Sheet and unlock reads are framework-thread only.
        var built = await Plugin.Framework.RunOnFrameworkThread(provider.Build).ConfigureAwait(false);

        // An unlock landing while the build was on the framework thread has already cleared
        // the cache; storing now would put the pre-unlock list back into it, and the notice
        // that follows would serve exactly that stale list to the client it just woke up.
        // The request still gets what it built -- only the caching of it is dropped.
        if (Volatile.Read(ref generation) == generationAtDispatch)
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
        // Before the removals, so a build already in flight sees the move whichever branch
        // this takes. Bumping for a scoped invalidation costs an unrelated kind one
        // uncached rebuild; a per-kind counter would buy that back and little else.
        Interlocked.Increment(ref generation);

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
