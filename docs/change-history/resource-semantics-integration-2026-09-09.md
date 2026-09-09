# Resource Semantics Integration and Disease-Field Verification

Date: 2026-09-09. This change follows the user's approval to integrate the differences identified after the [2026-09-08 research](resource-semantics-live-2026-09-08.md). It also answers why correct quirk selection alone does not guarantee correct HP, why skill effects can supply quirk clues, and why empty encounter content does not remove a numeric slot.

## Additional Native Verification

Platform: Windows x64 build 27890, `Darkest.exe` SHA-256 `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`.

A temporary `DDSE Disease Field Probe` Mod contained only two effect files and its manifest/project metadata. It was enabled in profile_1 (`ERROR404`), then loaded through Steam into town. The user had authorized this profile and asked not to make another whole-profile backup. No heroes, encounters, rewards, or game functions were invoked by the probe. Memory access requested only `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ`.

Seven independent named Effects checked replacement, omission, explicit empty clearing, empty-then-set, never-set default, empty-only default, and repeated fields on one line. All seven native disease strings matched the [rule matrix](../resource-duplicate-semantics.md#3-handle-effect-and-buff-definitions-separately). Reads were bounded and checked for stable object bytes. The Effect loader passes object offset `+0x20F` to string reader `0x14036C950` at `0x1404E6558`; absence leaves the existing buffer untouched, while an explicit empty value writes NUL.

Local evidence is under the ignored workspace `workspaces/resource_semantics_integration_20260909/`: `prepared/`, `expected.json`, `live.runtime.json`, and install/cleanup receipts. These files record reproducible fixtures and process observations; they are not packaged product dependencies. Native disassembly is archived under the preceding experiment's workspace.

The game was exited through its menu. Steam recorded exit code `0xC0000005`, also seen during prior normal-menu exits; this is not claimed as a clean-exit test. The seven stable resource captures completed before exit. Cleanup removed only the owned temporary Mod and its activation/history records. Encoding was decoded again and compared before installation; every other profile file was unchanged by cleanup. The town UI showed 357,940 gold and eight heroes throughout the observation.

## Product Changes

- `HeroClassCatalog` resolves Buffs by the last complete native definition, rather than re-selecting a source or declaring ordinary duplicate files ambiguous. HP selection and generated `current_hp` consume those effective modifiers. Unknown/malformed HP conditions remain blocked.
- Level-0 skill `.effect` references append in the observed order, retain duplicates, and survive empty declarations and the info-to-override builder copy. Higher skill levels are not merged into level-0 quirk clues; UI summaries still deduplicate clues intentionally.
- Effect disease tracking distinguishes an omitted field from an explicit empty string. Referenced quirks are clues about later gameplay, not mandatory initial quirks or additional generated hero state.
- Recruit event results use the first matching event, including a winner with no recruits. Quantity-item reference analysis likewise ignores later same-ID result `data` while retaining the other event candidate fields. This does not deduplicate or simulate the random event pool.
- The UI describes ordinary recruitment as enabled/disabled rather than claiming a class has no game acquisition path.
- Explicit empty encounter actors retain indexes but cannot be placed or selected as Bridge sources. Normal later entries remain addressable; direct preflight, Bridge tail/index preservation and maintenance share the retained sequence. Missing fields, unknown sizes, hash/path collisions and unverified mount ordering retain their guards.

Manifest preparation, file overlays, ordinary item/trinket duplicate rules, quirk-definition selection, upgrade-tree resolution, and native battle ownership policy remain unchanged. No real profile map battle was created, replaced or removed for this change; map operations are checked with isolated fixtures.

## Validation

- The seven native disease cases passed, including a second check of the archived string bytes and the temporary-Mod cleanup receipt.
- The `--semantics` contracts passed for Buff/Effect/skill/event resolution, generated candidate HP, all three supported encounter types, direct and Bridge create/replace/delete, and unchanged maintenance.
- The `--catalogs` contracts passed for catalog loading, localization, synchronization, heroes, inventory and save writes. Existing assertions that expected skill-effect clearing or ambiguous same-ID Buff/event definitions were updated to the verified rules; they initially failed under the corrected behavior.
- A Release solution build passed with zero warnings and errors. The App output's Core assembly matches the validated Core output. `git diff --check` and 56 relative documentation links/anchors passed.
- The final full contract suite passed with exit code 0. Artifacts are under `workspaces/contract_tests/20260909_015220_083_1c2de46f34054ce8a95911e1d135ac17`; its complete log and final Release build log are in the integration workspace. Earlier full runs exposed the obsolete assertions described above and were not reported as passes.
- One fresh independently briefed read-only reviewer inspected the 26-file task-scoped diff against the captured dirty-worktree baseline, traced the affected HP/clue/event/encounter workflows, and checked the validation logs and archived native evidence. The review reported no blocking findings or unresolved candidate issues. It did not repeat live experiments or the long suites. No further code changes resulted from review.

## Remaining Boundaries

This is a bounded parser integration, not complete emulation of game mechanics. Untested Effect fields, other `*_effects` lists, higher-level quirk clue tracing, all AI/death execution branches, special recruitment scripts, a general Effect-to-item reference graph, and actual combat with missing/empty actors remain outside the verified coverage. Ordinary empty text lines are ignored; explicit `.types ""` is a retained encounter slot; an absent or valueless `.types` declaration remains guarded.
