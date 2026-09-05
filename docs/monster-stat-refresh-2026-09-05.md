# Monster stat regeneration — 2026-09-05

## Outcome and authorization

The user identified Workshop Mod `3705402044` (Monster attribute) as their own stat-generation script. They authorized running its existing saved configuration across all three scopes, then explicitly approved refreshing the original baselines for the four externally changed Mods listed below.

Executed the existing `auto_balance.py` unchanged using Python 3.14 and PowerShell 7.6.5. The game was not running. The script exited successfully and logged `Task complete!`.

| Scope | Sources | Eligible info files processed | High-risk info files skipped | Files whose content changed since checkpoint |
| --- | ---: | ---: | ---: | ---: |
| Game-root monsters → generated overrides in `3705402044/monsters` | 1 | 250 | 4 | 2 |
| Workshop monster Mods | 31 | 632 | 16 | 631 |
| Local `Shuiyue_Monster_Enhancement` | 1 | 480 | 21 | 0 |
| Total | 33 | 1,362 | 41 | 633 |

“Processed” is not the same as “changed.” The local Mod was restored from its recorded original backup before recalculation; its final content matches the pre-run output, so no second multiplier was stacked. High-risk base files are omitted from generated overrides; high-risk files in other Mods remain unchanged from their baseline. Final output filename sets match the checkpoint filename sets.

The two generated files that changed are:

- `monsters/ancestor_big/ancestor_big_D/ancestor_big_D.info.darkest`
- `monsters/ancestor_pod/ancestor_pod_D/ancestor_pod_D.info.darkest`

Both now preserve the current game source's `.is_valid_on_burn_dot True` field. All 250 generated outputs match the current game source outside the adjusted `stats:` line. The earlier audit's old-baseline concern for these two files is resolved at the generated-file level, not an in-game compatibility certification.

## Baseline refresh decisions

The following four Mods had changes relative to both the recorded original and last script output. No old backup was restored over their current files. With explicit user approval, current files became the new original baseline before applying the configured stat changes:

| Workshop ID | Mod | Observed change examples before the run |
| --- | --- | --- |
| `2433996706` | Here Be Monsters: Full Release | Burn effects added to swine-forger skills; three changed info files |
| `2937204043` | The Kraken - CC Addon | Tentacle skill effect changes; three changed info files |
| `3662096012` | The Crag Fiend Miniboss | Skill phase transition and sprite/atlas changes |
| `3669966489` | The Fiend Festival - New Dungeon | Added content and updated skills, stats and loot |

Mod `3571802725` had no script backup and received its first original backup. All other existing original backup archives were preserved. The four superseded archives and metadata files remain recoverable from the independent checkpoint.

## Checkpoint and validation

Checkpoint directory:

`E:\数据文件\SelfMod\DarkestDungeonSaveEditor\workspaces\monster_refresh_20260905\20260905_234608_c6421b7b\`

- `checkpoint.json`: exact original directories, per-file SHA-256, protected input hashes and backup locations.
- Each source subdirectory contains `monsters-before.zip`, existing script backup archives/metadata, and relevant source metadata. The generated-source snapshot also preserves the old log, scripts and configuration.
- `run.log`: copy of this run's script log.
- `verification.json`: source-by-source executable verification results.
- Checkpoint and verification artifacts occupy about 1.50 GiB and are Git-ignored.

Before execution, every snapshot archive entry and sidecar copy was hash-verified. Recursive targets were resolved under the configured Mod roots and checked for reparse points; existing script backup entries were checked to stay within each Mod's `monsters` tree.

After execution:

1. Reproduced exactly one application of the existing transformation from each current original baseline into isolated scratch files and compared every eligible output byte-for-byte. This checks execution against the supplied script, not an independent review of its balance formulas.
2. Separately checked that content outside `stats:` and all non-info resources matched the relevant baseline.
3. Verified refreshed/new original archives against the pre-run checkpoint, preserved other original archive hashes, and validated all script metadata hashes.
4. Verified 445 protected input files unchanged, including 254 game-root monster definitions and 124 save-directory files, plus scripts, configuration, manifests and project metadata.
5. Confirmed no processing error or failure in the run/verification logs. The verifier exited 0 with an empty error list.

For exact rollback, close the game and restore the affected source's `monsters-before.zip` and matching sidecars from this checkpoint to its recorded directory. Account for sidecars that were absent before this run. Do not use `auto_restore.py` as an exact checkpoint rollback: that script now sees refreshed baselines and clears generated overrides rather than restoring the previous generated files. No rollback has been performed.

## Effect on the editor's discovery discussion

Historical discussion as of this operation. The later 2026-09-06 policy removed unlisted XML supplementation and malformed-XML recovery, and added manifest-constrained legacy LOC support. The following observations and recommendations are not the current implementation contract; see [the current discovery and localization rules](content-save-rules.md#26-adopted-resource-discovery-policy).

The script does not regenerate `modfiles.txt`. The 250 generated override files therefore remain outside Mod `3705402044`'s five-entry manifest. Regeneration fixes outdated generated content; it does not resolve the editor/game discovery boundary.

At the time of this operation, the editor's business-definition scanners generally used listed paths when a manifest existed and standard content directories plus enabled DLC paths otherwise. Localization then had a deliberate exception for supplemental authoring XML. Consequently:

- A manifest can omit useful generated or manually added definitions. Whether omission from the editor is a runtime mismatch depends on whether the game actually loads those particular files; file existence or an ID reference alone does not prove that.
- Recursive no-manifest discovery can encounter leftovers retained under standard directories with valid suffixes. Parsing success does not establish that the definition is the author's intended current version. No blanket list of confirmed false inclusions was established by this audit.
- Supplemental XML helps recover names, but may itself be stale; compiled localization and authoring XML should retain separate provenance. A translated label is not evidence that its business definition is loaded.
- Same bytes, the same ID, and the same effective resource identity are different concepts. Region, language, difficulty, relative path and Mod order must be retained when resolving providers.
- The current encounter catalog uses these monster files to establish monster IDs and Boss tags; it does not serialize their HP/damage into a generated encounter. These 250 overrides introduce neither new IDs nor changed Boss tags, so they are not 250 missing encounter choices.

Recommended next implementation milestone: unified file inventory and manifest-difference diagnostics while retaining a separate effective catalog. Then admit additional candidates by verified file-family/loading rules and regression tests. Keep unexplained extras reviewable; do not infer obsolete status merely from absent references, names such as `old`, or equal hashes. Do not automatically execute a Mod's scripts or rewrite its manifest during catalog loading.

The game's official guide supports relative-path resource organization and top-of-list Mod precedence, but does not specify the treatment of unlisted files in `modfiles.txt`. Do not present the latter as proven by that guide. [Red Hook's official modding guide](https://steamcommunity.com/sharedfiles/filedetails/?id=819597757).

No editor production code, game save, script settings or manifest was changed in this task. Earlier uncommitted editor fixes were preserved. No game session or UI test was run. This was an operational script run plus analysis, not a new product-code implementation; a completion code reviewer and full editor build were not required.
