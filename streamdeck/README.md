# Tea Time Deck — Stream Deck plugin

Talks to the Dalamud plugin over the localhost WebSocket described in
[`../docs/protocol.md`](../docs/protocol.md).

## Build

```powershell
cd streamdeck
npm install
npm run build      # or: npm run watch
```

Output lands in `xiv.teatime.deck.sdPlugin/bin/plugin.js`.

## Install for development

```powershell
..\tools\Install-StreamDeckPlugin.ps1 -Restart
```

That junctions the `.sdPlugin` folder into
`%AppData%\Elgato\StreamDeck\Plugins\` so a rebuild does not need reinstalling.
The Stream Deck app only loads plugins at startup, so **restart it after every
rebuild** — `-Restart` does that for you.

To remove the link: `..\tools\Install-StreamDeckPlugin.ps1 -Uninstall`.

## Setting it up

1. Drag **Emote / Mount** onto a key.
2. Pick a type and an entry. The lists come from what your character has
   actually unlocked.

There is nothing to pair. The API has no key: see
[`../docs/protocol.md`](../docs/protocol.md) for why it went. The only thing
that has to match is the port, and the plugin finds that by reading the game
plugin's own config file —
`%AppData%\XIVLauncher\pluginConfigs\TeaTimeDeck.json`.

Multiple XIVLauncher profiles are handled: every `XIVLauncher*` folder is a
candidate, most recently written first, and the client works down the list until
one answers. Whichever did is tried first from then on. If no config is found at
all it still tries 37985, which is where a default install would be listening.

**Port** under **Connection** in the inspector is an override, for an install
the search does not reach; clearing it hands the choice back to discovery.
`TEATIMEDECK_CONFIG` points the search at one exact file if it is set.

## The browser

The headline feature. Instead of configuring one key per emote, place these and
the plugin fills them from the catalog:

| Action | What it does |
| --- | --- |
| **Browser Slot** | One cell of the viewport. Place as many as you want. |
| **Browser: Previous / Next Page** | Pages the slots. Wraps at both ends. |
| **Browser: Switch Type** | Cycles emotes → mounts → back. |

A suggested 15-key layout is 12 slots plus the three navigation keys; that pages
95 emotes in 8 presses. Nothing needs configuring — no property inspector, no
per-key setup.

**Page size is however many slots are currently visible.** That falls out of the
SDK having no way to create keys: the user places the viewport, the plugin fills
it. Add or remove a slot and the paging adjusts on the spot.

**Each Stream Deck page keeps its own type and page number.** Put a browser on
two pages and one can sit on emotes while the other sits on minions; neither
disturbs the other. The choice is remembered across restarts.

That works because the SDK exposes no page identifier, but only one page per
device is visible at a time — so the visible **Switch Type** key stands in for
one, and stores the state in its own settings. A page with slots but no Switch
Type key falls back to a per-device default held in memory.

Slot registration stays per device, so a 15-key and a 32-key deck page at their
own rates rather than fighting over one page number.

Slots are filled in reading order — top-left to bottom-right — regardless of
where the navigation keys sit.

The **Switch Type** key carries the readout for the whole viewport: the type in
large text, the page as `3/8`, and a row of blocks with the current page lit.
Past about twenty pages the blocks stop being countable, so they give way to a
filled track. The paging keys show their arrows and nothing else — the same
counter on three keys was repetition.

## Status keys

Read-only keys that display game state and execute nothing. Four of the five
need no configuration at all — drag and they work.

| Action | Configure | Shows |
| --- | --- | --- |
| **Status: Vitals** | which gauge, default Automatic | Current value and a large gauge |
| **Status: Job** | — | Job icon, level, sync marker, experience bar |
| **Status: Duty** | — | Not queued / in queue / **READY** / in duty |
| **Status: Ventures** | — | Ventures ready, or time to the soonest |
| **Status: Recast** | which action | Sweep ring and seconds left |

**Automatic** on the vitals key follows the job: GP while gathering, CP while
crafting, MP otherwise — the same gauge the game itself would show you. Pin a
specific one if you would rather it never move.

The vitals key prints the current value but not the maximum — the gauge already
says how full you are, and the space buys a bar big enough to read without
looking directly at the key.

The job key's experience bar always tracks your real level, even while synced:
a sync caps what you fight, not what you are earning towards. At maximum level
it reads `MAX` instead, since there is no next level to fill.

The duty key's `READY` state gets a halo, because a pop expires in seconds and
is the one state that must be unmissable across a room.

Faces are drawn as SVG and pushed with `setImage`, so they stay sharp on every
deck generation and the plugin keeps its zero native dependencies. The panel,
hairline and gauge colours follow the game's own UI.

Vitals, job and duty repaint when the game pushes a change. Ventures and recast
also tick locally once a second, because their countdowns come from an absolute
time and need no traffic to stay accurate.

When the game is closed the keys say `offline` rather than showing stale
numbers.

## Layout

| Path | What |
| --- | --- |
| `src/plugin.ts` | Entry point. Registers actions, applies global settings. |
| `src/xiv-client.ts` | WebSocket client for the game. Reconnects on its own. |
| `src/server-discovery.ts` | Finds the port in the game plugin's config file. |
| `src/actions/entry-action.ts` | A single pinned entry: title, icon, executes on press. |
| `src/actions/browser-actions.ts` | Slot and navigation keys. |
| `src/browser.ts` | Per-device paging state and repainting. |
| `src/actions/status-actions.ts` | The five read-only status keys. |
| `src/status-render.ts` | SVG key faces, styled after the game's UI. |
| `xiv.teatime.deck.sdPlugin/manifest.json` | Action and plugin metadata. |
| `xiv.teatime.deck.sdPlugin/ui/entry.html` | Property inspector. |
| `xiv.teatime.deck.sdPlugin/imgs/` | Generated by `tools/New-StreamDeckIcons.ps1`. |

## Notes

The property inspector is hand-written rather than using `sdpi-components`,
which loads from a CDN. Nothing here should need the internet — the only server
involved is the game on loopback.

The game not running is the normal state, not an error. The client retries with
backoff and requests fail cleanly while it is down; a key press in that state
shows the Stream Deck alert.

Logs: `xiv.teatime.deck.sdPlugin/logs/`.
