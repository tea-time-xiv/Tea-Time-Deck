# Tea Time Deck

Drive Final Fantasy XIV from an Elgato Stream Deck.

Two halves that talk over a localhost WebSocket:

```
[FFXIV process]
  └─ Dalamud plugin (C#, net10.0)      reads game data, executes hotbar slots
       └─ localhost WebSocket :37985
             ↕ JSON, no handshake — see docs/protocol.md
[Stream Deck app]
  └─ Stream Deck plugin (TypeScript)   renders keys, sends commands
```

## What it does differently

XIVDeck maps one key to one hand-picked action via a Property Inspector
dropdown. Tea Time Deck's headline feature is a **paged virtual browser**: a
plugin-owned profile of generic slot keys that the plugin paints at runtime from
live game data. Put a browser on your deck and every emote you own is there,
paginated, sorted, categorised — with no per-key setup, updating itself as you
unlock things.

The Stream Deck SDK has no API for creating keys, so this is the only way to get
a genuinely auto-populated menu.

## Running alongside XIVDeck

**You can install and enable both at the same time.** Tea Time Deck is not a
replacement for XIVDeck and does not interfere with it — keep XIVDeck for
whatever you already have set up and use Tea Time Deck's browser next to it, on
the same deck if you like.

Nothing is shared between them:

| | XIVDeck | Tea Time Deck |
| --- | --- | --- |
| Dalamud plugin | `XIVDeck` | `TeaTimeDeck` |
| Local port | 37984 | **37985** |
| Stream Deck plugin | `dev.wolf.xivdeck` | `xiv.teatime.deck` |
| Stream Deck category | XIVDeck | Tea Time Deck |
| Config command | `/xivdeck` | `/ttd` |

Separate ports, separate plugin identifiers, separate settings. Neither reads
the other's configuration.

They also stay out of each other's way in game. Tea Time Deck executes through the
hotbar module's dedicated `ScratchSlot`, so it never writes to, saves over, or
restores a real hotbar slot — there is no shared slot for the two to fight
over, and no window where one could fire the other's action.

The one thing to avoid is pointing both at the same port. Leave Tea Time Deck on
37985 unless you have changed XIVDeck's port too.

This is the setup the project is developed on: XIVDeck installed and enabled
throughout.

## Surfaces

Auto-populated browsers, all built from what your character has actually
unlocked:

| Catalog | Status |
| --- | --- |
| Emotes | Done — categories can be hidden individually in `/ttd` |
| Mounts | Done |
| Minions | Done |
| Fashion accessories, gear sets, macros, teleports, glamour plates | Planned |

Read-only status keys, drawn as SVG in the game's own visual style:

| Key | Status |
| --- | --- |
| Vitals — HP/MP/GP/CP, automatic per job | Done |
| Job and level, with level-sync marker | Done |
| Duty Finder queue state | Done |
| Retainer venture timers | Done |
| Recast timer for Sprint, Teleport, Return | Done |

These display and execute nothing, so they sit well inside the scope rules
below. See [docs/next-steps.md](docs/next-steps.md) for what is left.

## Scope discipline

One keypress equals one action, always. No auto-repeat, no queueing, no timers
that fire actions, no conditional rotations, no chat-command injection. Actions
are executed by writing a scratch hotbar slot and triggering it, the same
mechanism the game itself uses for a keyboard press.

## Building

See [BUILDING.md](BUILDING.md) for the game half and
[streamdeck/README.md](streamdeck/README.md) for the deck half.

Short version:

```powershell
. .\dev.ps1
dotnet build dalamud/TeaTimeDeck.slnx -c Release

cd streamdeck; npm install; npm run build; cd ..
.\tools\Install-StreamDeckPlugin.ps1 -Restart
```

Nothing to configure after that: there is no key to pair, and the Stream Deck
plugin finds the port by reading this plugin's own config file. Drag a key on
and it works.

## Status

Working end to end: emote, mount and minion browsers with real game icons,
executing in game. The wire protocol is documented in
[docs/protocol.md](docs/protocol.md), and the game half can be driven without a
Stream Deck at all using the scripts in `tools/`.

What is next is in [docs/next-steps.md](docs/next-steps.md).

## License

[GNU AGPL-3.0-or-later](LICENSE). Copyright (C) 2026 Tea Time.

Not affiliated with Square Enix or Elgato. FINAL FANTASY is a registered
trademark of Square Enix Holdings Co., Ltd.
