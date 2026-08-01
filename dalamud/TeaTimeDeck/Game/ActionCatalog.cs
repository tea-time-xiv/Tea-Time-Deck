using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Lumina.Excel.Sheets;
using GameAction = Lumina.Excel.Sheets.Action;

namespace TeaTimeDeck.Game;

/// <summary>
/// Everything the character can currently press: the job's own actions and the role
/// actions shared by everything of that role.
///
/// One catalog rather than two. Role actions are a handful of entries that a player thinks
/// of as part of the same list, and every kind added here costs another press on the
/// browser's Switch Type key. They stay tellable apart by category instead.
///
/// Alone among the catalogs here, this one describes the current job rather than the
/// character. Switching job replaces it entirely, which is why <see cref="CatalogWatcher"/>
/// watches for that.
/// </summary>
internal sealed class ActionCatalog : ICatalogProvider
{
    /// <summary>Category given to role actions, which the game groups under this name too.</summary>
    private const string RoleCategory = "Role Actions";

    /// <summary>
    /// The sheet stores job membership as one boolean column per job abbreviation, so the
    /// column to read is named after the job. Looked up rather than switched over: a switch
    /// would be forty arms long and would need another one every time a job is added.
    /// </summary>
    private static readonly ConcurrentDictionary<string, PropertyInfo?> Columns = new(StringComparer.Ordinal);

    public string Kind => "action";

    public string DisplayName => "Actions";

    public IReadOnlyList<CatalogEntry> Build()
    {
        var sheet = Plugin.DataManager.GetExcelSheet<GameAction>();
        if (sheet is null)
        {
            Plugin.Log.Warning("Action sheet unavailable.");
            return [];
        }

        var job = Plugin.PlayerState.ClassJob.ValueNullable;
        if (job is null)
            return [];

        var abbreviation = job.Value.Abbreviation.ExtractText();

        // A Paladin still has the actions it learned as a Gladiator, and a few of those sit
        // in a category naming only the base class.
        var baseClass = job.Value.ClassJobParent.ValueNullable?.Abbreviation.ExtractText();

        // Resolved once, not per row: with a few thousand actions to walk, a lookup each
        // time would be the most expensive thing in this loop.
        var column = ColumnFor(abbreviation);
        var baseColumn = baseClass is null ? null : ColumnFor(baseClass);

        if (column is null)
        {
            Plugin.Log.Warning("No ClassJobCategory column for job '{Job}'; its actions cannot be listed.",
                abbreviation);
            return [];
        }

        // The real level, not the synced one: a sync caps what you may use, not what you
        // have learned, and the game keeps showing synced-away actions greyed rather than
        // removing them. Whether one can fire right now is reported by `usable` instead.
        var level = Plugin.PlayerState.Level;

        // A few hundred categories are shared across a few thousand actions, so resolving
        // each one once is the difference between hundreds of lookups and thousands.
        var categoryMatches = new Dictionary<uint, bool>();
        var entries = new List<CatalogEntry>();

        foreach (var action in sheet)
        {
            if (action.RowId == 0 || !action.IsPlayerAction || action.IsPvP)
                continue;

            // Level 0 is traits and system rows rather than anything pressable.
            if (action.ClassJobLevel == 0 || action.ClassJobLevel > level)
                continue;

            var name = action.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var category = action.ClassJobCategory.ValueNullable;
            if (category is null)
                continue;

            if (!categoryMatches.TryGetValue(category.Value.RowId, out var belongs))
            {
                belongs = column.GetValue(category.Value) is true ||
                          baseColumn?.GetValue(category.Value) is true;
                categoryMatches[category.Value.RowId] = belongs;
            }

            if (!belongs)
                continue;

            entries.Add(new CatalogEntry(
                Kind: Kind,
                Id: action.RowId,
                // Action names are display-ready in the sheet, so no title casing here.
                Name: name,
                IconId: action.Icon,
                // Role actions carry an ordinary category in the sheet, so they would
                // scatter through the job's own abilities without this.
                Category: action.IsRoleAction
                    ? RoleCategory
                    : action.ActionCategory.ValueNullable?.Name.ExtractText(),
                SortOrder: action.ClassJobLevel,
                Command: null));
        }

        // Grouped like the game's own Actions & Traits tabs, then in the order they were
        // learned, which is the order a player thinks of them in.
        entries.Sort(static (a, b) =>
        {
            var byCategory = string.CompareOrdinal(a.Category ?? string.Empty, b.Category ?? string.Empty);
            if (byCategory != 0)
                return byCategory;

            var byLevel = a.SortOrder.CompareTo(b.SortOrder);
            return byLevel != 0 ? byLevel : string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
        });

        return entries;
    }

    /// <summary>
    /// The category column covering the named job, or null when the game knows a job that
    /// this build of the sheet definitions does not.
    /// </summary>
    private static PropertyInfo? ColumnFor(string abbreviation) =>
        Columns.GetOrAdd(
            abbreviation,
            static name => typeof(ClassJobCategory).GetProperty(name, BindingFlags.Public | BindingFlags.Instance));
}
