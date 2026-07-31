using System;
using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace TeaTimeDeck.Game;

internal sealed class MountCatalog : ICatalogProvider
{
    public string Kind => "mount";

    public string DisplayName => "Mounts";

    public IReadOnlyList<CatalogEntry> Build()
    {
        var sheet = Plugin.DataManager.GetExcelSheet<Mount>();
        if (sheet is null)
        {
            Plugin.Log.Warning("Mount sheet unavailable.");
            return [];
        }

        var entries = new List<CatalogEntry>();

        foreach (var mount in sheet)
        {
            if (mount.RowId == 0)
                continue;

            var name = mount.Singular.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!Plugin.UnlockState.IsMountUnlocked(mount))
                continue;

            entries.Add(new CatalogEntry(
                Kind: Kind,
                Id: mount.RowId,
                Name: GameText.TitleCase(name),
                IconId: mount.Icon,
                Category: SeatingLabel(mount.ExtraSeats),
                SortOrder: mount.UIPriority,
                Command: null));
        }

        entries.Sort(static (a, b) =>
        {
            var byOrder = a.SortOrder.CompareTo(b.SortOrder);
            return byOrder != 0 ? byOrder : string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
        });

        return entries;
    }

    private static string SeatingLabel(byte extraSeats) => extraSeats switch
    {
        0 => "Single rider",
        _ => $"{extraSeats + 1} riders",
    };
}
