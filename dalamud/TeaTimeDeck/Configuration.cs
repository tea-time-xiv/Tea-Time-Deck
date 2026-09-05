using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace TeaTimeDeck;

/// <summary>
/// What pressing a Glamourer design key applies. One setting for every design rather than
/// a choice per key: it is a preference about what this plugin is for, and a deck key that
/// behaved differently from the one beside it would be a puzzle rather than a feature.
/// </summary>
public enum GlamourerApplyMode
{
    /// <summary>Everything the design holds, the same as <c>/glamour apply</c>.</summary>
    Everything = 0,

    /// <summary>
    /// Appearance only, the same as <c>/glamour applycustomization</c>: race, face, hair
    /// and the rest, leaving whatever the character is wearing alone.
    /// </summary>
    CustomizationOnly = 1,
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    /// <summary>Version 2 dropped the API key.</summary>
    public int Version { get; set; } = 2;

    /// <summary>Whether the local API server should be listening.</summary>
    public bool ServerEnabled { get; set; } = true;

    /// <summary>
    /// Loopback port for the local API. Deliberately not 37984, which XIVDeck uses,
    /// so both plugins can be installed at once.
    /// </summary>
    public int ApiPort { get; set; } = 37985;

    /// <summary>
    /// EmoteCategory row ids to leave out of the emote catalog. Facial expressions are the
    /// usual candidate: they are a quarter of the list and rarely worth a deck key.
    ///
    /// Stored as row ids rather than names so the setting survives a language change.
    /// </summary>
    public List<uint> HiddenEmoteCategories { get; set; } = [];

    /// <summary>
    /// What a Glamourer design key applies. No version bump for this one: a config written
    /// before it existed loads as <see cref="GlamourerApplyMode.Everything"/>, which is
    /// exactly what those versions did.
    /// </summary>
    public GlamourerApplyMode GlamourerApply { get; set; } = GlamourerApplyMode.Everything;

    /// <summary>
    /// Whether the "there is a second half to install" line has been said. No version bump:
    /// a config written before this existed loads as false, so an existing user is told
    /// once as well -- and if their deck is already connected when the notice comes due,
    /// it is marked said and never printed.
    /// </summary>
    public bool DeckDownloadNoticeShown { get; set; }

    public static Configuration Load()
    {
        var config = Plugin.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        if (config.Version < 2)
        {
            // Rewrite now rather than whenever the user next changes a setting: the old
            // file still holds the retired key, and there is no reason to leave it there.
            config.Version = 2;
            config.Save();
        }

        return config;
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
