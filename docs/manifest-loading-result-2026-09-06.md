# Local Mod manifest loading: live A/B result

## Outcome

On 2026-09-06 (Asia/Shanghai), the assistant completed the authorized experiment through Windows Computer Use. The tested existing-hero `.info.darkest` override stopped affecting the game when its entry was removed from `modfiles.txt`, despite the definition file remaining present and unchanged. The listed positive-control override remained effective.

| Phase | Manifest entries | Crusader / Reynauld speed | Highwayman / Dismas speed |
| --- | --- | ---: | ---: |
| A | Both hero files listed | 21 | 57 |
| B | Only Highwayman omitted | 21 | 7 |

The 50-point Highwayman difference matches the injected override exactly. Its ordinary total of 7 includes the unchanged `quick_reflexes` quirk; the test did not assume a total equal to the weapon speed alone. Crusader's persistent +20 control rules out simply disabling the entire Mod between observations.

This is direct evidence for these local existing-hero overrides, not a universal statement about every resource loader. No editor production scanning or save-writing code was changed in this test.

## Controlled environment

- Game build: **27890**, Windows 64-bit; launched through Steam.
- Executable SHA-256: `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`.
- New temporary campaign: `E:\Steam\userdata\1097809614\262060\remote\profile_2`, estate name `极暗`, game mode `base`, dungeon `tutorial`.
- Only active Mod in both decoded saves: `DDSE Manifest Loading Probe`, `mod_local_source`.
- Installed directory: `E:\Steam\steamapps\common\DarkestDungeon\mods\DDSE_Manifest_Loading_Probe`.
- Same initial two heroes, resolve XP 0, weapon/armour rank 0 (UI level 1), unchanged quirks and skills.
- No movement, combat, equipment changes, town return or week progression: both heroes retained zero steps and kills.
- Identical DLC selection in A and B: `arena_mp`, `musketeer`, `duelist`, `fires_edge`, `runaway`, `districts`, `flagellant`, `shieldbreaker`, `color_of_madness`. `crimson_court` was not enabled. This was not an all-DLC-disabled test.

The test package changes only all five weapon-rank speed values: Crusader +20, Highwayman +50. Both phases use identical hero files and `project.xml`; only the manifest differs. B omits the Highwayman entry but does not remove or revert its file. The game process was verified closed before switching the manifest, followed by a cold launch into the same campaign.

| Artifact | SHA-256 |
| --- | --- |
| Original Crusader `.info.darkest` | `6A0BE6287E6933C94686846C5058F6F82B961E435CF474CA9D5B252E2EA00010` |
| Original Highwayman `.info.darkest` | `E97D6D85226626E9AFA4D2925046601A446D3A56F02C6DF614EABE76CD2F0374` |
| Installed test Crusader `.info.darkest` | `AB048A463EE9E06B94E739A24C1AD27ADC85E809F9C7AD9123FC54587C2EC841` |
| Installed test Highwayman `.info.darkest` | `2D7BF6FDC8324E8202240ADC973AAE01E2BAA68F62C7DE5EBABB14C9D239AF2B` |
| `project.xml` | `778D441BFAA361195B5022B8E14FC57CFBCE29E1BA39C02F774EA71D9408E26B` |
| A manifest, 109 bytes | `C45F83B16277DDF4974823821C4BDE2C29699161C10571B02EC4CE75B60CE50F` |
| B manifest, 61 bytes | `4FF74F5B1CE47E3DF1B23DAB41D23D8FAE0E0DBF1EA9862823B6CEA8B054CBA3` |

## Runtime evidence and save comparison

The four raw character-panel captures show the speed values above. Saved campaign timestamps were `02:14:45` for A and `02:24:46` for B on the test date. Both phase snapshots decode successfully and confirm the same active Mod and scene.

The snapshots are not claimed to be byte-for-byte identical:

- Decoded maps are identical; the upgrade save is byte-identical.
- Roster data matches after excluding game-generated `nextGuid` and `buff_group_next_guid` counters. Both heroes retain empty `buff_group` collections; their progression and actual hero state are unchanged.
- Raid coordinates, `party`, `torchlight`, `in_doorway`, and combat state match. Reloading added a `ROOM_VISITED` history entry for the same initial room, taking that count from 2 to 3; the saved zero steps/kills and unchanged position do not indicate route traversal.

Keyboard automation did not successfully invoke the in-game exit menu in A/B. After observing the character panels and the game's saved files, the assistant used Steam **Stop** and its confirmation, then verified that the process had exited before copying evidence or changing the manifest. These were not normal menu exits, so the procedure's normal-exit step was not fully exercised. The complete snapshots remained decodable. Final cleanup did use the normal main-menu quit action.

The unchanged positive control, exact expected difference, cold restarts and installed-file hash verification made an additional C-phase gameplay check unnecessary. Disabling the Mod afterward was verified as cleanup, not represented as a third speed measurement.

## Existing saves and cleanup

Recursive aggregates cover sorted relative path, file length and file SHA-256, joined with LF and hashed as UTF-8. Existing campaign profiles were not entered or edited, and all of their file aggregates remained unchanged:

| Profile | Files | Same before/after SHA-256 |
| --- | ---: | --- |
| `profile_0` | 38 | `EDCF5DEF1E8D31D0616337FFD55286D4B4CE49EAF55CEE9429B72848CA8C876F` |
| `profile_1` | 38 | `A7F0884466169E29259195E7B9B697516982F70FB01DB48618203FCDF9DD4B9A` |
| `profile_3` | 35 | `7EFF37A1046F695874293D7F971D7023627A9207895DEF0DD5D418FE429A0999` |

**Background side effect:** shared/PvP `profile_9` did change while the game ran, despite never being entered or edited through the editor. Its 13-file aggregate changed from `F4A220C1201388C9F3A356BE233B9312E32EDBCF2AD1EB80AEAAC19BF48761DF` to `EA308DF244050151BB80273F9ED8EEA2200B0460663F3EDF7AFCCD461D709F8E`. Observed timestamps advanced for `persist.circus_estate.json` (`02:24:14`) and `persist.tutorial.json` (`02:14:48`). Only the pre-test aggregate was retained for this profile, not per-file copies; exact semantic differences and a trustworthy per-file rollback cannot be established. No speculative restoration was attempted. Future test snapshots should include game-maintained shared profiles, not only campaign slots.

At completion:

- The probe's enabled checkbox was cleared in the temporary campaign's Mod dialog.
- The decoded cleanup save has no active `applied_ugcs_1_0` list. `persistent_ugcs` retains historical usage; it is not evidence that the Mod is still enabled.
- The game process is closed; the original two game hero definitions and all prepared package files remain unchanged.
- `profile_2`, the disabled installed test Mod (B manifest), and all evidence are retained. No campaign, Mod or evidence directory was deleted.

## Evidence location and validation

Local, Git-ignored evidence directory (the result document is the portable record):

```text
E:\数据文件\SelfMod\DarkestDungeonSaveEditor\workspaces\manifest_loading_probe\20260906_015524_088_94af53d10791496d8dc2e7fbf914ab7b\live-evidence
```

Contents include:

- `A-crusader.png`, `A-highwayman.png`, `B-crusader.png`, `B-highwayman.png`: raw UI observations.
- `A-app.log`, `B-app.log`: captured game logs.
- `A-profile_2/`, `B-profile_2/`: full eight-file phase snapshots.
- `A/B-game-decoded.json`, `A/B-roster-decoded.json`, `A/B-raid-decoded.json`, `A/B-map-decoded.json`: decoded comparison evidence (`A/B` means separate A and B files).
- `B-installed-modfiles.txt`: manifest retained after the B run.
- `cleanup-probe-disabled.png`, `cleanup-game-decoded.json`: disablement evidence.
- `verification.json`: 46 successful file/save/process assertions, observations and the disclosed shared-profile side effect, recorded at `02:31:22 +08:00`.

No application build or contract-suite rerun was required: this task performed live testing and documentation only, with no product-code edits. A completion code reviewer was not invoked for the same reason. The package-preparation script's earlier validation is separate from this runtime result.

## Implication for the scanner

Keep **discovering a file**, **observing its manifest relationship**, and **accepting an effective game definition** separate. A full filesystem inventory remains useful for diagnostics, but it must not automatically make every unlisted hero definition effective. An unlisted file may be an intentionally excluded source or an outdated artifact; this test does not distinguish those explanations.

This experiment does not resolve manifest-free Mods, Workshop packages, new hero IDs, monster definitions, JSON, mash tables, DLC-conditional roots, textures, source XML or compiled LOC2. Do not remove existing localization compatibility rules or claim all unlisted files invalid based on this result. Expand behavior only with evidence for the relevant loader and a separately scoped implementation.

Reproduction procedure: [manifest-loading-probe.md](manifest-loading-probe.md). Durable content policy: [content-save-rules.md](content-save-rules.md#25-observed-local-manifest-loading).
