# Duplicate Resource Definitions and Effective Game Values

Research dates: 2026-09-08 and 2026-09-09. Evidence applies to Windows x64 build 27890, with `Darkest.exe` SHA-256 `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`.

This document distinguishes game behavior, the current editor implementation, and unverified coverage. Runtime inspection used only `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ`; it did not inject code, call game functions, or modify process memory. See the [experiment record](change-history/resource-semantics-live-2026-09-08.md).

## 1. Resolve Files Before Resolving Definitions

`modfiles.txt` controls which Mod files are eligible for discovery/opening; the correct resource directories and entry points are still required. Same-path overrides follow Mod priority while preserving that path's native enumeration slot. Effective files at different paths can still participate together in loading.

Throughout this document, "first" and "last" refer to the actual load sequence after file overrides have been resolved. They do not directly compare positions in the Mod list or merely sort the final filenames again. Many native lookups use a 32-bit ID hash; "same ID" below assumes ordinary IDs without hash collisions. These findings do not justify removing the editor's collision guards.

| Resource layer | Behavior established by this experiment and prior evidence | Conclusions this does not establish |
| --- | --- | --- |
| Quantity item `(type,id)` | Query the first matching effective definition | Every related Effect/Buff also uses the first match |
| Inventory config type (`raid`, `trinket_storage`) | Reuse the hash-keyed config; later `.max_slots` assignments replace earlier values, omission retains them | Pick the highest-priority Mod again across different file paths, or assume all inventory fields replace the complete object |
| Trinket ID | Use the first matching complete effective entry; state fields from later entries are not merged into it | A referenced Buff with duplicate definitions also uses the first match |
| Quirk ID | Use the last matching definition; see the existing quirk rules | All hero and monster attributes use the last complete declaration |
| Hero class files | Resolve standard `.info`, `.art`, and `.override` paths independently; read the effective override after info | Arbitrarily named hero files are merged by class ID |
| Hero combat skill `(skill ID, level)`, within one class | Reuse the existing skill object; tested scalars take later assignments, omitted scalars remain unchanged, and `.effect` lists append | A later declaration replaces the entire skill, or every list field appends |
| Duplicate skill ID in a monster's standard info | Tested fields behave consistently with the shared combat-skill parser | Every AI, animation, or story field follows the skill-field rules |
| Global Effect name | Reuse the existing object; tested DOT/duration fields update individually; `.disease` retains omitted values, replaces assigned values, and clears on `""` | Every Effect operation, condition, and list has been individually verified |
| Global Buff ID | A later complete Buff object overwrites the existing object | The trinket entry itself therefore also uses the last match |
| Town event ID | Retain all same-name event records; the ID lookups for executing results and reading result options select the first record | The random candidate pool is deduplicated, or duplicates cannot affect selection |
| Monster brain ID | Retain all same-name brain records; the monster binds to the first matching record | Desires from both brains are merged, or every AI subfield uses the first match |
| Repeated `death_class` lines | In this experiment, `.monster_class_id` appends weights and types; a later `.is_valid_on_crit` value overwrites the earlier value | The second line necessarily replaces the first corpse type, or every death condition is verified |
| Upgrade tree ID | A later complete tree replaces the same-ID tree; other trees remain | One whole file selected as the "best class match" should replace all upgrade trees |
| Encounter composition | Each region, difficulty, and type has its own ordered table and positional indexes | Entries are deduplicated by monster ID, or authors supply fixed composition indexes in the files |

See the prior [item/trinket experiment](change-history/item-trinket-live-2026-09-08.md) and the [upgrade-tree implementation record](change-history/hero-upgrade-trees-2026-09-08.md).

Do not apply a quantity-item filter that excludes entries lacking explicit ID references to trinkets. Loot tables can select trinkets from a rarity pool without naming each trinket ID; absence of an explicit reference does not establish that a trinket is unused. Manifest eligibility, definition validity, and class dependencies still require validation. This also does not mean that every valid trinket can naturally drop. See [trinket catalog and writes](content-save-rules.md#51-catalog-and-ordinary-inventory-writes).

## 2. Skills Combine Scalar Overrides with Effect Appends

A/B declarations used distinct values for the same level-0 Antiquarian (`antiquarian`) skill. AB, BA, and B-only each used a complete game exit and restart. Another comparison placed A in info and B in override.

| Field | A | B | Observed AB | Observed BA |
| --- | --- | --- | --- | --- |
| `.atk` | 61% | 93% | 93% | 61% |
| `.dmg` (hero tooltip) | -37% | -11% | -11% | -37% |
| `.crit` | 13% | 29% | 29% | 13% |
| `.launch` | 43 | 21 | 21 | 43 |
| `.target` | 12 | 34 | 34 | 12 |
| `.can_miss` | False | True | True | False |
| `.effect` | A, Shared | B, Shared | A, Shared, B, Shared | B, Shared, A, Shared |

When the second declaration contained only `.atk 93%`, the first declaration's crit, ranks, miss flag, and effects remained. The B-only control lacked these old values, establishing that retention came from merging duplicate declarations, rather than from the save or a previous launch's cache.

A second declaration containing `.effect ""` did not clear the first declaration's effects. When Shared occurred twice, the runtime list retained two references to the same Effect, and the tooltip also displayed duplicates. The editor must not deduplicate such a list on its own. This experiment did not use combat damage logs to verify that both references execute twice under every condition.

The info/override comparison likewise retained fields omitted by B and appended B's effects to A's effects. Same-path file replacement and a later override's field-level changes to a skill are separate stages.

The monster experiment used the new ID `ddse_probe_A`, repeated `double_slice` in its standard info, and tested omitted fields and empty effects on two other skills. Runtime scalars, rank masks, the miss flag, and effect-reference order followed the same observed behavior. Monster damage uses absolute values; the hero `.dmg` percentage format must not be applied to monsters.

Native locations: hero object-reuse branch `0x1404C5140`, monster object-reuse branch `0x1404D0F00`, and shared skill parser `0x140480CA0`. Coverage is limited to the table's fields and `.effect`. Skill modes, additional conditions, targeting mechanics, animation matching, and other fields are not automatically verified by these results.

## 3. Handle Effect and Buff Definitions Separately

For the same-name Effect `DDSE_SHARED`, A declared `.dotPoison 3 .duration 2`, and B declared `.dotBleed 7 .duration 4`.

- AB: blight 3, bleed 7, duration 4.
- BA: blight 3, bleed 7, duration 2.
- B-only: blight 0, bleed 7, duration 4.

The BA skill tooltip displayed both blight and bleed. The same Effect object was modified field by field; retaining only the last line of text and parsing it would lose data. Omitting blight and explicitly assigning blight 0 are different operations. This experiment did not exhaustively test explicit clearing behavior for other fields.

The native Effect loader `0x1404E4AD0` reuses an object when its name already exists and creates one only when that name is absent. The query branch is `0x1404E8140`.

On 2026-09-09, an effects-only temporary Mod supplied seven independent cases during a fresh Steam launch into profile_1's town. Native object reads confirmed these `.disease` states:

| Earlier declaration | Later declaration | Loaded disease string |
| --- | --- | --- |
| `"rabies"` | `"syphilis"` | `"syphilis"` |
| `"rabies"` | Field omitted; only duration changed | `"rabies"` |
| `"rabies"` | `""` | Empty |
| `""` | `"syphilis"` | `"syphilis"` |
| Field never declared | None | Empty |
| `""` only | None | Empty |
| One line repeats `.disease "rabies" .disease "syphilis"` | None | `"syphilis"` |

The setter at `0x1404E6558` passes the existing object's `+0x20F` string to reader `0x14036C950`. The helper returns without modifying it when the field is absent and copies an empty NUL-terminated string when explicitly empty. These runtime captures verify loaded definitions, not infection probabilities or combat execution. See the [integration and additional experiment record](change-history/resource-semantics-integration-2026-09-09.md).

The same-ID Buff `DDSE_DUP_BUFF` behaved differently: A was `combat_stat_multiply/max_hp/+0.11`, and B was `combat_stat_add/speed_rating/+7`. AB retained only speed +7; BA retained only max HP +11%; B-only also produced speed +7. The two stats were not combined. Native parser `0x1404A3450` uses `0x1404A4880` to locate the same-key entry, then copies the complete Buff object through `0x14042B250`.

The test definition of the existing coin trinket, displayed in Chinese as "炽热的灵魂硬币", referenced this Buff. Its BA tooltip showed max HP +11%. One equip drag did not change the equipment slot, so that action was not treated as verification of equipped HP/SPD. Effective Buff values were cross-checked against runtime objects and the trinket tooltip; complex conditional Buffs, stacking, and combat resolution remain separate questions.

## 4. Coverage of Events, Recruitment, and Monster Mechanics

### Event Records and Execution Lookups

Two effective event files both declared `ddse_recruit_probe`, with `bonus_recruit` counts of 1 in A and 2 in B. AB/BA retained two event definitions; B-only retained one. The native loader appends events. Relevant ID lookups in result execution `0x14058B390`, result options `0x14058C800`, and event startup `0x14058D080` stop at the first match.

Consequently, selecting a record randomly and executing an event result by that record's ID cannot be reduced to one simple dictionary policy. Both records may still affect random candidate weights, eligibility, and cooldowns. Observing the most recent event reward does not establish that the candidate pool was deduplicated.

### Ordinary Recruitment Flags Do Not Describe Every Acquisition Path

All Antiquarian test configurations set `.is_generation_enabled` to false, and the runtime value was confirmed as 0. The `bonus_recruit` execution branch at `0x14058B545` resolves the class, then calls hero creation at `0x14057E710` with an explicit class ID. This explicit-class branch bypasses the generation-flag filter used by the ordinary random class pool.

After completing the existing town quest "小镇 / 休息一周" (Town / Rest for a Week), AB generated 1 Antiquarian candidate. After reversing the order and completing the quest again, BA generated 2. The event UI, runtime vector, and `persist.town_event.json` agreed on counts and classes, while the ordinary generation flag remained 0. BA initially restored the previous single candidate; two new GUIDs appeared only after returning from the new quest. This rules out mistaking restoration of old save data for new generation. See the [experiment record](change-history/resource-semantics-live-2026-09-08.md) for details and cleanup results.

This actual execution and native-branch evidence covers only `bonus_recruit`. It does not establish that every Mod's quest unlocks, resurrection, special buildings, evolution, or script conditions have been simulated. Editor availability labels such as "manual only" should describe the acquisition paths currently recognized by the editor, not assert that no other path exists in the game.

### AI and Death Declarations

The A definition of `ddse_brain` gave `double_slice` weight 11; B gave `bandit_stabby` weight 29. Both brain records existed in AB/BA. The pointer bound to `ddse_probe_A` always equaled the first brain record's pointer; B-only bound B. Native query `0x140491E30` returns the first match. This establishes brain selection, not an identical duplicate policy for every desire, target condition, cooldown, or extra action.

Repeated `death_class` declaration A specified `corpse_A` and disallowed use on critical deaths; B specified `corpse_B` and allowed it. AB produced type list `[corpse_A,corpse_B]`, weights `[1,1]`, and a true crit flag. BA reversed the list and set the flag to false; B-only retained only B. The parser appends types/weights after `0x140485EE0`; crit-flag assignment is at `0x140486620`.

These results establish the death configuration actually loaded by the game. Repeated kills were not used to establish final corpse probabilities, and not every crit, DOT, corpse-clearing, summon, transformation, or story-driven death path was tested. Successful loading, resource existence, and acceptance into an encounter table do not establish that all mechanic dependencies are complete.

## 5. Index Occupancy of Invalid Encounter Rows

Track these states separately: whether the native table retains a slot, whether the editor can display the row, whether its index can be proven, whether placement is allowed, and whether combat was actually tested. One "invalid" Boolean cannot represent all of them.

An encounter index identifies a whole composition, not one monster in it. For example, `.types "monster_x" "" "monster_y"` is one composition with three raw actor slots, including the empty second slot. It consumes one encounter index. In a separate example, three consecutive retained rows containing a normal composition, `.types ""`, and another normal composition have encounter indexes 0, 1, and 2. The empty composition at index 1 remains forbidden for placement, but must be counted so the later normal composition is addressed as 2 and a Bridge append starts at 3. Blank physical text lines do not count. Raw actor slots here describe loader input; they do not prove visible empty ranks during combat.

In the tested profile_1, Weald difficulty 1 originally had hallway indexes 0–64. After appending nine test rows, the actual game table contained indexes 0–72:

| File row | Raw slot contents | Native index | Runtime result |
| --- | --- | --- | --- |
| 1 | One known size-1 monster | 65 | Retained |
| 2 | One missing ID | 66 | All four pointers were null; the slot remained |
| 3 | A normal monster and a missing ID | 67 | Normal pointer retained, missing pointer null; the slot remained |
| 4 | One size-5 monster | None | Total size exceeded 4; the native loader skipped the row |
| 5 | Two normal monsters | 68 | The oversized row did not shift this index by one |
| 6 | `.types ""` | 69 | All empty; the slot remained |
| 7 | One monster defined with size 0 | 70 | Retained in the native table; combat behavior was not tested |
| 8 | A normal monster, three empty slots, then a size-5 monster in the fifth slot | 71 | The fifth slot did not participate in parsing or the size sum |
| 9 | Three normal monsters | 72 | Final normal row's index was correct |

Actual counts from the same game session were hallway 73, room 69, and boss 4. These are row counts for the specific region/difficulty/type, not the number of bosses available across the game. The experiment added only hallway content; the other four native table types were also captured separately.

The editor before the 2026-09-09 integration could not prove hallway indexes because row 6 was entirely empty, so direct writes were also withheld for normal rows. It did not ignore that row and write shifted indexes. The implemented parser now retains explicit empty actors as non-placeable rows with native slots. Direct-write preflight, Bridge tail calculation, existing-index validation, and maintenance use the same retained sequence. Empty rows are excluded from Bridge sources and rejected even if a caller forges their placeability flag. An absent `.types` field, or a field without any value, remains unverified and guarded.

For missing-monster rows, the current editor retains a position when it can prove that position, then prohibits placement of the composition. Unknown sizes, parse failures, hash collisions, or unverified mount order defer the affected index calculations. These guards are intended to prevent shifts in later indexes.

This experiment establishes that the game loader accepts missing/empty rows and retains their slots. It did not actually trigger these test encounters. It therefore does not establish that the game fills missing units, that empty battles finish normally, or that the historical `ringleader_dead_A` case has been explained. Ignoring such slots while continuing to count would shift later indexes; this experiment did not reproduce the current editor silently writing such shifted indexes.

## 6. Editor Integration and Further Verification

The 2026-09-08 task was research-only. The 2026-09-09 implementation integrates these bounded rules:

- Hero HP analysis takes the last complete Buff definition in effective native file order. A later non-HP Buff removes the earlier HP contribution; missing/malformed HP fields and unknown conditions remain guarded.
- Hero level-0 `.effect` references append across repeated skills and info/override files, preserve duplicates, and survive explicit empty lists. UI quirk clues still deduplicate their display. Other `*_effects` lists retain their existing policy and are not claimed to share the tested append rule.
- Runtime quirk clues use the effective `.disease` field state, including omission and explicit clearing. They describe possible later gameplay grants; they do not automatically add those quirks to generated heroes. This is not a complete Effect interpreter or a general Effect-to-item dependency graph.
- Hero recruitment sources use the first same-ID event result, including an empty first result. Quantity-item analysis likewise ignores later same-ID `data` results while retaining other candidate fields; it does not simulate random event selection. Ordinary generation labels do not rule out event/script acquisition.
- Explicit empty encounters retain indexes across catalog, direct/Bridge preflight and maintenance, without becoming placeable.

The earlier upgrade-tree fix remains integrated. Manifest/file overrides, collision, monster-size and save-identity guards are retained. A single global first/last switch cannot replace these rules.

Unverified coverage still includes every Effect/skill list and condition field, all AI desires/cooldowns/action mechanics, actual death branches, all special recruitment scripts, every duplicate variant in curio interactions, and combat resolution with missing resources. These findings disprove a universal "last complete declaration wins" rule; they do not establish that the editor fully reproduces all game resource semantics.

## 7. Inventory System Capacity

The 2026-09-09 obsolete-rule audit traced the inventory config loader in the same executable identified above. This addition is static native-code evidence plus executable editor contracts, not a new in-game capacity A/B experiment.

- `0x1404C7EC0` calls `StorageManager::IO_FindFiles` with flags 0 for `.*inventory/.*\.inventory.system_configs.darkest`, then calls `0x1404C81C0` for every returned file in sequence (`0x1404C803D`). Existing native file-slot and overlay rules therefore apply.
- `0x1404C8297` reads the last `.type` string into a 64-byte NUL-terminated buffer. The key at `0x1404C82B0` is the polynomial-53 hash of the case-sensitive bytes. `raid` and `RAID` do not name the same inventory config.
- The tree-map lookup reuses an existing config when the hash exists (`0x1404C8303` to `0x1404C839F`). Only a new node is zero-initialized. This is neither first complete definition wins nor last complete definition replaces everything.
- `0x1404C83EF` calls the last-field helper for `.max_slots`. An absent field skips the assignment. A present field goes through `atoi` at `0x1404C8401` and overwrites the config's slot count at `node+0x64`.
- Within one record, `.max_slots 3 .max_slots 1` therefore assigns 1. `.max_slots +3suffix` assigns 3; `.max_slots invalid`, a quoted number, or an explicit empty value assigns 0. The editor rejects final non-positive capacities and unproven integer-overflow results. It does not revert to a preceding positive value. If no line assigns a capacity, the native default is 0 and editing is disabled.

For example, Base file `inventory/a.inventory.system_configs.darkest` sets 4. A high-priority Mod replaces that same file with 9, while a lower-priority Mod adds a distinct later file `inventory/z.inventory.system_configs.darkest` setting 3. The effective native sequence is `a=9`, then `z=3`; the capacity is 3. Mod priority selects the bytes at `a`, but does not move that slot after `z`. If `z` omits `.max_slots`, the capacity remains 9.

Both editor capacity catalogs use `InventorySystemConfigCatalog` and the shared `NativeDarkestReader`. The selected definition records the source path and SHA-256 of the same captured bytes that last assigned the field. Preview/commit still re-resolve and validate that result. Missing listed files remain overlay candidates so a missing winning file cannot expose lower-priority contents; a fully shadowed missing lower file does not invalidate readable winning bytes. Effective read failures, unresolved mount/path order, and type-hash collisions remain guarded. Config files containing a NUL byte are rejected: native LineReader stops at NUL (`0x14028E1F5` / `0x14028E1F7`), so managed text after it must not supply a larger capacity. This is a conservative rejection, not emulation of partial binary/corrupt config files.

This correction does not implement every inventory setting or runtime stack modifier. In particular, reading `.use_stack_limits` and `inventory.extra_stack_limits` as a complete gameplay system is separate from resolving the maximum slot count. The existing conservative base-stack allocation policy is unchanged. Audit decisions and validation are recorded in [obsolete-rule audit](change-history/obsolete-rule-audit-2026-09-09.md).
