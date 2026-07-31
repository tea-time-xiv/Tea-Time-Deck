using System.Collections.Generic;

namespace TeaTimeDeck.Game;

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
    /// Builds the full list of entries the player currently owns.
    /// Always called on the framework thread; sheet and unlock reads are not thread-safe.
    /// </summary>
    IReadOnlyList<CatalogEntry> Build();
}
