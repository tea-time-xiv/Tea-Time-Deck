# Next steps

Per-page browser state is done — the visible Switch Type key owns the type and
page number in its own settings. See the browser section of
[../streamdeck/README.md](../streamdeck/README.md).

## 1. Filter Category nav key

A fourth navigation action cycling the category filter within the current kind:
All → each category → back. Useful for emotes (General / Expressions /
Special) and now minions (grouped by race). Complements the config-side
category hiding rather than replacing it — hiding is permanent, this is
per-session browsing.

## 2. More catalogs

Each is one `ICatalogProvider` plus a line in `CatalogRegistry` and, if it
should be pressable, an entry in `HotbarExecutor.SlotTypes`.

| Catalog | Sheet | Unlock check | Slot type |
| --- | --- | --- | --- |
| Fashion accessories | `Ornament` | `IsOrnamentUnlocked` | `Ornament` |
| Orchestrion rolls | `Orchestrion` | `IsOrchestrionUnlocked` | — |
| Gear sets | `RaptureGearsetModule` (live, not a sheet) | n/a | `GearSet` |
| Macros | `RaptureMacroModule` (live) | n/a | `Macro` |
| Teleports | `Aetheryte` + `IAetheryteList` | n/a | — |

Gear sets and macros are live modules rather than Excel sheets, so they need
their own change detection instead of riding on the `Unlock` event.

## 3. Status key extras

The read-only keys are built — vitals, job, duty, ventures and recast. Things
they could still grow:

- Per-retainer keys rather than one summarising the soonest.
- Gil, deliberately left out for now.
- A party-member vitals key, which needs `IPartyList` rather than the local
  player.
- Ring rendering on browser slots for entries that have a recast.
