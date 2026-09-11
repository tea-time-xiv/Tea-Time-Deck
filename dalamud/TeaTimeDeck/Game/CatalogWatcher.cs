using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using TeaTimeDeck.Api;

namespace TeaTimeDeck.Game;

/// <summary>
/// Change detection for the catalogs the <c>Unlock</c> event does not cover.
///
/// Most catalogs only change when the player unlocks something, and the game raises an
/// event for that. Three kinds do not fit: gear sets have no event because they are edited,
/// renamed and reordered at will, the action list belongs to the current job rather
/// than to the character, so it is replaced entirely by a job switch or a level, and
/// Glamourer designs belong to another plugin that may not even be loaded.
///
/// Polling is cheap enough to be uninteresting: two field reads, plus a hash over a
/// hundred fixed-size entries while a deck is connected to see the result.
/// </summary>
internal sealed class CatalogWatcher : IDisposable
{
    /// <summary>
    /// Gear sets are edited by hand, so a second of lag is imperceptible. The catalog's own
    /// debounce adds to this, which is fine: nobody is waiting on a rename to reach a key.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly ApiServer server;
    private readonly CatalogRegistry catalogs;
    private readonly GlamourerIpc glamourer;

    private DateTime lastPoll = DateTime.MinValue;
    private int lastGearSetHash;

    /// <summary>
    /// Whether <see cref="lastGearSetHash"/> describes anything. Without this the first
    /// poll would compare a real hash against zero and invalidate for no reason.
    /// </summary>
    private bool primed;

    private uint lastJobId;
    private short lastJobLevel;
    private bool jobPrimed;

    private int lastDesignHash;
    private bool designsPrimed;
    private bool glamourerWasAvailable;

    public CatalogWatcher(ApiServer server, CatalogRegistry catalogs, GlamourerIpc glamourer)
    {
        this.server = server;
        this.catalogs = catalogs;
        this.glamourer = glamourer;

        Plugin.Framework.Update += OnUpdate;
    }

    private void OnUpdate(IFramework framework)
    {
        var now = DateTime.UtcNow;
        if (now - lastPoll < PollInterval)
            return;

        lastPoll = now;

        // Nothing to read. Baselines are kept rather than dropped, so a change made while
        // logged out is still spotted on the first poll after logging back in.
        if (!Plugin.ClientState.IsLoggedIn)
            return;

        try
        {
            // Not behind the session check below. Two field reads cost nothing, and the
            // cached action lists have to be right the instant a deck reconnects -- a job
            // change while nothing was connected would otherwise serve the old job's
            // actions until the next poll noticed.
            CheckJob();

            // Nobody to tell. The baseline is kept, so a rename made while disconnected is
            // caught on the first poll after something connects.
            if (server.SessionCount > 0)
                CheckGearSets();

            CheckDesigns();
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Catalog poll failed.");
        }
    }

    /// <summary>
    /// Actions belong to the job, not the character, so switching replaces the list.
    /// Level matters too: an action arrives the moment it is learned.
    /// </summary>
    private void CheckJob()
    {
        var jobId = Plugin.PlayerState.ClassJob.RowId;
        var level = Plugin.PlayerState.Level;

        if (!jobPrimed)
        {
            lastJobId = jobId;
            lastJobLevel = level;
            jobPrimed = true;
            return;
        }

        if (jobId == lastJobId && level == lastJobLevel)
            return;

        lastJobId = jobId;
        lastJobLevel = level;

        Plugin.Log.Debug("Job is now {Job} at level {Level}; invalidating the action catalog.", jobId, level);
        catalogs.Invalidate("action");
    }

    private void CheckGearSets()
    {
        var hash = HashGearSets();

        if (!primed)
        {
            lastGearSetHash = hash;
            primed = true;
            return;
        }

        if (hash == lastGearSetHash)
            return;

        lastGearSetHash = hash;
        Plugin.Log.Debug("Gear sets changed; invalidating that catalog.");
        catalogs.Invalidate("gearset");
    }

    /// <summary>
    /// Glamourer designs, which change for two reasons rather than one: the design list
    /// itself is edited, and Glamourer can be loaded or unloaded under us. Availability is
    /// two field reads and is checked whether or not a client is connected, so the type
    /// appears and disappears with the plugin; reading the list costs an IPC call and a
    /// dictionary, so that waits for a client the way gear sets do.
    ///
    /// Both sit behind the logged-in check above, which costs nothing: logging in
    /// invalidates every catalog anyway, so anything missed at the title screen is
    /// already covered.
    /// </summary>
    private void CheckDesigns()
    {
        var available = glamourer.Available;

        if (available != glamourerWasAvailable)
        {
            glamourerWasAvailable = available;

            // Whatever the list looked like belonged to the Glamourer that just went away.
            designsPrimed = false;

            Plugin.Log.Debug("Glamourer is now {State}; invalidating the design catalog.",
                available ? "loaded" : "gone");
            catalogs.Invalidate(GlamourerCatalog.KindName);
            return;
        }

        if (!available || server.SessionCount == 0)
            return;

        var hash = HashDesigns();

        if (!designsPrimed)
        {
            lastDesignHash = hash;
            designsPrimed = true;
            return;
        }

        if (hash == lastDesignHash)
            return;

        lastDesignHash = hash;
        Plugin.Log.Debug("Glamourer designs changed; invalidating that catalog.");
        catalogs.Invalidate(GlamourerCatalog.KindName);
    }

    /// <summary>
    /// What the deck shows of a design: that it exists, its name, the folder it is filed
    /// under and the colour it is drawn in -- a recolour in Glamourer changes a key face
    /// here, so it has to count as a change. What the design actually puts on the character
    /// is Glamourer's business.
    /// </summary>
    private int HashDesigns()
    {
        var hash = new HashCode();

        foreach (var design in glamourer.List())
        {
            hash.Add(design.Id);
            hash.Add(design.Name);
            hash.Add(design.FullPath);
            hash.Add(design.Color);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Covers everything the catalog puts on a key: which gear sets exist, what they are
    /// called, and which job each one is. Item changes within a set are deliberately not
    /// hashed -- re-equipping a ring does not change what the deck shows.
    /// </summary>
    private static unsafe int HashGearSets()
    {
        var module = RaptureGearsetModule.Instance();
        if (module is null)
            return 0;

        var hash = new HashCode();
        var gearsets = module->Entries;

        for (var id = 0; id < gearsets.Length; id++)
        {
            if (!module->IsValidGearset(id))
                continue;

            ref var gearset = ref gearsets[id];

            hash.Add(id);
            hash.Add(gearset.Id);
            hash.Add(gearset.ClassJob);

            // The raw name bytes, so a poll a second does not allocate a string per set.
            foreach (var b in gearset.Name)
                hash.Add(b);
        }

        return hash.ToHashCode();
    }

    public void Dispose() => Plugin.Framework.Update -= OnUpdate;
}
