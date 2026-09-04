using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Ipc;

namespace TeaTimeDeck.Game;

/// <summary>
/// Glamourer's designs, over Glamourer's own IPC.
///
/// Hand-rolled rather than referencing its NuGet package: every gate needed here is typed
/// in primitives -- the apply flags are a ulong and the result an int -- so there is
/// nothing to reference and nothing to keep in step beyond the labels below. A Glamourer
/// that renames or retypes a gate turns the catalog empty rather than breaking the build,
/// which is the right failure for something that is optional to begin with.
///
/// Glamourer being absent is a normal state, not an error: the gates exist either way and
/// <see cref="Available"/> is what says whether anything is listening.
/// </summary>
internal sealed class GlamourerIpc
{
    /// <summary>
    /// One design as Glamourer's extended list describes it: its display name, its full
    /// path within the design folders, and the colour its own UI draws it in.
    /// </summary>
    public readonly record struct Design(Guid Id, string Name, string FullPath, uint Color);

    /// <summary>
    /// Equipment | Customization, which is what <c>/glamour apply</c> sends. It does not
    /// force both on: a design's own per-slot toggles still decide what it touches, so a
    /// customization-only design leaves gear alone because it says so, not because of this.
    /// </summary>
    private const ulong ApplyEverythingTheDesignHolds = 2 | 4;

    /// <summary>
    /// The local player and nothing else -- Glamourer indexes the object table, where 0 is
    /// always the player. This is the API's <c>| &lt;me&gt;</c>, and the reason this plugin
    /// cannot dress anyone else.
    /// </summary>
    private const int LocalPlayerIndex = 0;

    /// <summary>
    /// No lock key. Locking would hold the state against Glamourer's own UI and every other
    /// plugin until something unlocked it again; a deck press has no business doing that.
    /// </summary>
    private const uint NoLockKey = 0;

    private readonly ICallGateSubscriber<(int Major, int Minor)> apiVersion;

    private readonly ICallGateSubscriber<Dictionary<Guid, (string DisplayName, string FullPath,
        uint DisplayColor, bool ShownInQdb)>> designList;

    private readonly ICallGateSubscriber<Guid, int, uint, ulong, int> applyDesign;

    /// <summary>Logged once per Glamourer, so a mismatch shows up in the log rather than as silence.</summary>
    private bool versionLogged;

    public GlamourerIpc()
    {
        apiVersion = Plugin.PluginInterface.GetIpcSubscriber<(int, int)>("Glamourer.ApiVersion.V2");
        designList = Plugin.PluginInterface
            .GetIpcSubscriber<Dictionary<Guid, (string, string, uint, bool)>>("Glamourer.GetDesignListExtended");
        applyDesign = Plugin.PluginInterface
            .GetIpcSubscriber<Guid, int, uint, ulong, int>("Glamourer.ApplyDesign");
    }

    /// <summary>
    /// Whether Glamourer is loaded and answering. Two field reads, so the watcher can ask
    /// every poll: this is how the type appears and disappears with the plugin instead of
    /// waiting for a restart.
    /// </summary>
    public bool Available => designList.HasFunction && applyDesign.HasFunction;

    /// <summary>
    /// Every design, ordered by the path Glamourer files it under so the deck matches the
    /// list the player already knows. Empty when Glamourer is not there; the caller does
    /// not get to tell the difference between that and having no designs, and does not
    /// need to.
    /// </summary>
    public IReadOnlyList<Design> List()
    {
        if (!Available)
        {
            // So a Glamourer that comes back -- reloaded, or updated -- says so again.
            versionLogged = false;
            return [];
        }

        try
        {
            LogVersion();

            return designList.InvokeFunc()
                .Select(pair => new Design(pair.Key, pair.Value.DisplayName, pair.Value.FullPath,
                    pair.Value.DisplayColor))
                // Deterministic, which the catalog wants for its ordering and the watcher
                // wants for its hash -- dictionary order would reshuffle on every edit.
                .OrderBy(design => design.FullPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(design => design.Id)
                .ToList();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Glamourer's design list could not be read.");
            return [];
        }
    }

    /// <summary>
    /// Applies one design to the player. Returns Glamourer's own result code; deciding
    /// what a non-success code means is the caller's, since only it knows what was asked.
    /// </summary>
    public int Apply(Guid design)
    {
        if (!Available)
            throw new InvalidOperationException("Glamourer is not installed or not loaded");

        return applyDesign.InvokeFunc(design, LocalPlayerIndex, NoLockKey, ApplyEverythingTheDesignHolds);
    }

    /// <summary>Glamourer's own words for a result code, for the ones worth reporting.</summary>
    public static string DescribeResult(int code) => code switch
    {
        2 => "no character is logged in",
        3 => "that character cannot wear a design",
        4 => "that design no longer exists in Glamourer",
        6 => "another plugin has locked your appearance",
        7 => "Glamourer could not apply that design",
        8 => "Glamourer could not read that design",
        _ => $"Glamourer refused with code {code}",
    };

    private void LogVersion()
    {
        if (versionLogged || !apiVersion.HasFunction)
            return;

        versionLogged = true;

        try
        {
            var (major, minor) = apiVersion.InvokeFunc();
            Plugin.Log.Information("Glamourer IPC {Major}.{Minor} is available.", major, minor);

            // Not a gate. Refusing to talk to a Glamourer we have not met would break the
            // browser on the day Glamourer ships a new API, when the gates we use may well
            // still work; a line in the log is enough to explain it if they do not.
            if (major != 1)
                Plugin.Log.Warning("Glamourer's API major version is {Major}, not 1. Designs may not work.", major);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Glamourer's version could not be read.");
        }
    }
}
