using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Config;

namespace TeaTimeDeck.Game;

/// <summary>
/// The game's own volume sliders, read and written through its system config -- the same
/// values the Sound Settings tab edits, so a change from a dial shows up there and a change
/// made there shows up on the dial. Framework thread only, like every other game read.
///
/// These are settings, not actions: nothing here fires anything in the world, which is why
/// a volume change does not claim the <see cref="ExecutionGate"/>. A dial sends a burst of
/// ticks when turned, and a floor meant to keep one press to one action would only make
/// turning it feel sticky.
/// </summary>
internal static class GameVolume
{
    /// <param name="Mute">
    /// The option that silences the channel without moving its slider, where the game has
    /// one. The per-player effect channels do not.
    /// </param>
    private sealed record Channel(string Name, SystemConfigOption Level, SystemConfigOption? Mute);

    /// <summary>
    /// In the order the Sound Settings tab lists them. The wire names are ours, not the
    /// config's: <c>SoundSe</c> and <c>IsSndEnv</c> are not names a deck should have to store.
    /// </summary>
    private static readonly Channel[] Channels =
    [
        new("master", SystemConfigOption.SoundMaster, SystemConfigOption.IsSndMaster),
        new("bgm", SystemConfigOption.SoundBgm, SystemConfigOption.IsSndBgm),
        new("effects", SystemConfigOption.SoundSe, SystemConfigOption.IsSndSe),
        new("voice", SystemConfigOption.SoundVoice, SystemConfigOption.IsSndVoice),
        new("system", SystemConfigOption.SoundSystem, SystemConfigOption.IsSndSystem),
        new("ambient", SystemConfigOption.SoundEnv, SystemConfigOption.IsSndEnv),
        new("performance", SystemConfigOption.SoundPerform, SystemConfigOption.IsSndPerform),
        new("self", SystemConfigOption.SoundPlayer, null),
        new("party", SystemConfigOption.SoundParty, null),
        new("others", SystemConfigOption.SoundOther, null),
    ];

    /// <summary>Every channel's current level. A channel the config will not answer for is left out.</summary>
    public static IReadOnlyList<VolumeStatus> Sample()
    {
        var result = new List<VolumeStatus>(Channels.Length);

        foreach (var channel in Channels)
        {
            if (Read(channel) is { } status)
                result.Add(status);
        }

        return result;
    }

    /// <summary>
    /// Applies whichever of the three the caller sent. <paramref name="delta"/> exists
    /// because a dial turned quickly sends several requests before the first answer comes
    /// back; each one moving the level relative to where the game has it lands on the right
    /// value, where absolute levels computed from a stale reading would fight each other.
    /// </summary>
    public static VolumeStatus Set(string name, int? volume, int? delta, bool? muted)
    {
        var channel = Channels.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"unknown volume channel '{name}'");

        if (volume is not null && delta is not null)
            throw new ArgumentException("send 'volume' or 'delta', not both");

        if (muted is not null && channel.Mute is null)
            throw new ArgumentException($"volume channel '{channel.Name}' cannot be muted");

        if (!Plugin.GameConfig.TryGet(channel.Level, out uint current))
            throw new InvalidOperationException($"the game did not report volume channel '{channel.Name}'");

        if (volume is not null || delta is not null)
        {
            var (min, max) = Range(channel);
            var target = volume ?? (long)current + delta!.Value;
            var clamped = (uint)Math.Clamp(target, min, max);

            if (clamped != current)
                Plugin.GameConfig.Set(channel.Level, clamped);
        }

        if (muted is { } mute)
            Plugin.GameConfig.Set(channel.Mute!.Value, mute);

        return Read(channel)
            ?? throw new InvalidOperationException($"the game did not report volume channel '{channel.Name}'");
    }

    private static VolumeStatus? Read(Channel channel)
    {
        if (!Plugin.GameConfig.TryGet(channel.Level, out uint level))
            return null;

        bool? muted = null;
        if (channel.Mute is { } option && Plugin.GameConfig.TryGet(option, out bool isMuted))
            muted = isMuted;

        return new VolumeStatus(channel.Name, level, muted);
    }

    /// <summary>
    /// The config knows its own bounds; asking beats assuming 0-100 and finding out on the
    /// day a patch changes one.
    /// </summary>
    private static (long Min, long Max) Range(Channel channel) =>
        Plugin.GameConfig.TryGet(channel.Level, out UIntConfigProperties? properties)
            && properties is not null && properties.Maximum > properties.Minimum
            ? (properties.Minimum, properties.Maximum)
            : (0, 100);
}
