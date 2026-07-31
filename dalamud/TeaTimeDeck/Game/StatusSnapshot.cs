using System.Collections.Generic;

namespace TeaTimeDeck.Game;

/// <summary>
/// Everything the read-only deck keys need, gathered in one pass.
///
/// One snapshot rather than a request per key: the deck may hold several status keys and
/// they all want data from the same frame, so gathering once and pushing once keeps the
/// game thread cheap and the keys consistent with each other.
/// </summary>
public sealed record StatusSnapshot(
    bool LoggedIn,
    JobStatus? Job,
    VitalsStatus? Vitals,
    DutyStatus Duty,
    RetainerStatus Retainers,
    IReadOnlyList<CooldownStatus> Cooldowns);

/// <param name="Experience">Points into the current level.</param>
/// <param name="ExperienceToNext">Points the level needs in total, or 0 at max level.</param>
public sealed record JobStatus(
    uint Id,
    string Abbreviation,
    string Name,
    uint IconId,
    short Level,
    short EffectiveLevel,
    bool IsLevelSynced,
    int Experience,
    int ExperienceToNext);

/// <param name="Preferred">
/// Which secondary bar this job actually uses: "gp" for gatherers, "cp" for crafters,
/// "mp" otherwise. Lets a key show the meaningful bar without the user configuring one
/// per job, the same way the game only shows you the gauge that applies.
/// </param>
public sealed record VitalsStatus(
    uint Hp,
    uint MaxHp,
    uint Mp,
    uint MaxMp,
    uint Gp,
    uint MaxGp,
    uint Cp,
    uint MaxCp,
    byte ShieldPercent,
    string Preferred);

/// <param name="State">One of idle, queued, ready, inDuty.</param>
public sealed record DutyStatus(string State, string? DutyName);

/// <param name="SoonestCompleteAt">Unix seconds, or null when nothing is out on venture.</param>
public sealed record RetainerStatus(int Total, int Active, int Ready, long? SoonestCompleteAt);

/// <summary>
/// A general action currently on recast. Actions that are ready are simply absent, which
/// keeps the pushed snapshot small since most of them are ready most of the time.
/// </summary>
public sealed record CooldownStatus(uint Id, float Remaining, float Total);
