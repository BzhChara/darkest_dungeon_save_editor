# Contract test layout

`Program.cs` only handles the command-line boundary and reports an unhandled failure. `ContractSuite.cs` owns the intentionally serial execution order.

The suite is split by responsibility:

- `ContractFixture.cs`: isolated game, Mod, profile, localization, and DSON fixtures;
- `UiContractTests.cs`: XAML, theme, asset, and UI source contracts;
- `UiLayoutContractTests.cs`: fixed load footer, selected-row details for all three catalogs, responsive tab headers, minimal quirk layout, and themed checkbox contracts;
- `ContentDiscoveryContractTests.cs`: manifest-free enabled-DLC root discovery, overlay dependencies, XML/loc2 names, capacities, and exclusion of disabled/backup content;
- `ContentInventoryContractTests.cs`: diagnostic-only full active-Mod inventory, manifest differences, unlisted XML diagnostics, scope classification, read/cancellation/link failures, log routing, fresh reloads and unchanged catalog/source data;
- `LocalizationPolicyContractTests.cs`: strict manifest gating for XML/LOC/LOC2, malformed XML rejection, per-language format/provider precedence, enabled-DLC and manifest-free roots, and source-file preservation;
- `LegacyLocalizationContractTests.cs`: legacy LOC layout, multi-value hashes, colour controls, whole-file rejection for structural corruption, per-value rejection for invalid UTF-8/colour controls, and binary fixture construction;
- `LocalizationEntryIsolationContractTests.cs`: shared LOC/LOC2 bounded-value isolation, late structural errors rejecting all names, bounded diagnostic summaries, strict XML parsing with individual invalid-entry isolation, XML-only random-name consistency, same-language fallback, and source-file preservation;
- `HeroAvailabilityContractTests.cs`: per-level catalog preflight agreement with generation, missing skill/camping dependencies, and partially available levels;
- `BridgeClassificationContractTests.cs`: original classification preservation, authored zero-weight special rows, legacy metadata recovery, and read-only unknown-origin/corrupt-manifest handling;
- `BattleMapContractTests.cs`: map reading/monitoring, movement, hard deletion, battle placement, room attachments, global encounter catalogs, managed Bridge append/reuse, and force-town transactions;
- `RegionalMapContentContractTests.cs`: current-region trap/obstacle weighted selection, zero/invalid weights, rejected resources, duplicate rows, file/ID overlays, numeric boundaries, and refreshed catalog guard rebinding;
- `QuantityItemCatalogContractTests.cs`: town/raid item discovery and direct mutations;
- `QuantityItemSaveContractTests.cs`: guarded quantity preview and commit transactions;
- `TrinketCatalogContractTests.cs`: trinket overlays, localization, limits, and state definitions;
- `TrinketSaveContractTests.cs`: pristine trinket construction and guarded commits;
- `HeroContractTests.cs`: orchestration of the following hero-related modules;
- `HeroCatalogContractTests.cs`: effective hero definitions, overrides, localization, and progression catalogs;
- `HeroQuirkContractTests.cs`: quirk classification, HP rules, conflicts, and selection boundaries;
- `HeroCandidateContractTests.cs`: candidate serialization, progression, full skill unlocks, and initial quirks;
- `StagecoachHeroSaveContractTests.cs`: ordinary/shard pool routing, GUID/upgrade append, stale guards, and multi-file transactions;
- `RealModLocalizationContractTests.cs`: optional read-only probes selected by `DDSE_SCHINESE_LOC_PROBE_MOD_ROOT` (Workshop 1143685298: 43 invalid values skipped while known valid names remain), `DDSE_RULER_LOC_PROBE_MOD_ROOT`, `DDSE_LOC2_PROBE_MOD_ROOT` (Eos_Nyx), and `DDSE_RURUTIA_LOC2_PROBE_MOD_ROOT`;
- `ContractTestSupport.cs`: shared assertions and binary/image/localization helpers.

Keep the modules serial unless their fixtures are made independent. Several transaction contracts deliberately alter a shared file and restore it before the next assertion.

Run the complete suite from the repository root:

```powershell
dotnet run --project tests\DarkestDungeonSaveEditor.ContractTests\DarkestDungeonSaveEditor.ContractTests.csproj -c Release -- "<repository-root>"
```
