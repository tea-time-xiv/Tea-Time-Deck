using System.Text.Json.Serialization;

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
/// <param name="Color">
/// Tint a client may draw the entry in, as 0xRRGGBB, or 0 for none. For kinds with no
/// game artwork it is most of what tells one key from another at a glance; Glamourer
/// fills it from the colour its own UI draws each design in, so the deck matches the
/// list the player already knows. A hint, like <see cref="Pinned"/>.
/// </param>
/// <param name="Pinned">
/// Asks a browser to keep this entry on a key of its own rather than letting it page away
/// with the rest -- currently only Glamourer's Reset, which is worth reaching from page
/// four as much as from page one. A hint: a client that ignores it simply shows the entry
/// first, which is where it already sorts.
/// </param>
public sealed record CatalogEntry(
    string Kind,
    uint Id,
    string Name,
    uint IconId,
    string? Category,
    int SortOrder,
    string? Command,
    string? Key = null,
    // Left off the wire when false, which is every entry but one: a hundred emotes each
    // carrying "pinned": false would be a hundred lines saying nothing.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool Pinned = false,
    // Same reason: every kind that has artwork has no colour to report.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    uint Color = 0);
