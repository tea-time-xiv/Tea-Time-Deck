using System;
using System.Collections.Generic;
using System.Globalization;
using Dalamud.Game;

namespace TeaTimeDeck.Game;

internal static class GameText
{
    /// <summary>
    /// Words the game leaves lowercase inside a name: "Puff of Darkness", not "Puff Of
    /// Darkness". Not exhaustive, but it covers what shows up in mount and minion names.
    /// </summary>
    private static readonly HashSet<string> MinorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "at", "for", "from", "in", "of", "on", "or", "the", "to", "with",
    };

    /// <summary>
    /// Capitalises a name the way the game displays it.
    ///
    /// Mount and minion names are stored lowercase because the game composes them into
    /// sentences ("You obtain a company chocobo."), so they need capitalising before they
    /// go on a key. Emote names are already display-ready and must not go through this.
    ///
    /// Only the first letter of each word is touched, so hyphenated names keep their
    /// shape: "wind-up airship" becomes "Wind-up Airship", not "Wind-Up Airship".
    /// </summary>
    public static string TitleCase(string name)
    {
        // Only English stores names this way. German already capitalises its nouns, and
        // Japanese has no case at all, so meddling there would only do damage.
        if (Plugin.ClientState.ClientLanguage != ClientLanguage.English || string.IsNullOrEmpty(name))
            return name;

        var words = name.Split(' ');

        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            if (word.Length == 0)
                continue;

            if (i > 0 && MinorWords.Contains(word))
            {
                words[i] = word.ToLower(CultureInfo.CurrentCulture);
                continue;
            }

            words[i] = char.ToUpper(word[0], CultureInfo.CurrentCulture) + word[1..];
        }

        return string.Join(' ', words);
    }
}
