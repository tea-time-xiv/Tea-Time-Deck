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
    /// path within the design folders, and the colour its own UI draws it in, as 0xRRGGBB
    /// rather than in the packing Glamourer hands over. 0 is no colour, which is what an
    /// untouched design has.
    /// </summary>
    public readonly record struct Design(Guid Id, string Name, string FullPath, uint Color);

    private const ulong EquipmentFlag = 2;

    private const ulong CustomizationFlag = 4;

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

    private readonly Configuration config;

    private readonly ICallGateSubscriber<(int Major, int Minor)> apiVersion;

    private readonly ICallGateSubscriber<Dictionary<Guid, (string DisplayName, string FullPath,
        uint DisplayColor, bool ShownInQdb)>> designList;

    private readonly ICallGateSubscriber<Guid, int, uint, ulong, int> applyDesign;

    private readonly ICallGateSubscriber<int, uint, ulong, int> revertState;

    /// <summary>Logged once per Glamourer, so a mismatch shows up in the log rather than as silence.</summary>
    private bool versionLogged;

    public GlamourerIpc(Configuration config)
    {
        this.config = config;

        apiVersion = Plugin.PluginInterface.GetIpcSubscriber<(int, int)>("Glamourer.ApiVersion.V2");
        designList = Plugin.PluginInterface
            .GetIpcSubscriber<Dictionary<Guid, (string, string, uint, bool)>>("Glamourer.GetDesignListExtended");
        applyDesign = Plugin.PluginInterface
            .GetIpcSubscriber<Guid, int, uint, ulong, int>("Glamourer.ApplyDesign");
        revertState = Plugin.PluginInterface
            .GetIpcSubscriber<int, uint, ulong, int>("Glamourer.RevertState");
    }

    /// <summary>
    /// What a press sends, from the setting in <c>/ttd</c>. Neither choice forces anything
    /// on: a design's own per-slot toggles still decide what it touches, so asking for
    /// everything a customization-only design holds still leaves gear alone.
    /// </summary>
    private ulong ApplyFlags => config.GlamourerApply == GlamourerApplyMode.CustomizationOnly
        ? CustomizationFlag
        : EquipmentFlag | CustomizationFlag;

    /// <summary>
    /// The chat command that would do what a press does, for the catalog to report. Nothing
    /// sends it; it is there so a client can tell the user what a key is about to do.
    /// </summary>
    public string ApplyCommand =>
        config.GlamourerApply == GlamourerApplyMode.CustomizationOnly ? "applycustomization" : "apply";

    /// <summary>
    /// Whether Glamourer is loaded and answering. Two field reads, so the watcher can ask
    /// every poll: this is how the type appears and disappears with the plugin instead of
    /// waiting for a restart.
    /// </summary>
    public bool Available => designList.HasFunction && applyDesign.HasFunction;

    /// <summary>
    /// Whether reverting is on offer as well. Checked apart from <see cref="Available"/> on
    /// purpose: a Glamourer that grew or lost this one gate should cost the catalog its
    /// Reset entry, not every design in it.
    /// </summary>
    public bool CanRevert => revertState.HasFunction;

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
                    ToRgb(pair.Value.DisplayColor)))
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

        return applyDesign.InvokeFunc(design, LocalPlayerIndex, NoLockKey, ApplyFlags);
    }

    /// <summary>
    /// Puts the character back the way the game has it, undoing whatever design was applied.
    /// The same thing Glamourer's own revert does, and it answers with the same result codes.
    ///
    /// Deliberately not filtered by <see cref="ApplyFlags"/>: reverting appearance while
    /// leaving a design's gear on is not a reset of anything, and the setting is about what
    /// a design press should reach, not about what should survive one being undone.
    /// </summary>
    public int Revert()
    {
        if (!CanRevert)
            throw new InvalidOperationException("Glamourer is not installed or not loaded");

        return revertState.InvokeFunc(LocalPlayerIndex, NoLockKey, EquipmentFlag | CustomizationFlag);
    }

    /// <summary>
    /// Glamourer's design colour, which is an ImGui packing (0xAABBGGRR), as the 0xRRGGBB
    /// the catalog reports. The alpha goes: a client draws the colour as its own design
    /// asks, and a half-transparent swatch on a key is a worse answer than a solid one.
    ///
    /// Opaque white is what Glamourer hands over for a design nobody has coloured -- it is
    /// the colour its list draws an uncoloured name in, not a choice anyone made -- so it
    /// reports as no colour. Verified against a design list where every entry came back
    /// 0xFFFFFFFF; taking it at face value gave a deck of identical white bands, which
    /// says less than no band at all.
    ///
    /// A design deliberately coloured white, or black, therefore reports the same 0 as one
    /// never coloured. Both get the client's own choice of colour, which for white is the
    /// better outcome anyway: the band sits under near-white text.
    /// </summary>
    private static uint ToRgb(uint packed)
    {
        if ((packed & 0xFF000000u) == 0)
            return 0;

        var rgb = ((packed & 0xFFu) << 16) | (packed & 0xFF00u) | ((packed >> 16) & 0xFFu);

        return rgb == 0xFFFFFFu ? 0 : rgb;
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
