using System;

namespace TeaTimeDeck.Game;

/// <summary>
/// The single point where a request becomes something the character does.
///
/// Shared by every executor on purpose. The floor is what makes one press equal one
/// action rather than a rate a script can pick, and a kind that reached the game through
/// its own private timer would quietly undo that -- so there is one timer, here.
/// </summary>
internal sealed class ExecutionGate
{
    /// <summary>
    /// Floor on the gap between executions. A human pressing a key cannot beat this;
    /// a script trying to drive the game through the API can, and gets refused.
    /// </summary>
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(100);

    private DateTime lastExecution = DateTime.MinValue;

    /// <summary>
    /// Framework thread only: the check reads player state. Throws rather than returning
    /// false, because every caller's only answer to a refusal is to pass it on.
    /// </summary>
    public void Claim()
    {
        if (!Plugin.PlayerState.IsLoaded)
            throw new InvalidOperationException("no character is logged in");

        var now = DateTime.UtcNow;
        if (now - lastExecution < MinimumInterval)
            throw new InvalidOperationException("executing too fast; one action per press");

        lastExecution = now;
    }
}
