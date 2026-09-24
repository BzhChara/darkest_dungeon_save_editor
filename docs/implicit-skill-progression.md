# Implicit combat-skill progression

## Scope and result (2026-09-06)

This concerns generated heroes that define a combat skill without a corresponding guild upgrade tree. Skill selectability does not determine eligibility: `true`, `false`, and unspecified classes use the same implicit purchase policy. Their existing equipped-skill selection rules remain unchanged. This does not add or alter a Mod, an existing hero, or a building tree.

The previous base-only policy made Kaltsit's `Kaltsit_Ranged_8` usable but did not upgrade it. The user cast this recall command with generated level-6 hero GUID 894; the saved party received `Kaltsit_PROT5` and `Kaltsit_StressHealRec5`, each `0.05`. The Mod's defined level 4 instead references the `25` buffs. Purchases remained intact: only code `0` for skill 8. The captured report is `workspaces/hero_live_verification_20260906/recall_20260906_055044/analysis.md` (ignored local evidence).

## Native evidence, not a guessed save field

Inspected read-only: `_windows/win64/Darkest.exe` in the local game installation, SHA-256 `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`. Addresses below are RVAs; preferred image base is `0x140000000`. These findings are specific to this inspected build; they are not a claim that every game version has been tested.

| RVA | Observed behavior |
| --- | --- |
| `0x5C4DBF`–`0x5C4E3B` | Serializes selected combat-skill names with a shared zero scalar, not each skill's current level. |
| `0x5C5987`–`0x5C5ACA` | Reads the selected-skill map's keys, resolves them to class-skill indices, and appends those indices; the values do not drive level selection. |
| `0x58E178`–`0x58E32E` | Loads each purchase's instance number, tree hash, requirement character, and purchased flag into the purchase map without a guild-tree lookup in that load loop. |
| `0x5C718F`–`0x5C7237` | Hashes `<class>.<skill>`, supplies the hero GUID and actual class-skill variant count, then stores the returned character minus ASCII `0` as the combat-skill level. |
| `0x58EA10`–`0x58EAA5` | Queries consecutive purchased characters starting with ASCII `0`, stopping at the first unpurchased code or the supplied variant-count bound. Does not enumerate guild tree requirements. |
| `0x5C7940`–`0x5C7A1A` | The game's implicit unlock inserts the same class/skill hash and hero GUID with character `0` and purchased=true. |
| `0x6742A2`–`0x674344`, `0x6748E4`–`0x6748FC` | The separate guild UI looks up the authored tree and logs the missing-tree warning when absent. Save purchases do not create this UI tree. |

The relevant non-arena level lookup is equivalent to this simplified pseudocode (names are explanatory, not recovered symbols):

```text
lastPurchased = none
for level in 0 .. definedVariantCount - 1:
    if not purchased(classSkillHash, ASCII('0') + level, heroGuid):
        break
    lastPurchased = level
return lastPurchased, or locked if no base purchase
```

Thus writing only code `4` would not work: the lower consecutive purchases must be present. Changing a selected-skill map value from `0` to `4` would not establish an upgrade either. The implementation leaves selection serialization unchanged and writes the complete numeric purchase sequence through the existing writer.

The constructed class/skill hash target is limited to the first 63 UTF-8 bytes by native `0x14036B3A0`. Since 2026-09-11, both explicit and implicit combat purchases and the catalog's tree binding share that bound. Original skill IDs remain separate for variant metadata and majority voting. The same native consecutive-code lookup also applies to authored trees: their base code `0` must be purchasable, and an intervening missing code produces a warning stating the reachable tier. It does not authorize renaming or synthesizing authored codes. See [combat purchase consumption](resource-duplicate-semantics.md#842-combat-purchase-targets-and-reachable-tiers) for guards and implementation evidence.

The read-only reproduction helper is `workspaces/hero_live_verification_20260906/inspect_skill_binary.py`. It uses standard-library PE parsing plus the already-installed Visual Studio `dumpbin`, without attaching to a process, injecting, installing dependencies, or modifying the executable.

## Choosing a resolve-level schedule

The binary establishes how a saved level is read; it does **not** define a resolve prerequisite for a missing tree. That part is an explicit editor policy:

1. Keep actual skill levels from the effective, overlay-resolved hero catalog.
2. A genuinely level-0-only skill keeps exactly one base purchase.
3. For a missing multilevel tree, require contiguous actual levels `0..N` and at most ten levels (single decimal-digit codes). Do not gate progression on `can_select_combat_skills`.
4. Examine only this class's distinct present combat skills with actual multilevel upgrade trees. A valid reference has numeric codes contiguous from `0`, nonnegative and nondecreasing resolve prerequisites with base prerequisite 0, and contiguous skill definitions matching its requirement count. Invalid references, single-level skills, tree-less skills, equipment trees, and other classes do not vote. This reference filter does not waive normal validation of an authored tree's own purchases.
5. Group valid reference skills by their complete code/resolve-prerequisite schedule. Use a schedule only when strictly more than half of the valid references support it; one valid reference is sufficient. A tie or a largest group without an absolute majority does not select a schedule. Each distinct skill has one vote. Input order cannot determine the result, and authored minority trees retain their own requirements.
6. The target must fit the known schedule; buy every tier permitted at the requested resolve level up to its actual defined maximum.
7. If that cannot be established, retain the previous base-only unlock and a warning with the reason. Do not guess from weapon rank, armor rank, another class, or vanilla XP thresholds.

This policy compares levels and resolve prerequisites, not damage, healing, buffs, effect references, or whether an effect changes between levels. Effects are selected by the game from the authored skill variant. A skill with levels `0..3` has four tiers (three upgrades after its base tier); it must not receive a fifth tier merely because most other class skills have five. An existing tree may also expose fewer purchasable tiers than the definitions contain; its authored requirements remain authoritative rather than being replaced with the majority policy.

With the currently inspected three classes' consistent `0,1,2,3,5` resolve prerequisites, resolve levels `0,1,2,3,4,5,6` produce highest zero-based skill levels `0,1,2,3,3,4,4`. The mapping is read from their active trees, not embedded as a universal rule. At level 6, Kaltsit skill 8 gets codes `0..4`; skill 9 remains code `0` because it only defines one level.

## Validation boundary

- Contract coverage includes every selectable resolve level with true/false/unspecified skill selectability, equipped-count preservation, real catalog compatibility, strict majorities, ties, pluralities without a majority, duplicate-vote prevention, invalid/ineligible reference exclusion, same-class custom schedules, shorter target skills, single-level skills, missing metadata, definition gaps, unsupported extra tiers, invalid codes, mismatched reference counts, and absent combat reference trees.
- Candidate serialization remains unchanged. Tests exercise the existing GUID/hash purchase writer and the real DDSaveEditor DSON codec for town, roster, and upgrades using synthetic saves in the test workspace.
- Ordinary authored-tree purchases, weapon/armor rank, camping unlocks, pool selection, source-save guards, and backup/rollback are unchanged.
- Both the old 5% result and the new 25% Kaltsit result are live-tested. Newly generated resolve-level-6 Kaltsit GUID 901 applied `Kaltsit_PROT25` and `Kaltsit_StressHealRec25`, each `0.25`, to all four party members after recall. All 53 personal purchases remained, including skill 8 codes `0..4`; selected-skill values remained `0`. See the ignored local report `workspaces/hero_live_verification_20260906/recall_upgraded_20260906_061506/analysis.md`. This does not claim live coverage of every Mod, selectable tree-less skill, or majority-inferred schedule.
- Existing generated characters are not retroactively upgraded. The guild missing-tree message can remain even after the saved combat level is corrected; removing that message would require separate content changes, which this feature does not perform.

### Executed editor checks

- Release solution build: passed, zero warnings/errors after correcting a test-only helper-access compile error.
- Full contract suite, including the new three-file DSON round trip: passed. Run: `workspaces/contract_tests/20260905_220422_452_c305c255a5f24173a8813f191da889b7`. The unrelated symbolic-link inventory fixture was skipped because the OS privilege was unavailable.
- Changed C# files: whitespace verification passed.
- Active `profile_1` content: all 21 in-memory plans for EosNyx, HMSTerror, and Kaltsit at resolve levels 0-6 matched the expected per-skill codes. All 18 source save hashes remained unchanged. Report: `workspaces/hero_live_verification_20260906/progression_probe/20260906_060445/report.json`.
- At resolve 6, Kaltsit has 53 purchases instead of the old policy's 49: exactly four additional codes for Ranged 8. This is an editor-plan result, not a claim that the user's existing GUID 894 was modified.
- Initial independent read-only completion review: no task-scoped correctness or regression findings. The reviewer independently reproduced the two key native disassembly sections and checked the DSON/21-plan artifacts. The subsequent live 25% result is recorded above.

### Selectability and strict-majority update (2026-09-06)

- Release solution build: passed with zero warnings/errors after closing the locked editor and correcting a test's internal-method call.
- Full contract suite: passed after updating the old missing-tree rejection assertion to the approved base-unlock behavior. Run: `workspaces/contract_tests/20260905_223645_064_5405072c07cf4c5b80735e0127bfa27a`. The symbolic-link inventory fixture remains skipped for unavailable OS privilege; all skill-policy tests executed.
- New coverage verifies all three selectability states over resolve levels 0-6, strict-majority and no-majority cases, existing minority-tree preservation, voting eligibility, and a four-tier target capped under a five-tier majority schedule. The real DSON round trip uses a selectable hero with an unequipped implicit skill.
- Changed C# whitespace verification and `git diff --check`: passed.
- Read-only real-content recheck: EosNyx, HMSTerror, and Kaltsit all retained the expected 21 generation plans, and all 18 source save hashes stayed unchanged. Report: `workspaces/hero_live_verification_20260906/progression_probe/20260906_063646/report.json`.
- No new live game session was performed for majority inference or selectable tree-less skills. Existing heroes, Mod files, and live saves were not modified.
- Independent read-only review of the 11-file task-scoped diff found no actionable issues. It checked catalog-to-writer consistency, the DSON artifacts, and the stored 21-plan report; it did not rerun the full suite or perform gameplay.
