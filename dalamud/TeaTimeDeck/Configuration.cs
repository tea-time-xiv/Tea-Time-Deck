using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace TeaTimeDeck;

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
