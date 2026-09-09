# Live Tests of Skills, Effects, Buffs, Events, and Encounter Indexes (2026-09-08)

This experiment used `profile_1` / ERROR404 to compare the game's loaded objects, UI, and saves, investigating duplicate declarations and encounter-slot occupancy. See [Duplicate Resource Definitions and Effective Game Values](../resource-duplicate-semantics.md) for the consolidated rules. No product code was modified in this task; existing working-tree changes for manifest loading, upgrade trees, and other features belong to earlier tasks.

## Environment and Experiment Isolation

- Windows x64 build 27890; `Darkest.exe` SHA-256: `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`.
- The game was launched through Steam, with a complete exit and restart for each group to prevent resource objects from a previous session affecting results.
- Only the temporary local Mod `DDSE Resource Semantics Probe` was created and placed first. SHA-256 values for six base-game and source Workshop files remained unchanged during installation, phase switches, and verification.
- As requested by the user, no new whole-profile backup was created. Installation added only the test Mod's activation record. Phase switches replaced only that Mod's files and checked that profile bytes remained unchanged. Normal quest progress was retained.
- Runtime tools used only `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ` to inspect objects located through static analysis and checked read bounds. They did not inject code, modify game memory, or call game functions.
- Temporary scripts, phase fixtures, and raw evidence are in the ignored directory `workspaces/resource_semantics_live_20260908/`. Rules and reports are archived under `docs/`; these experimental copies of game resources are not packaged.

## Comparison Design

"AB" means A precedes B in the effective load sequence. "BA" swaps their contents while retaining filenames and mount conditions. "B-only" retains only B. Another skill comparison uses info=A and override=B. Mod-list priority is not treated as the direct ordering rule for duplicate definitions in different files.

| Object | A | B | Purpose |
| --- | --- | --- | --- |
| Antiquarian level-0 `kris_stab` | Accuracy 61%, damage -37%, crit 13%, launches from rear ranks, targets front ranks, cannot miss, effects A/Shared | 93%, -11%, 29%, launches from front ranks, targets rear ranks, can miss, effects B/Shared | Determine whether scalars, ranks, and effect lists are replaced as a whole |
| `festering_vapours` | Complete A skill | Declare only accuracy 93% | Determine whether omitted fields are cleared or retained |
| `cower` | Complete A skill | Declare only empty `.effect ""` | Determine whether an empty list clears old references |
| `DDSE_SHARED` Effect | Blight 3, duration 2 | Bleed 7, duration 4 | Determine whether an Effect object is reused and updated field by field |
| `DDSE_DUP_BUFF` | Max HP multiplier +11% | Speed addition +7 | Determine whether a Buff is replaced completely |
| `ddse_brain` | `double_slice` weight 11 | `bandit_stabby` weight 29 | Determine whether both same-name brains remain and which one the monster binds |
| Monster `death_class` | `corpse_A`, crit flag false | `corpse_B`, crit flag true | Determine whether list appends and scalar overrides coexist |
| `ddse_recruit_probe` | 1 additional Antiquarian recruit | 2 additional Antiquarian recruits | Compare same-ID event execution and the ordinary class-generation flag |

The monster used the new ID `ddse_probe_A` and repeated skills in its standard info, with corresponding scalar, rank, omitted-field, and Effect-order comparisons. Absolute damage used A=2–3 and B=5–7, rather than the hero percentage format. Each pair of event, brain, and Buff definitions occupied different effective files. The class's ordinary generation flag remained disabled throughout the test configurations.

## Captured Results

| Experiment | AB | BA | B-only |
| --- | --- | --- | --- |
| Complete skill scalars | B | A | B |
| Complete skill effects | A, Shared, B, Shared | B, Shared, A, Shared | B, Shared |
| Fields omitted by the partial skill declaration | Retain A | Retain A | No old A values |
| Empty effect declaration | A, Shared remain | A, Shared remain | Empty |
| Shared blight/bleed/duration | 3 / 7 / 4 | 3 / 7 / 2 | 0 / 7 / 4 |
| Final Buff | Speed +7 | Max HP +11% | Speed +7 |
| Brain bound to the monster | First record A | First record B | B |
| Death-type list / crit flag | A, B / true | B, A / false | B / true |
| Same-name event definition count | 2 | 2 | 1 |
| Same-name event result-data order | 1, 2 | 2, 1 | 2 |

Antiquarian skill tooltips confirmed accuracy, damage modifiers, crit, and repeated effects. In BA, the test tooltip of the existing coin trinket, displayed in Chinese as "炽热的灵魂硬币", showed max HP +11%. These displays agreed with the captured resource objects.

### Actual Event Generation

The event phases increased event probability and overrode town-event probability settings only inside the test Mod. Candidates were not inserted directly into the save. The profile's existing quest "小镇 / 休息一周" (Town / Rest for a Week) triggered normal return-to-town processing.

First group, `event-AB`: the event UI displayed 1 Antiquarian candidate. The current runtime event hash was `3215555176`, represented in the save as signed value `-1079412120`. `bonus_hero_entries` contained 1 `antiquarian`, GUID 947. The ordinary generation flag remained 0 at runtime. `last_town_event_week` advanced from 50 to 51, and the activity log retained the normal quest results.

Second group, `event-BA`: loading after restart initially restored the previous single candidate. After completing the same quest again and returning to town, the UI displayed 2 Antiquarian candidates. The runtime vector and save both contained 2, with GUIDs 961 and 962, distinct from the first group's candidate. `last_town_event_week` was 52, and the same event gained another history entry. The ordinary generation flag remained 0. No candidate from either group was recruited into the roster; the roster remained at 8 heroes.

Event loading, result execution by ID, and random candidate filtering are separate stages. First-match execution does not establish that the random pool is deduplicated or that duplicate records' weights, eligibility, and cooldowns cannot affect selection.

### Actual Encounter-Index Loading

The game entered Weald difficulty 1 through normal gameplay. Reading all seven loaded native encounter tables produced counts `[73,69,4,13,9,0,0]`. The hallway table originally had 65 rows; appending nine rows in a dedicated file retained only eight:

| Raw row | Native index | Content / explanation |
| --- | --- | --- |
| 1 | 65 | One normal monster |
| 2 | 66 | All IDs missing; four null pointers, but the slot remained |
| 3 | 67 | Normal monster + missing ID; normal pointer retained and the row occupied a slot |
| 4 | None | Known total size 5; skipped by the native loader |
| 5 | 68 | Two normal monsters |
| 6 | 69 | Explicit empty `.types ""`; the slot remained |
| 7 | 70 | A monster defined with size 0; still added to the table |
| 8 | 71 | Normal monster + three empty slots; the fifth, size-5 monster did not participate |
| 9 | 72 | Three normal monsters; final index matched |

Native array positions matched each entry's stored index. A read-only C# probe called the current Core catalog loader with a Weald 1 context to check these nine rows. It did not write map or raid files; profile hashes were identical before and after. The current editor treats row 6 as unparseable and blocks direct writes for that hallway table. Row 4 is explicitly identified as skipped by the native loader. This experiment found no instance of silently discarding an occupied slot and then allowing incorrect later indexes.

These experimental indexes were not placed on the map to trigger battles. The findings establish native slot retention, not that the game fills missing units, that empty battles finish normally, or that size-0 actors function in combat. They also do not substitute for reproducing the historical `ringleader_dead_A` case in combat.

## Evidence Corrections and Unsuccessful Attempts

1. The initial Effect fixture incorrectly used `.poison/.bleed` and lacked hit-trigger fields. That capture is retained only as `AB.initial-skill-only.runtime.json` and is excluded from the DOT conclusions. After correcting the fixture to native `.dotPoison/.dotBleed` and valid trigger fields, AB, BA, and B-only were rerun.
2. The initial death-config decoder treated `+0x330` as the crit flag. Native field assignments established that this offset is `use_previous_monster_hp`; the actual crit flag is at `+0x334`. Saved raw bytes were decoded again, retaining a correction note. The tables above use the corrected results.
3. One trinket drag did not change the equipment slot and was not treated as verification of equipped HP/speed. Evidence instead comes from the definition object, native whole-object copy branch, and trinket tooltip.
4. The `-starttownevent` option also enables debug startup behavior that skips restoring and saving progress. That session was not used to verify actual profile events. Its info/override capture, taken after selecting the target profile and loading the test Mod, is used only as a resource-parsing comparison. Actual saved event results were obtained separately through normal Steam startup and quest completion.
5. Some short key presses from the Windows input tool were not accepted by the game. Actions counted as successful only when screen or data changes were observed. Steam recorded `0xC0000005` after menu exits; the same profile had this exit code before the experiment. These exits are not described as error-free, and the editor is not identified as their cause on that basis.

## Native Locations and Reproduction Scope

The following addresses are preferred-base VAs for the executable with the recorded SHA-256. Runtime inspection must apply relocation; these addresses must not be reused across versions without verification.

| Location | Purpose |
| --- | --- |
| `0x1404C5140` / `0x1404D0F00` / `0x140480CA0` | Reuse existing hero/monster skills and parse shared fields |
| `0x1404E4AD0` / `0x1404E8140` | Reuse and query Effect objects |
| `0x1404A3450` / `0x1404A4880` / `0x14042B250` | Parse Buffs, locate same-key entries, and copy complete objects |
| `0x140491E30` | Return the first matching brain |
| `0x140485EE0` / `0x140486620` | Append death types/weights and assign the crit flag |
| `0x14058B390` / `0x14058C800` / `0x14058D080` | Execute event results, read result options, and start events |
| `0x14058B545` / `0x14057E710` | Recruit through an explicit-class event and create heroes |
| `0x1404C90F0` / `0x1404CB740` | Parse the first four raw encounter positions, sum sizes, and skip totals above four |

The [official Red Hook Mod guide](https://steamcommunity.com/sharedfiles/filedetails/?id=819597757) provides background on resource directories, skills, AI, and encounter files. The first/last, field-merge, and slot-occupancy findings here come from local native analysis and actual captures; the guide is not cited as specifying these details.

## Validation and Remaining Coverage

Evidence assertions in `verify_results.py` passed for four resource phases, native encounter-slot occupancy, the editor's write guard, two actual event generations, and source-file hashes. The first assertion run found that the early BA capture lacked the desire payload. That value was neither fabricated nor skipped: a fresh `event-BA` launch with byte-identical brain files supplied the missing capture, after which validation passed. The read-only C# catalog probe was built and run.

The test Mod was removed, along with its 1 activation record, 1 historical Mod record, current event and 2 unrecruited candidates, and 2 experimental event-log records. The two experimental IDs in event history were replaced with 0, retaining history length and the positions of other events. Cleanup changed only `persist.game.json`, `persist.town_event.json`, and `persist.campaign_log.json`. All three completed encode/decode round trips; hashes of other profile files remained unchanged. No new whole-profile backup was created.

Normal quest progress was retained: the two Rest for a Week quests changed gold from 357860 to 357940 in total, and the roster remained at 8 heroes. No test candidates were recruited, and no experimental encounter indexes were written into the map.

`verify_cleanup.py` independently reconstructed the expected field-level changes in all three files, confirmed that only the experimental records described above were removed, and checked that round-trip output matched the actual saved bytes. A normal Steam restart then loaded ERROR404 into town, where the UI still showed 357940 gold and 8 roster heroes. Read-only runtime verification confirmed that the Antiquarian's ordinary generation flag was restored to 1 and level-0 `kris_stab` accuracy/crit returned to the original 85%/3%. Experimental monster, brain, Effect, Buff, and event definitions were absent; the current event ID and bonus-candidate count were both 0. Results are recorded in `restored.runtime.json`.

Local link-target and trailing-whitespace checks passed for the five task documents, as did `git diff --check`. An independent read-only review reran evidence assertions and checked key native branches and cleanup snapshots. It found no factual errors, conflicting rules, overstatements, or cleanup-safety issues in this task's documentation. Other existing product changes in the working tree were outside this review's scope.

This experiment did not exhaustively cover every Effect/skill condition or list field, all AI desires/actions/cooldowns, actual death paths, all special recruitment scripts, or combat resolution with missing resources. The rules retain explicit unverified-coverage statements for those areas. Integrating the new rules into the editor is a separate follow-up change, particularly for HP analysis, event recruitment detection, Effect dependencies, and empty encounter slots. A single global first/last switch is insufficient.
