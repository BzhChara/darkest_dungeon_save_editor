# Content Catalog and Save-Write Rules

This document is the durable record of the Darkest Dungeon Save Editor's confirmed content-resolution, hero-generation, quirk, trinket, and save-write rules. It separates behavior proven by the game or real saves from implementation conclusions and remaining live-test requirements, so later work does not turn one example Mod into a hard-coded special case.

- Last updated: 2026-09-08
- Persistent expedition path rule updated: 2026-09-10
- Historical audit baseline: `profile_1` (the counts and hash below are a snapshot, not live state)
- Baseline `persist.game.json` SHA-256: `f0f707d734a93b9e04c9792d47490acbc8bbc046edd51ee8b2c108ca712e6b17`
- Active Mods in the baseline: 124
- Current product behavior: [README](../README.md)

## 1. Evidence levels and safety boundary

| Level | Meaning | What it can prove |
| --- | --- | --- |
| A | Real saves, encode/decode round trips, or a documented authorized in-game observation operated by the user or through Computer Use | Direct evidence for save compatibility or observed game behavior; not blanket user acceptance |
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

### Automatic synchronization of the selected profile

- After one successful load, a shared monitor watches `persist.game.json`, `persist.estate.json`, `persist.roster.json`, `persist.town.json`, `persist.upgrades.json`, `persist.raid.json`, and `persist.map.json`. It reads disk saves only, including while the game runs; it never edits process memory or permits writes while the game is running.
- Debounce save bursts; use polling as a missed-event fallback and activation as a catch-up probe. Compare the complete relevant file-hash vector before and after reading. Locked, partial, missing-required, or changing files retain the last complete display but suspend edits and retry. This detects concurrent writes, not an undocumented atomic transaction ID in the game's multi-file format.
- Normal game progress updates the save guards and dynamic quantities, not the content-resolution rules. The content key contains game mode, DLC entries and numbered applied-Mod entries; JSON property order is not load order. An additional 10-second poll resolves sources and hashes catalog definitions, references and localization, including actor info/art/override files. Definition-only updates rebuild affected catalogs even if the profile, manifest, file length and timestamps are unchanged. Hashing runs off the UI thread; changed content is checked again before publishing rebuilt catalogs. Write services retain their own source-file guards. This broad refresh fingerprint is separate from the encounter-maintenance fingerprint: unrelated item/quirk changes do not clear editor battles.
- Town and raid quantity definitions/reference classifications are cached separately; visiting a scene for the first time builds its cache. Refresh only saved amounts, presence, residue entries and occupied slots. Existing unused entries remain visible under the established rules; an absent torch is not hidden. Honor `inraid`/`raiddungeon`, ignoring leftover raid files after force-town.
- Preserve search, scroll, selected IDs, quantity input, hero level and valid selected quirks on same-scene refresh. A scene transition clears the item selection, requiring a new target even for overlapping resources. Missing selected definitions are not silently replaced with another row.
- Relevant file changes invalidate prepared edits. Draft choices survive, but the user must generate a new preview. Recheck a modal confirmation's revision before committing; defer snapshot publication while any writer/preview operation is busy. Complete own writes with one catch-up refresh. Profile changes/close cancel old requests; old-profile results cannot publish into a new profile.
- The hero page remains a generation catalog, not a roster list. Roster/town/upgrades hashes invalidate stale hero and quirk-limit previews; fresh generation previews read those saves using the existing services. Rebind map candidate guards only after confirming unchanged content configuration, retaining definition fingerprints and backend validation.
- A single compact main-window synchronization status replaces duplicate map sync text. Detailed changes and deduplicated errors go to the session log. Scratch copies use two private reusable slots, not a per-save archival workspace.
- The snapshot reader remains read-only. A separate, user-authorized maintenance step runs after load/sync and before map writes: confirmed missing/changed editor encounter bindings trigger removal of invalid managed Bridge combinations, recalculation of retained indexes, and clearing of all still-present editor placements in the current raid. This includes direct local placements. Original game-generated battles are excluded. Clear only the battle, preserving attachments and exploration. Successful backups establish ownership; ambiguous provenance or unproven indexes defer maintenance. Game-running checks, profile/package backups and guarded group recovery apply. Maintenance warnings do not disable inventory/hero synchronization. See [automatic battle maintenance](encounter-runtime-order.md#automatic-editor-battle-maintenance-2026-09-07).

Validation (2026-09-06): Release build and the executable contract suite passed, with the unrelated symbolic-link inventory fixture skipped because Windows did not grant the required privilege. Computer Use tests on a project-only profile copy verified quantity/input preservation, partial-write recovery, town/raid transitions with leftover map files, rejection of stale map and quantity confirmations, hero level/quirk preservation across progress updates and mode-driven catalog rebuilds, and continued synchronization after a successful quantity commit. The live profile's 18 save files remained byte-identical. Shared mode also blocks the legacy map-only refresh path after failed operations; tab changes restore the current tab's input availability.

### 2.1 The selected profile defines the active content set

1. Read enabled Mods and their UI order from `persist.game.json/base_root.applied_ugcs_1_0`.
2. A Mod higher in the player's list has higher final priority.
3. Build the overlay from low to high priority, therefore applying the list from bottom to top.
4. Disabled Workshop Mods, local Mods, and DLC features do not enter the catalog even when their files remain on disk.
5. Shared DLC package content and `features/<name>` content are distinct. A feature only participates when the profile explicitly enables it.

### 2.2 File and semantic-ID overlays

- Replay verified native file slots in `base → DLC → Mod` application order. A path replacement stays in its original slot; new paths append in descending path-depth order, then ordinal filename order within each source. File order and semantic-ID lookup are separate rules.
- Inventory `(type,id)` and trinket ID lookup use the first loaded matching definition; quirk ID lookup uses the last loaded matching definition. A later filename does not automatically override an inventory/trinket ID, and later trinket state fields are not merged into the winner. Inventory/trinket identity preserves case and surrounding whitespace through discovery, quantities, reference matching, refresh, previews and writes. Case-distinct IDs with different native hashes are separate entries; actual native-hash collisions remain unavailable. See [inventory identities](resource-duplicate-semantics.md#9-inventory-identities-and-localization-keys).
- Hero definitions use the constructed `heroes/<id>/<id>` path, opening `.info.darkest`, then `.art.darkest`, then `.override.darkest`. Each path resolves independently; a lower-priority art/override file still applies after an effective info file if its own path was not replaced. Discovery of an info filename in an unrelated directory is not permission to use that file as the class template.
- Canonical actor lookups still respect a Mod's manifest. Native hash collisions between different inventory keys, trinket IDs, hero IDs or quirk IDs remain unavailable; evolution targets cannot resolve through such a collision. Monster collisions defer affected index calculations instead of guessing a string-specific size.
- Hero info, override and Effect attributes use the native record reader: slash/block comments are removed before string parsing without quote protection or backslash-escape decoding; NUL terminates input. Record/field names are case-sensitive. Scalar strings and bounded lists have distinct rules for empty/dot-prefixed tokens; see [native text semantics](resource-duplicate-semantics.md#8-hero-text-equipment-slots-camping-pools-and-item-references).
- Trinket and quirk provenance is checked at entry-ID level. A new ID introduced by a Mod inside a path that also exists in base content is still Mod content, not base content.
- The 2026-09-08/09 duplicate-resource experiments distinguish skill scalar patching/effect-list append, Effect field patching, complete Buff replacement, first-match event-result lookup, and first-match monster-brain binding. The editor integrates last-complete Buff selection for HP, level-0 skill `.effect` append, effective `.disease` fields, first event results for recruitment/item clues, and non-placeable empty encounter slots. The later static map-resource audit also integrates native CSV updates and bounded JSON default/difficulty metadata. Other Effect/skill fields, unmodeled map properties and untested mechanics remain unverified. See [duplicate-resource rules and coverage](resource-duplicate-semantics.md).
- Upgrade research on 2026-09-08 confirmed per-tree last-match lookup in the native guild/requirement queries, with same-path and different-path Antiquarian overrides both observed in game. The editor now resolves each exact tree ID to the last complete definition in native file order; whole-file compatibility selection has been removed. See [the experiment](change-history/antiquarian-upgrade-probe-2026-09-08.md) and [implementation](change-history/hero-upgrade-trees-2026-09-08.md).

Physical discovery follows the verified Windows directory-device exclusions for dot names, `_template` path components, and C-locale path-conversion failures. Manifest discovery and canonical file opening are separate branches. The complete filesystem inventory still reports excluded files for diagnosis. See the versioned evidence and remaining limits in [native loading verification](change-history/native-loading-2026-09-07.md).

### 2.3 Provenance display

- Content origin and the provider of the current effective definition are separate concepts.
- A base quantity item, hero, quirk, or trinket overridden by a Mod is displayed as `原版（当前由 … 覆盖）` rather than losing its base origin.
- A Mod-only ID shows only that Mod as its origin.
- Workshop providers are mapped by Workshop ID. Local providers are mapped by `project.xml/Title`. Missing or ambiguous mappings are reported and never guessed.
- All selected local and Workshop Mods require `modfiles.txt`. Explicit profile loading prepares missing manifests before catalogs are read (section 2.6). Catalogs never fall back to physical Mod discovery. Base game, game-mode and enabled DLC sources retain their own physical entry points.
- Item, hero, and trinket searches match both the internal source ID and the displayed provenance label, including original-content override labels.

### 2.4 File inventory and manifest-difference diagnostics

Each explicit content load performs one fresh, read-only inventory of every resolved active Mod directory, regardless of whether it has `modfiles.txt`. Only file metadata and manifests are read; assets are not decoded, scripts are not executed, and neither Mods nor saves are changed. Inactive Mods are not scanned. Reparse points are skipped and scan failures are reported as incomplete observations, not as manifest-free or missing content.

This inventory is a diagnostic layer separate from the effective catalogs. The inventory does not override manifest selection, enabled DLC paths, Mod priority, localization precedence, item scene rules or generation/write guards. A candidate means only a data/localization file under a root or enabled-DLC content directory; it does not mean that a definition was parsed, referenced, loaded by the game, or approved for writing. Unknown manifest relations remain unknown after a failed/invalid manifest read.

The runtime log shows one summary. The full project log retains profile/path/game-hash context, per-Mod counts, unlisted data/localization candidates, missing entries, declared/actual size differences and scan errors. Unlisted XML is diagnostic-only, not a supplemental source. Other unlisted files are aggregated in per-Mod counts. Size differences, equal bytes, absent references, or names such as `old` are not used to declare a file obsolete or corrupt. The inventory itself does not promote unlisted definitions or rewrite manifests. The separate explicit-load preparation stage below creates only missing manifests. The inventory is rebuilt on load and is not a permanent filesystem cache or an atomic content lock.

### 2.5 Observed local manifest loading

The 2026-09-06 Computer Use A/B experiment on build 27890 confirms a manifest gate for the tested local Mod's existing-hero `.info.darkest` overrides. With both files listed, Crusader and Highwayman displayed speed 21 and 57. After a full game exit and removal of only the Highwayman manifest entry, the same heroes displayed 21 and 7. Both hero files remained present and byte-identical; the listed Crusader stayed active as a positive control. The same temporary `profile_2`, sole active Mod, DLC selection, hero progression and position were retained.

This result supports keeping filesystem inventory separate from effective definition loading: finding an unlisted file is not sufficient to promote it into a writable catalog. It does not establish that the file is obsolete, nor decide manifest-free Mods, Workshop loading, new hero IDs, other definition types, enabled-DLC subdirectories or XML/LOC2 behavior. No production scan rule was changed for this experiment. See the [test result and evidence boundaries](change-history/manifest-loading-result-2026-09-06.md).

### 2.6 Adopted resource-discovery policy

Confirmed by the user on 2026-09-07/08: experimentally unify Mod reading around official manifests. This supersedes the previous manifest-absent directory fallback.

1. **Prepare only selected, resolved Mods on explicit profile load.** For local and Workshop sources lacking `modfiles.txt`, use the verified game-supplied uploader in `dont_submit` mode. Existing manifests, disabled Mods and unresolved sources are not rewritten. A missing manifest while the game is running stops loading until the game is closed.
2. **Isolate native preparation.** Copy source payload and matching base-game files into a private wrapper, so compilation cannot modify the original localization/maps. Only the official output with the wrapper prefix removed is eligible for installation. Base-identical files are omitted, as the user requested. Validate output entries, original files, tool version and profile configuration before installation. Unsupported uploader versions, output discrepancies or generation failure stop the load. Closing the main window cancels and awaits the tracked load task before allowing WPF shutdown, so process and temporary-file cleanup can complete.
3. **Install and record.** Prepare every missing manifest before publishing any. Create each target without overwrite via a same-directory temporary file; keep a receipt of prepared and created paths/hashes. A later installation interruption can leave earlier successful creations in place and reports them; retry handles the remaining missing files. Git rollback of the editor does not remove manifests installed outside the repository.
4. **All Mod catalogs are manifest-only.** Resolve listed paths with existing containment, type, enabled-DLC and existence checks. No missing/unreadable manifest, unlisted file or missing listed file authorizes physical Mod fallback. Base/DLC discovery remains separate. Automatic synchronization and write preflights read/check manifests but do not generate them; click Load to prepare newly enabled missing-manifest Mods.
5. **Separate file and ID rules.** All resource families use the shared native file resolver: the upper Mod wins the same path, preserving its enumeration slot. Inventory/trinket first-match and quirk last-match semantics remain. Upgrade trees and Buffs take the last complete definition in that resolved sequence. Event results use the first matching event, while the editor's `.disease` tracking applies field assignments rather than choosing a whole Effect by Mod priority. See section 2.2 for the bounded integration; unverified cases remain guarded.
6. **Keep diagnostics separate.** The full read-only inventory still reports unlisted files, missing entries, size differences and incomplete observations; it is never an effective resource fallback. No manifest regeneration on arbitrary resource edits, deletion of leftovers, XML supplementation or malformed-XML recovery is introduced.

This is the user's adopted experiment, not proof that an originally manifest-free Mod is equivalent to the native uploader output, or that every resource's duplicate-ID rule is known. See [implementation and validation](change-history/manifest-only-loading-2026-09-08.md).

### 2.7 Discovery consistency and manifest-read failures

- Room battle attachments, encounters, monster metadata, inventory/capacity, hero dependencies, item references and localization all require selected Mod manifests and share file overlay resolution. Disabled DLC and unrelated backup roots remain excluded.
- Mod colour-variation folders within hero definition providers count only when they contain an existing manifest-listed PNG. Physical unlisted/empty folders cannot extend the continuous A/B/... candidate range. Existing lower-provider art inheritance and base/DLC directory entry points remain. Texture-path existence participates in catalog refresh without repeatedly hashing image pixels; this is an eligibility check, not full texture/animation validation.
- Resource paths are extracted from the complete manifest path plus its optional trailing byte-count field, not from the first occurrence of a supported suffix. Embedded extension-like directory names and spaces are preserved. The final extension must still match a supported resource type.
- Effective catalog readers share an eager manifest reader. A manifest that is locked/unreadable, is a directory rather than a file, or contains an unparsable selected resource path causes that catalog request to fail with the manifest path and, for an invalid resource path, the line number. It must not produce a partial catalog or select manifest-free fallback. A readable empty manifest remains authoritative. Existing missing listed-file diagnostics and path-containment/DLC/type checks are retained.
- This is bounded error reporting, not automatic manifest repair, content promotion, or a guarantee that every malformed/semantically wrong package can be detected. Diagnostic inventory remains independent; it may report incomplete observations without returning an effective catalog. Unrelated workflows that do not require the failed catalog are not given a new global prohibition.
- The discovery fixes themselves did not remove localization compatibility. The later approved removal of XML supplementation/recovery and addition of legacy LOC support are recorded below and in [the compatibility inventory](change-history/content-compatibility-audit-2026-09-06.md). Ordinary nested-directory discovery is not a deliberate fallback to old files and was not changed by those removals.

## 3. Localization

1. Quantity-item, hero, quirk, trinket, monster and curio catalogs share Simplified Chinese/English name resolution while retaining internal IDs.
   Requested keys, LOC/LOC2 results and the shared name cache preserve exact case; XML entry IDs are not trimmed into a different key. Display values still use the established formatting policy. Search remains case-insensitive without changing resource identity.
2. Preserve the existing file overlay and Mod priority first. Within one provider, non-empty values have per-language precedence `.loc2 > .loc > valid .string_table.xml`; the relative-path order remains the tie-breaker within one format. A higher-priority provider's XML can override a lower provider's binary name. Missing values may still come from other eligible files, but an exact-path-replaced file is not resurrected. This is the editor's name-resolution policy, not proof of every current game build's handling of legacy LOC.
3. With a manifest, every XML/LOC/LOC2 source must be listed and pass containment, supported-directory, enabled-DLC and existence checks. Do not supplement unlisted XML even when a listed binary is missing, corrupt, or lacks the requested name. A readable empty manifest remains authoritative. Missing Mod manifests must be prepared before this reader runs. Base/DLC sources discover XML recursively under eligible localization roots and binary tables directly under those roots.
4. Binary tables must end exactly in `.loc` or `.loc2`; the stem must be `english`/`schinese` or end in `_english`/`_schinese`. Do not recurse into `unused`, platform or other nested directories for binary tables. `.string_table.xml.unused`, `.loc.unused` and `.loc2.unused` are unsupported final extensions. There is no blanket `old/backup/unused` directory-name filter for listed XML or recursively discovered base/DLC XML; changing that separate policy requires a separate decision.
5. Isolate invalid text only when its boundaries are trustworthy; never guess encodings or substitute replacement characters. LOC and LOC2 retain distinct layout/index validation and share bounded value decoding. Invalid headers, table alignment, indexes, offsets, lengths or NUL terminators reject the entire file, including when the bad record was not requested. A bounded value with invalid UTF-8 or an incomplete compiled colour control is skipped instead; other valid values, including later values in the same hash group, remain eligible. Colour starts/ends may cross strings and are not required to balance within one value. Diagnostics report the file, skipped-value count and at most three examples, only after structural validation succeeds. Other eligible same-language files retain their existing fallback role; unreadable/invalid manifests still stop the catalog request as described in section 2.7.
6. A missing language displays as `—`. Do not substitute the other language, the internal ID, a `[简中]` suffix, or invented translation.
7. Random personal names remain XML-only because hashed binary tables cannot enumerate original `hero_name_*` keys. Valid XML keeps the existing English-first, otherwise first-language-group rule; this does not change the separate bilingual fields.
8. XML must finish normal strict parsing before any entry is exposed. Invalid byte encoding, declarations, comments, CDATA or closing tags reject the entire document, including random personal names; XML has no independent binary value index with which to safely skip undecodable bytes. Do not sanitize or regex-recover it (A2 remains removed). Within a well-formed document, entries without a key/language ID or with ambiguous nested entry/language ownership are skipped and summarized like binary values. Blank translations remain absent without warning; a display-name normalization timeout is isolated to that entry. These checks apply to all shared display-name consumers and, where applicable, the XML random-name pool. A1 remains removed: no unlisted XML is made eligible by an invalid or absent binary value.

### 3.1 Legacy LOC evidence and implementation boundary

Ruler (Workshop `1596685165`, hero ID `JoanofArc`) lists `localization/1596685165_english.loc` and `localization/1596685165_schinese.loc` in its existing manifest. Its `localization/JoanofArc.string_table.xml` is unlisted. Both listed binary tables contain `hero_class_name_JoanofArc` (hash `819493669`) with the exact value `Ruler`, including the Simplified Chinese table; no translation or XML fallback is needed.

The observed legacy layout starts with two little-endian 32-bit offsets (value table, string data), followed by a 4096-byte bucket area. Hash records begin at byte 4104 and hold `(hash, value count, first value index)` in 12 bytes. Value records hold `(relative string offset, byte length including NUL, metadata)` in 12 bytes. Unlike LOC2, there is no separate value-group table. The editor scans validated hash records rather than relying on the bucket lookup area; unused bucket/metadata fields are not runtime-validated. Known key hashes select the first non-empty value from their validated group, consistent with the existing LOC2 display-name policy. No claim is made about unobserved format variants or the game's runtime loader.

For the read-only Ruler samples, the value/string offsets are 4896/5712, with 66 hash records and 68 values in each file. The English file is 7506 bytes (SHA-256 `AAF4ED3F760A3B217B21389BF15C60B5A5E22635E0F48BC92D6D2EA44F2CFD9E`); Simplified Chinese is 7547 bytes (SHA-256 `78F83463DFC2DAE98682F6259D019D3FE0E87086846C0194705C9D28E8278B8E`). This format support changes editor names only; it does not repair the Mod, rewrite manifests or change generation/save rules.

The baseline `profile_1` quirk-localization snapshot contains 473 bilingual entries, 58 Simplified-Chinese-only entries, 3 English-only entries, and 11 entries missing both names. A missing display name does not make an otherwise valid quirk save structure unwritable.

## 4. Quantity-based items

### 4.1 Save-context selection

The quantity editor chooses one target from the selected profile before building its item catalog:

- If the hash-guarded game snapshot declares `inraid=false` and `raiddungeon=none`, the editor exposes `base_root.wallet` and `base_root.estate_items.items` from `persist.estate.json`. A remaining raid file is logged as residue, not selected as the target.
- If `inraid=true`, `raiddungeon` names a dungeon, and `persist.raid.json` exists, the editor exposes `base_root.party.inventory.items` from that raid save and writes actual inventory stacks into empty bag slots.
- Missing/invalid game-state fields, contradictory flags, or a missing raid file while the game still declares an expedition block quantity loading and preparation instead of falling back to another container.

File existence alone is not a scene boundary: force-town intentionally leaves map/raid files behind. An empty raid inventory is not treated as town. The UI and commit preserve the game hash established by scene resolution; scene changes after catalog load or preview invalidate the operation even if the old raid file remains. Removal of an active raid target also blocks the edit. An edit is never redirected from one container to the other, and a town write does not clean up raid residue.

Town mode includes gold, heirlooms, shards, memories, blueprints, standard Mod wallet currencies, The Blood, invitations, and base/DLC/Mod `estate` or `estate_currency` items. Raid mode includes active inventory definitions such as `supply`, `provision`, `gem`, `gold`, `heirloom`, `shard`, `quest_item`, `estate`, `estate_currency`, and equivalent Mod-defined types. Trinket instances—including trinkets already carried in the raid bag—remain in the dedicated trinket workflow because their save shape can contain per-instance state. Scores, progression counters, hero fields, and unrelated numbers remain outside the item editor.

Catalog membership is contextual, not mutually exclusive. The same exact definition may be valid in both town and raid catalogs—for example gold, heirlooms, The Blood, or a reachable Mod `estate` item—and each catalog reads and writes only its own independent saved amount. A town-eligible type is therefore never used as a blanket reason to exclude an otherwise valid raid item, or vice versa.

### 4.2 Catalog identity and reachability

The catalog merges effective active-content definitions with the selected save. Official definitions remain visible. Mod definitions are classified by a typed, directional reference graph built across the complete active-content overlay: registered gameplay roots may reference an item directly or through one or more loot tables. Localization, icons, manifests, inventory definitions, standalone effects and an otherwise unreachable loot table are not gameplay roots and cannot make an orphan definition self-validating.

Manifest eligibility is followed by the resource-specific loader rule. In particular, quantity references in hero/monster info/art/override files are eligible only at the same canonical actor paths used by the hero and encounter catalogs; a listed classification-folder copy cannot prove a gameplay reference. Independent provision JSON retains its own discovery rule. Darkest reference fields use native record/comment/last-field parsing, including bare strings, multiline records and NUL termination. See [the detailed actor/reference rules](resource-duplicate-semantics.md#85-manifest-eligibility-and-actor-definition-lookup-are-separate).

Curio CSV references come only from effective type-library blocks and consumed item/Loot columns. Notes files, unused text columns and rows outside a block do not establish a use path. Default Loot requires a nonzero weight; item interactions use the actual typed input and result fields. Invalid UTF-8 or an unresolved combined loot-code buffer keeps reference analysis incomplete. The shared darkest reader also stops at an invalid current declaration header instead of skipping to a later plausible item definition. See [curio reference columns](resource-duplicate-semantics.md#123-quantity-item-references-use-curio-consumer-columns) and [record/purchase identity](resource-duplicate-semantics.md#13-record-boundaries-and-persisted-upgrade-identity-2026-09-10).

Reachability is evaluated separately for the selected save context. The town catalog accepts town persistence or use paths—such as town-event currency rewards and costs, quest completion rewards, district/building costs and estate targets—plus definitions that explicitly allow manual provisioning (`estate_can_be_provision true`). A hero-starting item, raid skill, monster, quest provision, curio, scene or raid loot table does not by itself make an absent item useful to edit in the estate save. The raid catalog accepts paths capable of putting or retaining an item in the expedition inventory: explicit provisioning, provision/raid targets, skills, heroes, monsters, quest provisions, curios, scenes and their reachable loot tables. A town-only currency or quest-completion reward, event cost, building/district cost or estate target does not by itself make that definition raid-reachable. This is deliberately not a mutually exclusive type list: one exact definition can have independent valid paths in both contexts.

Within consumer-eligible plot-quest JSON, only the actual `plot_quests[]` branches supply roots: `quest.completion_reward.items_definition.items` and the supported reward/dependency inventories are town references; `additional_provisions.items` is a raid reference. The inventory maps use numeric slot keys. A `system_config_type`, `completion_reward` or `(type,id)` object elsewhere, including notes, does not establish use. Independent references to the same typed item in both contexts keep it available in both scenes. Hero-starting fixed amounts remain independent of estate quantities. See [structured JSON consumers and remaining uncertainty](resource-duplicate-semantics.md#153-item-json-roots-require-both-a-loader-query-and-a-consumed-structure).

An absent Mod-only definition that is unreachable in the selected context is hidden by default as suspected unused. “Absent” means that no physical matching entry exists in the current target save; it does not mean that the summed amount happens to be zero. `显示当前场景隐藏项` switches the table to an exclusive view of those hidden definitions; clearing it restores the normal context-reachable and persisted rows. Any in-scope quantity entry physically present in the selected target save remains visible even when unreachable or amount zero; carried `trinket` entries are the explicit exception and never enter this catalog. A missing provider is shown as save-only. Failed relevant reference reads and malformed or missing relevant active files fail open as analysis-incomplete rather than hiding uncertain definitions. Binary starting-save templates remain excluded from reachability roots.

Definition-backed names use `str_inventory_title_<type><id>` and the observed underscore variant. A save-only town wallet entry also probes the standard heirloom key because the wallet persists an heirloom ID as its `type`. Raid identity is always the exact `(type, id)` pair; the empty ID used by gold and food/provision is meaningful and is preserved.

### 4.3 Town write rules

| Content definition | Persistent identity | Town location |
| --- | --- | --- |
| `.type "gold" .id ""` | `type=gold` | `wallet` |
| `.type "heirloom" .id "blueprint"` | `type=blueprint` | `wallet` |
| `.type "shard" .id ""` | `type=shard` | `wallet` |
| `.type "estate" .id "the_blood"` | `type=estate, id=the_blood` | `estate_items.items` |
| `.type "estate_currency" .id "…"` | `type=estate_currency, id=…` | `estate_items.items` |

The input is an absolute target amount from `0` through `Int32.MaxValue`. Existing matching town entries retain all non-amount fields. Duplicate totals are redistributed without deleting entries. A missing wallet item uses `type/amount`; a missing estate item uses the repeated real-save shape: `id`, `type`, `amount`, `added_buffs=0`, empty `hero_name` and `previous_trinket_id`, `did_transform=false`, and `trinkets_gained_count=0`.

`.base_stack_limit` is not a town holding limit and does not clamp estate totals. `estate_can_be_provision` only describes whether the game can transfer that estate item through provisioning. It is not an estate-persistence gate: `Floss_1`, `Floss_2`, `Floss_3`, and `Livia_Stw` declare `false` and still occur in real estate saves.

It is also not a raid-reachability or unconditional catalog-hiding flag. A definition with `estate_can_be_provision=false` can still enter a raid through an independent hero-starting, skill, event, quest, or loot rule. For example, Ailuoli's `raid_starting_hero_class_item_lists` injects `Ailuoli_Stw ×3` and `Ailuoli_Stw1 ×1`, while a camping-skill loot table can create more, even though both estate definitions declare `false`. These paths make the items raid-reachable only: editing their town count changes only `persist.estate.json` and does not change the fixed starting amount, camping result, or an already-created raid bag. An absent definition supported only by such raid paths is therefore hidden in the town catalog but remains visible in the raid catalog. A physical estate-save entry remains visible even at zero so that it can be inspected or cleared. The UI labels the field as `不可手动配给`; the field alone neither proves usefulness nor forces a row to hide.

### 4.4 Raid write rules

- The input remains an absolute total for the exact `(type, id)` identity across all matching bag stacks.
- Increasing first fills existing matching stacks up to the active `.base_stack_limit`, then creates the minimum required new stacks in the lowest numbered empty slots.
- Resolve inventory config files through the base/DLC/Mod overlay, then apply `.max_slots` assignments for the exact type `raid` in native file/record order. A later assignment replaces the prior value; omission retains it. Do not select a highest-priority Mod again across different files. Unknown, invalid final, ambiguous-path, or changed capacity disables the write rather than assuming 16 or reviving an earlier value. See [inventory config semantics](resource-duplicate-semantics.md#7-inventory-system-capacity).
- No occupied slot is replaced. If the requested total needs more stacks than the available slots, preview fails with a concise full-bag message.
- Decreasing preserves retained stack objects and removes only surplus matching stacks. A target of zero removes all matching stacks—including physically present zero-amount residues—and frees those slots.
- A current stack already above the base limit is preserved; the editor does not normalize a value that may result from a legitimate runtime stack modifier. Newly created stacks never exceed the active base limit.
- A save-only raid entry with no active stack definition can be reduced or removed, but cannot be increased safely.
- A definition with unresolved same-priority providers—including providers of the same virtual path—remains visible but read-only. It is not downgraded to a writable save-only row, does not fall back to a lower-layer definition, and never uses an arbitrarily ordered stack limit to create or expand a stack.
- Newly created ordinary raid entries use the observed `id/type/amount` shape. Quest items use the same structural rule but receive a concise warning because an unrelated quest item can affect the current objective.

The old test that injected an unreachable `GabrielCampingQuirk*` estate residue proved only that estate storage does not become raid loot. It is no longer the mechanism used for raid editing. In raid mode the editor writes `persist.raid.json` directly, so the appropriate smoke test is to add an active supply such as a torch, load the same expedition, and verify its bag stack and later sale/consumption behavior.

### 4.5 Evidence and safety guards

The active town-side `profile_1` audit contains 128 definitions: 10 wallet entries, 115 `estate`, and 3 `estate_currency`. Its context-specific graph shows 45 normal rows and 83 absent context-unreachable Mod definitions in the exclusive hidden-items view, while existing `GabrielCampingQuirk1` and `GabrielCampingQuirk10` residues remain visible because they are physically present in the save. Current Gabriel, Floss, Ailuoli, and Livia acquisition chains remain confirmed active in the context where each one can actually create or consume the item.

Applying the same current 136-source overlay to a read-only historical `profile_1` expedition produces 253 raid definitions, 198 visible rows, 55 absent raid-unreachable definitions, no analysis-incomplete rows, and an effective 16-slot bag. The real `heirloom/koban` definition is visible and active in town (current amount 28) through estate, district and town-event evidence, but is hidden in the raid catalog when absent because none of those paths puts it into an expedition bag. By contrast, `gem/koban2` remains raid-visible through curio and monster loot tables. A historical bag containing two onyx and two physical torch stacks totaling ten keeps both definitions visible and reports the saved totals correctly. An absent official torch also remains visible, confirming that the absent-Mod reachability filter cannot remove standard supplies. The local corpus contains 16 decoded estate paths and 13 decoded raid paths; observed raid entry types are `gem`, `gold`, `heirloom`, `provision`, `quest_item`, `shard`, `supply`, and `trinket`.

The manual `heirloom/koban` experiment additionally established that two injected raid stacks were converted into two estate currency on retreat (26 to 28). Its supplied raid icon is literally a placeholder asset. The same retreat ended with a native `0xc0000005` crash, so direct raid insertion of this town-only currency is not approved as a normal editor path even though conversion completed; available evidence does not prove that Koban caused the crash.

- Catalog load hashes the selected target save before decoding so displayed totals belong to one coherent revision.
- Preview pins the town/raid context, `persist.game.json`, active sources, Manifest fingerprints, selected definition path/hash, and—in raid mode—the effective capacity file/hash and slot count.
- The proposed full document must survive DSON encode/decode unchanged, preserve revision bytes, and contain the exact target total before Apply becomes available.
- Commit requires the game to be closed, rechecks context and all hashes, backs up every current `persist*.json`, locks relevant content files, atomically replaces only the chosen target, verifies the final hash, and attempts recovery from the actual displaced version on failure without overwriting newer external data.
- Contract fixtures cover town and raid catalogs, exact identity, overlapping definitions, carried-trinket exclusion, stack filling, first-empty-slot insertion, full-bag rejection, physical zero-stack removal, conflicting item/capacity fail-closed behavior, context changes, capacity changes, DSON roundtrips, revision preservation, complete backups, and proof that a raid edit leaves `persist.estate.json` byte-for-byte unchanged.

Real game loading after a deliberate raid edit remains a separate authorized smoke test, now preferably operated through Computer Use. Contract validation proves the save transformation, not undocumented runtime side effects of every Mod item. This operator preference does not authorize new save targets or expand a test's write scope.

## 5. Trinkets

### 5.1 Catalog and ordinary inventory writes

- Do not reuse the quantity-item absent/unreferenced filter for trinkets. Native loot tables can request a rarity pool without naming individual trinket IDs; lack of an explicit ID reference is not proof that a trinket is unused. Manifest eligibility, a valid definition and active class requirements remain necessary. This is not a claim that every parsed trinket can naturally drop; see [duplicate-resource rules](resource-duplicate-semantics.md).
- Parse ID, Chinese name, English name, rarity, definition limit, origin, current override provider, and stateful shape.
- All `hero_class_requirements` must resolve to discovered active classes, as in the native trinket loader. Entries referencing absent classes do not enter the usable catalog.
- Resource JSON takes the first exact member within an object, including trinket root arrays, IDs, requirements and instance counters. Wrong-typed first values cannot fall through to later duplicates. This is independent of first-entry lookup for repeated trinket IDs. File discovery follows the native entries query rather than a literal suffix; see [catalog JSON and filename rules](resource-duplicate-semantics.md#16-trinket-and-map-json-members-catalog-file-queries-2026-09-10).
- An ordinary trinket is added to the trinket inventory in `persist.estate.json`; it is not equipped onto a hero.
- Inventory counts do not include copies equipped by heroes.
- `limit=0` means unlimited, not a limit of zero.

### 5.2 Definition limits and storage capacity are different rules

| Limit | Editor behavior |
| --- | --- |
| Per-trinket definition `limit` | Count existing inventory copies plus the requested addition. Exceeding it produces a prominent warning, but console mode allows the explicit write. |
| Total `trinket_storage.max_slots` | Count current and resulting inventory occupancy. Exceeding effective capacity is a hard stop. |

Total capacity uses the same native config loader as the raid bag, with exact type `trinket_storage`. After same-path file overrides, the last assigned `.max_slots` wins; later records without that field retain it. Repeated fields use the last field and native integer-prefix conversion. A final non-positive or out-of-range result, an unreadable effective config, a native type-hash collision, or unresolved file order disables writes. A later known valid assignment can replace an earlier invalid numeric value. Different values in different ordered files are not inherently a conflict. The editor must not silently revive an earlier capacity after an invalid final assignment. See [inventory config semantics](resource-duplicate-semantics.md#7-inventory-system-capacity).

### 5.3 Pristine stateful trinket creation

The editor creates only brand-new, unconsumed trinket instances. It does not expose remaining-use or transformation controls.

- Every new trinket receives the native common fields with `added_buffs=0`, an empty hero and previous-trinket ID, `did_transform=false`, and `trinkets_gained_count=0`.
- Definition `quest_uses=N` becomes `quest_uses_remaining=N` plus `used_during_quest=false`.
- Definition `trigger_limit=N` becomes `triggers_remaining=N`.
- Progressive art, trigger exhaustion transformation/destruction, slot blocking, and quest-complete effects remain definition-driven; they are not duplicated into the instance.
- Multiple requested copies are separate pristine instances, each starting at the full definition count.
- A present counter that is not a positive integer is invalid and blocks creation. Definition-only lifecycle fields do not by themselves make a trinket read-only.

These mappings are backed by naturally generated `tinker_box`, `rw_pyro_accelerant`, and `lifestyle_guide` save instances. Runtime decrement, art progression, transformation, and destruction remain game responsibilities after creation.

### 5.4 Preview invalidation

A trinket preview pins the profile hash, active providers, Manifest existence/hashes, the effective capacity file, the selected definition, and related semantic fingerprints. It is rejected if any relevant save, Mod/DLC state, order, Manifest, definition, capacity source, or effective result changes before commit.

## 6. Heroes

### 6.1 An effective class template is sufficient

- The roster does not need to contain an existing instance of a class before the editor can generate it.
- A Mod that changes a base class causes a new candidate to use the highest-priority effective class template, equipment, skills, upgrade trees, and generation rules.
- The save stores class ID, level, equipment rank, skill selection, quirks, and state. Skill damage, accuracy, class attributes, and effects are resolved by the game from the active template when it loads.
- The editor must not copy a second, stale set of base-game combat values into the hero record.
- A class-specific quirk granted later by a skill/effect or town event is not a required creation field. Normal gameplay continues to use the Mod's own grant path.

### 6.2 Natural generation and manual generation

| UI text | Meaning |
| --- | --- |
| `普通招募开启 / 编辑器手动` | The ordinary generation flag is enabled and the editor can create the class; other recruitment conditions still apply. |
| `普通招募关闭 / 编辑器手动` | `is_generation_enabled=false`; ordinary random selection excludes it, but explicit-class events such as `bonus_recruit` can still create candidates. Its complete template also supports editor creation. |
| `自然状态未知 / 编辑器手动` | The template does not declare the flag, so the editor does not invent a natural-generation answer. |

The displayed `自然怪癖范围` is only the positive/negative quirk range used by natural game generation. It does not constrain explicit editor selections.

### 6.3 Resolve level, equipment, and skills

1. Read `resolve_level_thresholds` from the selected profile's effective `game_mode` and construct level 0 through that mode's maximum.
2. The selected level determines `resolveXp`, `weapon_rank`, `armour_rank`, and the base HP of that armor rank.
   Equipment slots follow first insertion of each native `.name` (the first 63 UTF-8 bytes, case-sensitive); numeric name suffixes do not define rank. Repeated names update that slot and retain omitted fields. Hero info/art/override fields use native record, boolean, integer-prefix and float/percentage semantics, rather than the former permissive token dictionary. See [equipment/text rules](resource-duplicate-semantics.md#8-hero-text-equipment-slots-camping-pools-and-item-references).
3. Discover manifest-eligible `upgrades/**/*.upgrades.json` files (including enabled DLC mounts), resolve same-path providers and retain the native file enumeration order. For each exact tree ID, use the last full definition in that sequence, including repeated trees within one file. Filenames do not identify the hero: one file may provide trees for several classes, and a partial file replaces only the tree IDs it declares. Do not select an entire file by hero compatibility or highest Mod priority after file resolution. Bind equipment using `<class>.weapon` / `<class>.armour` and skills using `<class>.<skill>`; preserve each winning tree's source and the raw ID/code strings, including case and surrounding whitespace. Do not trim them into different identities or valid single-character codes. Tags do not replace these ID lookups. A parsed winning tree with unsupported requirements or a native-hash collision remains explicitly unavailable rather than exposing an earlier tree or entering the missing-tree compatibility path. Unreferenced trees do not become personal purchases. See [upgrade-tree implementation](change-history/hero-upgrade-trees-2026-09-08.md).
4. Write every weapon, armor, and combat-skill personal purchase whose prerequisite is satisfied at the selected level.
5. A level-0 base purchase record means requirement 0 in each combat-skill tree is purchased. It does not mean the hero owns only two randomly chosen skills.
6. Camping skills use the implicit `<class ID>.<camping skill ID>` tree and requirement code `0`. Unlock every recognized shared and class camping skill.
7. Unlocked and currently equipped are different. The equipped count still follows the class/Mod selection template; it is not hard-coded to four and does not equip every unlocked skill.
8. A skill defined only at level 0 may synthesize the observed `<class>.<skill>` / requirement `0` purchase when no upgrade tree exists. Tree-less multilevel skills may also use a continuous numeric purchase sequence, regardless of whether the class permits skill selection. The target levels must be contiguous from 0 and fit single decimal-digit codes. A valid same-class reference is a distinct existing multilevel combat tree with contiguous numeric codes, nonnegative/nondecreasing resolve prerequisites starting at 0, and matching contiguous skill definitions. Adopt a complete schedule only when strictly more than half of the valid reference skills agree; ties and pluralities without an absolute majority do not qualify. Invalid references, single-level/tree-less skills, equipment, and other classes do not vote. Apply that schedule only to tree-less skills, capped by the target's actual definitions; never infer from skill effects, weapon/armor rank, or a hard-coded vanilla schedule. If definitions or a sufficiently long majority schedule are unavailable, retain code `0` and explicitly warn that only the base unlock was written. Existing trees, including minority schedules, still follow their authored requirements. Also reject an existing tree with no purchasable requirement at the selected level, an unrepresentable requirement code, or colliding tree hashes.

Catalog availability is preflighted per resolve level through the same in-memory candidate factory, using blank initial quirks. It covers skill upgrade policy, class camping counts, names, skins, HP, and generation prerequisites in that path; it does not write a candidate. The UI shows genuinely available levels and the selected level's failure in the existing warning area. Explicit quirk choices, GUID allocation, existing save state, and transactional guards remain validated during the actual preview/write workflow. Tree-less multilevel skills report the implicit purchased skill tier, or the base-only fallback reason. This policy writes personal save purchases, not new Mod/guild tree definitions; the game's missing-guild-tree warning can still occur. Skill effects are left to the game; levels `0..3` mean four skill tiers, not a requirement to invent a fifth tier.

Evidence checked on 2026-09-06: historical EosNyx GUID 228 has `<class>.EosNyx_SK8` (hash `1823066919`) purchased with code `0` in `workspaces/evolution_verify/20260830_1254/persist.upgrades.decoded.json`; Kaltsit samples have `<class>.Kaltsit_Ranged_8` (hash `-2099543411`) purchased with code `0`, including the fixed local corpus's live-profile-1 decoded upgrades. HMSTerror GUIDs 883/884 also retain base purchases for SK4/SK5/SK6 in the 2026-09-06 live snapshot. These establish base unlocks, not automatic advanced skill progression. Doombringer was removed from this compatibility work at the user's request after unsubscribe; required class camping counts remain strict and no Mod-specific exception was introduced.

Do not describe these tree-less skills as having no skill levels. EosNyx SK8 and HMSTerror SK4/SK5/SK6 each define levels 0-4, with identical per-level fields and effect references in the inspected files. Kaltsit Ranged 8 also defines levels 0-4, but references `Kaltsit SKH Buff 1` through `5`, which apply different buff IDs. The user's recall test on resolve-level-6 Kaltsit GUID 894 applied the 5% base buffs, not 25%, while all 49 old-policy purchases remained intact. This disproved automatic higher-level selection for this case. Read-only analysis of the current game executable then established the consecutive purchase-code lookup, allowing the bounded same-class policy above without changing Mod files. A subsequent live test with new GUID 901 confirmed both 25% buffs on all four party members and retention of all 53 purchases, including Ranged 8 codes `0..4`. See [implicit skill progression](implicit-skill-progression.md) for versioned evidence and the remaining untested Mod/selection combinations.

### 6.4 Destination and roster behavior

- A candidate carrying `shard_hungry` is appended to `shard_hero_recruit.generated`; every other candidate is appended to `hero_recruit.generated`.
- Adding `shard_hungry` is therefore both the saved quirk choice and the explicit destination signal; a shard candidate must never also be appended to the ordinary pool.
- A full roster neither evicts an existing hero nor prevents a stagecoach candidate from being generated.
- An in-raid profile remains eligible for generation, but its prepared preview carries a warning that returning to town and advancing the week may refresh the stagecoach and remove the new, unrecruited candidate. Show this warning in the existing preview risk area, apply confirmation, and log for both ordinary and shard pools; do not block writing, recruit the candidate, freeze the week, or protect it from the game's refresh. Derive this advisory from the hash-guarded game snapshot's `inraid=true`, not from the presence of a raid file. A town save with leftover raid data does not receive the warning. Existing game-hash guards require a new preview if the scene changes before application.
- The preview and confirmation text must identify the selected target pool and report counts for that pool only.
- Candidate names, color variants, and equipped skills follow the active template and stable randomization rules.
- Camping generation pools use each file's raw `hero_classes` count and threshold (default zero), with no hard-coded shared-skill names. Later same-ID records may grant class access, but classification comes from the first stored record. Invalid first classification and native-hash collisions do not become generated purchases. See [camping classification](resource-duplicate-semantics.md#84-camping-classification-comes-from-the-first-stored-record).

### 6.5 Files involved in hero preview and commit

- `persist.town.json`: candidate in the selected ordinary/shard stagecoach pool;
- `persist.roster.json`: advance GUID state without adding a recruited hero;
- `persist.upgrades.json`: personal equipment, combat-skill, and camping-skill purchases for the same GUID.

All three temporary files must pass the DSON encode/decode consistency check. If any commit step fails, recover each replaced target from its exact displaced version. Preserve external updates instead of overwriting them with the earlier full backup; an incomplete or externally changed transaction remains blocked pending reconciliation.

### 6.6 `actor.buff_group` and initial HP

- Keep `actor.buff_group` empty. The game applies buffs from the quirk definitions; serializing another copy would risk duplicate effects.
- `actor.buff_group_next_guid=2` is the smallest value observed in real stagecoach candidates and has loaded successfully in the test profile.
- `current_hp` reflects only maximum-HP effects active at the instant the candidate is generated. Runtime condition changes remain the game's responsibility.

## 7. General quirk rules

### 7.1 Selection and quotas

- A new candidate may start completely blank; the editor does not need to simulate the game's random natural roll.
- The user may explicitly select zero or more quirks from the active catalog.
- Preserve exact quirk IDs through selection, refresh, exclusions, evolution targets, limit counting and JSON/DSON writes. Buff IDs and references also preserve case/spaces; exact repeated definitions still use the last complete object. Search formatting must not alter saved identity. Only exact `shard_hungry` selects the shard recruit pool. See [identity rules](resource-duplicate-semantics.md#11-exact-buff-and-quirk-json-identities).
- Maximums are 5 positive quirks, 5 negative quirks, and 3 diseases. Diseases have their own quota.
- Quota exhaustion is shown by the summary and click guard, not repeated as an unavailable reason on every catalog row.
- `incompatible_quirks` remains a hard pairwise exclusion with a specific explanation.
- Console mode ignores class `incompatible_class_ids`. Any structurally writable quirk may be assigned to any class.
- Being writable on any class does not guarantee that every effect is meaningful. A mode-dependent buff assigned to a class with no mode system may never activate.

### 7.2 Write statuses

| Status | Meaning |
| --- | --- |
| `Direct` | The shape and every rule that affects persisted `current_hp` are resolved. |
| `RequiresSaveContext` | The definition is writable, but the profile must be inspected for a limit warning. Exceeding it warns rather than blocks. |
| `Unverified` | A shape affecting a persisted field lacks an accepted model and currently blocks selection. |
| `Unsupported` | The known shape cannot be represented by the current candidate format. |

Non-HP buffs are not precomputed into the candidate. The game applies damage, accuracy, dodge, resistance, stress, healing, and other effects from the active quirk definition at runtime. The editor's failure to precompute those properties is not by itself a reason to block the quirk.

### 7.3 `singleton` and `roster_limit`

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
- `shard_hungry` has `roster_limit=6`; it routes the generated candidate to the shard stagecoach, while the base-game shard-mercenary recruitment path remains responsible for enforcing the recruited-roster limit.

### 7.4 Evolving quirks

1. Parse `evolution_duration_min/max`, target quirk, death-on-expiry, town-progression change, and item-use threshold.
2. Derive a stable integer `evolution_duration_remaining` from the generation seed and quirk ID within the definition's range.
3. Preserve fixed ranges and author-declared `0–0` exactly.
4. The game owns subsequent countdown changes, transformation, and effect application. The editor only initializes required persistent fields.
5. Continue to block definitions with missing, reversed, or non-integer bounds, or with neither a target nor an explicit death result.
6. Resolve every target along the active evolution chain uniquely. A missing/conflicting target or an invalid downstream evolution makes the source unavailable for explicit and random generation. Valid authored cycles and death-only outcomes remain supported; this checks definitions without simulating future gameplay.

Evolving quirks have persisted and transformed during user testing. Duration values must come from each Mod definition; they must never be replaced by a universal `3–15` guess.

## 8. Maximum-HP rules and final quirk audit

### 8.1 General calculation model

Let `B` be the selected armor rank's base HP, `F` the sum of all flat HP modifiers active at generation, and `P` the sum of all percentage HP modifiers active at generation:

```text
current_hp = (B + F) * (1 + P)
```

Flat additions occur before percentage multiplication. A percentage buff with `amount=0.10` means `+10%`. Preserve the floating-point result; do not round it merely because the game UI displays an integer.

Selection validation has two safety layers:

1. the actually active generation-time formula must be greater than zero;
2. every reachable combination of recognized conditional HP effects and constant effects must also keep maximum HP greater than zero.

The second layer is required even though conditional buffs do not contribute to initial `current_hp`. Otherwise the positive quirk `Ailuoli_Quirk2`, whose active condition applies `-50%`, can combine with other negative-HP quirks and drive the runtime multiplier to zero or below. Block the dangerous combination, not the individual quirk.

Treat additive terms, multipliers, or results within `1e-9` of zero as non-positive. This prevents decimal definitions such as `-50% -20% -20% -10%` from becoming a tiny positive multiplier only because of binary floating-point summation.

Evidence:

- A: in the ordinary `profile_0` stagecoach, a rank-2 Crusader with base 47 and `insipid_ship -4` saves `current_hp=43`; a rank-2 Jester with base 27 saves 23.
- A: a tested candidate with base 26 and `fragile -10%` saved `current_hp=23.4`. The game displayed maximum HP 24 and `基础 26 / 特质 -10%`, demonstrating no duplicate application.
- B: active Mod definitions explicitly declare `combat_stat_add`, `combat_stat_multiply`, their values, and their rules.
- C: the independent [Darkest-Dungeon-Unity](https://github.com/Reinisch/Darkest-Dungeon-Unity) reverse-engineered implementation uses `(RawValue + FlatAddition) * Multiplier` and applies/reverts conditional buffs. This corroborates the ordering but is not official source code.

`always` is active at generation. `no_trinkets` is also active for an unequipped stagecoach candidate. The game handles later equip/unequip changes; the editor does not serialize quirk buffs into `buff_group`.

### 8.2 Implemented model and historical catalog result

The application now retains every recognized `max_hp` Buff on a quirk, distinguishes `combat_stat_add` from `combat_stat_multiply`, preserves `rule_data.float`/`string`, and supports `always`, `no_trinkets`, `afflicted`, `in_mode`, and `lightabove` with `is_false_rule`. Constant effects determine the stagecoach candidate's full-health `current_hp`; runtime-only conditions are left inactive at generation and are evaluated only for reachable-state safety. Unknown HP operations, conditions, or malformed condition data remain Unverified.

Since 2026-09-10, Buff amount and rule thresholds first adopt native single-precision values; stat/rule strings keep their native case, whitespace and bounded bytes. `in_mode` conditions compare native hashes, so case-distinct modes cannot falsely cancel HP changes. The existing formula and preview calculation order remain; candidate serialization rounds final HP to the DSON float value. DSON round-trip validation compares only the new candidate's HP by exact float bits to accommodate Java-version decimal differences; all other fields, existing heroes and plain-JSON saves retain strict equality. Final HP outside the finite float range is rejected before generation. See [the scalar, identity and reference rules](resource-duplicate-semantics.md#14-buff-scalarcondition-identities-and-resource-eligibility-2026-09-10). This does not change the save schema or claim full native HP arithmetic emulation.

Quirk selection and Buff selection are separate lookups. Even with the correct last-matching quirk definition, choosing the wrong referenced Buff can produce an incorrect HP modifier or falsely block selection. Since 2026-09-09 the Buff resolver uses the last complete definition after native file overlays, replacing the former source-priority/conflict heuristic. A later speed-only definition contributes no HP; a later incomplete max-HP definition does not inherit the old amount. Within each resource JSON object, the first exact member wins, including a wrong-typed first member. Every repeated Buff reference is retained for HP preview, validation and candidate persistence; only set-like exclusion/tag collections deduplicate. See [JSON members and Buff occurrences](resource-duplicate-semantics.md#151-json-lookup-is-a-separate-layer).

A read-only rebuild against the `profile_1` baseline after implementation produced the following result:

| Category | Count | Current status |
| --- | ---: | --- |
| Direct | 520 | Writable now; includes the 26 newly modeled HP entries |
| RequiresSaveContext | 25 | Writable now, with a warning when needed |
| Unverified | 0 | None in this baseline |
| Unsupported | 0 | None |
| Total | 545 | Effective `profile_1` catalog |

This establishes parser and editor write-path support, not in-game behavior for every Mod. The user subsequently completed the positive-flat and mixed-HP test groups and accepted the available evidence without further repetitive quirk testing. The conditional runtime cases below retain their narrower evidence limits; they are not outstanding mandatory release tasks.

### 8.3 Thirteen constant flat-HP quirks

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

Audit conclusion: all are `combat_stat_add max_hp + always` and enter `F`. Negative flat HP has Level-A evidence. The subsequent ordinary-stagecoach `hearty_ship` test group was completed by the user; it is no longer pending. The earlier shard-stagecoach samples retained base HP 37/46 while carrying `hearty_ship`; those historical samples alone neither prove nor disprove the ordinary-stagecoach full-health policy.

### 8.4 Ten Mordekaiser mixed-HP quirks

Every quirk from `Mordekaiser_Quirk1` through `Mordekaiser_Quirk10` has two constant HP buffs:

```text
tier n: F = +4n, P = +4n%
initial full HP = (B + 4n) * (1 + 0.04n), n = 1..10
```

- I through IX evolve after `20–30`, `30–40`, `40–50`, `50–60`, `60–70`, `70–80`, `80–90`, `90–100`, and `100–120` respectively;
- X is terminal;
- each defines `evolution_town_progression_duration_change=0`; the editor initializes the declared interval and does not invent an additional decrement rule.

Audit conclusion: multiple HP buffs are not an unknown shape once represented as `F` and `P`. The original 268-file corpus had no Mordekaiser quirk instance. The user subsequently completed the mixed-HP test group (tiers I and X), and the follow-up save analysis found empty `buff_group` values. The frozen corpus must not be described as containing those later tests unless it is explicitly refreshed.

### 8.5 Three runtime-conditional HP quirks

| Quirk ID | Condition and HP effect | Candidate `current_hp` at generation |
| --- | --- | --- |
| `Octopus_Quirk5` (信徒 / Believer) | `afflicted`: `+5%` | Inactive before affliction; use `B` plus other constant modifiers |
| `Kaltsit_Quirk` (大地守望者 / —) | a mode exists and is not `KaltsitA`: flat `+20` | No active stagecoach mode; do not pre-add 20 |
| `Ailuoli_Quirk2` (黑夜骑士 / Knight of the Night) | `lightabove 1`: `-50%` | No raid-light condition in the stagecoach; do not pre-subtract 50% |

Audit conclusion: write the quirk record but not an initial HP adjustment or `actor.buff_group` entry. The game applies/reverts the effect when affliction, mode, or light changes. A class with no mode can carry `Kaltsit_Quirk`, but its +20 effect will not activate. Before writing, all reachable conditional HP combinations still participate in the non-positive-HP safety check, especially `Ailuoli_Quirk2` combined with other negative-HP quirks.

The corpus proves structural persistence for `Octopus_Quirk5` and `Kaltsit_Quirk`; it has no `Ailuoli_Quirk2` instance. Existing conditional samples are injured or at 1 HP and cannot prove a full-health formula.

### 8.6 Implementation result and validation record

All 545 baseline quirks now have an individual write path: 520 Direct and 25 context-warning entries. No individual item remains blocked merely because its known HP rule was not modeled; a specific selection whose reachable HP can become zero or negative is still rejected. Contract tests cover flat and percentage ordering, multiple Buffs, all three runtime condition shapes, the selected hero level's armour HP, and a conditionally dangerous `-100%` boundary combination.

The original live-test plan is retained here as a validation record, not an instruction to repeat completed work:

1. **User-completed:** blank versus `hearty_ship` ordinary-stagecoach candidates, covering the positive flat-HP path.
2. **User-completed:** `Mordekaiser_Quirk1` and `Mordekaiser_Quirk10`, covering `(B+4)*1.04` and `(B+40)*1.40`; subsequent analysis recorded empty `buff_group` values.
3. **Not individually live-verified in this record:** `Ailuoli_Quirk2` across raid light `>1` and extinguished light `0`. Its definition, generation policy, and reachable-state safety are covered by the implemented model; do not invent a live result.
4. **Automated contract coverage:** combinations that can reach maximum HP `<=0` are rejected before preview, including the conditional negative-HP boundary.
5. **Optional future investigation only:** Kal'tsit mode transitions and effects on a class without modes.

The user explicitly ended further repetitive quirk verification after the first two test groups. Reopen a test only for a new failure, changed rule, or separately authorized investigation.

## 9. Transactions, backup, and explicitly unaffected state

1. Safe preview operates only on a temporary workspace.
2. Any relevant live-save or content-semantic change after preview invalidates the commit.
3. Before commit, verify `Darkest.exe` is not running and back up all `persist*.json` files in the profile.
4. Use atomic replacement with a unique copy of the actual displaced destination. Verify that displaced hash as well as the installed hash; hold the installed file against writes/renames through transaction verification. Recover only if the currently locked target is still the editor's installed version, copying the verified displaced bytes through that same write handle. Preserve any newer external target. Failed transactions retain displacement copies alongside the full profile backup; recovery I/O failure is reported explicitly, never described as a successful restore. Multi-file hero writes recover earlier targets in reverse order and retain an unfinished transaction if any target cannot return to its original state.
5. The item, trinket, and hero-generation workflows do not modify game content files, Mod files, week count, quests, town-event history, or existing heroes. The separate battle workflow has an explicit managed Encounter Bridge exception: it creates/updates its own local Mod and selected-profile activation entries, but does not edit unrelated Mods. See [battle-map rules](battle-map-editor-design.md).
6. Event triggering, including `从陵墓归来`, remains a separate future feature and does not belong in the hero-generation chain.
7. Standalone map curios/treasures and ordinary traps/obstacles edit only the guarded map file. Pools come from effective canonical `dungeons/<region>/<region>.props.darkest` paths; manifest eligibility alone does not make extra basenames part of these pools. Room and hall curios may be selected across active regions using exact resource hashes, retaining authored spaces and case; trap/obstacle choices are current-region only. Regional traps/obstacles go straight from the menu to write confirmation. One eligible resource is selected directly; multiple resources use positive native `.chance` values. Each raw type occurrence retains the full record weight, including duplicates. Invalid/zero weights and rejected resources do not participate; an empty pool stays disabled without cross-region fallback. Curio pool IDs must resolve through active `curio_props.csv` mappings to a type in active `curio_type_library.csv`; aliases are valid, names use the mapping's exact UI key, and both CSV families participate in the stale-content guard. Whole-cell replacement clears conflicting props and battle indexes, unlike the battle-attachment action, which retains the encounter. No additional Bridge or Mod activation is created for hash-addressable props. Missing/ambiguous/scripted trap and obstacle resources are diagnosed and withheld. Detailed bindings, bounded native parsing and conservative read-failure policy are in the [battle-map rules](battle-map-editor-design.md#standalone-map-content-2026-09-06).

### 9.1 Diagnostic log contract

- Each application run creates one unique `app-yyyyMMdd-HHmmss-fff-PID-random.log` under the existing project/standalone log directory. The file is initialized once and reused, including after midnight; multiple instances/restarts never intentionally share it. Appends within a run are serialized. Existing daily/session logs are neither cleared, renamed nor rewritten. Startup identifies the current file path, and a logging failure must not replace the original application error.
- Logs distinguish informational messages, warnings, errors and technical trace entries. Manifest preparation progress is informational; missing/unreadable manifests prevent catalog loading and are reported as errors. Unknown catalog issues and preview risk advisories remain warnings; presentation grouping does not alter the original catalog issues or any safety guard. Status helpers preserve explicit levels. Handled discovery, preview, quirk-dialog, load and commit failures record error summaries and their exceptions; missing-file/incomplete inventory diagnostics are warnings, not ordinary scan progress.
- Count saved enabled-Mod records, resolved Mod sources, unresolved/unsupported records and merged duplicate directories separately. The saved-record total includes both parsed records and malformed/incomplete records skipped by the resolver; show these two counts explicitly instead of calling rejected records missing installations. Total active sources also include Base, mode and DLC layers. An enabled record does not prove an installed source exists, and a deduplicated record is not a missing Mod.
- File inventory is diagnostic only. Manifest-external data/localization files are labelled `only inventoried, not loaded`; missing manifest references are counted separately, with the supported content-range subset identified. Only a missing file whose exact basename is `desktop.ini` (case-insensitive, Windows folder metadata) is informational; missing definitions, translations, assets and other names remain warnings. Severity is supplied with structured entries, not inferred from translated prose. These counts neither admit files nor prove that every candidate can be parsed or written.
- Within each catalog-load diagnostic batch, merge localization failures by Windows file path and exact failure reason; list all reporting modules. Main catalogs, battle encounters and room attachments share the same batch, including partial LOC/LOC2/XML-read diagnostics. Different causes at one path remain separate. Each successful reader contributes immediately, before an aggregate wait can propagate a sibling's failure; collection is synchronized without changing reader ordering or swallowing failures. Flush collected diagnostics once, including on failure/cancellation; a standalone battle load owns its own batch. The main summary reports combined diagnostic counts, not separate counts that could be mistaken for distinct failures. Name lookup reports compiled-table evidence only after the existing reader obtained valid requested entries from the same active source. Mere installation or an unlisted file is not evidence. Such entries do not prove that every name from a failed file is covered; random hero-name candidates continue to use the existing XML reader. Independent later load/refresh batches retain their own diagnostics.
- Encounter counts label proven direct indexes as `without Bridge` and list global Bridge candidates separately. Zero direct indexes does not mean zero Bridge candidates. Both are catalog counts, not approval to write in the current combat/save state; all existing write checks still apply.
- Current standard mash indexes follow first-provider file slots, native per-source order, and independent hall/room/boss counters. Bridge uses its dedicated managed mash file, proves its append position and validates retained bindings before installation. Unverified DLC mount order and unknown native row counts stay guarded. Five-or-more authored actor IDs are reported and read as the native first four; source files are preserved. The 2026-09-08 runtime test confirmed missing-ID and empty rows retain positions, while known size totals above four do not. The current empty-row guard blocks affected writes instead of silently shifting indexes. See [native evidence and compatibility boundaries](encounter-runtime-order.md) and [the measured row matrix](resource-duplicate-semantics.md#5-index-occupancy-of-invalid-encounter-rows).
- Record executable and Core module build identifiers, version, executable/assembly paths and runtime identity at startup. Build identifiers distinguish code changes even when the public version number is unchanged. Failure to collect identity must not prevent startup.
- Successful item/trinket/hero commit logs stand alone: operation/session ID, profile ID/path, target, before/after quantities or recruit-pool counts, and backup path. Record success immediately after verified commit, before UI refresh. An exception after that point is a post-commit UI failure, not evidence that the write failed. Do not infer rollback success merely from an exception. Battle edits and force-town recovery also record profile and operation context.
- Handled map and force-town failures record the full exception, phase, operation/session, profile path, committed state and available backup reference. Recovery errors identify retained backups/displaced files. Status text must distinguish post-commit refresh failure and incomplete recovery without promising unchanged or restored saves merely because an exception was caught.
- Map diagnostics keep a per-window baseline, reset on catalog invalidation or profile/context change. The first entry describes the snapshot; subsequent entries describe movement, battle-state changes, map-state changes, or changes confined to other saved data. Identical source pairs do not repeat diagnostics. New/cleared parser warnings are explicit, while each emitted update retains both full source hashes in a trace entry. This baseline never controls the live watcher, map rendering, or write guards.

## 10. Maintenance requirements

Whenever a catalog or save-write rule changes:

1. update the boundary between current implementation and audited future behavior in this document;
2. add a contract test, or record why only a live test can cover the behavior;
3. retain a detached evidence snapshot and SHA-256 rather than relying on the live profile;
4. model field type, condition type, and override semantics instead of hard-coding one example Mod;
5. do not treat an independent reverse-engineered implementation, author-facing tooltip, or personal recollection as sole proof of game behavior;
6. regenerate catalog statistics after the game or active Mod list changes; figures such as 545 quirks and 124 Mods are only this audit's baseline snapshot.

## Persistent expedition save directories (2026-09-10)

`persist.game.json` stays at the profile root. Its `base_root.raid_save` selects the relative directory containing `persist.map.json` and `persist.raid.json`; a missing field or empty string selects the root. The observed `profile_1` Courtyard save uses `plot_crimson_court_1/`. This is independent of resource/Mod directory layout and is not a hard-coded Courtyard/Farmstead rule. Farmstead's actual path has not been verified in this run.

All quantity, map, Bridge-maintenance, force-town and synchronization consumers use this selection. A specified but missing expedition never falls back to root leftovers. Game-save hashes guard route changes; snapshot identity also includes the relative directory. Reject absolute/traversal paths and symlinks/junctions. Backups preserve nested relative paths and exclude the game's `backup` copies; ownership history is scoped to the expedition directory and raid instance. Force-town changes only `inraid` and `raiddungeon`, preserving `raid_save` and persistent map/raid files. Evidence and regression coverage: [path fix record](change-history/persistent-raid-path-fix-2026-09-10.md).

Before removing or reindexing Bridge rows, inspect the other retained persistent maps for references to old Bridge indexes in the affected dungeon/difficulty/type tables. This applies in town and after entering another dungeon, including when `raid_save` has been reset to empty. If another map still depends on those rows, preserve the package; the current map's confirmed editor placements may be cleared first through the existing offline active-raid workflow. Visiting each dependent map can therefore make progress even when two persistent maps share a table. Do not edit an inactive map or retire its placements; a cleanup marker must match its directory and raid identity. Unchanged/metadata-only bindings and ordinary inventory/hero synchronization are not held by this rule.

Maintenance holds read locks on unchanged package files and the retained map/raid files used for this decision. Version 2 recovery journals also record these dependencies and their hashes: after interruption, recheck and lock them before restoring old map references. If a dependency changed, preserve the current files and keep recovery pending. Normal map logs include both file paths and treat a directory/raid-instance change as a new context even when file hashes match.
