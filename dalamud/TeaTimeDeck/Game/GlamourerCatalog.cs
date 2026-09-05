using System.Collections.Generic;

namespace TeaTimeDeck.Game;

/// <summary>
/// Glamourer designs. The first catalog that comes from another plugin rather than from
/// the game, which shows in two places: the list is empty whenever Glamourer is not
/// loaded, and a design is named by the GUID Glamourer gave it rather than by a row id.
/// </summary>
internal sealed class GlamourerCatalog : ICatalogProvider
{
    /// <summary>Named here so the executor and the watcher agree with the wire.</summary>
    public const string KindName = "glamourer";

    /// <summary>
    /// The one key of this kind that is not a design. Not a GUID, and it cannot be mistaken
    /// for one, which is what lets the executor tell the two apart by looking at the key it
    /// was handed rather than by keeping a second list.
    /// </summary>
    public const string ResetKey = "reset";

    private readonly GlamourerIpc glamourer;

    public GlamourerCatalog(GlamourerIpc glamourer)
    {
        this.glamourer = glamourer;
    }

    public string Kind => KindName;

    public string DisplayName => "Glamourer";

    /// <summary>
    /// GUIDs, and they have to stay GUIDs: a key that saved a position in the list would
    /// come back pointing at a different design after the next one was added or deleted.
    /// </summary>
    public CatalogAddressing Addressing => CatalogAddressing.Key;

    public IReadOnlyList<CatalogEntry> Build()
    {
        var designs = glamourer.List();
        var entries = new List<CatalogEntry>(designs.Count + 1);

        // The way back off. Glamourer's list holds designs and only designs, so the one
        // press a design browser most obviously wants -- undo -- is the one press it can
        // never list; it is minted here instead. Pinned, because a design applied from
        // page one is just as likely to be undone from page four.
        if (glamourer.CanRevert)
        {
            entries.Add(new CatalogEntry(
                Kind: Kind,
                Id: 0,
                Name: "Reset",
                IconId: 0,
                Category: null,
                SortOrder: 0,
                Command: "/glamour revert | <me>",
                Key: ResetKey,
                Pinned: true));
        }

        foreach (var design in designs)
        {
            entries.Add(new CatalogEntry(
                Kind: Kind,
                // Unused for this kind; Key is what addresses a design.
                Id: 0,
                Name: design.Name,
                // Designs have no game artwork, and inventing one would be a lie about what
                // the design contains. The deck falls back to the name on the key.
                IconId: 0,
                Category: FolderOf(design.FullPath),
                // Already ordered by path, so this keeps the deck's paging matching the
                // order Glamourer's own list shows.
                SortOrder: entries.Count,
                // Informational, like every other kind's command, and it follows the apply
                // setting so it says what a press will actually do. Execution goes over IPC:
                // it reports success, and it cannot be seen by anyone else.
                Command: $"/glamour {glamourer.ApplyCommand} \"{design.Name}\" | <me>",
                Key: design.Id.ToString("D")));
        }

        return entries;
    }

    /// <summary>
    /// The design's folder, which is what the browser groups by. Glamourer's full path ends
    /// in the design's own name, and designs filed at the root have nothing else in it.
    /// </summary>
    private static string? FolderOf(string fullPath)
    {
        var cut = fullPath.LastIndexOf('/');
        return cut <= 0 ? null : fullPath[..cut];
    }
}
