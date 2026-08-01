using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace TeaTimeDeck.Game;

/// <summary>
/// Gear sets. Unlike every other catalog here, these come from a live module rather than
/// an Excel sheet: they are the player's own arrangement, not a list of things that exist.
///
/// That means no <see cref="Dalamud.Plugin.Services.IUnlockState"/> event to ride on, so
/// changes are noticed by <see cref="CatalogWatcher"/> instead.
/// </summary>
internal sealed class GearSetCatalog : ICatalogProvider
{
    public string Kind => "gearset";

    public string DisplayName => "Gear Sets";

    public unsafe IReadOnlyList<CatalogEntry> Build()
    {
        var module = RaptureGearsetModule.Instance();
        if (module is null)
        {
            Plugin.Log.Warning("Gear set module unavailable.");
            return [];
        }

        var jobs = Plugin.DataManager.GetExcelSheet<ClassJob>();
        var gearsets = module->Entries;
        var entries = new List<CatalogEntry>();

        for (var id = 0; id < gearsets.Length; id++)
        {
            if (!module->IsValidGearset(id))
                continue;

            var gearset = module->GetGearset(id);
            if (gearset is null)
                continue;

            var name = gearset->NameString;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            // The module knows which icon the gear set list draws, including the job's own
            // artwork. Composing one from the job id would only be guessing at the same thing.
            var icon = module->GetClassJobIconForGearset(id);

            entries.Add(new CatalogEntry(
                Kind: Kind,
                // The loop variable throughout: it is the gearset id the module keys on,
                // which is what IsValidGearset, GetGearset and the icon lookup all take,
                // and what a hotbar slot equips by. It matches the entry's own Id field.
                Id: (uint)id,
                Name: name,
                IconId: icon > 0 ? (uint)icon : 0u,
                Category: jobs?.GetRowOrDefault(gearset->ClassJob) is { } job
                    ? GameText.TitleCase(job.Name.ExtractText())
                    : null,
                SortOrder: id,
                Command: null));
        }

        // Deliberately unsorted: gear set order is the player's own arrangement, and the
        // game's list shows them in id order. Grouping by job would undo that.
        return entries;
    }
}
