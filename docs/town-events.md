# Current town event editing

This feature is in the unreleased source. It replaces an existing current event in town, or adds an event when none is active. Neither operation advances a week or edits buildings directly. Hero generation is also restricted to town.

## Replacement policy

`persist.town_event.json` holds the current event identity, history, event-owned hero/revival offers, interaction state, costs, free-upgrade quotas and persistent multipliers. `persist.campaign_log.json` holds the corresponding weekly record. Replacement requires both records to agree, changes their latest existing event occurrence and leaves week counters and history lengths unchanged. When the current event ID is zero, insertion appends one event-history entry and one `TownEventStartedLogItem` to the current chapter, and sets `last_town_event_week` to that chapter's week. It never increments `total_weeks`, refreshes the town, or overwrites an earlier event.

Campaign chapter keys are serialization positions, not week numbers. Native saving assigns them sequentially (`0x1405507C1–0x14055088D`), while each chapter stores its actual `chapterIndex`. An early retreat can produce multiple chapters for one week. Native event selection reads the last chapter's index, falling back to `total_weeks` only for an empty log (`0x14058B899–0x14058B8C1`); its initialization appends a `TownEventStartedLogItem` to that chapter (`0x14058EF64–0x14058EFBF`). The editor follows serialized order and actual chapter indexes. A repeated event ID does not cause earlier occurrences to be rewritten. These paths were rechecked in local build 28063 (SHA-256 `9972b79f0ea892c82bf32cb1f4cf85a8e2b30270fce219d60385c19013965ea9`) against a real profile containing repeated-week chapters.

The old event's unclaimed offers, costs, unused quotas, interaction availability and multipliers are cleared. Existing state outside that event is retained. This includes ordinary and shard stagecoach candidates, recruited heroes, quests, inventories, currencies, retained trinkets, cured heroes, district unlocks, buildings and other settled progression. It is not limited to three named systems. An existing quest is never processed as ignored or failed because its originating event was replaced.

Insertion does not require earlier campaign event records to remain present. The native `max_campaign_log_file_size` option can prune them (`0x140550C60–0x140550CA5`) while leaving the separate event history intact. Replacement still requires the current event occurrence to be identifiable and consistent.

Ordinary stagecoach candidates have no reliable event provenance. They remain even if some originally arrived as event bonuses. Revival offers reference dead roster entries: clearing an offer does not remove the dead hero or invent a rollback of its earlier native preparation.

## Triggering and loading are different

The selected event's details display its native trigger conditions from the same effective definition as its effects. This is reference information for manual editing, not a new preview or write restriction, and does not claim that the current profile meets those conditions. Conditions include minimum week, dead heroes, available heroes at or above a resolve level, purchased upgrades, stored trinkets, previous events, finished or available plot quests, infestation stages, district definitions and unlocked town buildings. Requirements combine with AND; the configured infestation stages are alternatives. Resource IDs remain visible for Mod-defined dependencies.

Native build 28063 checks event requirements at `0x14058BFA0-0x14058C51D`. A unique event must not occur in the result-event history (`0x14046FBE4-0x14046FC14`); cooldown excludes the last N result events, not N weeks (`0x14058D79F-0x14058D7DB`). The district-count condition reads the loaded district-building definition list, not the player's constructed districts (`0x14058C1CB`, `0x14058C2CE`). For example, native revival requires week 15 and three dead heroes; native trinket theft requires week 42, eight stored trinkets and no previous occurrence. Weights, priority and event frequency still affect natural selection after these conditions pass.

The engine supports event-to-event prerequisites through `requirements.town_events` (`0x14046FC1A-0x14046FC70`), but none were found in the installed base, DLC and Mod definitions inspected on 2026-10-25. This supported field must not be presented as a confirmed common Mod case. Quest unlocking is separate: actual definitions such as Crimson Court's `plot_crimson_court_0` and the installed Mod's `plot_soul_swallow_attack` use `is_started_by_event` for native weekly generation. Manual event editing only generates quests explicitly requested by the selected event's `plot_quest` effects, subject to the quest eligibility checks below.

Crimson Court has real prerequisite chains, using different fields. In its shipped definitions, `infestation_intro` supplies the `plot_quest` effect for `plot_crimson_court_0`. The Baron, Viscount and Countess quests (`plot_crimson_court_1`, `_2`, `_3`) each depend on completing the previous quest through `plot_quest_dependency`. The `return_from_CCQ1` and `countess_dead` events require completed quests `_0` and `_3`, respectively, through `requirements.finished_plot_quests`. These are event-to-quest, quest-to-quest and quest-to-event relationships; they are not examples of `requirements.town_events`. The three boss quests also require their corresponding invitation when attempted (`estate_inventory_dependency.consume_on_attempt`), which is separate from generating the quest.

The event details show the localized `Trigger conditions: ...` label and values together. Descriptions and conditions wrap within the catalog viewport; they do not widen the grid. Long details remain vertically scrollable at the minimum window size.

Hero-count requirements use a map keyed by resolve level: each later nonzero count replaces that level's previous value (`0x14046E65B-0x14046E675`). The display follows this normalization rather than accumulating duplicate conditions. The bounded available-plot reference uses replacement characters for an incomplete UTF-8 suffix, so advisory rendering cannot reject an otherwise readable event file. These are constructed parser-boundary checks, not reported defects in installed Mod definitions.

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
| Trinket theft | Move eligible storage copies to the recovery pool. Protect existing quest rewards and match native saved item identities. Do not automatically generate a recovery quest. |
| Crimson Curse removal, district unlock, bonus currency | Apply the one-time roster cure, unlock or bounded wallet change. |
| Plot quests | Generate eligible quests explicitly requested by the event's `plot_quest` effects, including Mod events, with goals, rewards, modifiers and retention data. Preserve existing quests; skip already present or ineligible plots. |

Plot eligibility checks completed/dependent plots, dungeon and hero levels, infestation progress, applicable estate inventory and retained-trinket rarity/count requirements. The hero-level dependency uses the roster's saved `highest_resolve_xp` (`0x14057661C`), not a new maximum computed from living heroes. Native limits allow one new progression plot and one new retention plot per dungeon in a generation pass. Preserved old-event quests are not abandoned. Generation does not consume attempt-time inventory requirements or advance progression.

Event editing does not run the separate weekly quest-board generation pass. A theft effect updates storage and retained trinkets without appending or refreshing a recovery quest, even when the loss count and rarity meet its generation conditions. Existing quests and earlier losses remain unchanged. If an event explicitly requests a recovery quest through `plot_quest`, it still uses the normal quest eligibility and reward rules. An existing recovery quest reserves its dungeon's recovery slot and keeps its original rewards; an ordinary Shrieker quest rewarding a dedicated trinket does not reserve that slot. New recovery rewards copy retained instances without removing them from the recovery inventory. Later event replacement preserves the generated quest. General quest-board editing is separate future work.

For reference, native weekly generation orders recovery variants by their minimum-rarity rank (`0x14045E4A0`) and permits at most one new recovery quest per dungeon (`0x140577A20`, `0x140577D6A`). Its `is_started_by_event` gate requires a matching `plot_quest` effect from an event in `result_event_history` (`0x140577BB2-0x140577C41`), taking precedence over `is_generated_by_event`. These observations describe the game's weekly pass, which the event editor does not invoke.

In the current native save format, `persist.quest.json` stores `trinket_retention_ids` as an inventory object with an `items` object, not an integer array. Native quest loading opens that node at `0x140572D33` and calls the inventory reader at `0x1405D5420`. Theft creates an amount-one trinket instance (`0x14058EE49`) and inserts it into that inventory (`0x14058EE5D`). Event quest ownership, recovery eligibility and reward ordering read its positive trinket entries. Recovery rewards copy the existing retained instances (`0x14057726C`-`0x140577280`) with their counters and other saved item state. Generating rewards does not consume these entries, and replacing the event preserves already stolen items. The contract fixtures use the structure observed in a native `profile_0` save; real-definition offline checks complement these fixtures.

The initializer uses native eligibility rules, but does not promise the same random draw as a game session. Each preview uses a private random seed; applying that preview writes its prepared result without rerolling.

## Write protection

Preview works on private copies and distinguishes insertion from replacement. Changed files must pass DSON encode/decode equality and retain their original DSON revision. The event fields `heirloom_gain_multiplier` and `nomad_wagon_trinket_discount` are explicitly encoded as floats, including when their numeric value is zero or one. Game, town event, campaign log, estate, roster, town, upgrades, quest and progression saves are guarded even when unchanged by a particular effect.

Apply rechecks the game process, source hashes and active resources, backs up all profile saves, and publishes the current event after its other changed data. A failure rolls back already replaced files using the existing guarded replacement helper; concurrently changed files are not silently overwritten during recovery. An unfinished recovery blocks another event or hero transaction. Controls remain busy until post-write synchronization completes.

## Evidence and limitations

Lifecycle research used build 27890, executable SHA-256 `4d78fbfaa65b7d002d2208d02da809200f4a5723119521180e30100d6f1a75f2`. Automated contracts cover the 31 effect types, old-event cleanup, retained delivered results, composite events, quest prerequisites, HP/purchases, DSON round trips, file-by-file failure recovery and stale-preview guards. WPF contracts cover bilingual layout, selection restoration, scene transitions and operation locking.

The optional installed-resource test initializes base-game and enabled official DLC events against synthetic saves, including all four native curse states and a tagged singleton for cure/revival checks. It never edits real profiles and is not an in-game play test. Unknown effects, unresolved dependencies and inconsistent event/history state reject preview. Revival also rejects dead candidates with equipped trinkets or unsupported conditional HP effects instead of guessing. A particular Mod may require further verified support. Real-game acceptance of every event and every Mod combination has not been claimed.

Malformed-data checks, inherited candidate HP validation, and the 4,096-item preview work bound are editor protections, not claimed native event eligibility rules. Composite and deliberately modified resource fixtures exercise native code paths; they are not evidence that those exact combinations occur in a vanilla campaign or an installed Mod.
