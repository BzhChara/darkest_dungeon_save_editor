# Content Catalog and Save-Write Rules

This document is the durable record of the Darkest Dungeon Save Editor's confirmed content-resolution, hero-generation, quirk, trinket, and save-write rules. It separates behavior proven by the game or real saves from implementation conclusions and remaining live-test requirements, so later work does not turn one example Mod into a hard-coded special case.

- Last updated: 2026-08-31
- Audit baseline: `profile_1`
- Baseline `persist.game.json` SHA-256: `f0f707d734a93b9e04c9792d47490acbc8bbc046edd51ee8b2c108ca712e6b17`
- Active Mods in the baseline: 124
- Current product behavior: [README](../README.md)

## 1. Evidence levels and safety boundary

| Level | Meaning | What it can prove |
| --- | --- | --- |
| A | Real saves, encode/decode round trips, or a user-confirmed in-game load | Direct evidence for save compatibility or observed game behavior |
| B | Effective base-game/Mod definition files in the active content set | IDs, fields, values, override order, and author-declared conditions |
| C | An independent reverse-engineered implementation or behavior repeated across several samples | Corroborating evidence for a calculation order; never represented as official source code |
| D | An inference with no clean real-save instance | Enough to design a minimal test, not enough to claim live validation |

The fixed local evidence corpus is `local-analysis/save-corpus`. It is ignored by Git and contains 268 source-file copies, 268 decoded copies, and a `manifest.csv` with source paths, copy paths, format, size, SHA-256, decode status, and errors.

- `live-profile-0`, `live-profile-1`, `live-profile-3`: point-in-time snapshots of the user's current profiles;
- `legacy-research-profile-0`, `legacy-research-profile-1`: experimental saves from the old RuntimeFramework project;
- `legacy-ddsaveeditor-*`: fixtures and decoded samples from the researched save editor;
- `legacy-runtime-curated-town-decodes`: manually curated town-save analyses retained by the old project.

These files are read-only analysis snapshots. They do not automatically follow live-save changes and must never be copied back into a live profile as an implicit step. Any real-save mutation still requires separate authorization and the preview, confirmation, backup, atomic replacement, and verification workflow.

## 2. Active content and override order

### 2.1 The selected profile defines the active content set

1. Read enabled Mods and their UI order from `persist.game.json/base_root.applied_ugcs_1_0`.
2. A Mod higher in the player's list has higher final priority.
3. Build the overlay from low to high priority, therefore applying the list from bottom to top.
4. Disabled Workshop Mods, local Mods, and DLC features do not enter the catalog even when their files remain on disk.
5. Shared DLC package content and `features/<name>` content are distinct. A feature only participates when the profile explicitly enables it.

### 2.2 File and semantic-ID overlays

- First apply `base → DLC → Mod` replacement by normalized relative path.
- Hero, effect, buff, quirk, event, and upgrade definitions may merge by semantic ID across files; the highest-priority effective definition wins.
- A hero `.info.darkest` file is the full template. An `.override.darkest` file only replaces fields it actually declares and must not erase untouched lower-layer fields.
- Trinket and quirk provenance is checked at entry-ID level. A new ID introduced by a Mod inside a path that also exists in base content is still Mod content, not base content.
- If different definitions remain tied at the same highest priority, retain an unresolved conflict instead of guessing.
- Duplicate trinket IDs from different effective paths remain display-only under the current conservative policy.

### 2.3 Provenance display

- Content origin and the provider of the current effective definition are separate concepts.
- A base hero, quirk, or trinket overridden by a Mod is displayed as `原版（当前由 … 覆盖）` rather than losing its base origin.
- A Mod-only ID shows only that Mod as its origin.
- Workshop providers are mapped by Workshop ID. Local providers are mapped by `project.xml/Title`. Missing or ambiguous mappings are reported and never guessed.
- A local Mod without `modfiles.txt` still receives the standard-content-directory fallback scan. This is normal for many of the user's local Mods and does not make the Mod invalid.

## 3. Localization

1. Hero, quirk, and trinket catalogs read Simplified Chinese and English while always retaining the internal ID.
2. When the same provider contains XML source tables and compiled LOC2 data, LOC2 has effective-game precedence.
3. Even when `modfiles.txt` only lists an old `.loc`, directly contained `.string_table.xml` files under the Mod's top-level `localization` directory are supplemental sources.
4. Do not recurse into `unused`, platform directories, or files whose extension is `.unused`.
5. Validate LOC2 offsets, lengths, NUL boundaries, UTF-8, and color-marker cleanup. A corrupt table produces a catalog issue without contaminating other providers.
6. A missing language displays as `—`. Do not substitute the other language, the internal ID, a `[简中]` suffix, or invented translation.

The baseline `profile_1` quirk-localization snapshot contains 473 bilingual entries, 58 Simplified-Chinese-only entries, 3 English-only entries, and 11 entries missing both names. A missing display name does not make an otherwise valid quirk save structure unwritable.

## 4. Trinkets

### 4.1 Catalog and ordinary inventory writes

- Parse ID, Chinese name, English name, rarity, definition limit, origin, current override provider, and stateful shape.
- An ordinary trinket is added to the trinket inventory in `persist.estate.json`; it is not equipped onto a hero.
- Inventory counts do not include copies equipped by heroes.
- `limit=0` means unlimited, not a limit of zero.

### 4.2 Definition limits and storage capacity are different rules

| Limit | Editor behavior |
| --- | --- |
| Per-trinket definition `limit` | Count existing inventory copies plus the requested addition. Exceeding it produces a prominent warning, but console mode allows the explicit write. |
| Total `trinket_storage.max_slots` | Count current and resulting inventory occupancy. Exceeding effective capacity is a hard stop. |

Total capacity comes from the final active overlay. If the highest-priority capacity source is missing, malformed, or remains ambiguous at equal priority, trinket preview/application is disabled. The editor must not silently fall back to the base-game capacity.

### 4.3 Stateful trinkets that remain display-only

The editor currently does not create trinkets carrying per-instance runtime state such as:

- `quest_uses`;
- `trigger_limit`;
- progressive transformations or replacement state;
- consumption counters or comparable per-instance fields.

The ordinary ID/amount record is understood. The block exists because the current game version still lacks enough real saved instances to prove those state fields' initial values and lifecycle.

### 4.4 Preview invalidation

A trinket preview pins the profile hash, active providers, Manifest existence/hashes, the effective capacity file, the selected definition, and related semantic fingerprints. It is rejected if any relevant save, Mod/DLC state, order, Manifest, definition, capacity source, or effective result changes before commit.

## 5. Heroes

### 5.1 An effective class template is sufficient

- The roster does not need to contain an existing instance of a class before the editor can generate it.
- A Mod that changes a base class causes a new candidate to use the highest-priority effective class template, equipment, skills, upgrade trees, and generation rules.
- The save stores class ID, level, equipment rank, skill selection, quirks, and state. Skill damage, accuracy, class attributes, and effects are resolved by the game from the active template when it loads.
- The editor must not copy a second, stale set of base-game combat values into the hero record.
- A class-specific quirk granted later by a skill/effect or town event is not a required creation field. Normal gameplay continues to use the Mod's own grant path.

### 5.2 Natural generation and manual generation

| UI text | Meaning |
| --- | --- |
| `游戏自然 / 编辑器手动` | The game can naturally roll the class and the editor can create it. |
| `仅编辑器手动` | `is_generation_enabled=false`; the game does not naturally roll it, but its complete template supports editor creation. |
| `自然状态未知 / 编辑器手动` | The template does not declare the flag, so the editor does not invent a natural-generation answer. |

The displayed `自然怪癖范围` is only the positive/negative quirk range used by natural game generation. It does not constrain explicit editor selections.

### 5.3 Resolve level, equipment, and skills

1. Read `resolve_level_thresholds` from the selected profile's effective `game_mode` and construct level 0 through that mode's maximum.
2. The selected level determines `resolveXp`, `weapon_rank`, `armour_rank`, and the base HP of that armor rank.
3. Parse the effective base/Mod upgrade trees, supporting both `upgrades/heroes/<hero>.upgrades.json` and `upgrades/<hero>.upgrades.json`.
4. Write every weapon, armor, and combat-skill personal purchase whose prerequisite is satisfied at the selected level.
5. A level-0 base purchase record means requirement 0 in each combat-skill tree is purchased. It does not mean the hero owns only two randomly chosen skills.
6. Camping skills use the implicit `<class ID>.<camping skill ID>` tree and requirement code `0`. Unlock every recognized shared and class camping skill.
7. Unlocked and currently equipped are different. The equipped count still follows the class/Mod selection template; it is not hard-coded to four and does not equip every unlocked skill.
8. Reject generation when an active combat skill lacks its exact upgrade tree, the selected level has no purchasable requirement, a requirement code cannot fit the save's single ASCII-character representation, or two tree IDs collide under the game's hash.

### 5.4 Destination and roster behavior

- The current feature appends candidates to the ordinary stagecoach only; it does not insert directly into the roster.
- A full roster neither evicts an existing hero nor prevents an ordinary stagecoach candidate from being generated.
- The shard stagecoach is not written unless a future feature explicitly implements that destination.
- Candidate names, color variants, and equipped skills follow the active template and stable randomization rules.

### 5.5 Files involved in hero preview and commit

- `persist.town.json`: ordinary stagecoach candidate and `nextGuid`;
- `persist.roster.json`: advance GUID state without adding a recruited hero;
- `persist.upgrades.json`: personal equipment, combat-skill, and camping-skill purchases for the same GUID.

All three temporary files must pass the DSON encode/decode consistency check. If any commit step fails, the same complete backup restores every replaced target.

### 5.6 `actor.buff_group` and initial HP

- Keep `actor.buff_group` empty. The game applies buffs from the quirk definitions; serializing another copy would risk duplicate effects.
- `actor.buff_group_next_guid=2` is the smallest value observed in real stagecoach candidates and has loaded successfully in the test profile.
- `current_hp` reflects only maximum-HP effects active at the instant the candidate is generated. Runtime condition changes remain the game's responsibility.

## 6. General quirk rules

### 6.1 Selection and quotas

- A new candidate may start completely blank; the editor does not need to simulate the game's random natural roll.
- The user may explicitly select zero or more quirks from the active catalog.
- Maximums are 5 positive quirks, 5 negative quirks, and 3 diseases. Diseases have their own quota.
- Quota exhaustion is shown by the summary and click guard, not repeated as an unavailable reason on every catalog row.
- `incompatible_quirks` remains a hard pairwise exclusion with a specific explanation.
- Console mode ignores class `incompatible_class_ids`. Any structurally writable quirk may be assigned to any class.
- Being writable on any class does not guarantee that every effect is meaningful. A mode-dependent buff assigned to a class with no mode system may never activate.

### 6.2 Write statuses

| Status | Meaning |
| --- | --- |
| `Direct` | The shape and every rule that affects persisted `current_hp` are resolved. |
| `RequiresSaveContext` | The definition is writable, but the profile must be inspected for a limit warning. Exceeding it warns rather than blocks. |
| `Unverified` | A shape affecting a persisted field lacks an accepted model and currently blocks selection. |
| `Unsupported` | The known shape cannot be represented by the current candidate format. |

Non-HP buffs are not precomputed into the candidate. The game applies damage, accuracy, dodge, resistance, stress, healing, and other effects from the active quirk definition at runtime. The editor's failure to precompute those properties is not by itself a reason to block the quirk.

### 6.3 `singleton` and `roster_limit`

For `singleton`:

- the definition limit is 1;
- count matching quirks in the recruited roster, ordinary stagecoach, shard stagecoach, and other saved hero-candidate pools;
- add the pending candidate and warn when the result exceeds 1, but preserve the user's ability to continue;
- use the compact table reason `singleton 定义上限 1`.

The baseline contains 25 such quirks:

```text
inspiring_ship, master_blacksmith, mithridatism, taira_blessing,
thrombophilia, valiant, vital_member,
alien_calm, alien_eye, alien_force, alien_healing, alien_isolation,
alien_precision, alien_purity, alien_solidity, alien_speed,
alien_stability, twilight_dreamer,
vmt_bonehead, vmt_canary, vmt_daybreaker, vmt_hexer,
vmt_horn_rats_gift,
vampKraken_dmgQuirk, vampKraken_resQuirk
```

For positive `roster_limit`:

- it is a game-side recruitment limit over already recruited heroes carrying that quirk, not a count across every candidate pool;
- the editor creates a stagecoach candidate and does not mutate the roster, so it neither counts nor warns at generation time;
- `shard_hungry` has `roster_limit=6`; the base-game shard-mercenary recruitment path remains responsible for enforcing it.

### 6.4 Evolving quirks

1. Parse `evolution_duration_min/max`, target quirk, death-on-expiry, town-progression change, and item-use threshold.
2. Derive a stable integer `evolution_duration_remaining` from the generation seed and quirk ID within the definition's range.
3. Preserve fixed ranges and author-declared `0–0` exactly.
4. The game owns subsequent countdown changes, transformation, and effect application. The editor only initializes required persistent fields.
5. Continue to block definitions with missing, reversed, or non-integer bounds, or with neither a target nor an explicit death result.

Evolving quirks have persisted and transformed during user testing. Duration values must come from each Mod definition; they must never be replaced by a universal `3–15` guess.

## 7. Maximum-HP rules and final quirk audit

### 7.1 General calculation model

Let `B` be the selected armor rank's base HP, `F` the sum of all flat HP modifiers active at generation, and `P` the sum of all percentage HP modifiers active at generation:

```text
current_hp = (B + F) * (1 + P)
```

Flat additions occur before percentage multiplication. A percentage buff with `amount=0.10` means `+10%`. Preserve the floating-point result; do not round it merely because the game UI displays an integer.

Selection validation has two safety layers:

1. the actually active generation-time formula must be greater than zero;
2. every reachable combination of recognized conditional HP effects and constant effects must also keep maximum HP greater than zero.

The second layer is required even though conditional buffs do not contribute to initial `current_hp`. Otherwise the positive quirk `Ailuoli_Quirk2`, whose active condition applies `-50%`, can combine with other negative-HP quirks and drive the runtime multiplier to zero or below. Block the dangerous combination, not the individual quirk.

Evidence:

- A: in the ordinary `profile_0` stagecoach, a rank-2 Crusader with base 47 and `insipid_ship -4` saves `current_hp=43`; a rank-2 Jester with base 27 saves 23.
- A: a tested candidate with base 26 and `fragile -10%` saved `current_hp=23.4`. The game displayed maximum HP 24 and `基础 26 / 特质 -10%`, demonstrating no duplicate application.
- B: active Mod definitions explicitly declare `combat_stat_add`, `combat_stat_multiply`, their values, and their rules.
- C: the independent [Darkest-Dungeon-Unity](https://github.com/Reinisch/Darkest-Dungeon-Unity) reverse-engineered implementation uses `(RawValue + FlatAddition) * Multiplier` and applies/reverts conditional buffs. This corroborates the ordering but is not official source code.

`always` is active at generation. `no_trinkets` is also active for an unequipped stagecoach candidate. The game handles later equip/unequip changes; the editor does not serialize quirk buffs into `buff_group`.

### 7.2 Current implementation versus audited model

As of this document's date, the application only treats a single `combat_stat_multiply max_hp` buff with rule `always` or `no_trinkets` as Direct. Flat, multi-HP-buff, and conditional HP quirks therefore remain Unverified in the current build. The following 26 entries have an audited implementation model but are not yet released as supported.

| Category | Count | Current status |
| --- | ---: | --- |
| Direct | 494 | Writable now |
| RequiresSaveContext | 25 | Writable now, with a warning when needed |
| Unverified: HP rules | 26 | Currently blocked; model described below |
| Unsupported | 0 | None |
| Total | 545 | Effective `profile_1` catalog |

### 7.3 Thirteen constant flat-HP quirks

| Quirk ID | Display name | Flat HP | Note |
| --- | --- | ---: | --- |
| `hearty_ship` | 精神饱满 / Hearty | +4 | Incompatible with `insipid_ship` |
| `insipid_ship` | 平乏无力 / Insipid | -4 | Negative flat formula has a clean ordinary-stagecoach sample |
| `koshou` | 小姓寄生 / Koshou (Disease) | -3 | Disease |
| `orca_Quirk6` | 永生者赠礼 / — | +15 | English is genuinely absent |
| `Gabriel_Quirk2` | 艾格雷姆的庇佑 / Agrim's Blessing | +6 | Constant |
| `Floss_Quirk3` | 无尽的欲望 I / Endless Desire I | +4 | Evolves to II after 150 |
| `Floss_QuirkA2` | 无尽的欲望 II / Endless Desire II | +8 | Evolves to III after 150 |
| `Floss_QuirkA3` | 无尽的欲望 III / Endless Desire III | +12 | Evolves to IV after 150 |
| `Floss_QuirkA4` | 无尽的欲望 IV / Endless Desire IV | +16 | Evolves to V after 150 |
| `Floss_QuirkA5` | 无尽的欲望 V / Endless Desire V | +20 | Evolves to VI after 150 |
| `Floss_QuirkA6` | 无尽的欲望 VI / Endless Desire VI | +24 | Evolves to VII after 150 |
| `Floss_QuirkA7` | 无尽的欲望 VII / Endless Desire VII | +28 | Evolves to VIII after 150 |
| `Floss_QuirkA8` | 无尽的欲望 VIII / Endless Desire VIII | +32 | Terminal |

Audit conclusion: all are `combat_stat_add max_hp + always` and enter `F`. Negative flat HP has Level-A evidence. Positive flat HP still needs one clean ordinary-stagecoach full-health test. Existing shard-stagecoach samples retained base HP 37/46 while carrying `hearty_ship`; because they belong to `shard_hero_recruit`, they neither prove nor disprove the ordinary-stagecoach full-health policy.

### 7.4 Ten Mordekaiser mixed-HP quirks

Every quirk from `Mordekaiser_Quirk1` through `Mordekaiser_Quirk10` has two constant HP buffs:

```text
tier n: F = +4n, P = +4n%
initial full HP = (B + 4n) * (1 + 0.04n), n = 1..10
```

- I through IX evolve after `20–30`, `30–40`, `40–50`, `50–60`, `60–70`, `70–80`, `80–90`, `90–100`, and `100–120` respectively;
- X is terminal;
- each defines `evolution_town_progression_duration_change=0`; the editor initializes the declared interval and does not invent an additional decrement rule.

Audit conclusion: multiple HP buffs are not an unknown shape once represented as `F` and `P`. None of the 268 corpus files contains a Mordekaiser quirk instance, so tiers I and X still need live tests after implementation to cover fractional and large results.

### 7.5 Three runtime-conditional HP quirks

| Quirk ID | Condition and HP effect | Candidate `current_hp` at generation |
| --- | --- | --- |
| `Octopus_Quirk5` (信徒 / Believer) | `afflicted`: `+5%` | Inactive before affliction; use `B` plus other constant modifiers |
| `Kaltsit_Quirk` (大地守望者 / —) | a mode exists and is not `KaltsitA`: flat `+20` | No active stagecoach mode; do not pre-add 20 |
| `Ailuoli_Quirk2` (黑夜骑士 / Knight of the Night) | `lightabove 1`: `-50%` | No raid-light condition in the stagecoach; do not pre-subtract 50% |

Audit conclusion: write the quirk record but not an initial HP adjustment or `actor.buff_group` entry. The game applies/reverts the effect when affliction, mode, or light changes. A class with no mode can carry `Kaltsit_Quirk`, but its +20 effect will not activate. Before writing, all reachable conditional HP combinations still participate in the non-positive-HP safety check, especially `Ailuoli_Quirk2` combined with other negative-HP quirks.

The corpus proves structural persistence for `Octopus_Quirk5` and `Kaltsit_Quirk`; it has no `Ailuoli_Quirk2` instance. Existing conditional samples are injured or at 1 HP and cannot prove a full-health formula.

### 7.6 Expected result after implementation and minimum live tests

After implementing flat, mixed, runtime-conditioned models and the reachable-condition non-positive-HP check, all 545 baseline quirks should have an individual write path: 494 existing Direct, 25 context-warning entries, and 26 newly modeled entries. No individual item remains blocked merely because its known HP rule was not modeled; a specific selection whose reachable HP can become zero or negative must still be rejected.

Minimum release tests:

1. Generate blank and `hearty_ship` candidates of the same class and level in the ordinary stagecoach. The latter must save and recruit at full HP exactly 4 above base.
2. Generate `Mordekaiser_Quirk1` and `Mordekaiser_Quirk10`. Decode the preview and verify `(B+4)*1.04` and `(B+40)*1.40`, then verify no duplicate application in game.
3. Generate a candidate carrying only `Ailuoli_Quirk2` among HP quirks. Stagecoach HP remains base; raid maximum HP changes between light `>1` and extinguished light `0`.
4. Construct boundary selections containing `Ailuoli_Quirk2` and negative-HP quirks. Any combination that can reach maximum HP `<=0` must be rejected before preview.
5. Optional: on Kal'tsit, mode A has no +20 and another mode has +20; on a class without a mode, the effect remains inactive.

## 8. Transactions, backup, and explicitly unaffected state

1. Safe preview operates only on a temporary workspace.
2. Any relevant live-save or content-semantic change after preview invalidates the commit.
3. Before commit, verify `Darkest.exe` is not running and back up all `persist*.json` files in the profile.
4. Use atomic replacement; a multi-file hero write rolls back every target if any step fails.
5. Do not modify game content files, Mod files, week count, quests, town-event history, or existing heroes.
6. Event triggering, including `从陵墓归来`, remains a separate future feature and does not belong in the hero-generation chain.

## 9. Maintenance requirements

Whenever a catalog or save-write rule changes:

1. update the boundary between current implementation and audited future behavior in this document;
2. add a contract test, or record why only a live test can cover the behavior;
3. retain a detached evidence snapshot and SHA-256 rather than relying on the live profile;
4. model field type, condition type, and override semantics instead of hard-coding one example Mod;
5. do not treat an independent reverse-engineered implementation, author-facing tooltip, or personal recollection as sole proof of game behavior;
6. regenerate catalog statistics after the game or active Mod list changes; figures such as 545 quirks and 124 Mods are only this audit's baseline snapshot.
