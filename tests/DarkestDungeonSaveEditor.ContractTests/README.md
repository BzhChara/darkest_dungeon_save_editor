# Contract test layout

`Program.cs` only handles the command-line boundary and reports an unhandled failure. `ContractSuite.cs` owns the intentionally serial execution order.

The suite is split by responsibility:

- `ContractFixture.cs`: isolated game, Mod, profile, localization, and DSON fixtures;
- `UiContractTests.cs`: XAML, theme, asset, and UI source contracts;
- `BattleMapContractTests.cs`: map reading, monitoring, movement, and deletion;
- `QuantityItemCatalogContractTests.cs`: town/raid item discovery and direct mutations;
- `QuantityItemSaveContractTests.cs`: guarded quantity preview and commit transactions;
- `TrinketCatalogContractTests.cs`: trinket overlays, localization, limits, and state definitions;
- `TrinketSaveContractTests.cs`: pristine trinket construction and guarded commits;
- `HeroContractTests.cs`: hero, quirk, progression, skill, stagecoach, and upgrade contracts;
- `RealModLocalizationContractTests.cs`: optional environment-selected real Mod probes;
- `ContractTestSupport.cs`: shared assertions and binary/image/localization helpers.

Keep the modules serial unless their fixtures are made independent. Several transaction contracts deliberately alter a shared file and restore it before the next assertion.

Run the complete suite from the repository root:

```powershell
dotnet run --project tests\DarkestDungeonSaveEditor.ContractTests\DarkestDungeonSaveEditor.ContractTests.csproj -c Release -- "<repository-root>"
```
