# Obsolete-Rule Audit: 2026-09-09

## Request and checkpoint

The user requested a commit, followed by an audit of changes made before the native loading rules were understood, with removal of substantiated incorrect or obsolete logic.

The pre-audit working tree was committed as `3d2bd9c` (`feat: prepare Mod manifests and integrate verified resource semantics`). It was clean after that commit. This report describes the subsequent task-scoped changes, not a rollback to older unverified loading behavior. No live profile, game Mod, game setting, or map encounter was changed during this audit. No dependency was added.

## Newly corrected: inventory capacity

`TrinketStorageCatalog` and `RaidInventoryStorageCatalog` still applied an older two-stage winner policy: after file overlays they selected the highest-priority source again across different files, then rejected differing capacities at that priority. Each also maintained a separate regex parser requiring one quoted type and exactly one complete positive integer field. Those assumptions predated the native resource-rule investigation and survived the later shared file-order changes.

The exact native config loader contradicts those assumptions. It reuses a config object by type hash and applies each present field to that object in native file/record order. The maximum-slot field uses the last-field helper plus `atoi`; omitted fields retain prior values. The [current rule document](../resource-duplicate-semantics.md#7-inventory-system-capacity) records the executable hash, addresses, example, lexical details, and evidence boundary.

The old implementation was reproduced failing a fixture with two correctly quoted same-type declarations, first 4 and then 2: both capacity catalogs returned unresolved instead of 2. A separate initial fixture also demonstrated that bare-string types were incorrectly omitted. These were editor contract failures before the correction, not game launch failures.

Changes:

- Added shared `InventorySystemConfigCatalog`, preserving manifest-only Mod discovery, enabled-DLC path filtering, native file-slot order, and source/hash guards.
- Replaced both public storage catalog implementations with wrappers around that shared result. Their public result types and callers remain unchanged.
- Added an in-memory text entry point to the existing native field reader, so capacity parsing and provenance hashing use the same bytes without duplicating lexical rules.
- Deleted cross-file source-priority selection, false numeric-conflict detection, and the two obsolete capacity regex/comment parsers.
- Updated old tests that incorrectly required rejection of repeated fields and integer-prefix values. Final invalid values still reject; they do not revive preceding positive values.

Affected workflows are raid capacity display, town trinket capacity display, quantity/trinket preview, and the capacity recheck before save commit. The save mutation and rollback mechanisms themselves are unchanged.

## Removed: obsolete hero definition heuristics

Hero discovery already resolves a class through its canonical `heroes/<id>/<id>.info.darkest` path, with art and override files resolved independently. The later `SelectEffectiveDefinitions` pass still carried a former strategy for selecting sources again and comparing serialized semantic signatures across arbitrary candidate paths.

The audit removed that unused winner-election strategy, `GetHeroCandidateSignature`, the unused `GetQuirkSignature`, and their sorting helper. Repeated discovery of the same already-resolved physical file is deduplicated. If genuinely distinct canonical candidates reach the merge, the unresolved guard remains; semantic resemblance no longer supplies an arbitrary winner. A redundant colour-variation maximum over those repeated candidates was also removed. Asset-provider skin discovery and info/art/override application remain intact.

No hero level template, upgrade tree, quirk exclusion/evolution rule, or generation save shape was intentionally changed by this cleanup. Existing canonical-path, lower-priority override, manifest skin, and progression contracts cover this path.

## Earlier incorrect assumptions already removed before this audit

These were checked against `3d2bd9c`, its parent `4e1db6b`, earlier catalog history, and the current native-rule documents. They did not need another rollback:

| Former assumption | Current implementation retained |
| --- | --- |
| Every duplicate resource ID should be resolved by Mod position | Resolve files first; items/trinkets use first matching entries, quirks/Buffs use their verified last-object rules, and event results use first matching records |
| Any discovered actor filename can supply the class definition | Open canonical actor info/art/override paths independently |
| Select the best whole upgrade file for a class | Resolve complete upgrade trees by exact tree ID, last matching tree |
| Replace or deduplicate all repeated skill effect references | Append the tested level-0 `.effect` references; preserve duplicates and omitted/empty semantics |
| An entirely empty encounter makes the rest of its table unaddressable | Retain the proven empty index, prohibit that row's placement, and continue numbering later proven rows |
| Every historical Bridge composition must remain placeable before any new one can be added | Preserve index/ownership checks while applying the established maintenance policy; an unavailable historical composition does not alone veto unrelated valid placements |
| Missing Mod manifests require a second broad resource scanner | Prepare manifests first and require them for Mod resource loading; Base and enabled DLC still have their native physical discovery paths |

## Deliberately retained

These checks are not disproved by the newly established rules and must not be deleted merely because they resemble compatibility logic:

- **Quantity-item reference/reachability analysis.** Manifest inclusion establishes file eligibility, not an acquisition/consumption path or whether an item belongs in a town or expedition view. Existing save entries remain visible. No equivalent absent-ID-reference filter is introduced for trinkets, which can enter rarity pools.
- **Native identity and real ambiguity checks.** Same-path providers at an unverified equal priority, hash collisions, unknown monster sizes, and genuinely unproven encounter/mount ordering still require refusal or a bounded unavailable state.
- **Implicit skill progression and class camping requirements.** The per-tree last-match experiment did not disprove tree-less skill behavior; its dedicated evidence and contracts remain applicable.
- **Quirk exclusions, evolution targets and HP validity.** Correct duplicate lookup does not make missing targets or unknown HP-changing rules safe to use.
- **Bridge and save lifecycle protections.** Managed ownership/version, tail/index consistency, map generation identity, stale content/save checks, game-running checks, atomic replacement and recovery remain necessary.
- **Display representatives.** Choosing a provenance representative for identical map prop identities does not establish the game's value-loading policy. That display selection is not removed as though it were another capacity parser.

The audit does not certify every untested game field. Curio interaction duplicates, every skill/Effect list, all AI/death mechanics and special recruitment paths retain the explicit coverage limits in the resource-rule document. Complete runtime stack-limit emulation is also outside this capacity correction. No speculative universal first/last rule was added.

## Validation

- New `--capacities` contracts: passed after demonstrating the old implementation failure. Coverage includes AB/BA file order, same-path overlays retaining their original slot, a later different-file provider winning despite lower Mod priority, duplicate/bare/omitted/invalid fields, comments, exact type case, hash collisions, manifests, missing/shadowed/unreadable files, source hashes, and actual item-stack/trinket mutations with over-capacity rejection.
- Release solution build: passed with 0 warnings and 0 errors.
- `--catalogs`: passed, including hero paths/overrides/skins/progression, quantity/trinket save commits, synchronization, stale-content guards and localization. Artifacts: `workspaces/contract_tests/20260909_022718_634_266d0daaac12406792d1db827c196199`; log: `workspaces/obsolete-rule-audit-catalogs-20260909.log`.
- `git diff --check`, 51 relative documentation links/anchors, and the Release App/Core assembly hash comparison: passed.
- Full contract suite before the isolated NUL guard correction: passed with exit code 0, including direct/Bridge battle create/replace/delete, empty encounter indexes, DLC aliases, encounter maintenance, force-town behavior, guarded DSON writes and rollback. Artifacts: `workspaces/contract_tests/20260909_023039_470_c2652ab7c5cc4891aca91269911fe88d`; log: `workspaces/obsolete-rule-audit-full-20260909.log`.
- One fresh read-only completion reviewer found a P2 NUL-termination boundary in the new capacity path, with no other substantiated findings. The main agent independently reproduced `2 -> NUL -> 100` selecting 100 for both catalogs and checked the native stop branch. The capacity reader now rejects files containing NUL; regressions cover NUL both between records and inside a field body. This bounded rejection does not alter other resource parsers. No additional reviewer was used for this minor finding fix.
- After that correction, `--capacities` passed (`workspaces/contract_tests/capacities_95b8f636cf134fe085a2d57a705cef32`), `--catalogs` passed (`workspaces/contract_tests/20260909_024008_384_b96e868f3c4d4edb8cbe808d913fc94c`, log `workspaces/obsolete-rule-audit-post-review-catalogs-20260909.log`), and Release rebuilt with 0 warnings/errors. The affected capacity and real save-transformation paths were rerun; the unchanged battle group was not repeated. No substantiated review finding remains open.
