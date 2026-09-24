# Installing Darkest Dungeon Save Editor

## Requirements

- Windows 10/11, x64.
- A local Darkest Dungeon installation and save profile.
- Java 8 or newer, with `java` available on `PATH`. Java is not included. Open a new terminal and run `java -version` to check it after installation.

The Windows x64 release includes the .NET 8 runtime. You do not need to install the .NET SDK or Desktop Runtime separately. Keep all extracted files together, including the `tools` folder.

## Download and start

1. Open the [GitHub Releases page](https://github.com/BzhChara/darkest_dungeon_save_editor/releases) and choose a release. Versions marked **Pre-release** are testing builds.
2. Download `DarkestDungeonSaveEditor-v<version>-win-x64.zip` and `SHA256SUMS.txt` from the same release. The automatically generated **Source code** archives are for developers; they do not contain the runnable application.
3. Optionally verify the ZIP in PowerShell with `Get-FileHash -Algorithm SHA256 .\DarkestDungeonSaveEditor-v<version>-win-x64.zip`. Replace `<version>` with the downloaded version and compare the complete hash with `SHA256SUMS.txt`.
4. Extract the entire ZIP to a writable folder. Do not run the EXE inside the archive or copy it out alone.
5. Run `DarkestDungeonSaveEditor.App.exe` from the extracted folder. Starting with `v0.1.0-beta.2`, the compact **Language** menu offers English, Simplified Chinese, or the system default; changes apply on the next startup. Keep the included `zh-CN` resource directory. The original `v0.1.0-beta.1` ZIP uses Simplified Chinese only.

## First use

1. Close Darkest Dungeon before loading for the first time or applying changes, and keep a separate copy of the save profile you intend to edit.
2. Use **Auto discover**, or enter the game, Workshop, local-Mod and save paths manually. Check that the selected profile is the one you intend to edit.
3. Select **Load current save** and wait for loading and the first synchronization to finish. Missing Mod manifests may be prepared during this explicit load.
4. Choose the item, trinket, hero or map operation. Generate and inspect its preview, then confirm the write. Relevant save or content changes require a new preview.

Automatic synchronization reads save changes while the game runs. Applying changes requires the game to be closed. The editor creates a full profile backup before writes; some battle operations also create or update a profile-scoped Encounter Bridge Mod.

## Logs, backups and updates

- Session logs are in `logs` beside the EXE for a standalone installation. Logs can contain local paths and save details; inspect them before sharing.
- Workspaces and backups are under `%LOCALAPPDATA%\DarkestDungeonSaveEditor`. Keep the backup for any profile you edit.
- To update, close the editor and extract the new release into a new folder. Do not mix files from different versions. Existing backups remain in the location above.
- Report a problem through [GitHub Issues](https://github.com/BzhChara/darkest_dungeon_save_editor/issues), including the release version and reproduction steps.

## Scope and licenses

This is an independent offline editor, not an official Darkest Dungeon tool. Scripted encounters, special ambush/teleport behavior and hidden/story maps are not fully supported. Compatibility with every Mod is not guaranteed. See the [project README](https://github.com/BzhChara/darkest_dungeon_save_editor#readme) for the supported operations and write safeguards.

The editor is licensed under `GPL-3.0-or-later`; see `LICENSE` and `NOTICE`. The bundled DDSaveEditor codec retains its MIT license under `tools/DDSaveEditor`. Included .NET runtime components retain their own licenses and notices under `licenses/Microsoft.NETCore.App` and `licenses/Microsoft.WindowsDesktop.App`. Darkest Dungeon and third-party Mod content retain their respective owners' rights. Game installations, Java, and user saves are not included in this download.
