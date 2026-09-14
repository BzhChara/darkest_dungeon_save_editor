# Duplicate Resource Definitions and Effective Game Values

Research dates: 2026-09-08 and 2026-09-09. Evidence applies to Windows x64 build 27890, with `Darkest.exe` SHA-256 `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`.

This document distinguishes game behavior, the current editor implementation, and unverified coverage. Runtime inspection used only `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ`; it did not inject code, call game functions, or modify process memory. See the [experiment record](change-history/resource-semantics-live-2026-09-08.md).

## 1. Resolve Files Before Resolving Definitions

`modfiles.txt` controls which Mod files are eligible for discovery/opening; the correct resource directories and entry points are still required. Canonical opens select providers by Mod priority after matching the original request. Enumeration has consumer-specific flags: mode-1, flags-0 merges an alternate path into the first existing result containing its mount-relative path; Effect flags-1 and District/Curio/prop flags-9 can retain multiple same-path providers. See [the merge boundary](#21-alternate-mount-result-slot-merging-2026-09-14) and [canonical requests and query flags](#22-canonical-requests-effect-flags-1-and-district-flags-9-2026-09-14).

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
| Regional prop pool | Open the canonical region props path; each consumed type occurrence keeps the full record chance, including duplicates | Every manifest-listed props basename is loaded, or chance is divided among distinct types |
| JSON map prop | Apply file defaults, entry defaults and difficulty variations; append versions and query the first exact difficulty, otherwise nearest | One universal first/last dictionary suffices, or a future parent can be resolved retroactively |
| Curio CSV mapping | Update an existing prop in effective file order; explicit names replace, empty names retain/default | Equal-priority repeats are ambiguous, or a generic RFC CSV reader has equivalent behavior |

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

The editor before the 2026-09-09 integration could not prove hallway indexes because row 6 was entirely empty, so direct writes were also withheld for normal rows. It did not ignore that row and write shifted indexes. The parser retains explicit empty actors as non-placeable rows with native slots. Direct-write preflight, Bridge tail calculation, existing-index validation, and maintenance use the same retained sequence. Empty rows are excluded from Bridge sources and rejected even if a caller forges their placeability flag. The 2026-09-14 static follow-up also proves occupancy for an absent `.types` field and bare/whitespace-only values: the loader initializes four empty strings, skips only the optional field read when missing, and still calls AddMashEntry. Those forms now retain a non-placeable slot instead of blocking the whole type. Blank physical lines still do not count. See [native branches and query rules](encounter-runtime-order.md) and the [correction record](change-history/encounter-query-and-validation-fixes-2026-09-14.md).

For missing-monster rows, the current editor retains a position when it can prove that position, then prohibits placement of the composition. Unknown sizes, parse failures, hash collisions, or unverified mount order defer the affected index calculations. These guards are intended to prevent shifts in later indexes.

This experiment establishes that the game loader accepts missing/empty rows and retains their slots. It did not actually trigger these test encounters. It therefore does not establish that the game fills missing units, that empty battles finish normally, or that the historical `ringleader_dead_A` case has been explained. Ignoring such slots while continuing to count would shift later indexes; this experiment did not reproduce the current editor silently writing such shifted indexes.

## 6. Editor Integration and Further Verification

The 2026-09-08 task was research-only. The 2026-09-09 implementation integrates these bounded rules:

- Hero HP analysis takes the last complete Buff definition in effective native file order. A later non-HP Buff removes the earlier HP contribution; missing/malformed HP fields and unknown conditions remain guarded.
- Hero level-0 `.effect` references append across repeated skills and info/override files, preserve duplicates, and survive explicit empty lists. UI quirk clues still deduplicate their display. The later static audit also traced the bounded mode-effect lists; see section 8 for their entry conditions and append behavior.
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
- A NUL byte ends that file's readable input without discarding preceding assignments or poisoning later independent files. The common reader provides this boundary; capacity loading must not add an earlier whole-file rejection. `max_slots 2`, then NUL, then `max_slots 100` retains 2. Full file bytes still participate in fingerprints. The 2026-09-14 check traced `0x14028E1F5` to the end return at `0x14028E2D9–0x14028E2E5` and the persistent assignments at `0x1404C83EF–0x1404C8407`. See [the fix and DSON validation](change-history/case-policy-and-capacity-fixes-2026-09-14.md).

For example, Base file `inventory/a.inventory.system_configs.darkest` sets 4. A high-priority Mod replaces that same file with 9, while a lower-priority Mod adds a distinct later file `inventory/z.inventory.system_configs.darkest` setting 3. The effective native sequence is `a=9`, then `z=3`; the capacity is 3. Mod priority selects the bytes at `a`, but does not move that slot after `z`. If `z` omits `.max_slots`, the capacity remains 9.

Both editor capacity catalogs use `InventorySystemConfigCatalog` and the shared `NativeDarkestReader`. The selected definition records the source path and SHA-256 of the same captured bytes that last assigned the field. Preview/commit still re-resolve and validate that result. Missing listed files remain overlay candidates so a missing winning file cannot expose lower-priority contents; a fully shadowed missing lower file does not invalidate readable winning bytes. Effective read failures, unresolved mount/path order, and type-hash collisions remain guarded. The former whole-file NUL rejection was removed on 2026-09-14 after verifying that valid preceding assignments survive; text after NUL still cannot supply a larger capacity.

This correction does not implement every inventory setting or runtime stack modifier. In particular, reading `.use_stack_limits` and `inventory.extra_stack_limits` as a complete gameplay system is separate from resolving the maximum slot count. The existing conservative base-stack allocation policy is unchanged. Audit decisions and validation are recorded in [obsolete-rule audit](change-history/obsolete-rule-audit-2026-09-09.md).

## 8. Hero Text, Equipment Slots, Camping Pools and Item References

The second 2026-09-09 audit uses static disassembly of the same x64 build 27890 executable identified above, plus executable editor contracts. It is not another live-game A/B experiment. The complete audit, provenance and retained policies are in [legacy compatibility audit](change-history/legacy-compatibility-audit-2026-09-09.md).

### 8.1 Text fields are not a permissive dictionary language

Hero info/art/override and Effect records use the shared native record reader. Records can span physical lines or share one line. Slash and block comments are removed before strings are parsed, without quote protection or escape decoding. NUL terminates the readable text; text after it does not supply later records. Hash comments suppress record boundaries but remain in copied record bodies, as documented by the earlier reader research. Definition kinds and field searches are case-sensitive.

- Hero record dispatch is `0x1404C3860`. Generation integers call the last-field helper `0x14036B300` and `atoi`, for example class camping count at `0x1404C3F72`–`0x1404C3F91`. Skill-selection count follows the same pattern at `0x1404C40C9`–`0x1404C40EC`.
- Generation/selection booleans use `0x14036C950` followed by `0x14036B0B0`; skill `.generation_guaranteed` uses `0x14036C400` with its whitespace-check option disabled, then the same bool conversion. An absent field retains the prior value. An empty field is false, not an implied true. Accepted true strings are `t/T`, `true/True/TRUE`, `1`, `y/Y`, `yes/Yes/YES`, and `on/On/ON`; arbitrary mixed case such as `TrUe` is false.
- Integer prefixes are accepted: `2suffix` gives 2; quoted or nonnumeric values give 0. Integer overflow is outside the established supported range.
- Armor HP (`0x140486956`–`0x14048695D`) and generation card chance (`0x1404C401B`) call float reader `0x14036C270`. It selects the last field occurrence followed immediately by TAB, SPACE, `=` or NUL, parses a numeric prefix, stores a float, and multiplies by `0.01f` only when `%` immediately follows the consumed number. Thus `3100%` is HP 31, while `33suffix` is HP 33. Omitting HP retains its existing value; assigning nonnumeric HP overwrites with zero and makes that armor unusable for generation.

These are separate field helpers, not a universal instruction to take the last full resource definition. Buff, Effect, skill, item and tree object-merging rules remain distinct.

### 8.2 Skill lists have limits and explicit entry conditions

Initial equipped-skill selection is a separate consumer: `generation_guaranteed` means at least one marked skill, and an exhausted pool yields fewer skills instead of rejecting the hero. See [hero generation selection](hero-generation-selection.md) for the native branches, mounted skin directory query and current editor behavior.

`0x140480CA0` reads ordinary `.effect` into sixteen 64-byte buffers. The consumer at `0x140481550`–`0x1404815FA` stops at the first empty or dot-prefixed token, including a quoted token. Each nonempty matching Effect appends to the existing skill list. An empty list does not clear previous declarations. This differs from the encounter parser, whose empty raw monster positions must remain counted.

Mode effects are not every field whose name ends in `_effects`. The same native function first reads up to eight `.valid_modes` (`0x140481651` onward), then looks for `.<mode>_effects` for each mode named in that declaration (`0x1404817A3`). Empty/dot-prefixed mode slots are skipped, not terminal: the branch at `0x140481753` / `0x14048175B` reaches the increment at `0x140481AAB`. They still consume slots in the eight-token limit. In contrast, each mode's effect list reads at most twelve tokens, stops on empty/dot-prefixed tokens, and appends (`0x140481A36`–`0x140481A60`). A standalone `.orphan_effects` without that mode in the current declaration does not enter this branch. Missing/empty later mode-effect lists retain prior references.

The hero's `quirk_modifier .incompatible_class_ids` uses the corresponding bounded list reader with a maximum of 32 (`0x1404C463E`, wrapper `0x14028FEB0`, stop checks `0x14029002E`–`0x140290037`). These lexical/list corrections affect potential runtime quirk clues and generation constraints; they do not automatically assign skill-granted quirks to a new hero or simulate the effects in combat.

### 8.3 Equipment rank is a vector position, not a name suffix

Native weapon/armor lookup compares the authored `.name` strings and reuses the matching object. A new name appends to the class vector: weapon lookup/append is `0x1404C3956`–`0x1404C3A91`, armor lookup/append is `0x1404C3AEB`–`0x1404C3C1F`. Vector strides are respectively `0x130` and `0x128`.

The native name buffer is 64 bytes (`0x1404C3AC8`–`0x1404C3AD8`), including NUL. The editor retains authored names and keys slots by their first 63 UTF-8 bytes, without case folding or decoding a truncated byte sequence into replacement characters. Two longer names with the same 63-byte prefix update one slot. Rank follows first insertion order of these native names. `worn_coat`, `replacement_armour_0`, `Coat` and `coat` can identify four distinct slots in that order; suffix `0` does not move the second slot to the front. Later updates to `worn_coat` keep rank 0. Upgrade requirement codes still bind those slots to the effective `<class>.weapon` / `<class>.armour` trees. Numeric-name inference and its nonnumeric-name omission were removed. This is not a claim that every malformed equipment declaration or native override-only mode is fully modeled.

#### 8.3.1 Equipment purchase codes, targets and reachable ranks

Normal equipment refresh (`0x1405C7540`, x64 build 27890) formats `<class>.weapon` / `<class>.armour` through the 64-byte helper `0x14036B3A0`, then checks the corresponding personal purchases. Both catalog binding and writes therefore use the first 63 UTF-8 bytes of the constructed target. Full registered tree IDs remain unchanged. A longer full-name tree cannot substitute for a missing bounded tree; distinct IDs truncating to one target or aliasing a native hash are explicitly unsupported. Equipment/skill target overlaps are likewise restricted. This does not change the game's other automatic-upgrade callers, including `0x1405C8E86` / `0x1405C8F76`, which use the 128-byte helper `0x14036B750`.

Equipment `.upgradeRequirementCode` calls the single-byte reader `0x14036CC50` (weapon `0x1404868ED`, armor `0x1404869AF`). It finds the last exact field, skips ASCII whitespace, and reads one raw byte without unquoting: `alpha` means `a`; `"a"` means the double-quote byte `0x22`. New slots start at NUL (`0x1404C5E95`, `0x1404C5B55`). An absent update inherits; an explicit empty field writes NUL. This NUL/free requirement differs from the purchasable character `0`.

The native loop visits every slot, including slot zero, and stops at the first unpurchased nonzero code (`0x1405C76C9`, `0x1405C77E8`). Starting rank remains zero if the first slot is blocked. For ranks `0/free, 1/a, 2/b` with `a` requiring resolve level 3 and `b` requiring level 1, a generated level-1 hero may purchase `b` but still uses rank 0. At level 3 it reaches rank 2. Repeated codes can unlock multiple consecutive slots. Free upper slots can apply even at resolve level 0. Valid extra tree requirements neither invalidate the equipment nor disappear from the purchase plan.

Catalog profiles now derive ranks and armor HP from actual eligible equipment purchases; generation and save preflight recheck that the profile agrees with the same raw definitions and final purchase plan. A blocked earlier rank with otherwise eligible later slots produces a warning. Missing nonzero requirements, invalid HP, malformed winning trees and unresolved identities retain explicit restrictions. Incomplete templates may retain only a level-zero profile, but that profile must still reflect actual free/level-zero purchases. Raw equipment metadata participates in content identity even when a definition change leaves the displayed ranks unchanged. Nonfinite raw HP uses named JSON values in this metadata so fingerprinting still works for the whole catalog and distinguishes positive/negative infinity; this does not enable nonfinite HP in generated save data or change global JSON number handling.

The bundled DSON codec adds a separate save limitation: its one-byte decoder directly wraps printable chars in quotes without JSON escaping, and classifies other bytes as booleans. Purchase codes are therefore restricted to one nonblank printable ASCII character other than double quote/backslash. Definition preflight, generation and the direct writer share this guard; ordinary letters, digits, apostrophe and tilde round-trip. A quoted equipment token is still read as `0x22`, not repaired to its inner letter. Replacing the codec or declaring every native code unsupported is outside this fix.

See the [audit evidence](change-history/equipment-upgrade-consumer-audit-2026-09-11.md) and [fix validation](change-history/equipment-upgrade-consumer-fixes-2026-09-11.md). Equipment consumption is supported by pinned native disassembly and executable editor/codec tests; this round did not launch a new game experiment or migrate existing saves.

### 8.4 Camping classification comes from the first stored record

The camping loader `0x1404A4AF0` enumerates effective `raid/camping/.*camping_skills.json` files. Every file starts with threshold 0. An assigned `configuration.class_specific_number_of_classes_threshold` changes that file's threshold; omission does not inherit another file's value.

The raw JSON `hero_classes` array length is compared to the threshold at `0x1404A5F23`–`0x1404A5F3B`, using an unsigned comparison. Length at most the threshold means class-specific; greater length means shared. Duplicate/unknown class names still occupy raw array positions. The names `encourage`, `first_aid` and `pep_talk` receive no special exemption.

Every record adds its applicable classes' skill-ID access. Same-hash skill records are appended to a group, rather than merged into one classification. During generation, `0x1405C7F76` reads the hero's available skill IDs; `0x1405C7FC0`–`0x1405C7FD4` selects the first record in the matching group, then `0x1405C7FE8` reads its classification flag. Thus later records can grant the same skill to another hero without changing the first record's classification. The editor keeps that first flag and unions exact class access; it does not OR flags across all declarations. Native-hash collisions and a first record missing a valid class array remain unavailable with diagnostics.

#### 8.4.1 Camping purchase targets use a 63-byte buffer

Native camping unlock (`0x1405C7940`) and ownership query (`0x1405C7A20`) both format `<class>.<skill>` through `0x14036B3A0`, a 64-byte buffer including NUL, then hash the formatted bytes. They use requirement code `0` (`0x1405C79F0`, `0x1405C7AC6`) even when the camping definition's `upgrade_requirements` contains another code. Definition parsing alone does not determine this save-purchase key.

The editor bounds camping purchase targets to 63 UTF-8 bytes before the ordinary purchase writer computes their hash. It retains the full skill identity in the equipped-skill map and leaves generic resource hashing and the save schema unchanged. Equipment follows its separate consumption rule in §8.3.1. Catalog availability, generation and preview share this purchase-plan validation. NUL identities, a boundary cutting through UTF-8, and distinct skills truncating to the same target are explicitly unsupported. Purchase-hash collisions are also rejected in preflight as well as by the existing writer. These guards do not assert that the native byte engine rejects every such input; ambiguous targets are not silently merged. Repeated declarations/references of the same exact skill remain one purchase.

See the [native evidence and counterexamples](change-history/camping-purchase-identity-audit-2026-09-11.md) and [fix validation](change-history/camping-purchase-identity-fixes-2026-09-11.md). The verified cases cover ASCII and complete UTF-8 targets at and beyond the boundary; they do not establish support for all malformed or overlong actor IDs.

#### 8.4.2 Combat purchase targets and reachable tiers

The normal combat-level query at `0x1405C718F`–`0x1405C7215` and the guild query at `0x1406742C9`–`0x1406742DE` use the same 64-byte formatting helper as camping. Combat directory binding and explicit/implicit purchase plans therefore use the first 63 UTF-8 bytes of `<class>.<skill>`. Upgrade definition registration still hashes its original JSON ID (`0x14046FEE6`–`0x14046FF21`); it is not globally truncated. A longer full-name tree cannot substitute for the bounded query target. The original skill ID remains the key for skill variants, equipped selections and majority votes; it is never recovered by slicing the bounded tree name.

`HeroSkillPurchaseTargets` shares formatting and target validation between catalog preflight and generation. Distinct skills sharing a truncated target or target hash are unavailable, as is a computed combat target that aliases a different registered tree ID. A malformed authored winner remains unavailable instead of falling through to implicit unlock. NUL, a split UTF-8 boundary, invalid UTF-8 input and combat class/skill IDs individually longer than 63 bytes are explicit support limits; this is not a claim that the game rejects every such byte sequence. Camping's separately verified raw skill identity is not subjected to that additional combat input-length guard.

For an existing combat tree, a purchasable code `0` is required at the requested resolve level. Native `0x14058EA10` scans consecutive purchased characters starting at `0`, bounded by the skill variant count. `A/a` without `0`, an empty tree or an unmet base prerequisite cannot generate an unlocked hero. If `0` exists but a missing middle code prevents a later in-range purchase from taking effect, generation retains every eligible authored purchase and warns about the missing code and actual reachable tier. For example, `0,2` with three variants reaches tier 0 (skill level 1) and reports the gap; it does not silently add `1`. A normal delayed upgrade, shorter authored tree, or extra non-tier letter code does not force an invented skill level. Raw code case remains significant for generic trees and persistence.

The same generator drives catalog availability, preview and pre-commit purchase revalidation. Save schema, generic hashing, skill selection and the strict-majority policy for genuinely tree-less skills remain unchanged. Equipment follows §8.3.1, not combat's consecutive digit-code algorithm. See the [audit evidence](change-history/combat-upgrade-consumer-audit-2026-09-11.md) and [implementation/validation record](change-history/combat-upgrade-consumer-fixes-2026-09-11.md).

### 8.5 Manifest eligibility and actor definition lookup are separate

`modfiles.txt` still controls eligible Mod files; same-path content still follows native Mod/file-overlay order. It does not mean every listed actor definition is used from the path where its name was discovered. Hero definitions open `heroes/<id>/<id>.info.darkest`, with art/override resolved independently. Supported ASCII monster IDs open `monsters/<id minus final two characters>/<id>/<id>.info.darkest`, followed by art. Monster override files are not part of that observed branch.

Quantity-item reachability applies these canonical-path checks to actor info/art/override files too. A listed classification-folder copy can no longer prove an item/loot-table reference. Independent provision JSON and other resource classes retain their own loading rules; this check is not a universal ban on nested directories. `.darkest` reference analysis shares native comment/record/scalar reading, so block-commented references do not become roots, and quoted-only, physical-line regexes no longer omit valid bare/multiline references.

Reachability remains evidence of a potential path, not a full simulator of quest/event conditions, every Effect dependency, or runtime drop probabilities. It still preserves saved items and marks incomplete relevant scans as incomplete rather than claiming an absent reference proves universal nonuse.

## 9. Inventory Identities and Localization Keys

The native inventory and trinket lookups (`0x1404F1590` and `0x1404F6480`) compare resource hashes, not case-folded editor labels. The established UTF-8 polynomial hash distinguishes ordinary case variants. The 2026-09-09 third audit removes older normalization from the corresponding editor workflows; it does not introduce a new duplicate-definition policy or claim a new live-game experiment.

- Preserve the authored `(inventory type, item ID)` and trinket ID. Do not uppercase keys or trim quoted/JSON IDs into different strings. `ni_case`, `NI_CASE` and ` ni_case ` remain distinct when their native hashes differ. Same exact ID definitions still use the first effective entry, including its stack/state fields.
- Town storage classification recognizes exact built-in types (`gold`, `heirloom`, `shard`, `estate`, `estate_currency`). An arbitrary differently cased type is not mapped into that built-in town storage. Raid quantity entries match exact type and ID; exact `trinket` remains excluded from the quantity editor.
- Catalog aggregation, save-only residue, item/loot references, quantity refresh, selected-row refresh, instance counts and preview/commit rebinding use the same exact identity. Deleting a raid stack or reducing a town amount affects only the selected identity. Saved identifiers are read without display trimming.
- The transient inventory catalog key length-prefixes the persisted type, so a colon inside a type/ID does not create an editor-key collision. This key is not written into game saves; no save or Bridge format migration is needed.
- Continue rejecting actual native-hash collisions and unresolved providers. For example, `Az` and `BE` share hash 3567; preserving both strings does not make them independently writable. Windows file paths, source identities and SHA-256 spellings retain their separate comparison policies.
- Localization requests/results and the shared name cache use exact keys. LOC/LOC2 lookups still follow the actual binary hash, and XML attribute IDs remain untrimmed. Existing format/provider precedence, display formatting and case-insensitive search remain unchanged.

This integration concerns inventory/trinket identity and shared localization keys. It does not remove the remaining conservative identity guards in hero/quirk/Effect catalogs or establish support for every malformed, overlong or NUL-containing resource ID. See the [audit and executable validation](change-history/inventory-identity-audit-2026-09-09.md).

## 10. Map Prop Pools and Native Identities

The fourth 2026-09-09 audit traced the same executable identified above. This addition uses static native evidence and isolated executable contracts; it is not a new live-game A/B experiment. See the [audit](change-history/map-resource-rule-audit-2026-09-09.md) and [implementation record](change-history/map-resource-rules-fix-2026-09-09.md).

- `0x1404AF1EC`–`0x1404AF23C` constructs `dungeons/%s/%s.props.darkest` using the region ID twice. The editor now resolves only these canonical paths through the established manifest, Mod-priority and enabled-DLC rules. The native exclusion matches exact `arena`: `0x1404AF1DF` calls the thunk at `0x140B91D12`, whose IAT target `0x140C63378` is `strncmp` with a 64-byte limit. A distinct `ARENA` request can therefore load its own pool; Mod manifest eligibility still applies. An arbitrary extra basename or nested backup copy is not another pool. This rule does **not** change `.mash.darkest` multi-file encounter tables.
- Canonical opening is separate from directory-device discovery. For Base and enabled DLC, the editor does not apply `FindFiles` exclusions such as `_template` to a standard pool path. For example, an already registered `copy_template` region can open `dungeons/copy_template/copy_template.props.darkest`. Such files also participate in the refresh fingerprint, including same-size/same-timestamp changes; this does not relax discovery rules for other resource families.
- Calls at `0x1404AF249` and `0x1404AF512` use the shared native record reader. Records span physical lines or share a line; kinds and fields are case-sensitive. Slash/block comments are removed before string parsing, hash comments suppress record boundaries while remaining in copied bodies, and NUL ends readable input. Logs retain the physical source line plus a zero-based declaration index, rather than treating a record number as a line number.
- Map record dispatch tests a case-sensitive prefix at offset zero using `strstr` (for example `0x1404AF456`–`0x1404AF478` for `traps`). Thus `traps_extra:` joins the trap pool; `xtraps:` and `TRAPS_extra:` do not. The corresponding `room_curios`, `hall_curios`, `room_treasures` and `obstacles` prefixes follow their native branches. This is a map-consumer rule, not a global change to all record dispatchers.
- The curio/treasure and trap/obstacle readers (`0x1404AF800`, `0x1404AFC50`) use the last `.types` field and at most 64 raw string slots, each with a 64-byte buffer including NUL. Consumption stops at the first empty string. Dot-prefixed strings remain ordinary IDs in this pool reader; this differs from skill-effect lists. Repeated nonempty IDs remain repeated weighted entries.
- `.chance` uses float reader `0x14036C270`: the last qualifying field occurrence, numeric prefix, float precision, and immediate `%` scaling. For example, `50%` and `0.5suffix` both supply 0.5. Missing, negative or overflowing weights do not enter the editor's automatic pool; zero and nonnumeric-to-zero values are also excluded. This eligibility policy does not remove a valid ID from the separate explicit-choice catalog.
- The loop at `0x1404B0004`–`0x1404B003A` appends the same full chance for every consumed type. `chance 4 / types alpha beta` followed by `chance 4 / types gamma` therefore gives 4:4:4, not 2:2:4. `types alpha alpha beta` contributes twice for alpha. Display choices can be deduplicated, but automatic trap/obstacle selection preserves every eligible contribution after canonical file resolution. Curio/treasure selection remains explicit and does not roll chance.
- The callback at `0x1404B0D20` copies the raw fixed-width string; it does not trim it. The editor hashes the supported exact ID after native 63-byte truncation. `alpha` hashes to signed integer `781775590`, while ` alpha ` hashes to `-925614370`. Name requests also retain exact case and spaces. True hash collisions remain unavailable. The selected raw ID/hash is revalidated before both JSON and DSON map writes.

The editor diagnoses and omits IDs cut through a UTF-8 character at the native byte boundary. It also omits a pool with invalid UTF-8 before the first NUL, rather than inventing replacement-character identities. These are conservative supported-input limits, not claims that the native byte parser rejects every such input. Whitespace-only IDs remain unsupported. A manifest-listed missing canonical pool is retained through overlay selection; an unreadable winner blocks loading/writing rather than silently exposing lower contents. Missing fully shadowed providers do not block a readable winner. The native open-failure fallback has not been fully established, so this is documented as an editor protection.

The later fifth-audit integration below replaces the old CSV/JSON priority and ambiguity assumptions. Auto-sync still detects file-content changes without a manifest edit and rebinds current guards; preparation and commit recheck effective sources. Props continue to use hashes without Bridge activation or encounter reindexing.

## 11. Exact Buff and Quirk JSON Identities

Static analysis of the pinned executable shows raw Buff ID copying/hashing at `0x1404A3C0D`–`0x1404A3C94`, raw quirk Buff-reference hashing at `0x1404DEC20`–`0x1404DED1E`, and raw evolution-target hashing at `0x1404DF0A3`–`0x1404DF0C1`. These are identity operations, not display normalization. See the [fifth audit](change-history/hero-curio-rule-audit-2026-09-09.md) for the original failures and archived disassembly.

- Preserve case and surrounding spaces in Buff IDs, quirk Buff references, quirk IDs, incompatibility references and evolution targets. Exact same Buff/quirk IDs still take their last complete definitions. Actual native-hash collisions remain unresolved.
- Selection, refresh rebinding, pairwise exclusion, evolution traversal, singleton counting, candidate JSON and DSON use the same exact quirk ID. Only exact `shard_hungry` selects the shard recruit pool. Case-insensitive search and display sorting do not alter that identity.
- With base HP 20, Buff `NI_HP` at +10% and ` NI_HP ` at +50% are different resources. A quirk referencing the first yields HP 22, not 30. Selecting a space-bearing quirk preserves its own JSON key and HP contribution.
- Missing Buff/evolution targets, unknown HP rules, exact incompatible pairs and real hash collisions retain their guards. An unresolved Buff key cannot be deemed harmless solely because that string's own definition is non-HP: its colliding native key may name an HP Buff. Nonempty whitespace-only references remain visible to missing-reference checks; whitespace-only Buff definitions remain unsupported. Section 15 supersedes the earlier repeat-deduplication boundary: every Buff reference occurrence is now retained. Section 14 specifies the tested enum/condition identity rules.

The subsequent Buff scalar/condition and Effect/event identity corrections are specified in section 14. They extend this identity work without changing the resource-specific duplicate-definition policies.

## 12. Curio CSV and JSON Prop Consumption

This addition is static analysis of the pinned build plus isolated executable editor contracts, not a new live-game experiment. The loader `0x1404D8770` first opens the three root paths in this fixed order: `>props/prop_definitions.json`, `>props/obstacle_definitions.json`, `>props/trap_definitions.json` (`0x1404D87A0`–`0x1404D87D4`). The `>` bypasses alternate mounts: these initial defaults come only from Base, not a same-path Mod/DLC override. It then enumerates subdirectory prop JSON, subdirectory trap JSON, subdirectory obstacle JSON, curio type CSV, and curio mapping CSV using flags 9; explicit providers remain separate inputs (section 21). The root paths do not participate again as subdirectory entries. Do not apply another Mod-priority election to duplicate resource names. Enabled-DLC aliases are classified by the mounted relative path, not by absolute filesystem depth.

### 12.1 Native CSV format and field retention

The splitter `0x14038DCA0` consumes physical chunks of at most 4095 bytes, ending parsing at NUL, CR or LF. Every quote toggles quote state and is removed; commas delimit only outside quotes. There is no multiline quoted field or RFC doubled-quote escape. Only trailing ASCII spaces are removed; leading spaces and tabs remain part of the value. Mapping/type readers consume at most 16/24 columns respectively.

The mapping loader `0x1404D9CF0` discards the first physical chunk unconditionally. It clears its column buffers once per file, then overwrites only emitted columns. Rows with fewer than two emitted columns or an empty third buffer do not update a prop. A first three-column row can therefore create a mapping; a later two-column row can reuse the prior type and UI columns. Buffers reset for the next file, while existing prop objects remain.

The first column is the exact prop name; second and third set sprite and type. A nonempty fourth column replaces the UI key. An empty fourth column keeps an existing UI key (including one supplied by JSON); without an existing key it defaults to the sprite. These per-field rules replace the old equal-priority ambiguity check and blanket Mod-priority choice.

Type libraries use `0x1404D8DC0`/`0x1404D95B0` blocks. Search for exact `ID STRING` in column 3; the next physical row's column 3 supplies the raw type ID, without requiring column 5 to say `Nothing`. Each type row starts with clear buffers. After column 5 reaches `ITEM`, a subsequent nonempty column 2 ends the block and consumes that separator. A second `ID STRING` before that boundary is not a second block. The header comparisons call `strncmp` through `0x140B91D12` and are case-sensitive.

The editor supports complete UTF-8 fields within the native 511-byte payload bound. Invalid consumed UTF-8 or overlong fields fail catalog loading clearly; they are not silently skipped into a partial mapping library. Whitespace-only/NUL-containing or overlong prop names remain unsupported. This is a supported-input guard, not a claim that native unsafe buffer writes have been emulated. Interaction effects, complete type-table payloads, texture dependencies and scripted outcomes are not simulated.

### 12.2 Default data, parents and difficulty versions

`0x1404D76E0` first applies file-level `default_data` to a blank prop. Each `props[]` entry starts from that snapshot, then applies its own `default_data`. `ApplyPropData` (`0x1404D5010`) copies an already-loaded parent's default query result when `inherits_from.prop_type_name` is assigned; that whole parent copy replaces earlier defaults. Omitted fields retain their current values. Parents are resolved at the moment of loading, not from a final recursively merged dictionary.

JSON `props[].name` has a separate native registration limit: `0x1404D7CF7`–`0x1404D7D0A` copies it into 64 bytes including NUL, and `0x1404D7E63`–`0x1404D7E81` hashes the resulting name. Thus an overlong name and its 63-byte prefix register versions of the same prop, rather than two independent full-string IDs. The editor applies that byte truncation before registration and difficulty lookup; a prefix cut inside a UTF-8 sequence fails clearly. This does not apply the JSON registration limit to CSV names, Buff IDs or parent-reference fields.

Without `difficulty_variations`, an entry registers one version at difficulty -1. A present array, including an empty array, creates seven working copies and registers levels 1–7. Each variation modifies its matching working copy; repeated same-level variations update it sequentially. Native registration `0x1404D8560` appends even duplicate names/difficulties. Query `0x1404D82B0` returns the first exact difficulty; otherwise it returns the nearest, retaining the earlier entry on an equal-distance tie. Parent and CSV get/create lookups request -1. Placement queries for levels 1–7 must not mistake that default query for the universal winner.

The editor models effective `instance_type`, `ui_string`, `generate_ambush` (a string), `teleport` and `ancestor_talk` (booleans), plus the Curio mapping metadata. Kind comes from `instance_type`, not the spelling of an ancestor's name. Explicit `""`/`false` can clear inherited script flags. A script flag in an unselected duplicate definition does not reject a valid selected version. File/entry defaults, parent copies and repeated variations share these semantics.

The shared catalog has no selected quest difficulty, so trap/obstacle placement remains conservatively unavailable when any selected version among levels 1–7 has the wrong kind, an unresolved parent/relevant field, or active supported script flags. Curios require a valid mapping/type in every selected version too; display names use the default lookup. Collisions include the loaded resource library and inherited dependencies, even when the colliding sibling is not in a regional pool. Invalid variation levels/arrays and unreadable resource files fail loading rather than expose earlier partial data. This is bounded metadata validation, not a full JSON-property or gameplay interpreter.

See the [implementation and validation record](change-history/hero-curio-rules-fix-2026-09-09.md). Shared manifest rules, encounter numbering, Bridge maintenance, map transactions and automatic-refresh scheduling are unchanged.

### 12.3 Quantity-item references use curio consumer columns

Since 2026-09-10, the quantity reference analyzer and map catalog share the physical-row and type-block reader. Only effective `curios/...curio_type_library.csv` files, including eligible enabled-DLC paths, enter this CSV reference consumer. CSV is decoded from raw bytes using strict UTF-8 without text-reader BOM autodetection; a UTF-16 BOM must not convert an otherwise unsupported byte stream into valid consumer rows. A listed `notes.csv`, a localization cell, an arbitrary typed-item string outside an interaction, or text before a type block cannot prove reachability.

Native `0x1404D95B0` processes default result rows before `ITEM`: column 5 must be exact `Loot`, and column 6 must have a nonzero native integer-prefix weight. Numeric CSV cells use their own integer prefix directly: `0.count 1` and `0.weight 1` are zero, not a darkest field lookup ending at the later `1`. After `ITEM`, `0x1404D93A0` requires nonempty columns 5/6, copies the item input from column 5 to 255 bytes and splits its first `#` into ID/type; no suffix means `supply`. Only result kind `Loot` in column 6 adds loot-table roots. Registration of the item input precedes result validation. These are one-based column numbers.

Loot helper `0x1404DA1C0` consumes code columns 8/11/14. The first code is copied at least once even with count 0; the second/third need their full three-column group and a positive count in the following column. Repeated codes are joined with `&` into one 64-byte buffer. `CurioInteractionLootResult` construction at `0x1404A835F`–`0x1404A83DF` splits that buffer on `&`, skips empty tokens and copies each code to 31 bytes plus NUL. The editor applies those token identities before following the existing loot dependency graph. Combined values beyond 63 bytes, unsupported integer overflow and incomplete UTF-8 are reported as analysis-incomplete, not recognized as untruncated references or hidden as unused. This is reference evidence, not simulation of every curio outcome, eligibility condition or gameplay effect.

## 13. Record boundaries and persisted upgrade identity (2026-09-10)

`LineReader::ReadNextLine` (`0x14028E210`, checks `0x14028E239`–`0x14028E25C`) starts at its current cursor, skips native whitespace/hash lines, reads the declaration letters/underscores, and requires an immediately following colon. An invalid current header ends the file's resource loop; it does not search the remaining text for a valid-looking declaration. `BROKEN\ninventory_item: ...` therefore does not define an item. Record boundaries follow the next native declaration delimiter, not physical newlines. Comments and NUL retain the rules in section 8.1. The common reader now enforces this for its consumers, including encounters. Non-advancing malformed delimiters fail explicitly; this does not claim emulation of arbitrary unsafe native record-buffer overflows.

Upgrade tree selection still uses the last exact tree definition. Purchase persistence must hash the **exact purchase target** and keep the same requirement code selected by generation, without `Trim()`. A registered tree `hero.skill ` is different from `hero.skill`; constructed skill query targets additionally follow section 8.4's native byte boundary before binding and writing. Stagecoach generation and JSON/DSON purchase writes retain those identities; real hash collisions, duplicate purchases, unsupported codes and missing dependencies remain rejected. No change is made to skill/Buff/quirk merge rules or the recruitment pool policy.

See [the six-finding correction record](change-history/encounter-consumer-fixes-2026-09-10.md) and [encounter numbering](encounter-runtime-order.md) for the connected catalog, Bridge and map validations.

## 14. Buff scalar/condition identities and resource eligibility (2026-09-10)

These additions use static analysis of the same pinned build and executable editor contracts, not a new live-game session. [The audit](change-history/hp-reference-rule-audit-2026-09-10.md) records the counterexamples; [the correction record](change-history/hp-reference-rule-fixes-2026-09-10.md) records the implementation and validation.

- **Buff numbers:** `0x1404A3D62` converts JSON amount from double to float; `0x1404A4067` does the same for `rule_data.float`. Store these rounded values before HP classification, generation or condition checks. `-0.99999999` is native `-1` and cannot bypass the non-positive-HP guard; float overflow is not a usable finite HP/threshold value. This change does not assert that every later native HP operation has been reproduced bit for bit. The editor retains its existing formula and preview arithmetic; candidate serialization rounds final HP to a DSON float and preserves a floating-point JSON marker. Final HP outside the finite float range is rejected before generation. Java 8 and newer Java can print different decimals for the same float (`9.9999998E10` / `1.0E11`), so a DSON town round trip compares only the newly generated candidate's `current_hp` by exact positive finite float bits, then normalizes that one field in a comparison clone. All remaining town data, roster/upgrades and plain-JSON source saves retain strict full-document equality; existing heroes' HP is never normalized. This introduces no numeric tolerance and changes neither save structure nor codec/transaction behavior.
- **Buff strings:** stat/rule names and rule strings use 64-byte buffers, preserving case and whitespace, stopping at NUL and keeping at most 63 UTF-8 bytes. An incomplete UTF-8 result remains unverified rather than being converted to a replacement-character identity. `combat_stat_add`/`combat_stat_multiply` subtypes are checked against the ten native ActorCombatStat hashes initialized at `0x140068E60`; this subtype validation is not applied to unrelated Buff families. Unknown HP operations or rules remain unverified. No uppercasing, lowercasing or trimming makes an invalid enum valid.
- **Mode conditions:** runtime `in_mode` compares the actor's current mode hash with the Buff rule-string hash (`0x140473CFE`–`0x140473D0B`). Enumerate conditions by these hashes, including collisions that actually represent the same native condition. `Mode`, `mode` and ` Mode ` have different hashes and cannot cancel each other's HP changes. Rule thresholds use adjacent float values for boundary checks. Runtime-only conditions remain inactive during stagecoach creation.
- **Effect/event identities:** candidate groups and downstream runtime-quirk signal deduplication preserve exact ID case. Effect disease retention/clearing and the first event result rule remain unchanged. The event parser hashes the raw JSON event ID at `0x14046BC50`–`0x14046BC71`; the copied display buffer does not justify folding distinct IDs together. Actual native hash collisions remain unresolved and diagnosed.
- **File consumption:** the loot library searches `loot/` with `.*loot.json`; town-event loading searches `campaign/town_events/` with `.*campaign/town_events/.*\.town_events.events.json`. `loot/notes.json`, `heroes/audit.loot.json` and `campaign/town_events/audit.events.json` do not become these resources merely by being listed in a manifest. The unescaped dots also admit names such as `.lootXjson` and `.town_events.eventsXjson`; discovery, JSON parsing and content-fingerprint refresh must include them instead of requiring a literal `.json` extension. Nested eligible files, original/DLC sources and enabled-DLC paths inside a Mod use the same consumer rules. Town-event settings and quest-type guarantee files are consumed separately by the game, but are not item-reference roots; they do not supply currency result payloads. Missing-file diagnostics apply the same filters. Same-path provider priority is unchanged.
- **Uncertain references:** text tokens recovered from malformed root JSON enter a separate uncertain loot-root graph. Propagate them through nested tables into `AnalysisIncomplete`, including their diagnostic evidence; never promote them to confirmed active roots. An independent valid root still produces `ConfirmedActive`. Preserve normal visibility for saved items and incomplete analysis; do not infer unused content from a failed scan.
- **Encounter weights:** `.chance` now uses shared `NativeDarkestReader.ReadFloat` with consumer default zero. Recognized post-key boundaries are NUL/end, TAB, SPACE and `=`. Keep the last recognized field, numeric prefix, float conversion and float percent multiplication. An empty final field overrides an earlier usable value with zero; `1e-50` also becomes zero. Weight affects classification/ordinary filtering, not slot occupancy. A zero-weight valid formation remains directly addressable, and a following Bridge row uses the next index.

## 15. JSON members, Buff occurrences and structured item roots (2026-09-10)

This section uses the pinned build's static consumer paths and isolated executable editor contracts. It is not a new live-game experiment. See the [eighth audit](change-history/json-reference-rule-audit-2026-09-10.md) and [implementation record](change-history/json-reference-rule-fixes-2026-09-10.md).

### 15.1 JSON lookup is a separate layer

Native JSON parsing (`0x14024CF60`) retains members in order; member lookup (`0x14028E980`) returns the **first exact member name**, even if that member has the wrong type. It does not fall through to a later valid value. `NativeJsonReader` now supplies this rule to hero Buff/quirk, evolution, camping, upgrade, roster-threshold and recruit JSON consumers and the item-reference reader. Repeated root arrays follow the same lookup. This changes neither the last complete Buff/quirk/tree definition policy nor the last-field text rule for `.darkest`. Save JSON handling is separate.

A quirk's `buffs` value is an ordered list of occurrences, not a set. `0x1404DEC20` appends each resolved reference; `0x1405C32D0` / `0x1405C12D0` / `0x1405B8190` create separate actor instances; `0x140473570` and `0x1404799A0` process all active instances. With base HP 20, one +25% reference gives 25 and two identical references give 30. Two -75% references now reach the same non-positive-HP guard as two different Buff IDs with those amounts. Exclusion and tag collections may still deduplicate.

### 15.2 Town-event payload identity

Event `data[].type` uses a 64-byte C-string buffer and a case-sensitive native hash (`0x14046D426`–`0x14046D4B6`). `string_data` is hashed directly through NUL (`0x14046D725`–`0x14046D741`), without applying the type buffer's 63-byte payload limit. No trimming or case folding is allowed. Recruitment class grouping also uses this hash, matching `0x14058B575`; correcting only the field reader would still let later case-insensitive grouping bind the wrong class. The numeric recruit count takes the first member and the native float conversion.

Only `bonus_currency` and `event_cost` payloads establish the corresponding town currency references. `BONUS_CURRENCY` is not that enum. For item roots, only `events[].data[]` of the first matching event result is consumed; arbitrary root `notes`, nested `cost`, or a made-up `loot_table_code` field do not supply a reference. This does not simulate all event eligibility or execution conditions.

### 15.3 Item JSON roots require both a loader query and a consumed structure

Manifest membership and same-path provider election happen first. The item-reference reader then uses these bounded consumers:

| Mounted root / filename query | Supported reference structure | Context |
| --- | --- | --- |
| `campaign/provision/.*provision.json` | Length/store array-of-arrays; `raid_starting_hero_class_item_lists[].item_lists[]` | Raid |
| `campaign/estate/.*estate.json` | `currencies[].id` | Town |
| `campaign/quest/.*quest.plot_quests.json` | `plot_quests[].quest.completion_reward.items_definition.items`, threshold reward inventories, estate inventory dependency; `plot_quests[].additional_provisions.items` | Rewards/dependencies town; provisions raid |
| `campaign/quest/.*quest.generation.json` | `generation.rewards.item_table` and heirloom amount table | Town |
| `campaign/quest/.*quest.types.json` | `goals[].starting_items[]` | Raid |
| `campaign/town/districts/.*districts.json` | `buildings[].currency_cost[]`; recognized supply/replacement `buff_list[]` fields | Cost/estate target town; provision/replacement raid |
| `upgrades/**/*.upgrades.json` | `trees[].requirements[].currency_cost[]` | Town |
| Native event filename query from section 14 | First event result's recognized currency payloads | Town |

Evidence includes provisioning `0x1404503C0`, estate `0x14044CDA0`, quest types `0x140455680`, plot quests `0x140458980`, generated quests `0x14045CC90`, districts `0x140559E60`, and building query formatting `0x140536860`. Native unescaped dots are preserved by the query rules, so names such as `.questXplot_questsXjson` remain eligible. The shared refresh fingerprint covers these names; it intentionally includes some files needed only by other catalogs.

Inventory dictionaries are read at numeric slot keys, not by recursively matching arbitrary child objects. A repeated member keeps its first value. `system_config_type`, a `type/id` pair, or a loot code in an unrelated object no longer creates a gameplay root. `campaign/quest/notes.json` and matching quest/event files containing only `notes` do not prove use. Missing or malformed **eligible** files still retain analysis-incomplete safeguards; unconsumed filenames do not poison analysis.

The native district supply structure uses `item_type`, `item_name`, and `target_inventory`; it is not a generic loot-table container. Nested loot traversal remains supported through real actor and curio roots. For known loaded structures whose item consumer is not established (building-specific data/requirements, goal-specific data, quest modifiers/restriction/exit-penalty content), textual clues stay **uncertain**, never confirmed. These are coverage limits, not proof that the game cannot use the resource. No full mission/event trigger simulator or new blanket normalization rule is introduced.

## 16. Trinket and map JSON members; catalog file queries (2026-09-10)

The ninth audit extends the first-member rule to the remaining trinket and map-prop readers. Evidence is static analysis of the same pinned build 27890 and executable editor contracts, not another live-game A/B experiment. See the [audit](change-history/resource-json-query-audit-2026-09-10.md).

### 16.1 First members in trinket and map resources

`Trinket::Library::LoadEntriesFile` (`0x1404F2E40`) takes the first exact JSON member. This applies to `entries`, entry IDs, class requirements, rarity, limit, price and instance counters, as well as the editor's provider attribution. Within one object, `quest_uses: 2` followed by `quest_uses: 7` initializes `quest_uses_remaining` to 2; the analogous first `trigger_limit` initializes `triggers_remaining`. A wrong-typed first optional counter retains the native default `-1`; it does not block creation or use a later duplicate. Zero is supported. See section 23 for numeric flags and signed/omitted values. This is separate from choosing the first complete trinket entry for repeated definition IDs, and from the existing quantity-limit warning policy.

Map prop parsing (`0x1404D76E0`, applying data at `0x1404D5010`) uses the same first-member lookup for root arrays/defaults, names, nested inheritance references, script flags, difficulty arrays and variation levels. Repeated names are retained in the parsed JSON member list rather than inserted into a unique-key dictionary. Thus `instance_type: "trap"` followed by `instance_type: "obstacle"` uses `trap` without a duplicate-key exception. Wrong-typed first fields retain the relevant unsupported-metadata or structural-error guard. Repeated resource names and separate same-level variation objects still follow section 12's registration/update rules; they are not deduplicated as JSON members.

### 16.2 Match the query of each consumer

After manifest eligibility and mounted-path resolution, these catalog queries govern file consumption. Native unescaped dots match a single character; they are not literal filename suffixes.

| Family / query root | Native pattern | Relevant distinction |
| --- | --- | --- |
| Trinkets / `trinkets/` | `.*trinkets/.*\.entries.trinkets.json` | The leading dot is literal; later dots are not. |
| Buffs / `shared/buffs/` | `.*shared/buffs/.*\.buffs.json` | `z.buffsXjson` is accepted; `zXbuffs.json` is not. |
| Quirks / `shared/quirk/` | `.*quirk_library.json` | Do not replace the query with a literal `.json` suffix. |
| Camping / `raid/camping/` | `.*camping_skills.json` | Both `camping_skills.json` and `x.camping_skillsXjson` are accepted. |
| Nested prop definitions / `props/` | `.*props/.*/prop_definitions.json` | Nested query stage 3. |
| Nested traps / `props/` | `.*props/.*/trap_definitions.json` | Nested query stage 4. |
| Nested obstacles / `props/` | `.*props/.*/obstacle_definitions.json` | Nested query stage 5. |

Call sites are `0x1403E8333`, `0x1404A32BF`, `0x1404DDC5F`, `0x1404A4B5A`, and the three nested searches in `0x1404D8770`. The three root prop paths remain exact direct opens in prop/obstacle/trap order, stages 0–2; a root `props/trap_definitionsXjson` does not become valid through the nested query. Upgrade trees retain `.*upgrades/.*\.upgrades\.json$`, with both dots literal. These differences must not be flattened into one universal filename rule.

The shared `NativeResourceFileRules` supplies discovery and missing-file eligibility; map stage sorting uses the same classification. The rules cover Base, mode, enabled DLC, local/Workshop manifests and enabled-DLC paths inside Mods. Unlisted Mod files, disabled DLC paths and unrelated files remain excluded. Same-path Mod priority and each resource's duplicate-ID rule are unchanged. Existing content fingerprints include these consumed `*json` names and invalidate stale map choices after a resource changes.

Contracts verify the consequences through persistence: trinket instances retain the first counter values through DSON, and a later consumed +75% Buff replaces an earlier +25% Buff, yielding a stagecoach candidate with HP 35 from base HP 20, also verified through town DSON. Map contracts check first-member inheritance/difficulty behavior, root-before-nested stage ordering and stale-choice rejection. This does not establish full simulation of every resource effect, script or native malformed-input behavior.

## 17. Inventory, Effect, Curio and encounter file queries (2026-09-10)

The tenth audit found remaining literal-suffix filters and a broad legacy reference exclusion. The following queries come from static analysis of the same pinned build 27890. Their editor consequences are covered by executable contracts, including encoded save writes; this is not a new live-game test. See the [audit](change-history/resource-discovery-audit-2026-09-10.md) and [fix record](change-history/resource-discovery-fixes-2026-09-10.md).

For enabled local/Workshop Mods, a path must first be listed in `modfiles.txt`, be in an enabled mounted root, and satisfy the relevant consumer query or canonical direct-open path. A listed README, note, unrelated configuration or asset is not automatically a parsed item, hero, curio or encounter. Missing-manifest preparation remains the existing separate load step. Base, mode and official DLC use their corresponding game roots; they do not require a Mod manifest.

| Family / query root | Native pattern | Example accepted by the query |
| --- | --- | --- |
| Inventory items / `inventory/` | `.*inventory/.*\.inventory.items.darkest` | `inventory/a.inventoryXitems.darkest` |
| Inventory capacity / `inventory/` | `.*inventory/.*\.inventory.system_configs.darkest` | `inventory/z.inventory.system_configsXdarkest` |
| Effects / `effects/` | `.*\.effects.darkest$` | `effects/a.effectsXdarkest` |
| Curio types / `curios/` | `.*curios/.*curio_type_library.csv` | `curios/a_curio_type_libraryXcsv` |
| Curio mappings / `curios/` | `.*curios/.*curio_props.csv` | `curios/a_curio_propsXcsv` |
| Standard encounters / `dungeons/<region>/` | `.*<region>.<difficulty>.mash.darkest` | `dungeons/cove/a.coveX2.mash.darkest` |

Inventory calls are `0x1404C7FE4`/`0x1404C7FFB` (capacity) and `0x1404C808B`/`0x1404C80A2` (items); Effects use `0x1404E4965`/`0x1404E497D`; Curio queries are `0x1404D8A22` and `0x1404D8ACE`. MashGuide constructs its region/difficulty query at `0x1404C91D8` and searches at `0x1404C9239`. Unescaped dots match one character, whereas the escaped leading inventory/Effect dot is literal. These filename queries are case-sensitive. README filtering is a consequence of resource eligibility, not a universal filename blacklist.

Encounter discovery, current-table selection, direct-write checks, Bridge append planning and maintenance share the same description of the matched region/difficulty. Difficulty is the canonical decimal token for that region query, not an arbitrary trailing run of digits: `c.cove22XmashYdarkest` can match difficulty 2, with the first `2` consumed by the wildcard separator. `cove.02.mash.darkest` is not the difficulty-2 query. Conditional/additional collections remain separate. Effective file order, independent hall/room/boss counters, empty retained slots, unsupported size guards, DLC-index guards and existing Bridge bindings retain their established rules.

Item references now exclude definition-only directories by the mounted resource root, rather than any matching folder segment. A listed `loot/inventory/a.loot.json` remains a loot file under the recursive `loot/` consumer, just like `loot/rewards/a.loot.json`. This does not remove unused-item analysis or treat arbitrary nested JSON/notes as real references. Curio type files accepted by their query also use the native CSV reference parser even when their extension is not literally `.csv`.

For actor item/loot references, the existing canonical info/art/hero-override path checks also reject other text filenames in `heroes/` and `monsters/`. A listed `heroes/inventory/README.darkest` cannot establish use merely by containing an example item record; its absence cannot make reference analysis incomplete either. Conversely, `heroes/inventory/inventory.info.darkest` and `monsters/inventory/inventory_A/inventory_A.info.darkest` remain valid reference sources despite their folder name. This is the supported actor item-reference consumer boundary, not a claim that the game never loads other actor assets or AI files.

Candidate enumeration, missing-file diagnostics, resource dispatch and pre-write revalidation use the same predicates. Catalog fingerprints include consumed `*darkest` and `*csv` names, so same-size/same-timestamp changes without a manifest edit invalidate the catalog; the fingerprint is intentionally a broader change detector, not proof that every inventoried file is consumed. Refreshing does not change same-path Mod precedence, item first-match definitions, capacity later-field updates, Effect field semantics or Curio stage/registration semantics.

The new contracts cover Base, mode, enabled DLC, local Mod, Workshop Mod and enabled-DLC Mod aliases, plus unlisted/disabled/unrelated/missing files and same-path overlays. Actual DSON checks cover capacity-limited inventory stacks, all three encounter types with direct replacement and Bridge append, subsequent deletion, and stable maintenance after unchanged bindings. This does not establish complete resource dependency or combat-script simulation.

## 18. Reference context, actor records and directory devices (2026-09-11)

The eleventh audit's fixes distinguish three independent decisions: file eligibility, native record consumption, and reference context. They preserve the manifest-only Mod policy and all resource-specific duplicate-ID policies. [Audit](change-history/resource-reference-and-region-audit-2026-09-11.md); [implementation/validation](change-history/resource-reference-and-region-fixes-2026-09-11.md).

1. **Reference context follows the mounted consumer root.** Strip an enabled DLC prefix using the active mount inventory, then use anchored resource roots. A Curio CSV under `curios/upgrades/` or `curios/campaign/town_events/` is a raid source, just like one under `curios/rewards/`. A hero called `upgrades` does not become a town consumer. Source paths in evidence remain the original authored paths. Context-sensitive missing-file, unreadable-file and uncertain-JSON diagnostics use this same mounted path.
2. **Actor fields need a consumed record.** HeroClass's dispatcher (`0x1404C3860`) recognizes `extra_battle_loot:` and `extra_curio_loot:`; MonsterClass uses `loot:`. These records feed the existing Loot graph. `.type/.id`, `.item_id` or `.use_item_id` inside an unknown actor record do not prove item use. The existing native record/comment reader and last-field lookup remain in place. Hero starting items, quest provisions/rewards and district item consumers still go through their existing JSON paths; this fix does not generalize the actor whitelist to arbitrary non-actor resources.
3. **Directory access and filename queries are different operations.** MashGuide passes a requested table ID into its case-sensitive filename query. Physical Base/mode/DLC enumeration uses the Windows directory device, so a `Cove` directory can satisfy a `cove` directory request without making a `COVE` filename match that table. Local/Workshop manifest insertion (`0x1403EBE30`, `0x140239251`) and lookup (`0x1402397E0`) hash original directory bytes; there is no Windows case folding in that tree query. A lowercase manifest path may subsequently open a differently cased physical folder through Windows. Do not apply either device's rule universally to the other. Custom table IDs retain their case. Existing case-only overlay collisions and unproven DLC-order guards remain.

The native evidence is from the pinned Windows build 27890 and the executable checks use isolated source/profile fixtures. It is not a new live-game experiment or a proof of all platform/filesystem behavior. Both successful reads and refresh/preflight/maintenance must use the same accepted table inventory so that an ignored file never shifts an index and a consumed file never disappears from its predecessors.

## 19. Manifest directory queries and actor registration (2026-09-11)

The same directory-device distinction now applies to the other resource queries, not only encounters. Mod queries for `inventory/`, `trinkets/`, `shared/buffs/`, `shared/quirk/`, `effects/`, `raid/camping/`, `curios/`, nested `props/`, and the existing reference JSON/Loot consumers compare the authored manifest directory bytes. `Shared/buffs/` and `shared/Buffs/` do not satisfy `shared/buffs/`. Enabled DLC prefixes in these queries also retain their case. Base/mode/official DLC use Windows directory matching. A correctly cased manifest path can still open an uppercase physical folder or filename on Windows; this does not change the raw manifest query. Filename expressions remain consumer specific and case sensitive.

Apply raw manifest eligibility before case-insensitive physical-path deduplication. A rejected `Loot/query.loot.json` must not erase a later valid `loot/query.loot.json`. Both listing orders must yield the same accepted consumer; the rule also applies to Curio/JSON references and DLC-prefix aliases.

Actor registration uses different native expressions: heroes `.*\.info.darkest$` (`0x1403E7425`), monsters `.*\.info\.darkest$` (`0x1403E7ADE`). Uppercase `.INFO` or `.DARKEST` is not a discovery match. Native discovery removes the final dot suffix twice and then removes the directory: a hero seed `query.seed.infoXdarkest` registers `query`. Monster discovery escapes both dots and rejects that seed. Once an ID is registered, canonical info/art/override opening is a separate operation. A differently cased physical filename may supply that actor, but a Mod must first list the exact constructed request in its manifest. An uppercase-only manifest key cannot supply the lowercase request merely because another seed registered the actor. See section 22.

Hero generation and trinket hero requirements use the same registration query. Item-reference analysis only consumes canonical files for registered actors, including a canonical physical path excluded from discovery when another valid seed registers the ID. Unknown actor records still do not create loot roots. Monster existence, size, Boss classification, direct/Bridge placement and maintenance use the same discovered IDs and canonical definitions.

Example: the only alpha definition is `alpha_A.info.DARKEST`, declaring size 3. An encounter containing two `alpha_A` references retains index 0 because alpha was not registered; a following valid bravo encounter uses index 1, and Bridge appends at 2. Do not classify alpha as size 3 and remove its six-slot encounter. If a Mod manifest is changed to the lowercase filename, Windows can open the existing physical uppercase file: alpha then registers, the oversized encounter is skipped, bravo becomes 0, and append becomes 1. Content fingerprints and preflight guards invalidate choices made before that registration change. Missing/empty slots and known oversized rows otherwise retain their established semantics.

Canonical actor opens, the three direct root prop opens, roster variables, and broad diagnostic/refresh inventories are not converted into case-sensitive directory searches. Same-path priority, resource duplicate-ID policies, gameplay mechanisms and save layout are unchanged. [Fix record and validation](change-history/resource-directory-and-actor-query-fixes-2026-09-11.md).

## 20. Hero upgrade filenames and mounted roster variables (2026-09-11)

Upgrade-tree definition discovery and upgrade item-reference discovery share the same file predicate. Native `0x1403E8883` queries `.*upgrades/.*\.upgrades\.json$`: both suffix dots are literal and filenames are case-sensitive. `x.upgrades.JSON`, `x.UPGRADES.json`, `xXupgrades.json` and `x.upgradesXjson` do not supply trees. Mod directory eligibility uses the raw manifest directory tree; Base/mode/official DLC still use Windows directory lookup. A lowercase manifest entry can open a physically uppercase filename on Windows. An ineligible uppercase entry appearing before that valid entry must not erase it during deduplication. The per-tree last-match rule, native enumeration slots, purchase codes and invalid-tree/hash guards remain unchanged.

Roster variables use a canonical open of `campaign/roster/roster.variables.json` (`0x140468711` / `0x14046877B`). Match that original root request against each Mod's exact manifest key, then select the highest applicable provider. Official DLC directory devices can supply their physical root-relative file through Windows lookup; a Mod's `dlc/<feature>/campaign/roster/roster.variables.json` does not answer the root request. Preserve the actual selected source for diagnostics. Nested namesakes and unlisted files do not qualify. Invalid winning thresholds retain the level-zero-only fallback and do not expose an earlier valid provider. Physical Windows aliases remain valid after manifest matching; see section 22.

These rules drive the generation-level choices, generated XP, equipment ranks, initial HP and personal upgrade purchases. They do not rewrite existing saved heroes. [Fix record and validation](change-history/hero-progression-resource-fixes-2026-09-11.md).

## 21. Alternate-mount result-slot merging (2026-09-14)

Pinned build 27890's `0x140247970` takes a distinct branch for return-path mode 1, flags 0. After stripping the new path's alternate-mount prefix (`0x140247B9C`–`0x140247BDC`), it scans existing result paths in order using `strstr` (`0x140247C0A`, IAT `0x140C62BC8`, imported as `VCRUNTIME140.dll!strstr`). The first case-sensitive substring match is replaced in place; no match appends the path. Base installs its initial result list without running this merge. It does not remove every matching result, compare just basenames, compare full keys for equality, or globally sort the final winners.

For example, in one Mod's inventory query, `inventory/archive/inventory/a.inventory.items.darkest` is encountered before `inventory/a.inventory.items.darkest`. The second path replaces the first slot even though their full paths differ. Definitions exclusive to the replaced file no longer enter the catalog; its first-ID stack limit must not mask the retained definition. The same rule applies to the verified trinket and encounter query callers. Quantity `(type,id)` and trinket ID still use first-match definitions, quirks/Buffs retain their own established rules: this correction changes their input file list, not those duplicate-ID policies.

`NativeContentFileResolver.Resolve` implements this enumerated-file rule. `ResolveOpenedFiles` accepts explicit original requests for actor info/art/override paths, roster variables and canonical region props, and resolves those requests directly against active source devices (section 22). The three root JSON prop defaults have a distinct Base-only `>` open, described below. Encounter queries retain separate region/difficulty/collection lists during current-table and global Bridge discovery. Actor item-reference reads use the same canonical-open rule as the actor catalog. Skin directory names still use their separately verified mode-0 comparison.

Enumeration slots and opened bytes are separate. `0x140248051` checks for a leading `>`: when present, `0x14024812E`–`0x14024815F` strips it and bypasses the alternate-mount search. Otherwise `0x140248064`–`0x14024812C` searches alternate providers before falling back to the base device. Flags-0 Base results have no `>`. If Base supplies `[archive/.../a, a]`, and a Mod's `a` replaces only the first containing slot, the surviving Base `a` reopens through that same Mod. Both positions then read the Mod file; both still count. Do not deduplicate physical paths or discard the second encounter position. The resolver completes slot merging before rebinding surviving unprefixed Base paths to their opened providers.

That re-open uses the original request against each Mod manifest, including case (`0x1402480DC` calls `0x1402393C0`). Base `inventory/A.inventory.items.darkest` does not match a manifest listing only lowercase `inventory/a.inventory.items.darkest`: both slots remain, and a duplicate item's first definition still comes from Base. Filter eligible providers before same-path election; changing only a final dictionary comparer cannot recover an already discarded provider. Physical Base/mode/DLC fallback keeps Windows path matching. A Mod entry authored as `dlc/feature/inventory/a...` also does not answer the root request `inventory/a...`; fallback to that DLC device reads its physical file, whereas opening an enumerated full DLC path can resolve the prefixed Mod entry.

The three root defaults explicitly use `>props/...` (`0x1404D87A0`–`0x1404D87D4`); `0x1404D76E0` passes each path to the JSON reader unchanged. Same-path files under a Mod, game mode or DLC do not supply these defaults. Nested prop files are still consumed by their own flags-9 queries and can declare or update resources under the established JSON rules.

PropLibrary's nested JSON searches and both Curio CSV searches pass **flags 9**, not 0 (`0x1404D87F7`, `0x1404D88AA`, `0x1404D895A`, `0x1404D8A0A`, `0x1404D8AB6`). Bit 8 prefixes the initial Base paths with `>` (`0x140247DFC`–`0x140247E42`, string `0x140E28E10` is `>%s`); alternate results retain their mount prefix. With bit 1 set, the merge erases only an offset-zero match, while a match at a positive offset appends (`0x140247C1F`–`0x140247C5C`). These explicit provider paths therefore remain separate inputs, in application order, including root Base/Mod files sharing a relative name. `ResolveAdditiveFiles` models this bounded branch. The resource's CSV/JSON update rules then determine the final fields. Type libraries and prop mappings remain separate queries. This exception must not be generalized to inventory/trinket/mash queries or canonical opens.

Quantity-item references partition the enumerated inputs by consumer: Loot, each supported JSON query family, each building ID, and Curio type libraries. For example, `campaign/town_events/loot/a.loot.json.town_events.events.json` must not be removed by `loot/a.loot.json`, because they belong to independent event and Loot queries. Curio and District references use flags-9 input lists. Actor references use exact canonical requests; unmodeled reference consumers retain their existing conservative full-path overlay analysis without claiming a proven native query.

Mod manifest eligibility, low-to-high application order, DLC/root provider provenance, competing-case diagnostics and unproven multi-DLC guards remain. Base/mode/official DLC retain physical discovery. This correction does not imply all file-return modes or every resource consumer behave identically, and it does not resolve previously guarded case-collision or DLC-registration ambiguity.

Evidence is pinned executable disassembly/import verification plus isolated executable contracts and DSON writes, not a new live-game run. See [audit evidence](change-history/resource-overlay-slot-audit-2026-09-14.md) and [implementation/validation](change-history/resource-overlay-slot-fixes-2026-09-14.md).

## 22. Canonical requests, Effect flags 1 and District flags 9 (2026-09-14)

Canonical opening does not pick an arbitrary case-insensitive mounted-path winner from enumerated candidates. Actor constructors format fixed paths (`0x1404C33BC`, `0x1404CD363`); roster and regional prop loaders do likewise. `0x1402480DC–0x1402480F2` passes the original request to the manifest lookup at `0x1402393C0`. Directory and filename hashes use original bytes (`h = h * 0x35 + byte`), without case folding or DLC-prefix removal. A leading `>` bypasses alternate mounts entirely.

| Request and source | Result |
| --- | --- |
| Root `heroes/query/query.info.darkest`; Mod lists only `...info.DARKEST` | Mod does not answer; try lower providers |
| Same request; Mod lists lowercase key, physical filename is uppercase | Manifest matches; Windows opens that physical alias |
| Same request; Mod lists both spellings in either order | Correct key still matches; physical-path deduplication must not erase it |
| Root request; Mod lists only `dlc/feature/heroes/query/query.info.darkest` | Mod does not answer the root request |
| Root request; enabled official DLC has physical `heroes/query/query.info.darkest` | That directory device can answer after higher matching providers |
| `>props/prop_definitions.json` | Read only the Base directory device |

The resolver receives the original requested paths and active sources, so prior enumeration, excluded physical discovery aliases, and case-insensitive deduplication cannot substitute another manifest key. It retains the requested spelling for actor IDs while preserving source/path provenance. Listed missing winning files remain selected and produce an unavailable/read-error result rather than reviving lower definitions; this is the editor's conservative open-failure protection, not a complete emulation of all native I/O failures.

The rule is shared by hero metadata, monster size/Boss metadata, actor item references, roster XP and regional props. In a table containing `alpha_A alpha_A` then `bravo_A`, a Base alpha of size 1 remains active when a Mod lists only uppercase info: bravo is index 1, Bridge appends at 2. Do not let an ineligible Mod size of 3 remove the first row. When the exact Mod key is genuinely active, its size of 3 does remove the oversized row, giving bravo 0 and append 1. The oversized/empty/missing-row rules themselves are unchanged.

Regional pools retain the requested dungeon ID after opening. `cove` and `Cove` must not be merged in definition keys, availability checks or automatic-selection weights. The map view passes the current snapshot's exact dungeon ID to `BattleRoomAttachmentCatalog.Load`; its guard retains that request for validation, and synchronization rebuilds the catalog when that ID changes. This extra request is necessary even when physical discovery returns a differently cased directory: `MistyGrove` may open a physical `mIsTyGrOvE/MistyGrove.props.DARKEST` on Base/mode/DLC, while a Mod still requires the exact original manifest key. Catalog callers querying a physical spelling alias must supply that dungeon ID at load time. Explicitly empty winning pools do not fall back to another spelling or provider. Explicit requests retain the existing `arena` exclusion: Dungeon::Load skips the ordinary props request at `0x1404AF1CF–0x1404AF1E6`.

Effect initialization passes **flags 1** at `0x1404E494D`, unlike the flags-0 inventory/encounter queries. The merge loop's first match has three outcomes: no match appends; an offset-zero match erases that slot then appends at the end; a positive-offset match appends without erasing the earlier provider. Base starts unprefixed; alternate results have mount prefixes. For Base `[a,b]` followed by lower Mod `a` and upper Mod `a`, the resulting sequence is `[Base b, lower a, upper a]`. Surviving Base requests still resolve their opened bytes afterward, retaining repeated slots.

Thus two Mods' same-path Effect files can both contribute. If the earlier one assigns `.disease special` and the later one omits `.disease`, the existing field rule retains `special`; explicit `.disease ""` still clears it. This fixes skill-to-quirk clues, not the separate Buff duplicate policy or all unmodeled skill/Effect mechanics.

District initialization passes **flags 9** at `0x140559EB3`. It shares the existing explicit-provider merge branch used by Curio and nested prop resources, including `>` on Base paths. A later Mod's same-path `{"buildings":[]}` does not erase an earlier source's distinct building. Its `DistrictSupplyBuffData` therefore remains a valid item-reference root: `target_inventory=estate` affects town, `provision` affects raid. This does not establish the complete duplicate-building-ID or building-trigger behavior.

See [the audit and reproduction evidence](change-history/canonical-open-and-query-flags-audit-2026-09-14.md) and [implementation/validation](change-history/canonical-open-and-query-flags-fixes-2026-09-14.md). Executable coverage is in `CanonicalResourceContractTests.cs` (`--canonical-resources`) and the full contract suite. Tests use isolated resources and DSON saves; no new live-game validation or save/Bridge format migration is claimed.

## 23. Inventory save identities and optional trinket counters (2026-09-14)

Definition identity and saved identity are separate consumers. Trinket definitions copy a 64-byte name but hash the original C string (`0x1404F3422–0x1404F3463`). Inventory item definitions use separate 64-byte name and 512-byte hash-input reads (`0x1404C85F9–0x1404C86E1`). This does **not** permit an arbitrary full ID in a saved item: restore at `0x1405D0910` reads `type` and `id` into 64-byte buffers, then hashes the restored names. Estate wallets also read `type` with size `0x40` at `0x14055BD53–0x14055BD6A` and hash that buffer.

The DSON restore string slot calls `0x140237480`, which copies at most destination-size minus one bytes. JSON restore uses `strncpy_s(..., _TRUNCATE)` with the same limit. Thus a 64-byte buffer holds at most 63 UTF-8 payload bytes. A 64-byte selection such as 63 `a` characters plus `b` can load as the different 63-byte definition; DDSaveEditor.jar preserving the full JSON string does not prove game compatibility. Twenty-one `饰` characters already use 63 bytes. NUL ends the C string before hashing; NUL-containing aliases participate in collision diagnostics rather than leaving their short counterpart falsely unambiguous.

The editor now preserves the authored catalog names and exposes a computed `SaveIdentityIssue`. It prevents mutations when actual persisted fields would change under the native reader, including save-only rows, and displays the reason before preview. Wallet heirloom IDs are persisted as `type`; unused wallet definition IDs are not rejected. In-bound case and whitespace remain significant. No truncation, renaming, resource-wide 63-byte normalization, save migration or history cleanup is performed.

The two optional trinket counters use the first exact JSON member and the signed Int32 flag `0x400` (`0x1404F3D7D–0x1404F3D9F`, `0x1404F4012–0x1404F4034`). The JSON path is `0x14028E2F0 → 0x14024C130 → 0x14024C980`, then object/array parsing to `0x14024D660`. Its numeric branches establish the flag:

- Signed Int32 negatives have `0x1606`; nonnegative Int32 values have `0x3E06`, including zero (`0x14024E0ED–0x14024E122`).
- Wider integer branches add bit 10 only when the value fits Int32 (`0x14024E057–0x14024E0EB`).
- Fraction/exponent numbers use `0x4206`, without bit 10 (`0x14024E005–0x14024E019`). `2.0` and `2e0` therefore do not act as the integer counter 2.
- Wrong types, out-of-Int32 integers and absent members leave the initial definition counter `-1`. An explicit Int32 negative is assigned without a positivity test. Later duplicate fields or same-ID trinket entries do not replace the first definition's value.

New instances serialize counters only for `N >= 0`, matching `0x1405D0820–0x1405D0874`. Omitted counters restore from the definition (`0x1405D0D9C–0x1405D0DE5`); omission and zero must not be collapsed. There is no longer a `ReadPositiveInstanceCounter` or `UnsupportedStateFields` creation blocker. This does not change total storage capacity, per-ID limit warnings, manifest/order resolution or prepared-save content guards.

Evidence uses the same fixed x64 build 27890, SHA-256 `35e5a653279992564809ff8406febd5a02a7d6961044781b1296b38a7096f59b`. See the [original audit](change-history/trinket-counter-and-save-identity-audit-2026-09-14.md), [fix record](change-history/inventory-persistence-fixes-2026-09-14.md) and [additional native fragments](../workspaces/inventory-persistence-fix-20260914/native-evidence.json). Formal coverage is `InventoryPersistenceContractTests.cs` (`--inventory-persistence`), the corrected first-member tests and the complete suite. These are isolated editor/DSON tests with static native corroboration; exhausted-trinket lifecycle effects and malformed whole JSON documents are not newly claimed as live-tested.
