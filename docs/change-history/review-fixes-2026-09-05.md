# Review fixes — 2026-09-05

Baseline: `a9ee5d9`. This change set addresses the five functional findings in [the original review](review-2026-09-05.md). It does not redesign the UI, relax hero safety checks, or modify real profiles/Mods as part of development.

## Implemented scope

- R1: manifest-free local/Workshop discovery includes root standard directories and explicitly enabled DLC package/feature roots. Hero dependencies, trinkets, quantity definitions, both capacity catalogs, encounter tables, monster metadata, and display-name localization participate. Backup roots and disabled DLC remain excluded; overlay priority and same-priority conflict behavior are unchanged. The existing `.loc2` language and top-level-only restrictions are retained.
- R2: quantity catalog/preparation resolve the game snapshot's `inraid` and `raiddungeon`. Town with raid residue exposes estate quantities and preserves the residue. Contradictory/missing state or missing active raid data fails closed. UI/commit retain game-hash guards, so force-town after preview invalidates the raid edit even if the raid file remains.
- R3: each hero/level is preflighted through the actual in-memory candidate factory with blank quirks. Available levels and selected-level failure reasons are shown before preview; the preview button is disabled for a failed level. The four reported Mod classes are not force-enabled: multilevel skill trees and class camping counts remain required. Quirk selection and save-dependent transactional checks still run at preview/write time.
- R4: hero/trinket filters match displayed provenance as well as internal source IDs, matching the existing item behavior.
- R5: new managed Bridge entries retain their original classification separately from carrier weight. Reads verify package identity, mash hash, indexes, and composition. Legacy version-3 records without the optional field recover from original rows or source/roaming metadata. Unresolvable legacy rows are excluded from generation choices with diagnostics, never guessed or removed from existing maps. Loading does not migrate a package or reassign indexes.

## Code and workflow map

- Discovery and names: `ContentFileOverlay.cs`, `ContentLocalizationCatalog.cs`, the hero/quantity/encounter source-discovery partials, `TrinketCatalog.cs`, `TrinketStorageCatalog.cs`, and `RaidInventoryStorageCatalog.cs` share the allowed fallback roots.
- Quantity loading and transactions: new `QuantityItemSaveScene.cs`, `QuantityItemCatalog.cs`, `SaveEditService.QuantityItems.cs`, and `MainWindow.State.cs` align scene selection with the guarded game snapshot.
- Hero availability: new `HeroGenerationAvailability.cs`, `HeroClassCatalog.cs`, `Models.cs`, `MainWindow.RowModels.cs`, and `MainWindow.State.cs` expose the factory's per-level result without changing generation algorithms.
- Search: `MainWindow.CatalogInteraction.cs` matches displayed hero/trinket provenance.
- Encounter categories: `ManagedBattleEncounterBridgeService.cs`, its new `Classification` partial, `BattleEncounterCatalog.cs`, and `BattleMapView.Commands.cs` preserve original categories and exclude unconfirmed carriers from generation choices.
- Tests: new content-discovery, hero-availability, and Bridge-classification modules; extended quantity/UI contracts and fixture/suite wiring. Rule, design, review, and test-layout documents describe the changed behavior.

Existing save backup, rollback, game-running guards, item/trinket construction, hero skill/quirk rules, and map move/delete/force-town write formats are unchanged. The only scene-routing change concerns quantity editing; this is not a new map or return-to-town implementation.

## Validation

- Release build: 0 warnings, 0 errors.
- Full contract suite passed, including new enabled/disabled DLC fallback, residue-town writes, stale scene rejection, invalid scene boundaries, per-level hero preflight, provenance bindings, and Bridge classification/legacy recovery checks.
- Final passing fixture run: `workspaces/contract_tests/20260905_084948_828_eabbedd8485f4b10a1e9b9488d6acee4` (ignored local artifacts), after both reviewer findings were corrected.
- Earlier checks exposed a test helper's inaccessible internal JSON reader and a malformed missing-flag fixture encoded as null. The helper now uses the public JSON parser; the fixture genuinely omits the flag. A build-server run also ended with a nonzero status and no diagnostic; a single-process build with build servers disabled succeeded.
- Changed C# files were whitespace-formatted successfully, and the final scoped `--verify-no-changes` check passed. Sandbox formatter attempts could not connect to the build-host pipe; the same scoped operations succeeded with approved escalation.

## Independent review

One fresh read-only reviewer found two task-scoped gaps: missing `InvalidDataException` handling for invalid Bridge manifests, and the display-name localization scanner still omitting manifest-free enabled DLC roots. Both were independently reproduced with failing regression tests, then corrected. The new checks cover malformed JSON, mismatched hashes, out-of-range carrier indexes, and XML/loc2 names for heroes, quirks, trinkets, quantity items, and monsters. Corrupt Bridge classification metadata now produces diagnostics and suppresses unconfirmed carriers without aborting unrelated catalog discovery or rewriting package/map files. No other substantive findings were reported; the final extended suite passed. These bounded corrections did not trigger a recursive completion review.

## Read-only current-profile check

The post-fix probe used the current `profile_0` and `profile_1` without modifying any source profile or Mod. Every existing `persist*.json` file had the same SHA-256 before and after the scan. Both profiles currently declare an expedition; this probe is not a real town-transition test.

| Profile | Active sources | Heroes | Trinkets | Quantity definitions | Unclassified managed rows in current table |
| --- | ---: | ---: | ---: | ---: | ---: |
| `profile_0` | 102 | 44 | 990 | 109 | 0 |
| `profile_1` | 139 | 51 | 1143 | 253 | 0 |

The preflight reports the existing missing multilevel purchase trees for `EosNyx`, `Kaltsit`, and `HMSTerror`, and insufficient class camping skills for `Doombringer`. The first two are active in both profiles; all four are active in `profile_1`. Each profile has 15 hero and 490 trinket displayed provenance labels matching `原版`, which the updated predicates include. These counts describe this snapshot only.

The diagnostic project and decoded copies are ignored under `workspaces/review_fix_20260905/`. The initial scan was `20260905_084101`; the final post-review scan `20260905_085231` confirmed the same counts and unchanged profile hashes.

No game launch, desktop smoke test, real save write, or real Bridge installation/update is part of this validation. The shared schemas remain backward-compatible; there are no new dependencies or environment configuration changes.
