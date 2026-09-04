using System.Collections.Generic;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using TeaTimeDeck.Api;
using TeaTimeDeck.Game;
using TeaTimeDeck.Windows;

namespace TeaTimeDeck;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/ttd";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IUnlockState UnlockState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ITextureReadbackProvider TextureReadback { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    internal readonly WindowSystem WindowSystem = new("TeaTimeDeck");
    internal Configuration Configuration { get; }
    internal GlamourerIpc Glamourer { get; }
    internal CatalogRegistry Catalogs { get; }
    internal ExecutionGate Gate { get; }
    internal HotbarExecutor Executor { get; }
    internal GlamourerExecutor Designs { get; }
    internal IconService Icons { get; }
    internal ApiServer ApiServer { get; }
    internal StatusService Status { get; }
    internal CatalogWatcher Watcher { get; }

    private ConfigWindow ConfigWindow { get; }

    public Plugin()
    {
        Configuration = Configuration.Load();

        // Optional, and absent most of the time for most people: nothing below cares
        // whether Glamourer is installed, only whether it is answering right now.
        Glamourer = new GlamourerIpc(Configuration);

        Catalogs = new CatalogRegistry(Configuration, Glamourer);

        // One gate for both executors, so the 100 ms floor cannot be walked around by
        // pressing a design key and a hotbar key alternately.
        Gate = new ExecutionGate();
        Executor = new HotbarExecutor(Catalogs, Gate);
        Designs = new GlamourerExecutor(Catalogs, Glamourer, Gate);

        Icons = new IconService();
        ApiServer = new ApiServer(Configuration, Catalogs, Executor, Designs, Icons);

        // Created after the server: it samples only while something is connected.
        Status = new StatusService(ApiServer);
        ApiServer.AttachStatus(Status);

        // Gear sets and Glamourer designs have no unlock event to ride on, so something
        // has to look at them.
        Watcher = new CatalogWatcher(ApiServer, Catalogs, Glamourer);

        // An unlock invalidates the cache; tell clients so their browsers refetch.
        Catalogs.Invalidated += OnCatalogsInvalidated;

        if (Configuration.ServerEnabled)
            ApiServer.Start();

        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Tea Time Deck settings window.",
        });

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUi;

        Log.Information("Tea Time Deck loaded. Local API port {Port}, server {State}.",
            Configuration.ApiPort, Configuration.ServerEnabled ? "enabled" : "disabled");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUi;

        CommandManager.RemoveHandler(CommandName);

        Catalogs.Invalidated -= OnCatalogsInvalidated;

        Watcher.Dispose();
        Status.Dispose();
        ApiServer.Dispose();
        Catalogs.Dispose();

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
    }

    /// <summary>
    /// Null kinds means every catalog, which is what the event has always meant: a client
    /// that ignores the payload still refetches everything and stays correct.
    /// </summary>
    private void OnCatalogsInvalidated(IReadOnlyCollection<string>? kinds) =>
        ApiServer.Broadcast(Message.Event("catalog.invalidated", kinds is null ? null : new { kinds }));

    private void OnCommand(string command, string args) => ToggleConfigUi();

    private void DrawUi() => WindowSystem.Draw();

    internal void ToggleConfigUi() => ConfigWindow.Toggle();
}
