# Hero Skins and Initial Skill Selection

Updated: 2026-09-14. These rules apply to new stagecoach candidates created by the editor. Skill purchases, hero progression, and quirk selection continue to use their respective rules.

## Evidence scope

Evidence consists of native disassembly of Windows x64 build 27890 and isolated catalog, candidate-generation, and DSON encode/decode tests. `Darkest.exe` SHA-256: `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`. This is a static check of native branches, not a new live-game experiment. It does not guarantee identical results when the editor and game use the same random seed.

Historical counterexamples, native addresses, and fix validation are kept in the local-only records `hero-generation-selection-audit-2026-09-14.md` and `hero-generation-selection-fixes-2026-09-14.md` under `docs/change-history/`. They are not included in a new clone. UI labels below are English descriptions of the localized controls.

## Skin directories and saved indexes

Skin discovery supplies `actor.colour_variation` for a new candidate; it does not modify images or add a skin-selection UI. The value is an index into the game's mounted skin list, with a generation range of `0..count-1`.

`0x140485410` queries direct subdirectories of `heroes/<class>/` using the regex `(.*)<class>_([A-Z])(\\|/)`; the query is at `0x1404857C6` and list insertion at `0x140485871`. The class ID and uppercase letter must match the regex, and a prefix is permitted. Deeper directories with matching names are not direct skin entries.

- Query every active source, including standalone skin Mods; a source need not also provide info, art, or override files.
- Local/Workshop Mod virtual directories come from descendant paths listed in `modfiles.txt`. Descendants need not be PNG files, and image existence does not determine whether a virtual directory occupies a slot. A physically present but unlisted directory cannot add a skin entry through that Mod.
- Base-game, game-mode, and official-DLC mounts use physical directories, including empty direct subdirectories. Directory lookup follows Windows case rules, while the skin-name regex remains case-sensitive. Dot-prefixed names, `_template`, and native character-conversion limits follow physical discovery rules; those filters do not apply to the manifest directory tree.
- The `heroes/<class>/` directory segment is case-sensitive in Mod-manifest queries. DLC aliases participate only for enabled mounts. Returned direct-directory names are merged by exact comparison: identical spelling occupies one slot, but `Pack_<class>_C` and `pack_<class>_C` occupy different slots. Case aliases for one Windows physical directory are enumerated once, while separate sources or manifest directory trees can provide both names. This counts directory slots rather than selecting file-content providers.
- Directories A and C produce two slots, allowing indexes 0 and 1. C alone produces one slot, allowing index 0. Letters do not convert directly to saved indexes and need not be contiguous from A.
- Refresh observes the same directory queries without reading image pixels to count slots. Deleting a texture does not remove its slot while the manifest retains that directory. Directory existence does not prove complete skeleton, texture, or animation assets.

Native generation samples by list length at `0x14057E24E`–`0x14057E28D`. Saved-index reading and directory lookup are at `0x1405C6051`–`0x1405C6086` and `0x140479930`, respectively. The editor currently needs the exact count and chooses any valid slot randomly; it does not promise a particular image name for an index.

Directory merging compares names at `0x140247C90` with a length argument of `0x100`, calling trampoline `0x140B91D12` and then CRT `strncmp` through IAT `0x140C63378`. Skin queries use return-path mode 0 and therefore follow this case-sensitive name-merging branch. They cannot reuse the file overlay resolver's case-insensitive keys.

## Meaning of generation_guaranteed

Boolean parsing is unchanged. For a new hero without existing skills, a mark means that the initial selection must include at least one marked skill; it does not require every marked skill to be selected.

Native function `0x1405C7B10` first samples the skill pool without replacement. After reaching the target count, if the pool has marked skills but the result contains none, it continues drawing and replaces the first temporary selection until it obtains a marked skill or exhausts the pool. Conditions are checked at `0x1405C7CF0`–`0x1405C7D14`, with replacement at `0x1405C7D36`. Draw helper `0x140440BE0` removes both the skill and its weight.

For example, if s0 and s1 are marked and s2 is not, a target of 1 can select s0 or s1; a target of 2 can select s0+s1, s0+s2, or s1+s2. The editor's label therefore means **Marked N (select at least one)**.

The editor retains the smaller of the generation count and selection limit as the equipped target. Classes without selectable skills continue to retain every skill. These equipped selections do not change the existing skill-level or purchase-record policy.

## Insufficient skill pools

Combat skills and both camping pools use bounded draws from the actual available entries and warn when fewer can be selected. Class and shared camping pools remain independent, without filling shortages from the other pool. For example, requesting 3 class skills when only 2 exist, plus 1 shared skill when 1 exists, produces 2+1.

The combat shortage warning compares the original generation request with pool size. With a request of 4, a pool of 3, and a selection limit of 2, the editor still warns that the request exceeds the pool while equipping 2 skills.

The combat empty-pool exit is at `0x1405C7D10`; the two camping-pool branches are at `0x1405C80D0`–`0x1405C80F8` and `0x1405C8120`–`0x1405C814B`. This removes only the whole-hero rejection caused by a request larger than the pool. Existing guards remain for non-positive combat targets, negative camping targets, zero selection limits, missing skill identities, and no usable combat capability.

Selected skills still serialize as native zero-valued mappings. Personal purchase records express progression and unlocks. All valid camping skills remain unlocked under the editor's existing policy, regardless of how many are equipped. Existing candidates are neither migrated nor rechecked.
