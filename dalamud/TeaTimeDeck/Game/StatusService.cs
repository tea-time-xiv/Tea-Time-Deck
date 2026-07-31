using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using TeaTimeDeck.Api;

namespace TeaTimeDeck.Game;

/// <summary>
/// Samples the read-only state the deck displays and pushes it when it changes.
///
/// Sampling is throttled and diffed rather than sent every frame: HP ticks constantly,
/// but a 72-pixel key cannot show more than a few updates a second and a WebSocket
/// message per frame would be wasteful for no visible gain.
/// </summary>
internal sealed class StatusService : IDisposable
{
    /// <summary>
    /// Four samples a second. Fast enough that a health bar feels live, slow enough that
    /// the cost of gathering and serialising is irrelevant.
    /// </summary>
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(250);

    private readonly ApiServer server;

    private DateTime lastSample = DateTime.MinValue;
    private string? lastSerialised;

    public StatusService(ApiServer server)
    {
        this.server = server;
        Plugin.Framework.Update += OnUpdate;
    }

    /// <summary>Most recent snapshot, for clients that ask rather than wait for a push.</summary>
    public StatusSnapshot Current { get; private set; } = Empty;

    private static StatusSnapshot Empty => new(
        LoggedIn: false,
        Job: null,
        Vitals: null,
        Duty: new DutyStatus("idle", null),
        Retainers: new RetainerStatus(0, 0, 0, null),
        Cooldowns: []);

    private void OnUpdate(IFramework framework)
    {
        var now = DateTime.UtcNow;
        if (now - lastSample < SampleInterval)
            return;

        lastSample = now;

        // Nobody is listening, so do not pay for the sample at all.
        if (server.SessionCount == 0)
            return;

        StatusSnapshot snapshot;
        try
        {
            snapshot = Sample();
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Status sample failed.");
            return;
        }

        Current = snapshot;

        // Comparing the serialised form sidesteps writing equality for the nested lists,
        // and the result is what would be sent anyway.
        var serialised = JsonSerializer.Serialize(snapshot, Protocol.Json);
        if (serialised == lastSerialised)
            return;

        lastSerialised = serialised;
        server.Broadcast(Message.Event("status.update", snapshot));
    }

    private StatusSnapshot Sample()
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player is null || !Plugin.ClientState.IsLoggedIn)
            return Empty;

        return new StatusSnapshot(
            LoggedIn: true,
            Job: SampleJob(),
            Vitals: SampleVitals(player),
            Duty: SampleDuty(),
            Retainers: SampleRetainers(),
            Cooldowns: SampleCooldowns());
    }

    private static JobStatus? SampleJob()
    {
        var job = Plugin.PlayerState.ClassJob.ValueNullable;
        if (job is null)
            return null;

        var level = Plugin.PlayerState.Level;

        return new JobStatus(
            Id: job.Value.RowId,
            Abbreviation: job.Value.Abbreviation.ExtractText(),
            Name: GameText.TitleCase(job.Value.Name.ExtractText()),
            // Job icons are laid out contiguously from this base, one per ClassJob row.
            IconId: JobIconBase + job.Value.RowId,
            Level: level,
            EffectiveLevel: Plugin.PlayerState.EffectiveLevel,
            IsLevelSynced: Plugin.PlayerState.IsLevelSynced,
            // Experience is always the real level's, never the synced one: a level sync
            // caps what you fight, it does not change what you are earning towards.
            Experience: Plugin.PlayerState.GetClassJobExperience(job.Value),
            ExperienceToNext: ExperienceForLevel(level));
    }

    /// <summary>
    /// Points the given level needs before the next one. Returns 0 at max level, where
    /// ParamGrow has no further row to describe.
    /// </summary>
    private static int ExperienceForLevel(short level)
    {
        if (level <= 0)
            return 0;

        var row = Plugin.DataManager.GetExcelSheet<ParamGrow>()?.GetRowOrDefault((uint)level);
        return row?.ExpToNext ?? 0;
    }

    private const uint JobIconBase = 62100;

    private static VitalsStatus SampleVitals(Dalamud.Game.ClientState.Objects.Types.ICharacter player)
    {
        // Which secondary bar matters is a property of the job, and the game already
        // encodes it: gatherers have GP, crafters have CP, everyone else uses MP.
        var preferred = player.MaxGp > 0 ? "gp" : player.MaxCp > 0 ? "cp" : "mp";

        return new VitalsStatus(
            Hp: player.CurrentHp,
            MaxHp: player.MaxHp,
            Mp: player.CurrentMp,
            MaxMp: player.MaxMp,
            Gp: player.CurrentGp,
            MaxGp: player.MaxGp,
            Cp: player.CurrentCp,
            MaxCp: player.MaxCp,
            ShieldPercent: player.ShieldPercentage,
            Preferred: preferred);
    }

    private static DutyStatus SampleDuty()
    {
        var condition = Plugin.Condition;

        if (condition[ConditionFlag.BoundByDuty] || condition[ConditionFlag.BoundByDuty56] ||
            condition[ConditionFlag.BoundByDuty95])
        {
            var name = Plugin.DutyState.ContentFinderCondition.ValueNullable?.Name.ExtractText();
            return new DutyStatus("inDuty", string.IsNullOrWhiteSpace(name) ? null : name);
        }

        // The pop dialog is up and waiting to be accepted -- the moment that matters most.
        if (condition[ConditionFlag.WaitingForDutyFinder])
            return new DutyStatus("ready", null);

        if (condition[ConditionFlag.InDutyQueue])
            return new DutyStatus("queued", null);

        return new DutyStatus("idle", null);
    }

    private static unsafe RetainerStatus SampleRetainers()
    {
        var manager = RetainerManager.Instance();
        if (manager is null || !manager->IsReady)
            return new RetainerStatus(0, 0, 0, null);

        var total = (int)manager->GetRetainerCount();
        var active = 0;
        var ready = 0;
        long? soonest = null;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        for (uint i = 0; i < total; i++)
        {
            var retainer = manager->GetRetainerBySortedIndex(i);
            if (retainer is null || retainer->VentureId == 0)
                continue;

            active++;

            var completeAt = (long)retainer->VentureComplete;
            if (completeAt <= now)
            {
                ready++;
                continue;
            }

            if (soonest is null || completeAt < soonest)
                soonest = completeAt;
        }

        return new RetainerStatus(total, active, ready, soonest);
    }

    /// <summary>
    /// Only actions actually on recast. Everything else is ready, and saying so for a
    /// dozen actions four times a second would be noise.
    /// </summary>
    private static unsafe IReadOnlyList<CooldownStatus> SampleCooldowns()
    {
        var sheet = Plugin.DataManager.GetExcelSheet<GeneralAction>();
        if (sheet is null)
            return [];

        var manager = ActionManager.Instance();
        if (manager is null)
            return [];

        var cooldowns = new List<CooldownStatus>();

        foreach (var general in sheet)
        {
            if (general.RowId == 0 || general.Action.RowId == 0)
                continue;

            var total = manager->GetRecastTime(ActionType.GeneralAction, general.RowId);
            if (total <= 0)
                continue;

            var elapsed = manager->GetRecastTimeElapsed(ActionType.GeneralAction, general.RowId);
            var remaining = total - elapsed;
            if (remaining <= 0)
                continue;

            cooldowns.Add(new CooldownStatus(general.RowId, remaining, total));
        }

        return cooldowns;
    }

    /// <summary>General actions the player has, for the property inspector's picker.</summary>
    public static IReadOnlyList<object> DescribeCooldownSources()
    {
        var sheet = Plugin.DataManager.GetExcelSheet<GeneralAction>();
        if (sheet is null)
            return [];

        return sheet
            .Where(general => general.RowId != 0 && general.Action.RowId != 0)
            .Where(general => !string.IsNullOrWhiteSpace(general.Name.ExtractText()))
            .Where(general => general.UnlockLink == 0 || Plugin.UnlockState.IsGeneralActionUnlocked(general))
            .Select(general => (object)new
            {
                id = general.RowId,
                name = general.Name.ExtractText(),
                // The sheet stores this signed, and unset rows use -1.
                iconId = general.Icon > 0 ? (uint)general.Icon : 0u,
            })
            .ToList();
    }

    public void Dispose() => Plugin.Framework.Update -= OnUpdate;
}
