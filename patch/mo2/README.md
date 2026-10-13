# Optional MO2 compatibility package

This is a replacement-component package for **DarkestDungeonSaveEditor 0.1.0-beta.3**. It is not an editor plugin. The ordinary editor source and distribution do not include this adapter. The package contains `DarkestDungeonSaveEditor.Core.dll`, its Chinese resource assembly, an example configuration, and source code. It includes the same native C# codec as the matching editor; normal use does not require Java. The editor's explicit `--codec=java` reference option remains available.

## Supported setup

This adapter targets the `game_support_DarkestDungeon` module supplied in the tested **MO2_2.5.3b** bundle. That module creates a local game Mod titled **`merge mod`**, maps it into `mods/!!MOD`, and writes its final file list and merged files to MO2's `overwrite` directory. This is not a promise of compatibility with every MO2 game module or fork.

- Enabled Mods must exist in the configured MO2 `mods` directory. Their files are read directly from that directory.
- Original local/Workshop copies and `meta.ini` `source_path` metadata are not required. Mods installed directly into MO2 can be used with the same final-manifest rules.
- The selected save must already enable `merge mod` in the game's native Mod list.
- Use a regular MO2 profile with `+`/`-` entries in `modlist.txt`. Foreign/unmanaged entries and linked directories are rejected with a diagnostic.
- Trinket rarity files used for alias ordering must be UTF-8 or BOM-marked Unicode. The original module's automatic legacy-encoding detection is not reproduced.

## Install

1. Close the game and editor. Use an editor build matching `build-info.json`. Keep a copy of the complete ordinary editor folder so you can restore the exact original components.
2. Copy `payload/DarkestDungeonSaveEditor.Core.dll` into the editor folder, replacing the ordinary Core DLL. Copy the provided `payload/zh-CN/DarkestDungeonSaveEditor.Core.resources.dll` into the editor's `zh-CN` folder.
3. Copy `payload/mo2-compat.example.json` next to the editor executable and rename it to `mo2-compat.json`.
4. Set the three absolute paths to your actual MO2 **profile**, **mods**, and **overwrite** directories. For a portable installation these are commonly `MO2/profiles/Default`, `MO2/mods`, and `MO2/overwrite`. For a global instance or custom paths, use the actual instance directories shown by MO2. The adapter uses the configured profile; it does not automatically follow the profile selected in MO2.
5. In MO2, select that same profile and launch the game through MO2. The supported game module automatically refreshes `overwrite/modfiles.txt` and merged files during launch; no manual generation is needed. Save with `merge mod` enabled if necessary. Fully close the game before editing.
6. Start the editor normally, set the game and Workshop directories as usual, select the save, and load it. You do not need to start this compatibility editor through MO2.

Example configuration (JSON requires doubled backslashes):

```json
{
  "profileDirectory": "D:\\MO2\\profiles\\Default",
  "modsDirectory": "D:\\MO2\\mods",
  "overwriteDirectory": "D:\\MO2\\overwrite"
}
```

After changing Mod selection, priority, hidden files, or Mod versions, launch the game through that MO2 profile once and fully exit it before loading the editor. This refreshes the generated files automatically. `overwrite/modfiles.txt` does not record which profile generated it, so use the same profile configured in `mo2-compat.json`.

## How content is resolved

The final `overwrite/modfiles.txt` is the whitelist. Generated merged files come from `overwrite`; other bytes come directly from enabled Mods under MO2's configured `mods` directory. Those same files determine visible paths, enabled providers, and alias ordering. The adapter does not merge JSON itself or follow `source_path` back to original installations. Updating or removing an original local/Workshop copy does not change this MO2 content until the MO2 copy is updated. Other Mods enabled separately in the game's native list still use the editor's ordinary source resolution.

MO2 stores the profile list in reverse priority order ([MO2 2.5.2 profile implementation](https://github.com/ModOrganizer2/modorganizer/blob/v2.5.2/src/profile.cpp)). The adapter reconstructs low-to-high priority, applies overwrite last, preserves the supplied module's `999...` virtual names, and reproduces its trinket rarity dependency ordering. It exposes the result as **one local `merge mod` source in the save's existing slot**. The editor's native resource queries, duplicate-ID handling, catalogs, backups, and save-write checks then run normally. Other native Mods and DLC keep their original bindings.

Files with `.mohidden` stay hidden. There is no blanket removal of that suffix. The supplied scanner may still list hidden paths; these requests are omitted from the adapter's private manifest, including files inside hidden folders and `.git` directories. MO2's original manifest is never changed. Original rarity filenames are retained when the final manifest lists them alongside aliases. Missing visible providers, stale manifest sizes, missing generated merges, path escapes, and ambiguous virtual paths stop loading instead of exposing a partial catalog. An unrelated physical project also titled `merge mod` is rejected; the known `mods/!!MOD` slot and configured overwrite project are the only permitted physical counterparts.

## Cache and write safety

The adapter creates a complete private content copy under `%LOCALAPPDATA%/DarkestDungeonSaveEditor/workspaces/mo2-compat`. Large texture/audio packs require corresponding disk space and can make the first load slow. Unchanged inputs reuse the copy. A changed input creates a new snapshot; existing snapshots are not rewritten. Old snapshots and interrupted `building-*` directories are retained and can be removed manually while the editor is closed.

An optional absolute `cacheDirectory` setting can place this cache on a larger drive. It must be separate from the game, Workshop, and MO2 `mods`, profile, and overwrite directories.

Content checks hash definition/localization bytes from MO2's `mods` and `overwrite` directories, including changes that preserve file size and timestamp. Large visual/audio assets use size/timestamp checks because the editor does not interpret their bytes. Pre-write source validation rechecks the MO2 mapping; changes require reloading/recreating the preview. Original-copy changes and `meta.ini` sync metadata are not inputs to this cache. Existing game-running checks, save backups, and save-write guards remain active.

The adapter writes only its own cache. Its `modfiles.txt` is required by the ordinary catalog readers and lives outside MO2, so it does **not** need a `.mohidden` suffix. The adapter never restores metadata or generates manifests inside MO2/original Mod folders. Normal editor operations such as creating a managed encounter bridge retain their existing write behavior; newly created game Mods must be handled in your MO2 workflow as appropriate.

The package builder does not install the patch, change MO2 settings, edit saves, or start the game.

## Restore or update

Close the editor and restore the original Core DLL and its original Chinese resource assembly from your backup, or extract the matching ordinary editor release to a new folder. Remove the optional `mo2-compat.json` from that editor folder. The ordinary editor does not consume the MO2 cache. You can remove that cache separately while the editor is closed.

When updating the editor, install the ordinary release first and use a compatibility package rebuilt for that release. Do not carry an old replacement DLL into a new editor version just because the assembly version happens to match.

## Build and validate

From this repository, with Windows, PowerShell 7.1+, Git, and a .NET SDK supporting .NET 8:

```powershell
pwsh -NoProfile -File patch/mo2/Build.ps1
```

The script builds both Core variants, runs compatibility contracts and public API/version checks, then writes a new ZIP and `SHA256SUMS.txt` under `patch/mo2/artifacts`. It does not overwrite a previous package. The compatibility Core links ordinary Core sources and injects one source-resolution hook into a generated resolver under `obj`; unexpected upstream hook changes fail the build.

When pairing the patch with a packaged editor, add `-EditorDirectory <extracted-editor-directory>`. The script checks that package's Core version and public API and records its exact SHA-256 as `ordinaryCoreSha256` in `build-info.json`. Without this argument it compares against the local ordinary Core build.

The ZIP includes the ordinary Core inputs and compatibility sources used for that build. To rebuild the DLL directly from the extracted `source` tree:

```powershell
dotnet build patch/mo2/Core/Mo2.Core.csproj -c Release -m:1
dotnet build patch/mo2/Tests/Mo2.Tests.csproj -c Release -m:1
dotnet patch/mo2/Tests/bin/Release/net8.0-windows/Mo2.Compatibility.Tests.dll
```

The repository's regular contract groups can additionally verify native behavior:

```powershell
dotnet build tests/DarkestDungeonSaveEditor.ContractTests/DarkestDungeonSaveEditor.ContractTests.csproj -c Release -m:1
dotnet tests/DarkestDungeonSaveEditor.ContractTests/bin/Release/net8.0-windows/DarkestDungeonSaveEditor.ContractTests.dll . --overlay-slots
dotnet tests/DarkestDungeonSaveEditor.ContractTests/bin/Release/net8.0-windows/DarkestDungeonSaveEditor.ContractTests.dll . --content-sync
dotnet tests/DarkestDungeonSaveEditor.ContractTests/bin/Release/net8.0-windows/DarkestDungeonSaveEditor.ContractTests.dll . --manifests
```

An optional read-only integration probe consumes real MO2 inputs but writes only the configured cache:

```powershell
dotnet patch/mo2/Tests/bin/Release/net8.0-windows/Mo2.Compatibility.Tests.dll --real <mo2-compat.json> <game-directory> <workshop-directory>
```

Synthetic tests cover MO2-only Mods without original copies or sync metadata, original-copy independence, the final whitelist, priority, overwrite merges, generic and rarity aliases, hidden files, missing providers, traversal rejection, cache reuse, unchanged-size MO2 definition edits, stale preview rejection, ordinary saves, and localized resources. The local real-data probe verifies a 93-file skin Mod read from MO2's directory. Multi-Mod merges and aliases are covered by synthetic contracts; in-game end-to-end validation with the recipient's full MO2 setup is still required before treating this as a broadly tested release.

## License

The compatibility component is part of DarkestDungeonSaveEditor and uses the repository's GPL-3.0-or-later license. See the included `LICENSE` and `NOTICE`. The derived native C# codec retains robojumper's MIT notice under `tools/DDSaveEditor/LICENSE` and `source/src/DarkestDungeonSaveEditor.Core/Dson/LICENSE`. The supplied third-party MO2 program and game assets are not redistributed. Project repository: https://github.com/BzhChara/darkest_dungeon_save_editor.
