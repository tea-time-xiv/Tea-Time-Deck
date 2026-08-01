using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;

namespace TeaTimeDeck.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private static readonly Vector4 Green = new(0.4f, 0.85f, 0.4f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.4f, 0.4f, 1f);

    private readonly Plugin plugin;
    private readonly Configuration config;

    private int portInput;

    public ConfigWindow(Plugin plugin) : base("Tea Time Deck###TeaTimeDeckConfig")
    {
        this.plugin = plugin;
        this.config = plugin.Configuration;
        this.portInput = config.ApiPort;

        Size = new Vector2(420, 440);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("Local API");
        ImGui.Separator();

        var enabled = config.ServerEnabled;
        if (ImGui.Checkbox("Accept Stream Deck connections", ref enabled))
        {
            config.ServerEnabled = enabled;
            config.Save();
            plugin.ApiServer.Restart();
        }

        var server = plugin.ApiServer;
        if (server.IsRunning)
        {
            ImGui.TextColored(Green, $"Listening on 127.0.0.1:{config.ApiPort}");
            ImGui.SameLine();
            ImGui.TextDisabled($"({server.SessionCount} client(s) connected)");
        }
        else if (server.LastError is { } error)
        {
            ImGui.TextColored(Red, $"Not listening: {error}");
        }
        else
        {
            ImGui.TextDisabled("Not listening.");
        }

        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Port", ref portInput))
        {
            portInput = Math.Clamp(portInput, 1024, 65535);
        }

        ImGui.SameLine();
        if (ImGui.Button("Apply port"))
        {
            config.ApiPort = portInput;
            config.Save();
            plugin.ApiServer.Restart();
        }

        ImGui.TextDisabled("XIVDeck uses 37984; leave this on 37985 to run both.");

        ImGui.Spacing();
        ImGui.TextWrapped("There is nothing to pair. The Stream Deck plugin finds this port on its " +
                          "own, and any program on this PC may drive your character while the box " +
                          "above is ticked -- including using your job's actions and equipping gear " +
                          "sets, one press at a time. Web pages cannot: they are refused. Untick it " +
                          "to close the port entirely.");

        ImGui.Spacing();
        DrawEmoteCategories();
    }

    private void DrawEmoteCategories()
    {
        ImGui.TextUnformatted("Emote categories");
        ImGui.Separator();
        ImGui.TextWrapped("Unticked categories are left out of the emote list sent to the deck. " +
                          "Facial expressions are a quarter of the list and rarely worth a key.");

        var sheet = Plugin.DataManager.GetExcelSheet<EmoteCategory>();
        if (sheet is null)
        {
            ImGui.TextDisabled("Emote category data unavailable.");
            return;
        }

        foreach (var category in sheet)
        {
            if (category.RowId == 0)
                continue;

            var name = category.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var shown = !config.HiddenEmoteCategories.Contains(category.RowId);
            if (!ImGui.Checkbox(name, ref shown))
                continue;

            if (shown)
            {
                config.HiddenEmoteCategories.Remove(category.RowId);
            }
            else
            {
                config.HiddenEmoteCategories.Add(category.RowId);
            }

            config.Save();

            // Drops the cached list and tells connected decks to refetch. Only the emote
            // list is built from this setting, so nothing else needs rebuilding.
            plugin.Catalogs.Invalidate("emote");
        }
    }
}
