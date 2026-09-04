namespace TeaTimeDeck.Game;

/// <summary>
/// One thing the player owns and can put on a deck key. Deliberately flat and
/// kind-agnostic: the Stream Deck browser renders emotes and mounts with the same code.
/// </summary>
/// <param name="Kind">Catalog this came from, e.g. "emote".</param>
/// <param name="Id">Excel row id, unique within the kind.</param>
/// <param name="Name">Display name, already localised by the game's sheets.</param>
/// <param name="IconId">Game icon id. Fetch the image separately; it is not inlined here.</param>
/// <param name="Category">Grouping hint for the browser. Null when the kind has no natural grouping.</param>
/// <param name="SortOrder">The game's own ordering, so lists match what the player sees in-game.</param>
/// <param name="Command">Text command, where the kind has one. Informational.</param>
/// <param name="Key">
/// Identifier for kinds the game does not number, currently Glamourer's design GUIDs.
/// Null for everything that has an Excel row id, which is what <see cref="Id"/> is for.
/// </param>
public sealed record CatalogEntry(
    string Kind,
    uint Id,
    string Name,
    uint IconId,
    string? Category,
    int SortOrder,
    string? Command,
    string? Key = null);
