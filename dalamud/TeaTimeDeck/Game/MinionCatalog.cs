using System;
using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace TeaTimeDeck.Game;

/// <summary>
/// Minions. The sheet is called Companion, but nothing in the game's UI calls them
/// companions, so the wire name follows the player-facing term.
/// </summary>
internal sealed class MinionCatalog : ICatalogProvider
{
    public string Kind => "minion";

    public string DisplayName => "Minions";

    public IReadOnlyList<CatalogEntry> Build()
    {
        var sheet = Plugin.DataManager.GetExcelSheet<Companion>();
        if (sheet is null)
        {
            Plugin.Log.Warning("Companion sheet unavailable.");
            return [];
        }

        var entries = new List<CatalogEntry>();

        foreach (var companion in sheet)
        {
            if (companion.RowId == 0)
                continue;

            var name = companion.Singular.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!Plugin.UnlockState.IsCompanionUnlocked(companion))
                continue;

            entries.Add(new CatalogEntry(
                Kind: Kind,
                Id: companion.RowId,
                Name: GameText.TitleCase(name),
                IconId: companion.Icon,
                // The game groups minions by race in the Minion Guide; match that.
                Category: companion.MinionRace.ValueNullable?.Name.ExtractText(),
                SortOrder: companion.Order,
                Command: null));
        }

        entries.Sort(static (a, b) =>
        {
            var byCategory = string.CompareOrdinal(a.Category ?? string.Empty, b.Category ?? string.Empty);
            if (byCategory != 0)
                return byCategory;

            var byOrder = a.SortOrder.CompareTo(b.SortOrder);
            return byOrder != 0 ? byOrder : string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
        });

        return entries;
    }
}
