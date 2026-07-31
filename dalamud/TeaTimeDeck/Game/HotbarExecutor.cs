using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using HotbarSlotType = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlotType;

namespace TeaTimeDeck.Game;

/// <summary>
/// Fires a catalog entry the way the game itself does: fill a hotbar slot, execute it.
///
/// The module keeps a ScratchSlot for exactly this purpose, so nothing the player has
/// arranged on a real hotbar is written to, saved over, or restored.
///
/// Scope discipline, deliberately enforced here rather than left to the client:
/// one request executes one thing, only things the player already owns, only of the
/// kinds this plugin exposes. There is no queueing, no repeat and no scheduling.
/// </summary>
internal sealed class HotbarExecutor
{
    /// <summary>
    /// Floor on the gap between executions. A human pressing a key cannot beat this;
    /// a script trying to drive the game through the API can, and gets refused.
    /// </summary>
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Only kinds listed here can be executed. HotbarSlotType covers far more than this
    /// (raw Actions, Items, Macros); leaving them out keeps the API away from combat.
    /// </summary>
    private static readonly Dictionary<string, HotbarSlotType> SlotTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["emote"] = HotbarSlotType.Emote,
            ["mount"] = HotbarSlotType.Mount,
            ["minion"] = HotbarSlotType.Companion,
        };

    private readonly CatalogRegistry catalogs;
    private DateTime lastExecution = DateTime.MinValue;

    public HotbarExecutor(CatalogRegistry catalogs)
    {
        this.catalogs = catalogs;
    }

    public async Task<object> ExecuteAsync(string kind, uint id)
    {
        if (!SlotTypes.TryGetValue(kind, out var slotType))
            throw new ArgumentException($"kind '{kind}' cannot be executed");

        // Look the entry up rather than trusting the id. The catalog only contains things
        // the player has unlocked, so a client cannot ask for anything else.
        var entries = await catalogs.GetAsync(kind).ConfigureAwait(false);
        var entry = entries.FirstOrDefault(e => e.Id == id)
                    ?? throw new ArgumentException($"{kind} {id} is not in your catalog");

        return await Plugin.Framework
            .RunOnFrameworkThread(() => Execute(entry, slotType))
            .ConfigureAwait(false);
    }

    private unsafe object Execute(CatalogEntry entry, HotbarSlotType slotType)
    {
        if (!Plugin.PlayerState.IsLoaded)
            throw new InvalidOperationException("no character is logged in");

        var now = DateTime.UtcNow;
        if (now - lastExecution < MinimumInterval)
            throw new InvalidOperationException("executing too fast; one action per press");

        lastExecution = now;

        var module = RaptureHotbarModule.Instance();
        if (module is null)
            throw new InvalidOperationException("hotbar module is not available yet");

        var slot = &module->ScratchSlot;
        slot->Set(module->UIModule, slotType, entry.Id);

        // Not a gate: the game refuses and tells the player why far better than we could
        // (wrong zone, in combat, already mounted). Reported so clients can show state.
        var usable = slot->IsSlotUsable(slotType, entry.Id);
        var result = module->ExecuteSlot(slot);

        Plugin.Log.Debug("Executed {Kind} {Id} ({Name}): usable={Usable} result={Result}",
            entry.Kind, entry.Id, entry.Name, usable, result);

        return new
        {
            kind = entry.Kind,
            id = entry.Id,
            name = entry.Name,
            usable,
            result,
        };
    }

    public static IEnumerable<string> ExecutableKinds => SlotTypes.Keys;
}
