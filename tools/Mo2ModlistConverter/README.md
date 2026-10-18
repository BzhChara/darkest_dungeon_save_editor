# Darkest Dungeon Mod Order Converter

A standalone Windows utility for transferring enabled Mod membership and priority
between Darkest Dungeon's `persist.game.json` and an MO2 profile's `modlist.txt`.
It uses the editor's native C# DSON codec and has no Java dependency. It does not
modify or replace the editor, its MO2 compatibility patch, or the MO2 plugin.

## Run

Extract the complete package into its own directory and run
`DarkestDungeonModOrderConverter.exe`. Do not copy its DLLs into the editor.
The interface follows the Windows UI language by default: Chinese uses Simplified Chinese; other languages use English. The **Language** menu matches the editor and offers **Follow system**, **Simplified Chinese**, and **English**. Changes apply immediately without clearing inputs or previews. The selection is saved separately in `%LOCALAPPDATA%/DarkestDungeonSaveEditor/mod-order-language.json`; explicit choices override the system language on subsequent launches.
Detailed diagnostic messages are English.
The utility reuses the editor's textures, palette, buttons, and table styles.

1. Select the conversion direction.
2. Select the target profile's `persist.game.json` (binary DSON or decoded JSON).
3. Select the MO2 profile's `modlist.txt` and the instance's `mods` directory.
   The standard `profiles/<profile>/modlist.txt` layout suggests its sibling
   `mods` directory; check the path if your instance uses custom directories.
4. Click **Preview conversion**. Inspect the enabled list, priority, identities,
   and errors. Uncheck **Enabled only** to see disabled entries.
5. Click **Export new files** and select an existing parent directory. Each
   export creates a new folder. Existing destinations and source files are never
   overwritten. Changed inputs invalidate the preview and require another preview.

## Save → MO2

The tool matches each enabled native Mod to one installed MO2 folder. It enables
those folders in the save's priority order and disables all other regular entries.
Other installed folders absent from the profile list are added as disabled.
Disabled entries retain their relative order. Separator lines retain their file
positions, but their group headings may no longer describe the reordered Mods.

The export contains `modlist.txt`, the original list in `original/modlist.txt`,
`conversion-report.json`, and application instructions. Close MO2 and the game,
back up the target list, replace it with the export, and reopen the target profile.

Only enabled Mods are stored in a native save. It cannot recover the historical
ordering of disabled Mods. A save that contains `merge mod` cannot reveal the
individual Mods inside that container; use a native save from before migration.

## MO2 → Native save

This direction restores individual native Mod entries for launching the game
**without MO2**. It replaces only `base_root.applied_ugcs_1_0` in a save copy.
Enabled MO2 entries are written as consecutive indices, highest priority first.
Other save fields, including DLC settings and campaign progress, are preserved.
The output retains the input format (binary or decoded JSON). Binary output is
decoded again and compared with the complete intended JSON before publication.

The export contains `persist.game.json`, its exact original in
`original/persist.game.json`, `conversion-report.json`, and application instructions.
Close the game and MO2, back up the entire save profile, replace only
`persist.game.json`, then launch the game directly. Restore the original file to
undo the list change before playing further.

This workflow assumes MO2 and the game's Mods have already been synchronized.
No original Workshop or native local-Mod path is requested or scanned. The MO2
`mods` path is used only to match folder names with identity metadata. The utility
does not install/copy Mods, manage Steam subscriptions, copy `overwrite` files, or
reproduce the MO2 plugin's content merges; it transfers membership and priority.

## Identity and order

- Native Workshop entries use `source: Steam` and a numeric Workshop ID.
- Native local entries use `source: mod_local_source` and `project.xml`'s `Title`.
- MO2 entries use folder names. The utility reads `meta.ini` and either
  `project.xml` or `project.xml.mohidden`; it never unhides resource files.
- The Darkest Dungeon sync tool's explicit `isworkshop` flag takes precedence.
  A local Mod may retain a published Workshop ID. `General/modid`, URLs, folder
  names, and the sync tool's `local_id` are not used as native identities.
- Manual installations without sync metadata can be matched using their project
  title/ID. For reverse conversion, metadata must identify a single native source;
  a project containing both a title and a published ID without an origin flag is ambiguous.
  Ambiguity, duplicate identities, malformed entries, and unsupported `*` foreign
  entries block export instead of being silently dropped. Fix the reported mapping
  in your installation or resync its metadata, then preview again.
- Both the native enabled list (ascending numeric keys) and `modlist.txt` text
  order are highest priority first. The MO2 plugin processes the latter in the
  opposite direction internally; this is not a reason to reverse the exported
  text. See MO2's [profile writer](https://github.com/ModOrganizer2/modorganizer/blob/v2.5.2/src/profile.cpp#L226-L255)
  and [separator recognition](https://github.com/ModOrganizer2/modorganizer/blob/v2.5.2/src/modinfo.cpp#L60-L65).

## Build and test

From the repository root with the .NET 8 SDK on Windows:

```powershell
dotnet build tools/Mo2ModlistConverter/App/ModOrder.App.csproj -c Release
dotnet run --project tools/Mo2ModlistConverter/Tests/ModOrder.Tests.csproj -c Release
pwsh -File tools/Mo2ModlistConverter/Build.ps1
```

`Build.ps1` creates a separate self-contained Windows x64 package under this
tool's ignored `artifacts` directory. No new NuGet libraries are used; the .NET
runtime comes from the SDK's standard publish assets. The ordinary editor build
and release pipeline are unchanged.
