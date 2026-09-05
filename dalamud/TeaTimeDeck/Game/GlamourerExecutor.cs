using System;
using System.Linq;
using System.Threading.Tasks;

namespace TeaTimeDeck.Game;

/// <summary>
/// Applies one Glamourer design to the player, or takes the last one back off.
///
/// Deliberately shaped like <see cref="HotbarExecutor"/> even though nothing here touches
/// a hotbar: the design is looked up in the catalog rather than passed through, one
/// request applies one design, and the same <see cref="ExecutionGate"/> holds. A design
/// that Glamourer no longer lists cannot be applied by a client that remembers it.
///
/// Reset is the same journey with a different ending: it is a catalog entry like any
/// other, so a client that remembers it while Glamourer is unloaded is refused by the
/// lookup rather than by anything here.
/// </summary>
internal sealed class GlamourerExecutor
{
    /// <summary>Glamourer's own "the function did nothing, and that is fine".</summary>
    private const int NothingDone = 1;

    private const int Success = 0;

    private readonly CatalogRegistry catalogs;
    private readonly GlamourerIpc glamourer;
    private readonly ExecutionGate gate;

    public GlamourerExecutor(CatalogRegistry catalogs, GlamourerIpc glamourer, ExecutionGate gate)
    {
        this.catalogs = catalogs;
        this.glamourer = glamourer;
        this.gate = gate;
    }

    public async Task<object> ExecuteAsync(string kind, string key)
    {
        if (!string.Equals(kind, GlamourerCatalog.KindName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"kind '{kind}' cannot be executed");

        var entries = await catalogs.GetAsync(kind).ConfigureAwait(false);
        var entry = entries.FirstOrDefault(e =>
                        string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"{kind} {key} is not in your catalog");

        if (string.Equals(entry.Key, GlamourerCatalog.ResetKey, StringComparison.OrdinalIgnoreCase))
        {
            return await Plugin.Framework
                .RunOnFrameworkThread(() => Revert(entry))
                .ConfigureAwait(false);
        }

        // The catalog only ever holds GUIDs Glamourer handed us, so this is a formality --
        // but it is the value that goes back over IPC, so it is checked rather than assumed.
        if (!Guid.TryParse(entry.Key, out var design))
            throw new ArgumentException($"{kind} {key} is not a design id");

        return await Plugin.Framework
            .RunOnFrameworkThread(() => Apply(entry, design))
            .ConfigureAwait(false);
    }

    private object Apply(CatalogEntry entry, Guid design)
    {
        gate.Claim();

        var result = glamourer.Apply(design);

        // Report first: it is what decides a refusal is a refusal, and a log line claiming
        // the design was applied would be wrong on exactly the presses worth reading about.
        var report = Report(entry, result);

        Plugin.Log.Debug("Applied design {Design} ({Name}): result={Result}", design, entry.Name, result);

        return report;
    }

    /// <summary>
    /// Undoes whatever design is on, back to what the character is actually wearing.
    /// Claims the same gate a design press does: from the game's point of view this is
    /// another appearance change, and it is no cheaper to send than the one it undoes.
    /// </summary>
    private object Revert(CatalogEntry entry)
    {
        gate.Claim();

        var result = glamourer.Revert();
        var report = Report(entry, result);

        Plugin.Log.Debug("Reverted the player's appearance: result={Result}", result);

        return report;
    }

    /// <summary>
    /// Glamourer's answer, turned into either an error or the response shape every other
    /// kind returns. <c>NothingDone</c> is a success: reverting a character wearing no
    /// design did what was asked of it.
    /// </summary>
    private static object Report(CatalogEntry entry, int result)
    {
        if (result is not (Success or NothingDone))
            throw new InvalidOperationException(GlamourerIpc.DescribeResult(result));

        return new
        {
            kind = entry.Kind,
            key = entry.Key,
            name = entry.Name,
            // Glamourer answers rather than predicting, so by the time there is anything to
            // report the design has already been applied. Kept for shape: every execute
            // response says the same things.
            usable = true,
            result,
        };
    }
}
