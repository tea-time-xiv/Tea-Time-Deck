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
///
/// The interval floor lives in <see cref="ExecutionGate"/> rather than here, so that
/// executing something Glamourer owns cannot sidestep the one this holds.
/// </summary>
internal sealed class HotbarExecutor
{
    /// <summary>
    /// Only kinds listed here can be executed. HotbarSlotType still covers more than this --
    /// items, macros and the rest stay unreachable -- but job and role actions are exposed
    /// deliberately, so this API does reach combat.
    ///
    /// What keeps that honest is above, not here: one request performs one action, there is
    /// no queueing, repeat or scheduling, and the interval floor holds regardless of kind.
    /// A key press fires one action, the same as pressing the hotbar would.
    ///
    /// Every kind stores its catalog id in the slot unchanged. Gear sets look like they
    /// ought to be the exception, since the gear set list numbers them from one on screen,
    /// but the slot wants the module's own id -- adding one equips the next gear set along.
    /// </summary>
    private static readonly Dictionary<string, HotbarSlotType> SlotTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["emote"] = HotbarSlotType.Emote,
            ["mount"] = HotbarSlotType.Mount,
            ["minion"] = HotbarSlotType.Companion,
            ["gearset"] = HotbarSlotType.GearSet,
            ["action"] = HotbarSlotType.Action,
        };

    private readonly CatalogRegistry catalogs;
    private readonly ExecutionGate gate;

    public HotbarExecutor(CatalogRegistry catalogs, ExecutionGate gate)
    {
        this.catalogs = catalogs;
        this.gate = gate;
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
        gate.Claim();

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
