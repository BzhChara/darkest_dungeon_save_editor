# Current town event replacement

This feature is in the unreleased source. It replaces an existing current event in town; it does not roll a future event, advance a week, add another history occurrence, or edit buildings directly. Hero generation is also restricted to town.

## Replacement policy

`persist.town_event.json` holds the current event identity, history, event-owned hero/revival offers, interaction state, costs, free-upgrade quotas and persistent multipliers. `persist.campaign_log.json` holds the corresponding weekly record. Replacement requires both records to agree, changes their existing event entries and leaves week counters and history lengths unchanged.

The old event's unclaimed offers, costs, unused quotas, interaction availability and multipliers are cleared. Existing state outside that event is retained. This includes ordinary and shard stagecoach candidates, recruited heroes, quests, inventories, currencies, retained trinkets, cured heroes, district unlocks, buildings and other settled progression. It is not limited to three named systems. An existing quest is never processed as ignored or failed because its originating event was replaced.

Ordinary stagecoach candidates have no reliable event provenance. They remain even if some originally arrived as event bonuses. Revival offers reference dead roster entries: clearing an offer does not remove the dead hero or invent a rollback of its earlier native preparation.

## Triggering and loading are different

In native Windows x64 build 27890, `StartEvent` (`0x14058F8E0`) runs initial effects (`0x14058DAF0`) and registers current-event behavior (`0x14058F060`). Loading an existing event (`0x14058CA20`) restores saved state and registers behavior; it does not replay initial effects. The editor writes the one-time state that would already exist after starting the new event and leaves later callbacks to the game.

An event-generated levelled recruit needs a complete candidate, not just increased XP. Native `AddResolveXp` (`0x1405CB8E0`) updates personal purchases and equipment/skill ranks, then fills HP. The editor reuses its candidate generator and purchase helpers for this result. An `idle_resolve_level` event for existing heroes is not applied during replacement; its later native settlement remains responsible for the upgrade.

Manual hero generation retains the editor's existing rule: explicitly selecting `shard_hungry` sends the candidate to the shard stagecoach. Native event initialization uses its own destinations: event offers stay in `bonus_hero_entries`, while ordinary coach bonuses go to `hero_recruit`. The native ordinary shop inserts its candidate before applying upgrade quirks (`0x140547922`, then `0x140547950`). The test that forces `shard_hungry` into an ordinary upgrade is a synthetic routing check, not a base-game event or a verified user Mod case.

Natural event-recruit draws use the native singleton exclusion scope: roster heroes and stagecoach candidates (`0x1405C6D90`, `0x140583E50`, `0x140549190`). Unclaimed event offers are not added to that scope. A quirk classified as `RequiresSaveContext` remains eligible when that context permits it; the native `twilight_dreamer` definition has both a positive random weight and a `singleton` tag. This is separate from the editor's advisory checks for a manually selected quirk.

Curse removal and revival use effective quirk tags, compared by native hash (`0x1405C6B00`, predicate `0x1405CF5F0`). The `vampire` tag covers all four native `disease_vampire_*` states. Revival also removes quirks tagged `singleton`, including `twilight_dreamer`. A matching quirk name without the tag is not sufficient for removal.

## Effect families

All 31 known native effect types are classified by timing. Composite events initialize each applicable effect; costs and free-upgrade quotas combine by native identity.

| Effects | Replacement behavior |
| --- | --- |
| Activity access/cost, provisioning price/amount, upgrade discounts, quest level restrictions, trinket selling | Native consumers use the new current event rules. |
| Departure, idle and activity Buffs; idle resolve levels and negative quirk removal; stress-treatment and shard Buffs | Do not execute later treatment, departure or settlement early. |
| Free upgrade quotas and event costs | Initialize unused quotas and costs. |
| Roster stress/disease healing | Enable the new unclaimed interaction; do not heal before it is claimed. |
| Heirloom multiplier and Nomad Wagon discount | Initialize the current multiplier or available discount. |
| Event recruits, stagecoach-level event recruits and quirked recruits | Generate complete event-owned offers with class/level/quirk rules, personal purchases and HP. Do not recruit them into the roster. |
| Revival | Draw dead heroes without replacement from a shared pool; native behavior refills an exhausted pool at a later effect row. Heroes stay dead until the game accepts an offer. HP uses old equipment and supported HP effects before native preparation clears buffs and resets equipment. |
| Extra stagecoach recruits | Append candidates using deck history, eligibility and purchased upgrade rules. Existing candidates remain. |
| Trinket theft | Move eligible storage copies to the recovery pool, protecting existing quest rewards and matching native saved item identities. |
| Crimson Curse removal, district unlock, bonus currency | Apply the one-time roster cure, unlock or bounded wallet change. |
| Plot quests | Generate eligible event plots with goals, rewards, modifiers and retention data. Preserve existing quests; skip already present or ineligible plots. |

Plot eligibility checks completed/dependent plots, dungeon and hero levels, infestation progress, applicable estate inventory and retained-trinket rarity/count requirements. The hero-level dependency uses the roster's saved `highest_resolve_xp` (`0x14057661C`), not a new maximum computed from living heroes. Native limits allow one new progression plot and one new retention plot per dungeon in a generation pass. Preserved old-event quests are not abandoned. Generation does not consume attempt-time inventory requirements or advance progression.

The initializer uses native eligibility rules, but does not promise the same random draw as a game session. Each preview uses a private random seed; applying that preview writes its prepared result without rerolling.

## Write protection

Preview works on private copies. Changed files must pass DSON encode/decode equality and retain their original DSON revision. Game, town event, campaign log, estate, roster, town, upgrades, quest and progression saves are guarded even when unchanged by a particular effect.

Apply rechecks the game process, source hashes and active resources, backs up all profile saves, and publishes the current event after its other changed data. A failure rolls back already replaced files using the existing guarded replacement helper; concurrently changed files are not silently overwritten during recovery. An unfinished recovery blocks another event or hero transaction. Controls remain busy until post-write synchronization completes.

## Evidence and limitations

Lifecycle research used build 27890, executable SHA-256 `4d78fbfaa65b7d002d2208d02da809200f4a5723119521180e30100d6f1a75f2`. Automated contracts cover the 31 effect types, old-event cleanup, retained delivered results, composite events, quest prerequisites, HP/purchases, DSON round trips, file-by-file failure recovery and stale-preview guards. WPF contracts cover bilingual layout, selection restoration, scene transitions and operation locking.

The optional installed-resource test initializes base-game and enabled official DLC events against synthetic saves, including all four native curse states and a tagged singleton for cure/revival checks. It never edits real profiles and is not an in-game play test. Unknown effects, unresolved dependencies and inconsistent event/history state reject preview. Revival also rejects dead candidates with equipped trinkets or unsupported conditional HP effects instead of guessing. A particular Mod may require further verified support. Real-game acceptance of every event and every Mod combination has not been claimed.

Malformed-data checks, inherited candidate HP validation, and the 4,096-item preview work bound are editor protections, not claimed native event eligibility rules. Composite and deliberately modified resource fixtures exercise native code paths; they are not evidence that those exact combinations occur in a vanilla campaign or an installed Mod.
