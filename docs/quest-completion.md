# Force complete expedition objectives

The battle toolbar provides **Force complete quest** for a loaded, stationary, non-wave expedition outside the Old Road tutorial. It prepares a preview and asks for confirmation before writing. After writing, the editor waits for profile synchronization before restoring editing. The player reloads the expedition and uses the game's completed-quest return and settlement flow.

This is an exceptional offline operation. It does not simulate skipped battles, curio interactions, item consumption, or their loot. Deleting a required battle also deletes treasure or curios attached to that location, using the existing map deletion implementation. Unrelated map locations and possessions are not cleared. The editor does not award completion rewards, increment the week, change campaign completion history, or force the profile into town.

## Objective handling

Targets come from the current expedition's `raid_instance.goal_ids`, resolved against effective Base/DLC/Mod definitions using the existing quest resource order and native identity rules. Display names and quest ID substrings do not determine behavior.

| Native objective | Offline operation |
| --- | --- |
| `explore_room` | Recompute the target using eligible rooms, explicit amount or native float32 percentage/truncation; raise `explored_room_count`. Rooms with a hidden door are excluded. |
| `battle_room`, percentage based | Delete remaining qualifying room battles, including attached content. Set the saved battle counts to zero, matching the target that startup recomputes from the now-empty eligible battle set. |
| Fixed positive battle count | Reject: startup resets the current count and retains the fixed target, so deleting battles does not solve this objective. |
| `kill_monster` | Raise the saved kill count to the definition amount. A boss encounter does not count as killed merely because it was deleted. The Flesh's native requirement is four kills despite displaying as one boss. |
| `activate` | Raise the activation count. Do not simulate curio scripts or additional loot. `gather_bloodcrate` is this type despite its ID. |
| `gather` | Add missing actual items using the existing native inventory identities, stack limits and capacity checks. Never discard unrelated inventory to make room. |
| Old Road / `tutorial_room` | Unsupported. Complete the tutorial in the game; map editing and force return to town are also disabled on the Old Road. The first Ruins quest is not the Old Road. |
| `encounter_monster`, `deaths_door`, `trait_applied` | Update the corresponding saved counter only if explicitly present in the main objective list. Ordinary town-progression goals are never edited. These native serializers are verified statically, not claimed as observed ordinary missions. |
| Wave/survival or another unsupported type | Reject the entire operation without a partial write. Force return to town remains available for waves. |

An expedition with several objectives is prepared as one change. Any unsupported or unresolved objective aborts preparation. Already sufficient counters and inventory quantities are not reduced. Missing definitions, changed input, active battle/camp/loot/transition state, or a failed native codec roundtrip prevents writing.

## Save boundary

`persist.game.json` determines the active `raid_save` directory. Only the active `persist.map.json` and/or `persist.raid.json` are written. Root leftovers and other persistent expeditions remain untouched. Preparation reuses `BattleMapWriteGuard`, checks the current content and snapshots, and performs a C# DSON encode/decode comparison with revision preservation.

Commit uses the existing guarded file replacement and reverse recovery mechanism, a full profile backup, and a transaction status record. Game/map/raid hashes and active resource fingerprints are checked around the write. A newer external file is not silently overwritten during recovery. Normal profile synchronization refreshes the inventory and map together after a successful operation.

## Evidence and validation

Static analysis of the installed x64 build established restore/start ordering (`0x140572710`, `0x140600370`), the clear-battle reset (`0x140579F80`), room eligibility (`0x14057A250`), exploration target recomputation (`0x14057A740`), and native objective completion/return (`0x140572480`, `0x140744250`). The examined executable SHA-256 is `9972B79F0EA892C82BF32CB1F4CF85A8E2B30270FCE219D60385C19013965EA9`.

Contract tests use reduced native save structures and objective excerpts from Base/DLC and the inspected Sunward/Vermintide content. Explicit synthetic combinations are transaction/failure controls, not claims that those combinations naturally occur. Run the C# contracts with `--quest-completion`; `--wave-battle` covers the disabled completion control and retained town recovery. `--tutorial-battle` covers Old Road rejection, its localized notice and ordinary Ruins availability. No Java checks are required.

Steam live acceptance on 2026-10-29 and 2026-10-30 passed for all five ordinary objective types: Ruins exploration, a Carnival Mod room-battle mission, the Siren boss, Cove activations, and Holy Relic collection. Each case used the production preparation/commit path, reloaded into the native completion prompt, completed the game's victory settlement, and verified the saved result. The original profile was restored after testing, with all 35 file hashes matching. Local evidence is retained in `artifacts/quest-completion-live-20261029/acceptance.md`.

These representative tests do not prove that every Mod's independent combat or interaction scripts have executed. Those scripts are deliberately not simulated by this operation. Old Road and wave missions were excluded from completion acceptance according to the supported scope above.
