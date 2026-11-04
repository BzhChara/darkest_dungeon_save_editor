# Local storage layout

Keep reusable project code and confirmed rules in version control. Local evidence, user saves, external research material and generated output are ignored by Git and are not included in a new clone.

| Location | Purpose |
| --- | --- |
| `artifacts/` | Generated test runs, diagnostic projects, analysis results, review snapshots, build output and release packages. Keep a diagnostic run's internal files together, including any save copies needed to interpret it. |
| `.research/` | External tools, reference material, supplied bug reports and Mod samples. External source checkouts are grouped under `.research/source-code/`. |
| `local-analysis/save-corpus/` | The fixed, reusable save evidence corpus and its original manifest. |
| `local-analysis/save-snapshots/` | Retained standalone save captures and decoded copies from earlier investigations. These are historical inputs, not live profiles. |
| `backups/` | Retained manual profile backups, Git recovery bundles/repositories and retired documentation archives. Preserve complete backup sets and their records together. |
| `docs/` | Maintained, versioned rules, designs and release summaries. |

The former repository-level `workspaces/` has been retired. New tests and diagnostic scripts must use `artifacts/` or the matching tool's own `artifacts/` directory. The manifest-loading probe writes to `artifacts/manifest_loading_probe/`. The MO2 build script uses `patch/mo2/artifacts/tests/` for its contracts and `patch/mo2/artifacts/` for packages. Its standalone test executable defaults to `artifacts/` beside the executable unless an explicit output directory is supplied.

Do not treat `artifacts/` as automatically disposable: historical diagnostic runs can contain the only retained copy of save evidence. This organization moves files without deleting saves or backup sets. Storage changes have a local path mapping and SHA-256 verification record under `artifacts/workspace-organization-20261025/`. Raw saves, historical logs, result manifests, recovery records and external source trees retain their original bytes; paths embedded in those records describe their original capture location. Maintained documentation and executable diagnostic helpers use the relocated paths. Code snapshots and previously built binaries are historical evidence, not current tool entry points.

This layout applies to the development repository. The installed editor still uses `%LOCALAPPDATA%/DarkestDungeonSaveEditor/workspaces/` for decoded working files, previews and the default MO2 cache, and `%LOCALAPPDATA%/DarkestDungeonSaveEditor/backups/` for guarded-write backups. Synthetic tests can model those names inside their own artifact directories. Do not move or rename the installed editor's application data as part of repository housekeeping.

The local `artifacts/relocated_paths.py` helper resolves recorded old paths through the migration map when an archived evidence reader needs them. It translates paths in memory and does not rewrite the original JSON or logs. Historical verification scripts can still require their original commit, binaries and expected results; updating their paths does not make those old experiments a current regression suite.

## Retired history archive

On 2026-11-03, the former `docs/change-history/` directory was frozen into `backups/change-history-20261103.zip` and removed from the working documentation. The archive preserves all 97 original files (880,679 uncompressed bytes), including the historical index. Its companion `backups/change-history-20261103.manifest.json` records the ZIP SHA-256 and each file's original relative path, byte length, SHA-256 and modification time. Every archived file was checked against its source before the original directory was removed. Both files are ignored local backups and are not included in a new clone or release.

References marked `archived record` in current rule documents identify entries named `docs/change-history/<filename>` inside that ZIP. They preserve provenance without linking to a removed working file. The records, their old maintenance instructions and embedded paths describe their historical context; current behavior and maintenance policy come from the maintained documentation. Existing raw experiment outputs remain in their separate local evidence directories.

For recovery, verify the ZIP's SHA-256 against the manifest, extract into a new temporary directory, and check the extracted files against the per-file hashes. The ZIP retains the original relative directory layout. Keep the archive and manifest together; recovery does not reinstate the retired reporting workflow.

Do not recreate `docs/change-history/` or maintain per-round audit and per-fix history reports. Update current rules/designs in `docs/`, executable regressions in `tests/`, and user-facing release summaries in `docs/releases/`. Record new durable research conclusions and their evidence limits in the relevant current rule document, with necessary raw experiment output under `artifacts/`.
