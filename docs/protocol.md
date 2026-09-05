# Tea Time Deck local API

Protocol version **1**.

The Dalamud plugin listens on `http://localhost:37985/` (configurable). Only two
things are served:

| Route | Auth | Purpose |
| --- | --- | --- |
| `GET /health` | none | Liveness probe. Returns `{ "server", "protocolVersion" }` and nothing about the character. |
| `GET /ws` | none | WebSocket upgrade. Everything else happens here. |

## Authentication

There is none, and no key to present. Connect and start sending messages.

**Loopback is not an authorisation boundary.** Any process on the machine can
reach the port, including a web page the user has open. One thing guards it:

- Any request carrying an `Origin` header is rejected with `403`. Browsers
  always send one and cannot suppress it; native clients never do. This is what
  stops a malicious page from driving the game via a cross-site WebSocket.

That leaves other local programs, which are not gated. They were not really
gated before either: the shared secret this protocol used to require lived in
the plugin's own config file, readable by anything running as that user, so a
program that wanted it had it — while every honest client paid a copy-paste per
install. It was removed rather than kept as theatre.

Anyone who does not want that should untick **Accept Stream Deck connections**
in `/ttd`, which closes the port.

At most 8 concurrent sessions; further connections get `503`.

Closing the port deliberately — unticking that box, changing the port, unloading
the plugin — sends each session a `1000` normal closure and gives it a moment to
answer before the socket goes. A `1006` therefore means the game actually went
away. Clients should reconnect on both; the codes are worth telling apart only
for what they log.

### What an ungated client can learn and do

Worth stating plainly, since there is no auth to hide behind:

- `hello` returns the logged-in character's name. `/health` deliberately does
  not, so a probe that has not opened a socket learns nothing about you.
- `execute` covers emotes, mounts, minions, gear sets, the current job's actions
  and — when Glamourer is loaded — its designs, and the id is looked up in the
  catalog rather than trusted, so it can only fire something the character
  already has. Items and macros are not reachable.
- Applying a design goes to Glamourer over its IPC, aimed at object index 0: the
  local player and nobody else. No chat command is sent for it, or for anything.
- **That includes combat actions.** A local program can make your character
  attack, and can equip a gear set. Both are one press at a time — see below —
  but neither is cosmetic, and this is the honest reading of leaving the port open.
- One execution per 100 ms, enforced server-side.

Nothing here reads chat or moves the character, and nothing queues, repeats or
schedules: one request performs one action, the way one key press does. What the
plugin will not do is play the game for you, and that is a property of the API
surface rather than a promise about the caller.

## Message shape

Client to server:

```json
{ "id": "abc123", "type": "ping", "payload": { } }
```

`id` is an opaque correlation token echoed on the response. Omit it for
fire-and-forget. `payload` is optional and request-specific.

Server to client:

```json
{ "id": "abc123", "type": "ping", "ok": true, "payload": { "pong": true } }
```

On failure:

```json
{ "id": "abc123", "type": "error", "ok": false, "error": "unknown request type 'foo'" }
```

Server-pushed events use the same envelope with no `id`.

Requests are processed one at a time per connection, so responses arrive in the
order the requests were sent. Text frames only; anything over 512 KB closes the
connection.

Outbound messages queue in a bounded, drop-oldest buffer. A client that stops
reading loses events rather than stalling the game thread — treat pushed events
as a hint to refetch, never as an authoritative log.

## Requests

### `ping`

No payload.

```json
{ "pong": true, "serverTime": "2026-07-31T12:00:00Z" }
```

### `hello`

No payload. Send this first; it tells you whether the client can talk to this
server version and whether anyone is logged in.

```json
{
  "server": "Tea Time Deck",
  "pluginVersion": "0.1.0.0",
  "protocolVersion": 1,
  "language": "English",
  "loggedIn": true,
  "characterName": "Firstname Lastname"
}
```

`characterName` is null when no character is loaded.

### `catalog.kinds`

No payload. Lists the browsers this server can populate.

```json
{ "kinds": [ { "kind": "emote", "displayName": "Emotes", "addressing": "id" },
             { "kind": "glamourer", "displayName": "Glamourer", "addressing": "key" } ] }
```

`addressing` says which field names an entry of that kind, and so which one an
`execute` has to carry. Everything the game numbers is `id`; `glamourer` is
`key`, because Glamourer names designs with GUIDs.

### `catalog.list`

```json
{ "kind": "emote" }
```

Returns everything the player owns of that kind, in the game's own display order.

```json
{
  "kind": "emote",
  "count": 95,
  "entries": [
    { "kind": "emote", "id": 16, "name": "Wave", "iconId": 246011,
      "category": "General", "sortOrder": 11, "command": "/wave" }
  ]
}
```

```json
{
  "kind": "glamourer",
  "count": 31,
  "entries": [
    { "kind": "glamourer", "id": 0, "name": "Reset", "iconId": 0,
      "category": null, "sortOrder": 0,
      "command": "/glamour revert | <me>",
      "key": "reset", "pinned": true },
    { "kind": "glamourer", "id": 0, "name": "Elezen F", "iconId": 0,
      "category": "Casual", "sortOrder": 3,
      "command": "/glamour apply \"Elezen F\" | <me>",
      "key": "10765f9e-9377-40d7-b3aa-c86d8dd41c33" }
  ]
}
```

`iconId` is a game icon id, not an image. Fetch images separately. `0` means the
entry has no game artwork at all, as Glamourer designs do not; do not request it.
`category` is a grouping hint and may be null — for designs it is the folder
Glamourer files them under. `command` is informational; the server does not
execute text commands, designs included.

`key` is present only on kinds whose `addressing` is `key`, where `id` is always
`0` and carries no meaning. Clients that save an entry against a deck key must
save the `key`, not a position: designs are added and deleted freely.

`pinned` is present only when true, and asks a browser to give that entry a key
of its own on every page instead of letting it page away. It is a hint about
layout, not about behaviour: a client that ignores it shows the entry first,
which is where it already sorts. Only leading entries are ever pinned, and a
browser too small to spare the slot should ignore the request rather than lose
its ability to page.

`glamourer` is the only kind with one so far — `key` `reset`, which is not a
GUID and never collides with one. It is Glamourer's own revert rather than a
design: the list can hold every look a character can put on, but not the way
back off, so the server mints that entry itself. It appears whenever Glamourer
answers the `Glamourer.RevertState` gate, and disappears with Glamourer.

Results are cached and rebuilt when the player unlocks something. Three kinds sit
outside that: `gearset` is edited rather than unlocked, so it is polled once a
second while a client is connected, `action` describes the job you are on, so
it is rebuilt whenever the job or its level changes, and `glamourer` belongs to
another plugin, so it is polled on the same second — for edits while a client is
connected, and for Glamourer itself loading or unloading either way.

`glamourer` is listed even when Glamourer is not installed, and is simply empty
then. A client that shows a type with no entries is showing the truth. A press on
a key still holding a design from before is refused with `is not in your
catalog`, the same as one whose design was deleted.

`action` holds both the job's own actions and its role actions. The role ones
carry the category `Role Actions` rather than the one the sheet gives them, so
they group together instead of scattering through the job's abilities.

### `execute`

```json
{ "kind": "emote", "id": 16 }
```

```json
{ "kind": "glamourer", "key": "10765f9e-9377-40d7-b3aa-c86d8dd41c33" }
```

Performs the entry in-game. Which field is required follows the kind's
`addressing`, not the client's preference.

```json
{ "kind": "emote", "id": 16, "name": "Wave", "usable": true, "result": 1 }
```

`usable` is the game's own view of whether the slot can fire right now (zone,
combat, already mounted). It is reported, not enforced — the game refuses and
explains far better than this plugin could.

Refused with an error response when:

| Condition | Error |
| --- | --- |
| Kind is not executable | `kind 'item' cannot be executed` |
| Kind does not exist | `unknown catalog kind 'ornament'` |
| Id is not owned or does not exist | `emote 99999 is not in your catalog` |
| Key is not in the catalog | `glamourer 10765f9e-… is not in your catalog` |
| Called again within 100 ms | `executing too fast; one action per press` |
| No character loaded | `no character is logged in` |
| Glamourer stopped between list and press | `Glamourer is not installed or not loaded` |
| Glamourer refused | its own words, e.g. `that design no longer exists in Glamourer` |

`emote`, `mount`, `minion`, `gearset`, `action` and `glamourer` are executable.
`HotbarSlotType` covers more still — items, macros — and those remain unexposed.

The first five go through a hotbar scratch slot. `glamourer` does not: it calls
Glamourer's `Glamourer.ApplyDesign` gate against object index 0 — the local
player — with the flags chosen in `/ttd`, either `Equipment | Customization`
(`/glamour apply`, the default) or `Customization` alone
(`/glamour applycustomization`). It is one setting for every design, and the
`command` a catalog entry reports follows it, so a client can show what a press
will do. What the design actually changes stays the design's own business. The 100 ms floor is
shared with the hotbar path rather than counted separately, so alternating
between them buys nothing.

```json
{ "kind": "glamourer", "key": "reset" }
```

The `reset` key calls `Glamourer.RevertState` against the same object index,
putting the character back to what it is actually wearing. It always sends
`Equipment | Customization`, whatever the apply setting says: that setting is
about how much of a design a press should reach, and an undo that left the gear
half of one on would not be an undo. Reverting a character with nothing on is a
success, not an error — Glamourer answers `result` `0` for it (measured against
1.6.1.7) and `1`, nothing done, is treated the same way. It claims the same
100 ms gate a design press does.

Entries are looked up in the catalog rather than passed through, so a client
cannot execute anything the player does not have. For actions that means the
list of the job you are on right now: a client holding yesterday's Paladin
actions gets `action N is not in your catalog` after you switch, rather than
firing something the current job cannot use.

### `icon.get`

```json
{ "iconId": 246216 }
```

```json
{ "iconId": 246216, "mimeType": "image/png", "data": "iVBORw0KGgo..." }
```

`data` is base64 PNG with no data-URI prefix. Game icons are 80x80 BGRA.
Errors with `no icon with id N` when the id does not resolve.

### `icon.getMany`

```json
{ "iconIds": [246216, 246217, 246218] }
```

```json
{ "mimeType": "image/png", "icons": [ { "iconId": 246216, "data": "..." } ] }
```

Batched because a browser page needs a deck's worth of icons at once and a
request per key would mean dozens of round trips before the page finished
drawing. At most 64 ids per request; duplicates are collapsed.

Ids that fail to resolve are **omitted rather than erroring**, so one bad id
does not cost the client the whole page. Match on `iconId`, do not assume the
response is positional or complete.

Icons are cached in the plugin after first read. Measured on a live client, a
cold batch of 32 takes about 1.1 s and a warm one about 30 ms, so fetch a page
ahead of showing it the first time.

### `status.get`

No payload. Returns the most recent status snapshot — the same shape that arrives
as a `status.update` event. Ask once on connect; after that, wait for the pushes.

```json
{
  "loggedIn": true,
  "job": { "id": 26, "abbreviation": "ACN", "name": "Arcanist", "iconId": 62126,
           "level": 28, "effectiveLevel": 27, "isLevelSynced": true,
           "experience": 33536, "experienceToNext": 61900 },
  "vitals": { "hp": 503, "maxHp": 531, "mp": 8200, "maxMp": 10000,
              "gp": 0, "maxGp": 0, "cp": 0, "maxCp": 0,
              "shieldPercent": 0, "preferred": "mp" },
  "duty": { "state": "inDuty", "dutyName": "the Thousand Maws of Toto-Rak" },
  "retainers": { "total": 3, "active": 3, "ready": 1, "soonestCompleteAt": 1785000000 },
  "cooldowns": [ { "id": 4, "remaining": 42.3, "total": 60 } ]
}
```

- `preferred` is the secondary gauge this job actually uses — `gp` for gatherers,
  `cp` for crafters, `mp` otherwise. A job without a gauge reports its maximum as
  0; do not divide by it.
- `duty.state` is one of `idle`, `queued`, `ready`, `inDuty`. `ready` means the
  pop dialog is up and waiting.
- `retainers.soonestCompleteAt` is absolute unix seconds, so a client can count
  down locally without the server pushing anything.
- `cooldowns` contains **only actions currently on recast**. An absent id is
  ready, not unknown. Most are ready most of the time, and listing them all four
  times a second would be noise.
- `job.iconId` is `62100 + job id`.
- `job.experience` is progress into the real level, never the synced one — a level
  sync caps what you fight, not what you earn towards. `experienceToNext` is 0 at
  maximum level, where there is no next level to describe; do not divide by it.

Everything except `loggedIn` may be null or empty when no character is loaded.

### `status.cooldownSources`

No payload. General actions the player has unlocked, for a picker.

```json
{ "sources": [ { "id": 4, "name": "Sprint", "iconId": 104 },
               { "id": 7, "name": "Teleport", "iconId": 111 },
               { "id": 8, "name": "Return", "iconId": 112 } ] }
```

## Events

### `status.update`

Carries a full status snapshot, same shape as `status.get`.

Sampled four times a second and sent **only when the snapshot differs from the
last one sent**. Health ticks constantly, but a 72-pixel key cannot show more
than a few updates a second, so a message per frame would buy nothing.

Sampling is skipped entirely while no client is connected.

### `catalog.invalidated`

No `id`. Cached lists were dropped; refetch what you are showing.

```json
{ "kinds": ["emote"] }
```

`kinds` names what was dropped. **An absent payload means every kind**, which is
what this event meant before the field existed — a client that ignores it and
refetches everything stays correct, just does more work than it needs to.

Sent on unlock and on login, debounced — logging in replays every unlock the
character has, and that must collapse into one notice. Kinds accumulate over the
debounce window, and one unscoped invalidation inside it swallows the rest.

## Testing without a Stream Deck

```powershell
.\tools\Test-Api.ps1
.\tools\Get-Catalog.ps1  -Kind emote
.\tools\Invoke-Entry.ps1 -Kind emote -Name Wave
```
