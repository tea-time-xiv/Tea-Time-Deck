using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using TeaTimeDeck.Game;

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
        DrawStreamDeckHalf();

        ImGui.Spacing();
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
        DrawGlamourer();

        ImGui.Spacing();
        DrawEmoteCategories();
    }

    /// <summary>
    /// The download, first and unconditionally.
    ///
    /// This half is only half: without the Stream Deck plugin nothing appears on a deck,
    /// and there is no symptom to search for -- the game plugin behaves perfectly. It sits
    /// at the top of the window rather than behind a "nobody has connected yet" test,
    /// because the same button is what a user reaches for after a deck rebuild or on a
    /// second PC, when something has certainly connected before.
    /// </summary>
    private void DrawStreamDeckHalf()
    {
        ImGui.TextUnformatted("Stream Deck plugin");
        ImGui.Separator();

        ImGui.TextWrapped("Tea Time Deck is two plugins. This one is the game half; the deck half " +
                          "is a separate download that the Stream Deck app installs.");

        if (ImGui.Button("Download the Stream Deck plugin"))
            Util.OpenLink(Plugin.StreamDeckDownloadUrl);

        ImGui.SameLine();
        if (ImGui.Button("Copy link"))
            ImGui.SetClipboardText(Plugin.StreamDeckDownloadUrl);

        // Spelled out as well as linked: a browser opened from a game running full screen
        // is not always the browser the user is looking at.
        ImGui.TextDisabled(Plugin.StreamDeckDownloadUrl);
        ImGui.TextDisabled("Download xiv.teatime.deck.streamDeckPlugin and double-click it.");
    }

    /// <summary>
    /// One switch for every design key. Glamourer's own commands are the two things a
    /// press can sensibly mean, so the options are named after them rather than after
    /// flags nobody outside this file has heard of.
    /// </summary>
    private void DrawGlamourer()
    {
        ImGui.TextUnformatted("Glamourer designs");
        ImGui.Separator();

        if (plugin.Glamourer.Available)
        {
            ImGui.TextColored(Green, "Glamourer is loaded; your designs are on the deck.");
        }
        else
        {
            ImGui.TextDisabled("Glamourer is not loaded. The Glamourer type stays empty until it is.");
        }

        var mode = config.GlamourerApply;

        var everything = mode == GlamourerApplyMode.Everything;
        if (ImGui.RadioButton("Everything the design holds  (/glamour apply)", everything))
            SetApplyMode(GlamourerApplyMode.Everything);

        var appearanceOnly = mode == GlamourerApplyMode.CustomizationOnly;
        if (ImGui.RadioButton("Appearance only  (/glamour applycustomization)", appearanceOnly))
            SetApplyMode(GlamourerApplyMode.CustomizationOnly);

        ImGui.TextWrapped("Appearance only leaves what you are wearing alone. Either way a design " +
                          "still decides for itself what it carries, so one that holds no gear " +
                          "changes none under both settings.");
    }

    private void SetApplyMode(GlamourerApplyMode mode)
    {
        if (config.GlamourerApply == mode)
            return;

        config.GlamourerApply = mode;
        config.Save();

        // Only the reported command text is built from this, but a deck showing
        // "/glamour apply" while a press applies appearance only would be lying.
        plugin.Catalogs.Invalidate(GlamourerCatalog.KindName);
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
