using System;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using TeaTimeDeck.Api;

namespace TeaTimeDeck;

/// <summary>
/// Says once, in chat, that there is a second half to install.
///
/// This is the one thing this plugin cannot show by working: it loads, listens, and looks
/// exactly the same whether or not the Stream Deck plugin exists, so a new user is left
/// with a blank deck and nothing to read. The Dalamud installer's repository link lands on
/// a README rather than on a download, which is the same gap seen from further away.
///
/// Once, and then never again: a notice that repeats is an advert. A deck that is already
/// connected proves the user found it, so that run is spent marking the notice done rather
/// than showing it.
/// </summary>
internal sealed class FirstRunNotice : IDisposable
{
    /// <summary>
    /// Long enough for the deck plugin to have connected if it is installed -- it retries
    /// every second or so -- and short enough that a new user is still looking at the game
    /// they just changed something in.
    /// </summary>
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(15);

    /// <summary>Ours alone, so a click always means this notice's link.</summary>
    private const uint LinkCommandId = 1;

    private readonly Configuration config;
    private readonly ApiServer server;
    private readonly DalamudLinkPayload link;
    private readonly DateTime due = DateTime.UtcNow + Delay;

    private bool done;

    public FirstRunNotice(Configuration config, ApiServer server)
    {
        this.config = config;
        this.server = server;

        // Registered whether or not the notice fires: it costs one handler, and a link
        // payload that outlives the message it was printed in is what keeps an old line
        // in the chat log clickable.
        link = Plugin.ChatGui.AddChatLinkHandler(LinkCommandId,
            (_, _) => Util.OpenLink(Plugin.StreamDeckDownloadUrl));

        if (config.DeckDownloadNoticeShown)
        {
            done = true;
            return;
        }

        Plugin.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
        Plugin.ChatGui.RemoveChatLinkHandler(LinkCommandId);
    }

    private void OnUpdate(IFramework framework)
    {
        if (done || DateTime.UtcNow < due)
            return;

        // Chat before a character is loaded goes nowhere. Waiting costs nothing: the
        // notice is about a download, and it keeps until there is somebody to read it.
        if (!Plugin.ClientState.IsLoggedIn)
            return;

        done = true;
        Plugin.Framework.Update -= OnUpdate;

        // Marked shown either way. Somebody with a deck attached does not need telling,
        // and somebody who was told does not need telling twice.
        config.DeckDownloadNoticeShown = true;
        config.Save();

        if (server.SessionCount > 0)
            return;

        Print();
    }

    private void Print()
    {
        var message = new SeStringBuilder()
            .AddText("The Stream Deck half is a separate download. ")
            .Add(link)
            .AddUiForeground("[Get it here]", 34)
            .Add(RawPayload.LinkTerminator)
            .AddText("  or type /ttd. This notice will not appear again.")
            .Build();

        Plugin.ChatGui.Print(new XivChatEntry
        {
            Name = "Tea Time Deck",
            Message = message,
            Type = XivChatType.Notice,
        });
    }
}
