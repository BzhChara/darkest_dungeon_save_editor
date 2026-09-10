# Battle Map Editor Design

## Status

Last reviewed: 2026-09-06. Historical probes below establish particular engine contracts; they do not require users to repeat the old manual Bridge installation/removal workflow.

This document records the agreed product behavior for the battle-map workspace. The current implementation milestone is **real-map display, transactional delete and party movement, direct save-based force-town recovery, guarded current-table battle placement, guarded room-battle attachment editing, standalone map content placement, and a persistent profile-scoped Encounter Bridge for eligible global encounters**. `删除`, a proven battle placement, and a guarded room-battle attachment edit may replace `persist.map.json`, while `移动队伍到此` may replace `persist.raid.json`; these operations require the game to be closed, validate the game state and displayed map/raid hashes together, perform a DSON encode/decode round trip, create a full-profile backup, and attempt recovery without overwriting newer external data if final verification fails. `强制返回城镇` validates the displayed pair plus `persist.game.json`, creates the same full-profile backup, then replaces only `persist.game.json`. Standalone room/hall curios, room treasures, and current-region ordinary traps/obstacles now use the same guarded map-only transaction. Ambush composites and unresolved named/script content remain unsupported. For a selected global encounter, the application stages and validates a managed local Mod, installs it under the configured local-Mod root, activates it at the top of the selected profile, then writes the resolved stable index to the map. It never removes or reorders earlier managed rows automatically.

The battle page now joins the save's static topology to its dynamic knowledge/content state and current raid position. Its map panel, rooms, corridors, exploration markers, event markers, and party marker are loaded at runtime from the user's selected Darkest Dungeon installation. Original game artwork is not copied into or redistributed with the editor. A visibly simplified fallback is allowed only when the selected installation does not contain the required asset. Proven single-file and root-level multi-file current-table battles and eligible standalone map content use guarded write paths; unresolved ordinary content categories have disabled picker entries rather than successful-looking in-memory writes.

The editor uses a display-only global-vision rule. Every room and visible corridor tile is shown even when its persisted knowledge is unknown, and available room types plus hallway content markers are presented as if the location had been scouted. The underlying `knowledge` value is retained for tooltips and state decisions; opening the editor does not reveal the map inside the game or write scouting progress to the save. Deleting content or moving the party likewise does not promote knowledge or mark skipped routes explored.

“Real time” means following complete save snapshots after the game writes them to disk. The shared profile monitor includes `persist.map.json` and `persist.raid.json`, debounces burst writes, verifies stable copies and the pair across a quiet window, retries transient partial writes, and periodically compares file stamps in case an operating-system event is missed. Monitoring belongs to the loaded profile rather than the currently visible tab, so hiding and reopening the battle tab cannot silently miss a save. It does not read game process memory, inject code, or modify a running game.

## Product goal

Add a fourth main workspace named `战斗 / BATTLE` to the right of `人物 / HEROES`. When a profile is currently in an expedition, this workspace will eventually display the complete current dungeon map and act as a save-console surface for:

- inspecting rooms, visible corridor tiles, exploration state, and pending content;
- creating, replacing, or deleting room and hallway content;
- placing any resolvable Base, DLC, or enabled-Mod boss, roaming-boss, or supported special encounter on a source-compatible room/corridor target;
- moving the party to a selected room or corridor tile.

When the selected profile is in town, the workspace must show an empty state rather than a map.

## Terminology and topology

The UI exposes only two selectable map elements:

| UI term | Static map meaning | Stable meaning |
| --- | --- | --- |
| Room | Area `kind=0`, normally one static room tile | A room destination and content container |
| Corridor tile | Visible corridor tile with `type=1` | A normal hallway position, including the first and last visible squares nearest rooms |

The save can also contain `type=2` door-transition nodes between a room and its first visible hallway square. They describe the internal gap/transition used by the game and are not normal rooms or corridor squares. The snapshot reader may retain them to resolve topology and transient party state, but the editor deliberately does not render, select, right-click, or offer movement to them. All selectable corridor positions use the same corridor-tile semantics; there is no separate endpoint category in the editor.

## Workspace layout

The battle tab replaces the catalog table with a map viewport. The ordinary catalog search, quantity/level inputs, preview button, and apply button are hidden while this tab is active.

The viewport contains:

- a compact title and profile/expedition status;
- an important `强制返回城镇` recovery button available only for a real loaded expedition;
- zoom-out, zoom percentage, zoom-in, and fit-to-view controls;
- a pannable and zoomable map surface;
- one concise status line describing the selected tile or latest action result.

The map does not repeat the game's icon meanings in a permanent legend. This keeps the viewport uncluttered for players who already know the original map language; the concise status line remains available when a specific tile is selected.

The prototype renderer uses the neutral grid region cropped at runtime from the original `panels/panel_map.png` backdrop and the original `panels/icons_map` sprites. The crop deliberately excludes the game's map/inventory tab buttons; DPI metadata must not leave the backdrop confined to one corner. Room content is represented by the matching original room sprite; every visible hallway square uses `hall_dark`, `hall_dim`, or `hall_clear`; and pending hallway content uses the original marker sprites. The fire-torch `indicator.png` marks the party. These are runtime links to the installed game, not editor-owned approximations or packaged copies.

The original layout uses a 24-pixel map grid, 24×24 hallway sprites, and 64×64 room sprites. Drawing save `type=2` transition nodes as `hall_door.png` exposes clipped red fragments around rooms because those nodes lie in the visual gap rather than on a normal corridor square. The editor excludes those nodes from the rendered and interactive tile set instead of inventing artwork or a transparent target for them.

Persisted `mappos` Y values already follow the game's map-display order. The editor maps them directly from `minimumY` to the screen and must not apply a Cartesian Y-axis inversion. This keeps the displayed north/south orientation aligned with the in-game map.

Any open tile context menu is bound to one concrete snapshot cell. A successful live-map replacement or transition to the unavailable state closes that popup before detaching the old visual tree, and a delayed popup must refuse to open if its cell is no longer in the current map. Actions must never mutate an object retained from an older snapshot.

The main runtime log remains visible below the workspace.

## Map navigation

- Mouse wheel zooms around the pointer position.
- Right-button drag pans the map.
- A right click without meaningful pointer movement opens the selected tile's context menu.
- The implementation must use a movement threshold so a context-menu click and a pan gesture do not compete.
- `适应窗口` fits the complete map in the current viewport.
- `100%` restores neutral scale and recenters the map.
- Zoom is clamped to a usable range; the UI prototype uses 45% through 240%.

## State presentation

The real map renderer retains at least:

- current party position;
- the persisted unknown or scouted state in the selected-tile text and tooltip;
- visited area;
- completed/consumed content;
- pending curio, battle, trap, obstacle, treasure, or boss content;
- visible corridor connections.

Unknown locations use the original scouted visual treatment instead of hiding their room type or pending content. Completed or consumed content is muted rather than removed so the displayed topology remains stable. A selected tile uses a red fill/highlight consistent with the rest of the application and does not add a white focus frame.

## Evidence basis for content editing

The room/corridor split is based on three independent sources checked on 2026-09-04:

- Red Hook's [official modding guide](https://steamcommunity.com/sharedfiles/filedetails/?id=819597757) defines independent random-generation controls for hallway battle, trap, obstacle, curio and hunger content, then defines room battle, guarded curio, unguarded curio, guarded treasure and unguarded treasure as separate room controls. It also identifies `hall`, `room`, `stall`, `boss`, and `named` as different encounter-mash purposes and documents profile Mod priority.
- [Sherin's Modding Notes](https://steamcommunity.com/sharedfiles/filedetails/?id=2918752848) records the custom-map grammar: rooms accept mash, curio, quest-curio, treasure, final-room and entrance markers; corridors accept mash, curio, obstacle, hunger and hidden-door markers. It also distinguishes standard weighted mashes from conditional replacement encounters and additional second-hallway encounters.
- The official wiki independently describes [Collector](https://darkestdungeon.wiki.gg/wiki/The_Collector_%28Darkest_Dungeon%29) and [Shambler](https://darkestdungeon.wiki.gg/wiki/Shambler) as conditional hallway replacements, and the [Thing from the Stars](https://darkestdungeon.wiki.gg/wiki/Thing_from_the_Stars) as an extra hallway battle in its current roaming region.
- The current Base/DLC/local/Workshop installation contains 252 `hall_curios`, 113 `room_curios`, 75 `room_treasures`, 28 `traps`, 26 `obstacles`, and 21 `secret_room_treasures` source rows, but no `hall_treasures` source row. Its random-map definitions likewise have no hallway-treasure field. Fourteen unique decoded real-save snapshots supplied 989 dynamic cells: rooms used content 0/1/6/7/9/10, while corridors used 0/1/3/4/7/8/13; no corridor used either treasure value. Every one of the 202 active curio/treasure cells had matching nonzero static `cur` and dynamic `curio_prop`; all 31 traps had a dynamic `trap` hash, all four obstacles had a static `obstacle` hash, corridor battles used `mash_type=0`, ordinary/guarded room battles used 1, and four audited boss rooms used 2.
- The Base/DLC files also establish three different special-encounter mechanisms. Collector and Shambler are conditional `hall` replacements; the Thing from the Stars and Fanatic are additional `hall` encounters; a separately named Shambler mash is used by the altar summon path. The installed Workshop corpus includes explicit roaming IDs such as `dimma` and `ninja`, but an explicit roaming row is not necessarily a boss: the audited Raiju `ninja` row contains four `crab_shinobi` actors while the actual `crab_raiju` boss is a separate named mash. Conditional/Additional filenames likewise contain many ordinary plot-gated formations. The UI must therefore separate roaming-system evidence, boss-tag evidence, and source-table kind while still honoring authored `hall` versus `room` placement.

The localized UI terms come from the game's own Simplified Chinese map tooltips: `战斗`, `奇物`, `有奇物的战斗房间`, `有战斗的宝箱房间`, `陷阱`, `障碍`, `首领`, and `宝箱`. The editor uses `宝箱`, not the vague prototype label `宝藏`.

## Room and corridor content contract

Room and corridor menus are deliberately different. Choosing a category never asks the game to choose a category later: it resolves one concrete content type and all of that type's bindings before the confirmation preview.

### Ordinary rooms

| Menu entry | Saved content | Candidate source | Complete destination binding |
| --- | ---: | --- | --- |
| `奇物` | 7 | effective `room_curios` pool | one resolved prop hash in both static `cur` and dynamic `curio_prop`; no mash |
| `战斗 > 普通战斗` | 1 | selected complete global standard `room` mash for the chosen difficulty | one resolved mash index with `mash_type=1`; no prop |
| `战斗 > 游荡首领 / 特殊遭遇` | 1 | effective roaming, conditional, additional, or directly addressable zero-weight special `room` row | one resolved mash index with `mash_type=1`; no prop |
| `战斗附加内容 > 奇物` | 6 | current plain/guarded room battle plus one exact `room_curios` prop | preserve the current `mash_type=1`/`mash_index`; write the same prop hash in `cur` and `curio_prop` |
| `宝箱` | 9 | effective `room_treasures` pool | one resolved treasure-curio hash in both `cur` and `curio_prop`; no mash |
| `战斗附加内容 > 宝箱` | 10 | current plain/guarded room battle plus one exact `room_treasures` prop | preserve the current `mash_type=1`/`mash_index`; write the same treasure-curio hash in `cur` and `curio_prop` |
| `战斗附加内容 > 移除附加内容` | 1 | current guarded-curio or guarded-treasure room | clear static `cur` and dynamic `curio_prop`; preserve the current `mash_type=1`/`mash_index` |
| `战斗 > 固定首领` | 1 | selected complete global `boss:` mash | one resolved mash index with `mash_type=2`; no prop |

`普通战斗` means only a combat drawn from the room encounter pool. It is not a random choice among ordinary battle, guarded curio, and guarded treasure. Likewise, `奇物` selects only from `room_curios`, while `宝箱` selects only from `room_treasures`. A treasure room stores a treasure-curio such as a strongbox or heirloom chest; it does not directly add loose loot to the raid inventory.

The engine supports the unguarded room values 7 and 9. They occur in real/custom maps, so the console may expose them. However, all 99 Base and Color of Madness ordinary random-map records in the historical audit set `.room_curio`, `.room_treasure`, and `.room_battle` to `0 0`; those records generate ordinary room content through guarded curio/treasure controls instead. The user chose explicit search/selection and removed the proposed random-generation action. These generator weights remain evidence about natural generation, not a planned editor random mode. Explicit unguarded options remain intentional console overrides and must not be described as the Base game's usual random result.

### Corridor tiles

| Menu entry | Saved content | Candidate source | Complete destination binding |
| --- | ---: | --- | --- |
| `奇物` | 7 | effective `hall_curios` pool | one resolved prop hash in both static `cur` and dynamic `curio_prop`; no mash |
| `战斗 > 普通战斗` | 1 | selected complete global standard `hall` mash for the chosen difficulty | one resolved mash index with `mash_type=0`; no prop |
| `战斗 > 游荡首领 / 特殊遭遇` | 1 | effective roaming, conditional, additional, or directly addressable zero-weight special `hall` row | one resolved mash index with `mash_type=0`; no prop |
| `陷阱` | 3 | current-region effective `traps` pool | one resolved dynamic `trap` hash; no prop or mash |
| `障碍` | 4 | current-region effective `obstacles` pool | one resolved static `obstacle` hash; no prop, trap, or mash |

There is no general corridor treasure category. A corridor chest, discarded pack, sack, or similar loot-bearing object is still a `奇物` selected from `hall_curios`; the fact that interacting with it can yield treasure does not turn its map content into room `宝箱`. The context menu therefore omits `宝箱` from corridor `新建` and `替换`.

Persisted hunger (`content=8`) and secret doors (`content=13`) are not generic editable corridor events. Hunger displays the original `marker_hunger.png` icon and the `进食格` label, but exposes only movement, like the entrance. A secret door participates in hidden-room topology, so it also exposes movement only; it must not be created, replaced, or deleted through the ordinary corridor menu. The unverified ambush values 2, 11, and 12, scripted happenings (`content=5`), Mod-specific mash categories, and locked/quest door props remain outside the first content writer.

### Exact candidate selection

`普通战斗`, `游荡首领 / 特殊遭遇`, and `固定首领` all open the same searchable composition picker. The ordinary-battle picker has no `随机` action: the user always selects one complete ordered formation before confirmation.

- Catalogs are built from Base, enabled official DLC, and enabled profile Mods in effective load order. The profile's topmost Mod has final priority; disabled or merely installed content is not eligible.
- The ordinary picker is global across Base, enabled DLC, and enabled profile Mods. It preserves authored `hall` versus `room` placement and offers every concrete source difficulty; Base filenames conventionally use suffixes 1, 3, and 5, but the resolver accepts Mod-defined keys rather than hard-coding only those values. A composition already present in the current table writes directly, while a cross-region composition transparently uses the same managed Encounter Bridge as other global encounters.
- `conditional` mashes are independent condition/chance replacements, while `additional` mashes can add a second hallway fight. Neither is folded into the ordinary picker. `stall`, `boss`, `named`, and Mod-specific categories likewise stay out of it.
- Standard mash `.chance` and curio/treasure prop `.chance` remain evidence of the game's natural weighted pools; the explicit console picker does not roll those weights. Current-region automatic trap/obstacle selection is the separate exception described below. Repeated equivalent rows therefore remain valid source rows but are presented as concrete compositions rather than a random action.
- Apply the requested encounter classifications before deduplicating picker rows. Identical formations authored as ordinary and conditional/additional entries remain available under both purposes. Deduplication also retains separate source difficulties so the difficulty selector does not lose valid choices. Within the combined special menu, existing roaming/boss evidence remains the representative-selection preference; authored room/hall/boss target types are not merged.
- `战斗附加内容` never resolves or replaces a mash. It is shown only for non-entrance, non-final ordinary rooms whose saved content is 1, 6, or 10 and whose existing battle binding is `mash_type=1` with a non-negative index. It is absent from corridors, fixed-boss `mash_type=2` rooms, and every non-battle cell.
- The attachment picker reads effective `room_curios` and `room_treasures` records from the canonical `dungeons/<region>/<region>.props.darkest` path in Base, enabled DLC, and enabled Mods. Manifest-listed extra basenames or classification-folder copies do not supply these pools. Standard Mod/DLC overlays resolve the whole canonical file; `arena` is excluded by the native loader. A record can contain several `.types` values, as in `darkestdungeon.props.darkest`. Each usable ID becomes one searchable choice; the explicit picker does not roll or rewrite its `.chance`.
- The selected ID uses its exact UTF-8 base-53 name hash, including authored spaces and signed 32-bit JSON values. The catalog fingerprints the complete effective pool/resource inventory plus `persist.game.json`; a changed definition, active Mod configuration or hash collision invalidates the prepared edit. Repeated identical entries are valid source contributions, not ambiguous identities. Physical source line and native declaration index identify the selected source for preflight and logs.
- The concrete `mash_index` must be resolved from the effective encounter table actually used by the current raid. A source-file line number is not a valid substitute because DLC/Mod overlays plus standard, conditional, and additional tables can alter the final indexes.
- A directly indexed row requires every monster ID to exist in the active monster catalog, just like a Bridge candidate. An unresolved row retains its native index slot but cannot be selected for writing; never remove it and renumber following rows. Revalidate dependencies at prepare and final commit, not only when loading the picker. Removing a monster definition after preview must reject the transaction without changing the map.
- Encounter exclusion logs count global Bridge **source rows**, not distinct monsters or deduplicated picker formations. Rows are counted once: suspected field glue takes precedence over other unresolved references in the same row. The distinct unresolved-token count is separate; values such as `1` or `watched` can be swallowed field values, not missing monsters. A current-table failure may also have a direct-index diagnostic, but is not added to the global total a second time.
- Each excluded row records its source, full file path and line, dungeon, difficulty, placement kind, parsed `.types` tokens, and unresolved tokens. A missing full token ending in the observed fields `.limit` or `.quirk_tag`, whose prefix exists as an active monster ID, is a **suspected field-glue diagnostic**, never an automatic repair. Existing complete dotted IDs remain valid. Other missing references are not guessed to be typos, disabled dependencies, or missing difficulty variants. These diagnostics do not change parsing, selection, Bridge eligibility, source files, or native indexes, and do not claim that an entire Mod was disabled or that the game must crash.

### Roaming bosses and special encounters

Managed carrier weight is not an encounter category. New version-3 manifest entries retain an optional original `Classification` field. Catalog loading validates the managed package identity, generated mash hash, per-type index, and ordered actors before applying that classification. Legacy entries without the field recover from roaming/source-kind metadata or a matching current original source row; loading does not migrate or rewrite the package. If the original category cannot be established, the appended row is omitted from generation choices with a diagnostic, while existing map bindings and indexes remain untouched. Authored zero-weight special rows remain special.

`游荡首领` is not the same thing as a boss room. A boss-room encounter comes from a `boss:` mash, uses `mash_type=2`, belongs in a room, and may be tied to the quest's final room. A roaming encounter remains an ordinary map battle at its authored location: `hall:` writes `content=1, mash_type=0` to a visible corridor tile, while `room:` writes `content=1, mash_type=1` to a room. It never changes `final_room_id`; consequently the in-game map continues to use the ordinary battle marker rather than a final-room skull.

The catalog recognizes the source mechanisms separately:

- A standard mash is part of the ordinary weighted pool. A boss-tagged standard `hall`/`room` row may additionally be searchable as a special encounter, but remains part of natural ordinary randomization because that is how its author registered it.
- A conditional mash is a percentage-based replacement for an existing `hall` or `room` battle after its predicates pass. Base Collector and Shambler encounters use this path.
- An additional mash can add another encounter without the normal one-fight-per-hallway restriction. Base Thing from the Stars and Fanatic encounters use this path. “Additional” does not mean two battles can coexist in one save tile: a forced placement still occupies one selected cell's single content/mash binding.
- `.random_dungeon_roaming_id` is an explicit roaming-system declaration. It links an additional encounter to the weekly roaming-region state in `persist.campaign_mash.json`; the installed Mod corpus proves that Mod IDs such as `dimma` and `ninja` use the same field.
- A `named:` mash is a callable encounter for a curio or authored map, not a random roaming placement. The Shambler altar uses such a named entry. A named-only encounter stays under scripted/special compatibility until a bridge conversion proves that its trigger-specific setup is unnecessary.

Candidate classification remains lossless internally, but the UI deliberately combines the first three categories into one searchable `游荡首领 / 特殊遭遇` action. This matches player terminology: Collector is a roaming boss in play even though its authored mechanism is a conditional replacement rather than `.random_dungeon_roaming_id`. Each result still shows its precise row-level type:

1. `游荡首领` requires both `.random_dungeon_roaming_id` and at least one actor whose effective `.info.darkest` contains `tag: .id "boss"`.
2. `游荡遭遇` has `.random_dungeon_roaming_id` but no positive boss-tag evidence. This preserves legitimate roaming groups such as the Raiju Mod's `ninja` formation without calling them bosses.
3. `条件 / 额外战斗` contains the remaining effective conditional/additional `hall`/`room` rows without a roaming ID, plus directly addressable zero-weight special rows. A boss-tagged row is displayed as `条件首领`, `额外首领`, or `特殊首领`, so Collector and Shambler remain visibly bosses without misreporting their trigger mechanism.
4. `固定首领` contains complete `boss:` formations and is available only for rooms. It is global across current and foreign regions; an already present current-table formation writes directly, while every cross-region formation transparently uses the managed Bridge.

`.loading_screen`, `.limit`, a project title containing “boss/miniboss”, and the mere use of an additional file are displayable evidence but are not sufficient classifiers. The installed Fiend Festival Mod places loading-screen metadata on hundreds of ordinary additional compositions, while several installed miniboss Mods omit `tag: .id "boss"` entirely.

The picker shows the complete ordered composition on two lines: Simplified Chinese actor names above and raw monster IDs below. It also shows source/provenance, authored `hall`/`room` target, source mechanism, and concrete difficulty variant. A missing Chinese member is shown as `—` under the existing localization rule; the editor does not fabricate a translation. The internal roaming ID is retained for catalog identity and Bridge validation but is not exposed as a user-facing column.

Base and Mod roaming encounters are global search results, not restricted to the raid's current region. Placement nevertheless preserves the encounter's authored target kind: a `hall:` candidate is offered only on visible corridor tiles, while a `room:` candidate is offered only on rooms. Hallway is the normal case, but it is not universal; the installed Abyssal Horror, Shard Jester, and Lilo/Fanatic definitions are real room exceptions. The editor must not silently convert one kind into the other.

The default variant is the concrete source file whose difficulty key exactly matches the active raid. The editor never rewrites monster suffixes or synthesizes stats. If a Mod supplies no exact variant, automatic selection is disabled; an explicitly chosen nonmatching variant may be offered with its source difficulty and a clear balance warning.

Forced placement deliberately bypasses natural admission predicates such as darkness, inventory fullness, infestation, cursed-hero count, plot progression, chance, and per-expedition limit. Those fields explain how the game would have generated the encounter; they are not copied into the selected map cell. This console override does not suppress the encounter's own runtime behavior: loot, summons, backdrop/torch modifiers, retreat behavior, tutorials, death effects, quest-goal side effects, and Mod scripts attached to its actors can still run.

The first writer follows the smallest-state rule:

- clear the destination's old prop/trap/obstacle/mash bindings exactly as required by the destination table;
- write one battle binding with `content=1`, `mash_type=0` for a corridor or 1 for a room, and one proven concrete `mash_index`;
- do not change `final_room_id`, `persist.campaign_mash.json` roaming assignments, `persist.raid.json/mash/conditional_entry_counts`, or `valid_additional_mash_entry_indexes` merely to force the encounter;
- warn that a forced limited/roaming encounter does not consume its natural generation quota, so a second natural copy may still appear later;
- do not replay an expedition-load warning screen. A Fanatic-style `.loading_screen` belongs to dungeon loading and will not be shown when a battle is inserted into an already-created map.

The two raid mash collections are treated as natural-generation bookkeeping, not tile identity. A nonempty audited save records a conditional row's use in `conditional_entry_counts` and records condition-qualified additional row indexes separately in `valid_additional_mash_entry_indexes`; the map cell itself still carries only content, mash type, and mash index. The controlled `thing_A` probe confirmed that neither collection must change for the game to load, reload, complete, and save the forced encounter. After victory cleared every reference to the appended index, the user disabled the Bridge, reloaded the surviving expedition, and exited normally; no mash/index assertion, fatal error, or exception occurred. This completes the zero-weight carrier lifecycle evidence without inventing natural-generation bookkeeping.

Direct save placement is allowed only when the candidate already resolves to the same composition at the same `mash_type` and concrete index in the active raid's effective table. Otherwise the profile-scoped managed Encounter Bridge is required. A bridge entry preserves the complete ordered composition and authored `hall`/`room`/`boss` kind, is excluded from natural weighted selection, and gives the save a stable local index. Before mutation, the implementation reconstructs the selected global source row, revalidates the current table and Mod-configuration fingerprints, and uses the native multi-file ordering rules in [encounter runtime order](encounter-runtime-order.md). Unverified DLC mount combinations and unprovable native row counts remain guarded. The first use installs and activates one deterministic managed carrier for that profile; later uses append only genuinely new complete encounters and reuse an existing matching row. Legacy one-off DDSE Bridges may remain below it, but new placement never creates another disposable package. The editor does not automatically remove the managed carrier; manual removal is safe only after every map cell and active battle has stopped referencing all appended indexes. The controlled live test below proves the engine-level carrier behavior. Contract tests cover the automatic persistent lifecycle, and later user-operated writes were followed by an audit of three appended carrier records; this does not prove every possible formation or failure scenario.

### Special rooms and edit eligibility

- The entrance is identified by area ID equal to `base_root.map.entrance_id`, never by a name such as `rooA`. The audited corpus includes an authored entrance carrying a live curio, so empty content is not a valid entrance test. It exposes only `移动队伍到此`; `新建`, `替换`, and `删除` are prohibited even when it currently contains an event. If a room is both entrance and final room, entrance protection wins.
- Entering a non-entrance room or traversing a corridor does not by itself make it ineligible. Visited ordinary cells retain the same create/replace/delete categories as unvisited cells, subject to transient-state guards.
- A hidden room is detected from hidden-door topology, not only an area-name convention. The first writer offers only `宝箱` from `secret_room_treasures`, plus movement and applicable deletion. Authored hidden-room curios outside that pool require later map-specific compatibility.
- A final room is identified by `final_room_id` and remains quest-bound. The first writer may replace it only with a boss encounter; existing deletion remains available after the explicit quest-break warning. Ordinary battle/curio/treasure replacement is withheld until quest-goal consequences have dedicated handling. No content action changes `final_room_id`, quest goals, quest type, boss history, or the room icon directly.
- Authored plot maps are not rejected wholesale, but their special cells must be classified from the effective CSV/DM source. Quest curios, named/scripted mashes, wave encounters, locked doors, teleport destinations, and script-bound cells are not ordinary content targets. If the authored source cannot be resolved, create/replace is disabled rather than guessed. Existing active battle, camp, loot/reward, doorway, teleport, wave, and unsupported script-state guards continue to block all writes.

### Context-menu shape

Every selectable room or visible corridor tile still exposes `移动队伍到此`, including the protected entrance and special cells when movement itself passes the transient-state guard.

- An eligible empty ordinary room shows `新建` with the documented room choices above, then movement.
- An eligible occupied ordinary room shows `替换` with the same choices, `删除`, then movement.
- A plain or guarded ordinary room battle additionally shows the sibling submenu `战斗附加内容`. `奇物` and `宝箱` open a bilingual exact-ID picker; an existing guarded battle also shows `移除附加内容`. Selecting a different item of the same category or switching between curio and treasure replaces the old prop binding rather than stacking it.
- An eligible empty corridor tile shows `新建` with the documented corridor choices above, then movement.
- An eligible occupied corridor tile shows `替换` with the same choices, `删除`, then movement.
- Protected or unsupported special content omits `新建`, `替换`, and `删除` instead of presenting an action that will later fail. Final-room deletion is the documented warning exception.
- An eligible `content=0` tile still shows `删除` when any static curio/obstacle, dynamic curio/trap, or non-normalized mash binding remains. It otherwise behaves as an empty `新建` target.

`新建` and `替换` open exact-ID pickers for standalone room/hall curios and room treasure. Ordinary traps/obstacles instead select an eligible current-region resource by its positive authored weight and go directly to confirmation, without a picker. Confirmation replaces the entire cell content in a map-only transaction. The separate `战斗附加内容` submenu retains the battle and writes guarded room values 6 and 10; it is not an alias for whole-cell `替换`. Both paths preserve persisted knowledge, topology and quest identifiers; neither marks a cell visited or invents loose loot.

The first writable create/replace slice is deliberately narrower: an exact standard `hall`, `room`, or `boss` row may be placed only when all rows for that `mash_type` come from one effective file. The catalog then assigns the native zero-based index independently within each type. If two effective files contribute the same type, every candidate of that type remains unavailable because source-file ordering is not yet proven. The prepared edit records the complete effective mash-file inventory, hashes, active content roots, and `persist.game.json` hash; preparation, backup, the final write, and post-write verification all reject a changed table or Mod configuration. Placement writes one battle binding and clears old static `cur`/`obstacle` plus dynamic `curio_prop`/`trap` scalars so replacement cannot overlap a consumed prop. Entrance, hunger, secret-door, ambush, happening, and unsupported script cells remain ineligible.

Guarded room attachment editing deliberately preserves the already resolved battle. Adding or replacing an attachment changes only `content`, static `cur`, dynamic `curio_prop`, and clears impossible obstacle/trap overlap; it never writes `mash_type` or `mash_index`. Removing an attachment returns content to plain battle value 1 and clears both prop hashes while retaining the exact mash pair. Preparation rechecks the current content and mash pair, the props catalog guard, stationary raid state, and the profile identity. The same map/raid stale guards, backup, atomic replacement, rollback, and DSON round-trip checks used by battle placement apply.

### Standalone map content (2026-09-06)

- Reuse `BattleRoomAttachmentCatalog` and its bilingual exact-ID picker. Keep room `room_curios`, room `room_treasures`, and hallway `hall_curios` distinct, even when the same ID is present in more than one pool. No random choice or hallway treasure category is added.
- Curio and treasure choices span all active regions, not all installed Mods. They use the resource ID's signed base-53 hash directly, unlike region/type-local encounter indexes. The installed executable contains global scanner patterns `.*curios/.*curio_type_library.csv` and `.*props/.*/prop_definitions.json`; the historical save corpus confirms the static/dynamic hash pair. This supports direct hash placement without creating an Encounter Bridge or changing Mod activation. It is not live proof that every foreign Mod curio's scripted interaction works in every dungeon.
- Resolve a curio pool ID through effective `curios/**/*curio_props.csv` (including root files), then require its exact `Curio Type` in effective `*curio_type_library.csv`. Native CSV uses physical chunks, quote toggles, trailing-ASCII-space removal and entry-specific column buffers; it does not support RFC multiline fields. Mappings discard the first physical chunk, accept supported short rows with native retained-column behavior, and update existing props in effective file order. Explicit UI names overwrite; empty names retain prior JSON/CSV names or default to the sprite. Type libraries follow exact `ID STRING`/`ITEM` block boundaries without requiring a `Nothing` label. Missing mappings/types, empty sprites and real hash collisions remain unavailable. These are identifier/metadata rules, not a complete type-effect or texture validator. See [CSV rules](resource-duplicate-semantics.md#121-native-csv-format-and-field-retention).
- Traps and obstacles are region-bound by the effective canonical `dungeons/<region>/<region>.props.darkest` pool. Do not collapse identical IDs across different regions. UI filtering and preparation both enforce this restriction, including against forged metadata or a stale selection. The menu goes directly to write confirmation without a search dialog or choice submenu. One eligible resource is automatic; multiple resources use positive authored `.chance` weights. **Each raw type entry receives the full record weight**, including repeated IDs and repeated records; there is no division by type count and no second ID/Mod override after file resolution. The last qualifying field occurrence supplies `.chance`, using native float precision, numeric-prefix and percentage rules; a nonnumeric final value becomes zero instead of reviving an earlier value. Missing/negative/overflowing weights are diagnosed and withheld; zero and nonnumeric-to-zero values do not participate. Missing/scripted/ambiguous resources remain excluded before selection. Normalize eligible weights before summation. Empty pools stay disabled without cross-region fallback. Select once on click and commit that exact selection; resolve IDs against the current catalog after profile-sync guard rebinding.
- Props use native records, not physical lines: records may span lines or share one line; slash/block comments and NUL follow the shared reader. The last case-sensitive `.types` field supplies at most 64 raw slots, each limited to 63 UTF-8 bytes plus NUL. Stop at the first empty slot; dot-prefixed IDs do not terminate this particular list. Invalid UTF-8 or a split multibyte ID is diagnosed and withheld rather than hashed through replacement characters. See [native props rules](resource-duplicate-semantics.md#10-map-prop-pools-and-native-identities).
- Native map record dispatch is a case-sensitive prefix test: `traps_extra:` participates as a trap record, while `xtraps:` and `TRAPS_extra:` do not. Standard Base/DLC pool opens bypass directory-device discovery exclusions such as `_template`; the same canonical files are included in content-refresh fingerprints. Other resource scanners retain their own discovery rules.
- Resource IDs, curio type references, prop inheritance, localization requests and hash-collision checks are case-sensitive. Authored spaces in supported IDs are retained. Windows paths, region comparison, display sorting and user search keep their separate comparison rules. Distinct valid IDs such as `foo` and `Foo` receive independent names and hashes.
- JSON prop objects use first-member lookup at every consumed level; duplicate field names do not themselves break catalog loading. File discovery preserves the exact root opens and the three distinct nested filename queries, including native unescaped dots. The same classification governs stage sorting and missing-file diagnostics. See [catalog JSON and filename rules](resource-duplicate-semantics.md#16-trinket-and-map-json-members-catalog-file-queries-2026-09-10).
- Trap/obstacle resources first open the effective root prop/obstacle/trap JSON paths in that fixed order, then enumerate subdirectory prop/trap/obstacle JSON before both Curio CSV stages. Apply file defaults, entry defaults, copies of already-loaded parents and difficulty variations in that order. JSON prop names register their first 63 UTF-8 bytes; versions append and query chooses the first exact difficulty or nearest earlier-tie result. Check effective `instance_type` and supported script fields across the seven difficulty views, without reapplying Mod priority or treating repeated defaults as ambiguous. Explicit empty/false script fields clear inherited values. Missing parents, malformed relevant metadata, actual hash collisions and active selected script flags stay guarded; unsupported or unreadable files cannot expose a partial library. See [JSON prop rules and coverage](resource-duplicate-semantics.md#122-default-data-parents-and-difficulty-versions).
- A standalone replacement clears both old static scalars (`cur`, `obstacle`), both dynamic scalars (`curio_prop`, `trap`), and the mash pair before writing exactly the binding in the tables above. Completed scenery is cleared too. Exploration, critical scouting, coordinates, final-room ID, quest state, party position, inventory and other save files remain untouched.
- The guard pins the complete effective pool/resource inventory, including both curio CSV families, plus profile `persist.game.json`. Revalidate the selected source row, hash, region, catalog and stable map/raid pair during preparation and commit. Adding, modifying or deleting an effective resource invalidates an earlier selection. Confirm first, back up all `persist*.json`, encode/decode, atomically replace only the map, and roll back a failed final verification. Cancelled pickers do nothing.
- A manifest-listed missing canonical pool remains an overlay candidate. If it wins, loading/writing fails closed; do not revive lower-priority bytes. A fully shadowed missing source can still resolve to a readable winner. This is an editor safety policy; the native failure fallback has not been fully verified. An extra unused props basename does not enter the map catalog guard.
- The read-only 139-source `profile_1` scan found 61 room curios, 15 room treasures and 76 hallway curios. Current Weald candidates are `poison_cloud` and `thorny_thicket`; Cove uses `lurker` and `shipwreck`. Seven regional rows were withheld for special scripted behavior (including repeated `vmt_rattrap` pools). Counts are dated evidence, not constants.
- Dedicated contracts exercise every ordinary destination binding, completed scenery, cross-region curios, regional trap/obstacle rejection, room/hall mismatch, entrance/final/door/system guards, transient raid states, cancellation, changed saves/pools/resources, curio aliases and enabled-DLC CSV overlays, missing/changed/deleted CSV rejection, and DSON revision preservation. Tests write isolated fixtures only. A future user game test should naturally walk into the new content; teleporting onto it does not prove normal entry-trigger behavior.
- Computer Use smoke inspection used a project-local copied historical `carnival` map, not a live profile: room and hallway menus, standalone picker wording, current-region trap/obstacle counts (2/1), row selection and the final replacement confirmation displayed correctly. A curio picker was cancelled; no write confirmation was accepted, and the copied map/raid hashes remained identical. Modal keyboard input lost focus through the automation tool, so search typing is not counted as verified by this UI run. The shared search implementation remains covered by the existing code contracts, not a substitute for full native-input coverage.

### Deterministic Encounter Bridge probe

Rare natural generation is not part of the validation procedure. A controlled Bridge probe uses the following sequence:

1. Require one effective standard file to be the sole contributor for the target `hall` or `room` type, and preserve its exact bytes and per-type row order.
2. Build a temporary highest-priority local Mod that overrides that same relative mash file and appends one selected conditional/additional composition as an ordinary row with `.chance 0`. This zero weight is intentional: the probe must never depend on, or perturb, natural weighted selection.
3. Enable the temporary Mod, reload the editor's content directory, and require the appended row to resolve to the next exact zero-based index with the expected composition and file fingerprint.
4. With the game closed, use the normal backed-up map transaction to write that proven index to a selected ordinary cell. Do not change `conditional_entry_counts`, `valid_additional_mash_entry_indexes`, roaming-region state, or `final_room_id`.
5. Let the user load the game and naturally enter the selected cell. Success means the expected complete composition loads, combat resolves, save/reload remains healthy, and the natural side-table counters remain untouched. Any different composition, assertion, crash, or counter requirement rejects the carrier and keeps special placement disabled.

This procedure tested both disputed facts directly: whether a zero-weight standard row remains addressable by an explicit map index, and whether a forced special encounter needs any natural-generation bookkeeping. The production workflow does not require repeatedly starting expeditions until a low-probability encounter happens. The temporary one-entry package and its manual installation/removal sequence were a validation protocol, not the production interaction. Production now keeps one profile-scoped managed Bridge enabled and appends stable rows; the editor must never remove or reorder that table merely because one battle ended. Leaving the expedition or restoring the pre-placement backup remains the unconditional fallback for a bad encounter.

The 2026-09-04 audit deliberately uses two profiles for different purposes. The nearly all-Mod `profile_1` is the inclusion/load-order stress baseline and remains the designated live-write test profile. `profile_0`, which leaves more Mods and DLC disabled, is a read-only negative control for proving that installed-but-disabled content is excluded; it is not a replacement write target. Its active `ship`, difficulty-1 expedition resolved 91 enabled Mods, 103 active content sources, four effective current mash files, 131 direct hallway rows, and 72 direct room rows. The production global scan completed in about 2.1 seconds and exposed 1,015 hallway, 210 room, and 95 fixed-boss Bridge candidates across 10 regions after excluding 72 rows with 22 unresolved active monster IDs. A no-write synthetic overlay also resolved its `.random_dungeon_roaming_id ninja` composition as `mash_type=0, mash_index=131`, but that package is not the live-test artifact.

The actual `profile_1` live-test baseline is its active `carnival`, difficulty-1 expedition: 124 enabled Mods, 136 active content sources, eight effective current mash files, 57 direct hallway rows, 57 direct room rows, one direct boss row, and 52 current conditional/additional rows. The production global scan likewise completed in about 2.1 seconds and exposed 1,526 hallway, 255 room, and 120 fixed-boss Bridge candidates across 14 regions after excluding 77 rows with 24 unresolved active monster IDs. The chosen protocol probe is the single-monster `thing_A` source row, which explicitly declares `.random_dungeon_roaming_id thing`. A generated top-priority zero-weight overlay preserved all 57 existing hallway rows and resolved `thing_A` as the single new `mash_type=0, mash_index=57` row.

The 2026-09-04 controlled live run wrote that binding to `coCG.tile1`. The game log then recorded `FillOutBattle with mash entry index:57`, loaded `thing_A`, and entered the normal battle-state sequence without a mash-index assertion, fatal error, or exception. The first game-written `persist.raid.json` contained exactly one enemy, `monster_class=thing_A` (localized as `星辰造物`), at round 1 with 106 HP. The corresponding map retained `content=1, mash_type=0, mash_index=57`, as required for loading the saved active battle. The user then reloaded that battle, defeated the actor, completed the native loot/save sequence, moved onward, and exited normally. The final map normalized the target to `content=0, mash_type=7, mash_index=-1`; `inbattle` became false and the serialized `battle` object disappeared. No cell anywhere in the active map retained index 57. `conditional_entry_counts` remained empty, `valid_additional_mash_entry_indexes` remained `[4,16,18,32]`, and `persist.campaign_mash.json` remained byte-identical throughout. The other five pending battles still referenced only original indexes 0 and 31. The user then disabled the Bridge, reloaded the same surviving expedition, and exited; the applied Mod list contained no Bridge, the map remained clean, all other pending indexes remained valid, and the log contained no mash/index assertion, fatal error, or exception. This confirms engine loading, exact composition/index resolution, a mid-battle save/reload, native victory cleanup, no natural side-table mutation, and safe removal after all references are gone.

The same run was save-safe but not audio-clean: `app.log` recorded 285,302 repetitions each of missing FMOD parameters `musicfix` and `distance_to_goal`. `musicfix` is declared by the Carnival Mod's own `sinister_circus.raid_settings.json`, and that Mod explicitly recommends the separate Modded Dungeons Music Fix (Workshop 3554661916), which was not active in this profile. A control reload after disabling the Bridge and with no battle active still produced 400 repetitions of each warning in about three seconds. The warning therefore belongs to the current Carnival/audio-Mod configuration rather than the Bridge or `thing_A`. It is not an encounter-index failure; forced cross-region placement still must not promise compatible music, narration, backdrop, or other runtime presentation assets.

`删除` is a real save operation when the selected tile has persisted nonzero content or a completed tile still carries a content binding. It sets `content=0`, normalizes `mash_index=-1` and `mash_type=7`, and clears static `cur`/`obstacle` plus dynamic `curio_prop`/`trap`. Knowledge, light, critical-scout state, coordinates, connectivity, and other topology remain unchanged. A truly empty tile is rejected. Deleting final-room content is permitted only after an explicit warning because it may make the current quest impossible to finish.

The 2026-09-03 live `profile_1` test confirmed that the game's natural completion state can retain used scenery after the actionable event disappears. Curio identity exists as one static `static_save...tiles.<tile>.cur` scalar plus one dynamic `curio_prop` scalar; obstacle identity is the static `obstacle` scalar; trap identity is the dynamic `trap` scalar; and battle identity is the dynamic mash pair. These are not additive collections. Both hard delete and every implemented create/replace operation clear the old mutually exclusive bindings in the same guarded transaction; create/replace then writes exactly the destination type's complete set shown in the tables. Users therefore never need to run delete before replacement, and an old used prop cannot coexist with the replacement.

Guarded or ambush content is one composite `content` value rather than independent overlays. Deleting `guarded_treasure`, for example, removes the combat, reward event, and its prop binding together. “Remove the guard but keep the reward” belongs to replace (`有战斗的宝箱房间` to `宝箱`), not delete.

## Boss encounter catalog

The boss picker is global. It must not filter candidates to the current dungeon region. Its accepted end state is:

- all resolvable Base-game boss encounters;
- enabled official DLC boss encounters;
- enabled local and Workshop Mod boss encounters;
- bilingual name where supplied, source, original region, concrete difficulty variant, and complete enemy composition.

A boss encounter is a complete mash composition, not one monster ID. Required companion actors or props such as Prophet pews, the Hag's cauldron, or Wilbur must remain in their defined order.

This fixed-boss picker is distinct from the roaming/special picker above. `boss:` candidates are offered only to eligible rooms as `mash_type=2`; confirmed or compatibility-layer `hall:`/`room:` roaming candidates keep `mash_type=0/1` and their authored target kind. Both pickers are global across Base, enabled official DLC, and enabled profile Mods, and both use the same composition/dependency/fingerprint checks and Encounter Bridge when a candidate is absent from the active raid table.

Pure save data can directly reference only entries present in the current region/difficulty's final mash table. Cross-region placement therefore uses a profile-scoped managed Encounter Bridge Mod. The picker hides this implementation: clicking `普通战斗`, `游荡首领 / 特殊遭遇`, or `固定首领` opens its searchable catalog directly, and selecting a row either reuses a current-table index or updates the carrier before writing the selected map cell through one confirmed operation. None of these entries adds a decorative ellipsis: their candidate count and click behavior already make the searchable follow-up clear. The implemented service:

- scans only Base, enabled official DLC, and enabled profile Mods; a Mod manifest is authoritative when present, while Mods without `modfiles.txt` use their conventional `dungeons` tree;
- exposes every standard `hall`, `room`, and `boss` row plus conditional/additional `hall` and `room` rows; ordinary cross-region formations and explicit roaming rows therefore share the same stable carrier without being conflated in the picker;
- requires every ordered `.types` member to resolve to an active `*.info.darkest` monster definition; unresolved dependency rows are omitted from the picker and summarized in the diagnostic log;
- classifies candidates using authored mash type, explicit roaming ID, and effective monster boss tags, then presents the combined special catalog in a themed searchable table with bilingual composition, raw IDs, row-level encounter type, target kind, original dungeon, concrete difficulty, and source; this includes nonmatching difficulty variants without rewriting their monster IDs or scaling their stats;
- creates a readable local-Mod identity per Steam user/profile, `DDSE_Managed_Encounter_Bridge（<profile> - <Steam user>）`, and installs it under the configured local-Mod root (falling back to the game's `mods` root when the selected path is itself one Mod); directory, project title and enabled identifier agree, without a hash suffix;
- writes the existing application icon as `preview_icon.png` on creation and refresh, includes it in `modfiles.txt`, and points project metadata at its installed path;
- inserts that local Mod at index `0` of the profile's `applied_ugcs_1_0` list on first use, or moves the same identity back to the top without changing the relative order or payload of any other entry;
- creates a dedicated `dungeons/<region>/ddse_managed.<region>.<difficulty>.mash.darkest` file containing only selected zero-weight `hall`/`room`/`boss` rows, without copying or overriding source tables; verifies unique path ownership and runtime append order before installation;
- treats identity as the complete ordered composition plus target table/type: the same composition is appended once per region/difficulty/virtual table/type and every later placement reuses its recorded index; a different companion or order is a separate encounter;
- never removes rows and never reorders earlier rows. Adding another encounter appends to the final contributing file for its type; staged re-resolution verifies that every existing type/index binding remains stable before installation;
- records source identity, source-relative path, full ordered composition, original region/difficulty, global `MashIndex`, required file-local `FileRowIndex`, hashes, and the non-Bridge active-content fingerprint in the version-4 `ddse-managed-encounter-bridge.json`;
- reconstructs the selected global source and current target table immediately before mutation, performs a DSON round trip for automatic profile activation, and then reloads the active-content resolver and requires the new row to resolve at exactly the recorded index before map placement;
- rejects a running game, stale profile/catalog, externally edited carrier, duplicate manifest index, ambiguous target type, or an attempt to append a new row after the non-Bridge Mod set/order changed. An already recorded row remains reusable only after both its local ordinal and global runtime index are revalidated. If post-write resolution fails, it attempts recovery of the game file and restores the carrier only while the game configuration is safely confirmed; newer external configurations and possibly referenced carriers are retained with an explicit error;
- keeps the managed Mod installed and enabled after victory. Users do not copy, enable, reorder, reload, disable, or delete per-encounter packages.

One managed package may carry multiple target tables and multiple encounters. Each target table is append-only; reuse is scoped to the complete formation and its `mash_type`, so a hall row, room row, and fixed `boss:` row remain distinct even if they contain the same monster IDs. A different source difficulty is allowed deliberately but is shown explicitly; no balancing promise is made. Version-3 copied-file packages require explicit removal and a decision about existing saved references before the dedicated version-4 package is installed.

The 2026-09-06 dependency audit found 62 excluded rows in the inspected installation: 44 reference mismatched, concatenated, or apparently mistyped IDs; 12 lack the named monster or difficulty variant; 6 contain a field marker joined to the last monster token (for example `fishman_shaman_B.limit` or `drowned_pirate_B.quirk_tag`). These counts describe that snapshot, not permanent exclusion lists. Do not substitute similar IDs, remove group members, synthesize difficulty variants, or split the joined tokens without evidence that the game accepts the same lexical boundary. The six joined-field rows remain unavailable pending that evidence. A missing dependency must block both direct and Bridge placement, while its original table index remains reserved. No original Mod file is repaired by the editor.

Do not modify `final_room_id`, quest goals, quest type, or boss-kill history merely to obtain a boss icon. Script-bound multi-wave, plot, or bespoke-map bosses require individual compatibility work and must not be represented as ordinary portable single-mash encounters until verified.

## Party movement contract

The word `移动` means moving the current party, not rearranging map content.

The writer treats movement as coordinated raid state, not a visual-coordinate-only edit. It resolves and validates:

- target area and target tile;
- corridor orientation/reversal;
- movement direction;
- previous area semantics;
- retreat source;
- doorway/transition state;
- current battle, camp, loot, interaction, and scripted-transition state.

Only rooms and visible corridor tiles remain selectable in the UI. Internal door-transition nodes are not movement targets. A room uses the native stationary convention `in_area=<room hash>`, `areatile=1`, `last_room_id=<room hash>`, and `party.retreat_room=<room hash>`. A corridor target converts its physical tile ordinal back into traversal progress using the area’s persisted `reversed` flag; `last_room_id` is the room referenced by that direction’s entry endpoint, while `party.retreat_room` is the corridor hash. The writer clears the transient doorway to the canonical no-destination value, sets `IsMovingLeft()=false`, and leaves topology, content, and knowledge unchanged.

Movement and deletion are refused while a battle, camp, pending loot/reward, doorway transition, teleport transition, or unsupported wave/script state is active. A target containing content is allowed after a warning: the content remains in place, and moving must not mark skipped paths explored or complete skipped encounters.

Moving writes a stable saved location; it does not replay the game's transition into that tile. Destination behavior is therefore content-specific and is not guaranteed to fire immediately. A room battle may be initialized when the room loads, while corridor triggers such as traps and obstacles normally depend on traversal and additional raid state.

Hunger is deliberately outside the editor's map-content scope. Base `scripts/map_generator.darkest` uses `.hallway_hunger <min> <max>` to choose the initial number and placement of hidden nodes. Base `shared/rules.json` defines `corridor_return_content/ac_hunger` probabilities for spawning new nodes when revisiting a corridor; those values are not a per-step activation roll for an already persisted node. The 2026-09-03 `profile_1` evidence showed that `coFL.tile1` remained `content=8` after both an editor move and repeated manual traversal while `party.hunger_room_buffer` protected the route. This matches the original hunger-buffer behavior: an existing node can remain pending while the party is temporarily ineligible to activate it. The 2026-09-06 UI revision displays the original hunger icon and labels the tile `进食格`, so it is no longer mistaken for editable empty space. It still omits create/replace/delete and retains only `移动队伍到此`, like the entrance; movement must leave the persisted `content=8` unchanged.

## Force-town recovery contract

`强制返回城镇` is an emergency Mod-testing recovery action, not a save-editor reimplementation of quest retreat or reward settlement. The established crash-loop repair procedure uses a general save editor to change only `persist.game.json/base_root.inraid` from `true` to `false` and `base_root.raiddungeon` from the active dungeon to `none`. This directly changes the next-load route while leaving the game's own town-load cleanup responsible for the remaining expedition state.

The action requires a real loaded map/raid pair, a matching `persist.game.json` whose `inraid` value is still `true`, and a closed game. Preparation captures and revalidates SHA-256 hashes for all three files, writes only the two documented fields in a workspace copy, performs a DSON encode/decode round trip, and preserves the original DSON revision. Commit creates a verified full-profile backup, revalidates and read-locks the game/map/raid state, then atomically replaces only `persist.game.json`. The replace operation captures the exact displaced destination and compares its hash with the prepared source, closing the final atomic-replace race without overwriting a newer external version. Final verification keeps the installed replacement read-locked; a failure restores the displaced original or retains the newer external version as appropriate.

The editor deliberately leaves `raid_save`, `persist.map.json`, `persist.raid.json`, `persist.loading_screen.json`, roster status, quest state, inventory, rewards, retreat penalties, week progression, and campaign log untouched. The operation therefore does not promise normal-retreat settlement; unfinished expedition progress or loot may be discarded when the game next loads the profile. After a successful edit, the battle view becomes unavailable until the user starts the game and loads the named profile. A stale game/map/raid snapshot or an already-running game blocks the write.

## Snapshot and write-guard contract

Every map edit (including delete, move and attachment removal) validates `persist.game.json` together with the map/raid pair before preparation. `inraid` must be true and `raiddungeon` must match the actual raid. Missing flags, town-state residue, active battle/camp/loot/transition state are rejected. The prepared edit records the game hash, and commit locks and rechecks it even for operations without a content catalog dependency.

The managed Bridge performs the same live-input and stationary-state validation before any installation or activation. When invoked from a map action, it also validates the selected target tile before staging persistent changes, including the required integer `content` in the original map document. A missing or malformed field is not accepted as the display reader's empty-tile default. Game/map/raid inputs stay locked during preparation; only the game lock is released for an intentional Bridge configuration replacement. Both map writes and Bridge game writes capture the actual displaced version, preserve concurrent external updates during recovery and report recovery failures with retained-file paths. A Bridge package is not rolled back if a newer external game configuration may still reference it.

- `persist.map.json` supplies area hashes, room/corridor kind, static tiles, map coordinates, dynamic knowledge, pending content, encounter indices, entrance, and final room.
- `persist.raid.json` supplies dungeon, difficulty, length, battle state, current area, previous room, and the party tile.
- A room has only static `tile0`; current native saves commonly persist `areatile=1` there, so room display resolves directly to that sole tile.
- In a corridor, `areatile` is traversal progress measured from the endpoint through which the party entered, not an absolute static tile index. For a forward corridor the physical ordinal is `areatile`; for `reversed=true` it is `tileCount - 1 - areatile`. Native movement evidence shows stable positions beginning at `1` on the first interior tile, while `0` remains representable as the endpoint transition.
- Map coordinates use the game's 24-pixel map grid. Room and hallway sprites are centered on the persisted `mappos` coordinates; no invented connection graph is drawn over them.
- Content values 0 through 13 follow the game's map-content ordering: empty, battle, ambush, trap, obstacle, happening, guarded curio, curio, hunger, treasure, guarded treasure, ambush curio, ambush treasure, and secret door.
- The reader copies each source with shared-read access, compares its SHA-256 before and after copying, then requires both captured hashes to remain unchanged through a short pair-verification window. A changing source is rejected rather than rendering a mixed snapshot. The DSON header revision is a format/game revision—not a per-save transaction ID—and is not used to claim that two files are from one write generation.
- A stable pair is also rejected when the raid's party area/tile cannot be resolved in the captured map, or when the static and dynamic topology is structurally incomplete. Stability alone is not sufficient evidence that two separately written documents belong together.
- A temporary read or render failure keeps the previous complete map visible and schedules another bounded-delay attempt even when no additional file event arrives. New visuals are fully staged before replacing the live canvas. The shared profile snapshot uses consistent `inraid`/`raiddungeon` flags to switch to town, including force-town saves that retain both raid/map documents.
- If the game flags still describe an expedition, either missing raid/map document is an incomplete save: preserve the previous map and retry, not an inferred return to town. The standalone control's fallback pair watcher remains available outside the main-window integration.

The loaded main window now owns a shared profile watcher for game, estate, roster, town, upgrades, raid and map saves. The embedded battle view disables its independent watcher and joins the same read/write coordination. Town/raid quantity caches and stale previews follow this snapshot automatically. Definition catalogs rebuild when semantic Mod/DLC/mode configuration or the polled encounter/monster/inventory file fingerprint changes. Selected content paths still require explicit loading. A separate offline maintenance step removes invalid Bridge rows, recalculates their indexes, and clears only recorded editor battles when those bindings change; original game battles and non-battle map edits remain outside that cleanup. A single main-window sync indicator replaces the embedded map's duplicate live status. See [automatic synchronization rules](content-save-rules.md#automatic-synchronization-of-the-selected-profile) and [battle record maintenance](encounter-runtime-order.md#automatic-editor-battle-maintenance-2026-09-07).

## Persistence milestone boundaries

The current milestone explicitly does **not**:

- write ambush-composite or unresolved named/scripted content replacements. Standalone ordinary props and room-battle attachments are implemented; dedicated hidden-room/authored-map compatibility remains incomplete;
- automatically remove the managed Encounter Bridge or rewrite previously assigned indexes; it must remain enabled while the profile may reference it;
- write unresolved content selected through a stale or unavailable picker;
- claim that a preview action survives application restart.

Implementation and evidence are tracked separately for each stage:

1. Read-only map and raid parser with topology and state diagnostics. **Complete.**
2. Real-map rendering and on-disk live refresh using the existing UI interaction surface. **Complete.**
3. Effective content catalog and deterministic room/corridor menu binding. **Implemented for current-table and global ordinary, conditional/additional, explicit-roaming, and fixed-boss rows, with bilingual monster names and boss-tag classification. Entrance and final-room guards exist. Dedicated hidden-room/authored-map compatibility and unresolved named/script content remain incomplete; do not imply that every scripted map is supported.**
4. Transactional ordinary content deletion and replacement. **Implemented for delete, proven battle placement, guarded ordinary-room battle attachment add/replace/remove, standalone room/hall curios and room treasure, and current-region ordinary traps/obstacles. New standalone paths have contract and editor-UI checks; in-game cross-region interaction is not yet live-verified.**
5. Party movement with room/corridor orientation probes. **Implemented from observed native room, forward-corridor, and reversed-corridor save conventions. The user tested movement to empty and battle rooms; this does not prove that coordinate-only movement replays every tile-entry event.**
6. Current-pool fixed-boss and ordinary encounter placement. **Implemented for proven single-file and root-level multi-file tables; see the bounded native evidence and contract coverage in [encounter runtime order](encounter-runtime-order.md). User-operated encounter tests and the recorded room-attachment tests provide bounded live evidence, not exhaustive coverage of every formation.**
7. Deterministic zero-weight Bridge carrier probe for conditional/additional encounters. **Complete: live testing confirms that the explicit zero-weight `thing_A` row loads, survives a mid-battle reload, clears after normal victory without touching natural side-table bookkeeping, and leaves the expedition loadable after Bridge removal.**
8. Production Encounter Bridge Mod and cross-region/Mod ordinary, fixed-boss, or roaming/special placement. **Implemented as one persistent profile-scoped managed carrier with automatic local installation, profile activation/top ordering, append-only multi-encounter tables, exact-index re-resolution, duplicate reuse, rollback, and the guarded map placement transaction. The user exercised automatic encounter writes; the follow-up carrier audit found three appended records. The historical one-entry probes validate the engine contract. These observations do not exhaustively validate all future Mod combinations or failures.**
9. Direct save-based force-town recovery. **Implemented from the established two-field crash-loop repair. The user completed a live return test followed by log/save inspection. It is not normal retreat settlement and does not promise recovery from every corrupted profile.**

The latest hard-delete change additionally removes consumed prop bindings. Its code, DSON round trips, backup, and rollback are covered by contracts; the earlier live deletion test used the old completion-only behavior and must not be relabelled as a live test of the new hard-delete visuals.

## Current milestone acceptance criteria

- A fourth equal-width `战斗 / BATTLE` tab exists to the right of Heroes.
- Catalog-only controls and write actions disappear on the battle tab.
- Town/no-profile state does not show a map.
- Raid state shows a clearly labelled real map from a stable guarded save snapshot.
- Unknown rooms, hallway squares, and available content are visible with the original scouted treatment without changing persisted knowledge.
- Changes written by the game refresh the map automatically without attaching to game memory.
- Rooms and every visible corridor tile are selectable; internal door-transition gaps are absent from the UI.
- The room/corridor menu has no corridor `宝箱`; unsupported system/script cells expose movement only. Unavailable content categories remain disabled until their complete binding catalogs exist.
- Eligible ordinary room battles expose `战斗附加内容` as a sibling of whole-cell `替换`; its bilingual exact picker supports curio-to-curio, treasure-to-treasure, and curio/treasure switching, while removal restores plain battle without changing the encounter mash. Corridors, entrance/final rooms, fixed bosses, and non-battle cells never expose it.
- The entrance exposes movement only; visited ordinary rooms and corridor tiles remain editable; final rooms follow the implemented special guards. Dedicated hidden-room support remains an incomplete part of the design rather than an accepted implementation claim.
- `战斗` separates ordinary encounters, roaming/special encounters, and fixed boss-room encounters. Bridge-eligible Base/DLC/enabled-Mod candidates use a dedicated bilingual composition search table with region, source, and encounter-type information; difficulty is a separate selector, and the internal roaming ID is not a user-facing column. `hall:` candidates remain corridor-only, `room:` candidates remain room-only, and fixed `boss:` candidates remain room-only.
- Roaming/special placement writes an ordinary battle marker and does not mutate `final_room_id`, weekly roaming assignments, or natural conditional/additional counters. The visible category opens the searchable picker directly; the managed Bridge materializes or reuses the row and re-resolves a valid current-table index internally, so users never select or manage foreign indexes.
- Every displayed room and corridor tile offers confirmed transactional party movement when the raid is in a stationary supported state.
- A real loaded expedition offers a confirmed and fully backed-up force-town save edit that changes only `inraid` and `raiddungeon`, preserves the map/raid pair, and clearly does not promise normal-retreat settlement.
- Right-drag pan, wheel zoom, 100%, and fit-to-view operate without resizing surrounding layout.
- Runtime game assets remain read-only. Standalone `新建` and `替换`, `删除`, verified battle placement, room-battle attachment editing, and `移动队伍到此` use the battle-map save service, full-profile backup, paired stale-hash guards, round-trip validation, and rollback. Curios may cross regions, but not room/hall pool membership. Trap/obstacle automatic selection and the writer both require the current region.
