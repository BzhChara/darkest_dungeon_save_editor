# Local Mod manifest loading probe

This A/B probe compares existing hero `.info.darkest` overrides that are listed in `modfiles.txt` with an override that is present on disk but omitted from the manifest. It does not establish loading rules for every resource type or every game version.

| Hero | Weapon speed change at every rank | A manifest | B manifest |
| --- | --- | --- | --- |
| Crusader | +20 | Listed | Listed |
| Highwayman | +50 | Listed | Omitted |

The two packages have identical hero files and project metadata. Only the manifest changes. Compare the same heroes between A and B: displayed speed can also include quirks and other modifiers.

## Generate the package

From the repository root, use PowerShell 7.1 or later:

```powershell
./tools/PrepareManifestLoadingProbe.ps1 -GameDirectory 'E:/Steam/steamapps/common/DarkestDungeon'
```

The script reads the two base-game hero definitions and creates a unique directory under `artifacts/manifest_loading_probe/`. It does not install or enable a Mod, read or edit profiles, start the game, or upload anything. The returned `PackageDirectory` contains:

```text
README.md
evidence.json
A-listed/DDSE_Manifest_Loading_Probe/
B-unlisted/DDSE_Manifest_Loading_Probe/
```

`evidence.json` records source and output hashes, rank-specific speed changes and the intended installation path. The generated game-resource copies are local artifacts, not repository source files.

## Compare A and B in the game

1. Use a new temporary campaign with no other enabled Mods. Keep existing campaigns and backups. If no free profile slot is available, stop and arrange an isolated test instead of deleting a save.
2. Fully exit the game. Copy `A-listed/DDSE_Manifest_Loading_Probe` into the game's `mods` directory. Do not merge with an existing folder of the same name. The final project path must be `mods/DDSE_Manifest_Loading_Probe/project.xml`.
3. Enable **DDSE Manifest Loading Probe** only for the temporary campaign. Record the Crusader's and Highwayman's speed, weapon rank, profile path and game version. If both speed increases are not visible, the control has failed; resolve that before continuing.
4. Save and fully exit the game. Replace only the installed `modfiles.txt` with the one from `B-unlisted/DDSE_Manifest_Loading_Probe`. Leave both hero files in place. Do not regenerate the manifest or install A and B as separate Mods.
5. Restart the game, load the same temporary campaign and compare the same heroes without changing equipment or advancing a week.

If the Crusader keeps the bonus while the Highwayman loses 50 speed, this supports manifest-filtered loading for the tested local hero override. If both retain the bonuses, verify the installed manifest and enabled Mod before interpreting the result. Other outcomes invalidate the comparison and do not justify a general loading-rule change.

The game saves the temporary campaign normally and can update shared tutorial or Circus data during startup. A fresh campaign does not isolate every file in the game's userdata directory. Take any required snapshots before manually performing the game test.

## Finish

Disable the probe in the temporary campaign's Mod selection screen, then fully exit the game before removing the installed `DDSE_Manifest_Loading_Probe` folder. Keep the temporary save, recorded evidence and existing backups. Never delete the game's `mods` directory or another Mod as part of this cleanup.
