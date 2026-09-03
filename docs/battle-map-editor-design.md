# Battle Map Editor Design

## Status

This document records the agreed product behavior for the battle-map workspace. The current implementation milestone is **real-map display plus transactional delete and party movement**. `删除` may replace `persist.map.json`, and `移动队伍到此` may replace `persist.raid.json`; both operations require the game to be closed, validate the displayed map/raid hashes as one guarded pair, perform a DSON encode/decode round trip, create a full-profile backup, and restore the edited file if final verification fails. Content create and replace remain preview-only. Profile Mod configuration and generated Mod files are not changed.

The battle page now joins the save's static topology to its dynamic knowledge/content state and current raid position. Its map panel, rooms, corridors, exploration markers, event markers, and party marker are loaded at runtime from the user's selected Darkest Dungeon installation. Original game artwork is not copied into or redistributed with the editor. A visibly simplified fallback is allowed only when the selected installation does not contain the required asset. Create/replace context-menu actions remain preview-only and are discarded by the next save refresh; delete/move use the guarded write path described below.

The editor uses a display-only global-vision rule. Every room and visible corridor tile is shown even when its persisted knowledge is unknown, and available room types plus hallway content markers are presented as if the location had been scouted. The underlying `knowledge` value is retained for tooltips and state decisions; opening the editor does not reveal the map inside the game or write scouting progress to the save. Deleting content or moving the party likewise does not promote knowledge or mark skipped routes explored.

“Real time” means following complete save snapshots after the game writes them to disk. The implementation watches `persist.map.json` and `persist.raid.json`, debounces burst writes, verifies stable copies and the pair across a quiet window, retries transient partial writes, and periodically compares file stamps in case an operating-system event is missed. Monitoring belongs to the loaded profile rather than the currently visible tab, so hiding and reopening the battle tab cannot silently miss a save. It does not read game process memory, inject code, or modify a running game.

## Product goal

Add a fourth main workspace named `战斗 / BATTLE` to the right of `人物 / HEROES`. When a profile is currently in an expedition, this workspace will eventually display the complete current dungeon map and act as a save-console surface for:

- inspecting rooms, visible corridor tiles, exploration state, and pending content;
- creating, replacing, or deleting room and hallway content;
- placing any resolvable Base, DLC, or enabled-Mod boss encounter in an eligible room;
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

## Context-menu contract

Every eligible room and visible corridor tile exposes `移动队伍到此`.

### Empty tile

- `新建`
  - `奇物`
  - `战斗`
    - `普通遭遇`
    - `首领遭遇` for rooms
  - `宝藏`
  - `陷阱` for corridor tiles
  - `障碍` for corridor tiles
- `移动队伍到此`

### Occupied tile

- `替换`, with the same category rules as `新建`
- `删除`
- `移动队伍到此`

Entering a room or traversing a corridor does not by itself make the location ineligible for create/replace. Eligibility is determined by current transient state and special scripting, not simply by `visited` knowledge.

`新建` and `替换` still apply only to the in-memory preview and are labelled as not written to the save. Content previews must preserve the cell's persisted knowledge state so an unknown location remains labelled and styled as unknown/global-vision rather than being falsely promoted to visited. A subsequent game save refresh restores the authoritative map content.

`删除` is a real save operation only when the selected tile has persisted nonzero content. It sets `content=0`, normalizes `mash_index=-1` and `mash_type=7`, and deliberately preserves knowledge, light, critical-scout state, plus the original curio/trap identity hashes. This matches observed naturally consumed tiles and avoids turning a content deletion into a full tile reset. Deleting final-room content is permitted only after an explicit warning because it may make the current quest impossible to finish.

The 2026-09-03 live `profile_1` test confirmed the visible consequence of this native completion state: a deleted curio no longer has an actionable map event, but its already-used scenery can remain in the dungeon. Curio identity exists as one static `static_save...tiles.<tile>.cur` scalar plus one dynamic `curio_prop` scalar; obstacle identity is the static `obstacle` scalar; trap identity is the dynamic `trap` scalar; and battle identity is the dynamic mash pair. These are not additive collections, so future create/replace must never merge bindings. It must first normalize every old type-specific binding, then write exactly the destination type's complete scalar set. This prevents an old used prop from coexisting with a replacement even though ordinary delete continues to model natural completion rather than rewriting static map topology.

Guarded or ambush content is one composite `content` value rather than independent overlays. Deleting `guarded_treasure`, for example, removes both the combat and treasure event: the retained curio hash is inert while `content=0`, so neither the game nor the editor should display a treasure/curio marker afterward. “Remove the guard but keep the reward” belongs to the future replace operation (`guarded_treasure` to `treasure`), not delete.

## Boss encounter catalog

The boss picker is global. It must not filter candidates to the current dungeon region. Its accepted end state is:

- all resolvable Base-game boss encounters;
- enabled official DLC boss encounters;
- enabled local and Workshop Mod boss encounters;
- bilingual name where supplied, source, original region, concrete difficulty variant, and complete enemy composition.

A boss encounter is a complete mash composition, not one monster ID. Required companion actors or props such as Prophet pews, the Hag's cauldron, or Wilbur must remain in their defined order.

Pure save data can directly reference only entries present in the current region/difficulty's final mash table. Cross-region placement therefore requires a future profile-scoped Encounter Bridge Mod. The bridge design must:

- append stable, uniquely named encounter entries without reordering existing entries;
- preserve the complete composition and concrete variant actually defined by the source;
- validate source Mod/DLC availability and dependency resolution;
- retain old entries while an expedition can still reference them;
- record a composition fingerprint and refuse a stale or ambiguous mapping;
- be written and enabled before the save points at the new encounter;
- use full-profile backup and transactional rollback.

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

Hunger is deliberately outside the editor's map-content scope. Base `scripts/map_generator.darkest` uses `.hallway_hunger <min> <max>` to choose the initial number and placement of hidden nodes. Base `shared/rules.json` defines `corridor_return_content/ac_hunger` probabilities for spawning new nodes when revisiting a corridor; those values are not a per-step activation roll for an already persisted node. The 2026-09-03 `profile_1` evidence showed that `coFL.tile1` remained `content=8` after both an editor move and repeated manual traversal while `party.hunger_room_buffer` protected the route. This matches the original hunger-buffer behavior: an existing node can remain pending while the party is temporarily ineligible to activate it. The editor therefore renders a persisted hunger node as an ordinary empty hallway square, omits create/replace/delete for that square, and retains only `移动队伍到此`; movement must leave the hidden `content=8` unchanged.

## Snapshot and write-guard contract

- `persist.map.json` supplies area hashes, room/corridor kind, static tiles, map coordinates, dynamic knowledge, pending content, encounter indices, entrance, and final room.
- `persist.raid.json` supplies dungeon, difficulty, length, battle state, current area, previous room, and the party tile.
- A room has only static `tile0`; current native saves commonly persist `areatile=1` there, so room display resolves directly to that sole tile.
- In a corridor, `areatile` is traversal progress measured from the endpoint through which the party entered, not an absolute static tile index. For a forward corridor the physical ordinal is `areatile`; for `reversed=true` it is `tileCount - 1 - areatile`. Native movement evidence shows stable positions beginning at `1` on the first interior tile, while `0` remains representable as the endpoint transition.
- Map coordinates use the game's 24-pixel map grid. Room and hallway sprites are centered on the persisted `mappos` coordinates; no invented connection graph is drawn over them.
- Content values 0 through 13 follow the game's map-content ordering: empty, battle, ambush, trap, obstacle, happening, guarded curio, curio, hunger, treasure, guarded treasure, ambush curio, ambush treasure, and secret door.
- The reader copies each source with shared-read access, compares its SHA-256 before and after copying, then requires both captured hashes to remain unchanged through a short pair-verification window. A changing source is rejected rather than rendering a mixed snapshot. The DSON header revision is a format/game revision—not a per-save transaction ID—and is not used to claim that two files are from one write generation.
- A stable pair is also rejected when the raid's party area/tile cannot be resolved in the captured map, or when the static and dynamic topology is structurally incomplete. Stability alone is not sufficient evidence that two separately written documents belong together.
- A temporary read or render failure keeps the previous complete map visible and schedules another bounded-delay attempt even when no additional file event arrives. New visuals are fully staged before replacing the live canvas. Removal of the raid/map save switches the page to the town state while profile monitoring continues for the next expedition.
- If exactly one of the two raid/map documents is temporarily absent, the page treats it as an incomplete write, preserves the previous map, and retries. Only the absence of both documents is treated as the confirmed town state.

The profile watcher is intentionally reusable. This milestone connects only the battle map. A later change may let raid items refresh from `persist.raid.json` and town inventory/trinkets refresh from `persist.estate.json`; hero, trinket, quirk, and localization definitions should only be rebuilt when `persist.game.json` or selected content paths change, not on every map step.

## Persistence milestone boundaries

The current milestone explicitly does **not**:

- resolve curio, encounter, trap, obstacle, or boss catalogs;
- alter profile Mod order or generate an Encounter Bridge Mod;
- write content created or replaced through the provisional picker;
- claim that a preview action survives application restart.

The later write implementation should be split into independently testable stages. Stages 1 and 2 are now complete:

1. Read-only map and raid parser with topology and state diagnostics. **Complete.**
2. Real-map rendering and on-disk live refresh using the existing UI interaction surface. **Complete.**
3. Content catalog and deterministic menu binding.
4. Transactional ordinary content deletion. **Complete for delete; create/replace pending catalog binding.**
5. Party movement with room/corridor orientation probes. **Implemented from observed native room, forward-corridor, and reversed-corridor save conventions; real-game validation remains manual.**
6. Current-pool boss placement.
7. Encounter Bridge Mod and cross-region/Mod boss placement.

## Current milestone acceptance criteria

- A fourth equal-width `战斗 / BATTLE` tab exists to the right of Heroes.
- Catalog-only controls and write actions disappear on the battle tab.
- Town/no-profile state does not show a map.
- Raid state shows a clearly labelled real map from a stable guarded save snapshot.
- Unknown rooms, hallway squares, and available content are visible with the original scouted treatment without changing persisted knowledge.
- Changes written by the game refresh the map automatically without attaching to game memory.
- Rooms and every visible corridor tile are selectable; internal door-transition gaps are absent from the UI.
- Empty and occupied locations receive the correct context-menu shape.
- Boss appears under `战斗` and is offered for rooms through Base/DLC/Mod groups.
- Every displayed room and corridor tile offers confirmed transactional party movement when the raid is in a stationary supported state.
- Right-drag pan, wheel zoom, 100%, and fit-to-view operate without resizing surrounding layout.
- Runtime game assets remain read-only. `新建` and `替换` remain in-memory previews; `删除` and `移动队伍到此` use the battle-map save service, full-profile backup, paired stale-hash guards, round-trip validation, and rollback.
