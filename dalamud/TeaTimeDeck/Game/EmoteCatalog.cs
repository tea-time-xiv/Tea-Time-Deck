using System;
using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace TeaTimeDeck.Game;

internal sealed class EmoteCatalog : ICatalogProvider
{
    private readonly Configuration config;

    public EmoteCatalog(Configuration config)
    {
        this.config = config;
    }

    public string Kind => "emote";

    public string DisplayName => "Emotes";

    public IReadOnlyList<CatalogEntry> Build()
    {
        var sheet = Plugin.DataManager.GetExcelSheet<Emote>();
        if (sheet is null)
        {
            Plugin.Log.Warning("Emote sheet unavailable.");
            return [];
        }

        var entries = new List<CatalogEntry>();

        foreach (var emote in sheet)
        {
            if (emote.RowId == 0)
                continue;

            var name = emote.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            // No text command means the row is not a player-usable emote.
            var command = emote.TextCommand.ValueNullable?.Command.ExtractText();
            if (string.IsNullOrWhiteSpace(command))
                continue;

            if (!IsAvailable(emote))
                continue;

            var category = emote.EmoteCategory.ValueNullable;
            if (category is not null && config.HiddenEmoteCategories.Contains(category.Value.RowId))
                continue;

            entries.Add(new CatalogEntry(
                Kind: Kind,
                Id: emote.RowId,
                Name: name,
                IconId: emote.Icon,
                Category: category?.Name.ExtractText(),
                SortOrder: emote.Order,
                Command: command));
        }

        // Match the in-game emote list: grouped by category, then the game's own order.
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

    /// <summary>
    /// Emotes every character starts with (/wave, /bow, ...) carry no unlock link, and the
    /// game's unlock check reports those as locked because there is no link to look up.
    /// Only emotes that actually have a link get asked about.
    /// </summary>
    private static bool IsAvailable(Emote emote) =>
        emote.UnlockLink == 0 || Plugin.UnlockState.IsEmoteUnlocked(emote);
}
