# Hero Skins and Initial Skill Selection

Updated: 2026-11-10. These rules apply to new stagecoach candidates created by the editor. Skill purchases, hero progression, and quirk selection continue to use their respective rules.

## Evidence scope

Evidence consists of native disassembly of Windows x64 build 27890 and isolated catalog, candidate-generation, and DSON encode/decode tests. `Darkest.exe` SHA-256: `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`. This is a static check of native branches, not a new live-game experiment. It does not guarantee identical results when the editor and game use the same random seed.

Historical counterexamples, native addresses, and fix validation are preserved in the archived records `hero-generation-selection-audit-2026-09-14.md` and `hero-generation-selection-fixes-2026-09-14.md`. These are entries inside the retired local ZIP, not working files or files included in a new clone; see [archive location and recovery](local-storage.md#retired-history-archive). UI labels below are English descriptions of the localized controls.

## November 2026 skill flags

The [Ode to Mods update](https://www.darkestdungeon.com/news/darkest-dungeon-ode-to-mods-update/) adds `.skill_visible` (default true), `.counts_towards_skill_limit` (true), and `.force_selection` (false). The editor reads the first skill variant's flags and retains omitted values across partial overrides.

Static checks of the installed Windows x64 executable (SHA-256 `f9d914ca1d7d9ad3ff4f151a46e4853e532ce1f5f109f700b0c1381a3f7e0d12`) establish the following behavior. Addresses here are RVAs for that executable, separate from the older absolute addresses below.

- Initial random draws exclude hidden skills and, for selectable classes, forced skills (`0x5D5C40`). This two-stage process also applies to ordinary classes with no new flags: unlock the requested generation count first, then equip in class-definition order up to the cap (`0x5D5190`). Forced visible skills unlock separately (`0x5D4480`) and are equipped before ordinary choices. Only visible skills with `counts_towards_skill_limit=true` consume the cap. Existing manual generation continues to purchase other eligible visible skills.
- Hidden skills can be owned and equipped, but are omitted from character-sheet/upgrade selection and do not consume the visible cap; they remain combat skills. Automatic unlocking requires an authored upgrade tree (`0x5D4950`). Numeric codes start at `0`, stop at the first missing or ineligible code, and are bounded by the actual variant count. Code `0` ignores resolve prerequisites. A tree-less hidden skill is not implicitly unlocked.
- Forced visible base purchases also ignore resolve prerequisites (`0x5D4480` -> `0x5D5A70`). Level-zero event recruits retain every random unlock even when only a smaller set can be equipped, with or without forced skills. Event extra-skill draws use the same visible/nonforced eligibility and exclude already unlocked skills. Existing saved heroes are not migrated. Regression fixtures cover forced/exempt skills, hidden unlocks and gaps, partial overrides, ordinary-class unlock/equipment caps, and candidate/purchase DSON persistence.

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

Boolean parsing is unchanged. For a new hero without existing skills, a mark means that the initial unlocks must include at least one eligible marked skill; it does not require every marked skill to be unlocked or equipped.

Current native function RVA `0x5D5C40` first samples the skill pool without replacement. After reaching the requested unlock count, if the eligible pool has marked skills but the result contains none, it continues drawing and replaces the first temporary selection until it obtains a marked skill or exhausts the pool. A zero request still draws one eligible marked skill. Conditions are checked at `0x5D5EB0`–`0x5D5ECA`, replacement at `0x5D5EF6`, and purchases at `0x5D5F40`–`0x5D5F56`.

For example, if s0 and s1 are marked and s2 is not, a target of 1 can unlock s0 or s1; a target of 2 can unlock s0+s1, s0+s2, or s1+s2. The editor's marked-skill label applies to this initial draw.

Equipment is selected separately from those initial unlocks, in class order with forced skills first. Six ordinary unlocks and a cap of four retain six unlocks but equip the first four; a late marked skill can remain unlocked without being equipped. Classes without selectable skills continue to retain every owned skill. Manual creation retains its purchase-all-eligible-visible-skills policy; level-zero event creation persists the actual initial unlock set, including skills outside the equipped cap.

## Insufficient skill pools

Combat skills and both camping pools use bounded draws from the actual available entries and warn when fewer can be selected. Class and shared camping pools remain independent, without filling shortages from the other pool. For example, requesting 3 class skills when only 2 exist, plus 1 shared skill when 1 exists, produces 2+1.

The combat shortage warning compares the original generation request with pool size. With a request of 4, a pool of 3, and a selection limit of 2, the editor still warns that the request exceeds the pool while equipping 2 skills.

The historical combat empty-pool exit is at `0x1405C7D10`; the two camping-pool branches are at `0x1405C80D0`–`0x1405C80F8` and `0x1405C8120`–`0x1405C814B`. The current combat equivalent exits at RVA `0x5D5ED0`. Existing guards remain for negative combat/camping requests, missing skill identities, and ordinary templates with a nonpositive selection cap or no usable selected skill. A zero combat request with an eligible guaranteed skill now passes; it is not an empty-cap case. Flagged templates retain their existing hidden/forced/exempt capability rules.

Selected skills still serialize as native zero-valued mappings. Personal purchase records express progression and unlocks. All valid camping skills remain unlocked under the editor's existing policy, regardless of how many are equipped. Existing candidates are neither migrated nor rechecked.
