using System.Collections.Generic;

namespace TeaTimeDeck.Game;

/// <summary>
/// Which field on <see cref="CatalogEntry"/> names an entry, and so which one an
/// <c>execute</c> request has to carry.
/// </summary>
internal enum CatalogAddressing
{
    /// <summary>An Excel row id or a module's own index. The default.</summary>
    Id,

    /// <summary>A string the game did not mint, such as a Glamourer design's GUID.</summary>
    Key,
}

/// <summary>
/// Turns one Excel sheet plus the player's unlock state into deck-ready entries.
/// Adding a new browser (minions, ornaments, gear sets) means adding one of these.
/// </summary>
internal interface ICatalogProvider
{
    /// <summary>Stable wire identifier, e.g. "emote". Lowercase.</summary>
    string Kind { get; }

    /// <summary>Human-readable label for the browser's title.</summary>
    string DisplayName { get; }

    /// <summary>
    /// How entries of this kind are addressed. Only kinds sourced from outside the game
    /// need to say; everything with a row id gets the default.
    /// </summary>
    CatalogAddressing Addressing => CatalogAddressing.Id;

    /// <summary>
    /// Builds the full list of entries the player currently owns.
    /// Always called on the framework thread; sheet and unlock reads are not thread-safe.
    /// </summary>
    IReadOnlyList<CatalogEntry> Build();
}
