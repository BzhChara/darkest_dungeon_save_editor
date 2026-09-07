# 编辑器战斗自动维护与文档整理：2026-09-07

本次按已确认的方案实现：活动 Mod 变化导致编辑器记录的战斗编号、组合或可用性失效时，删除本档案 Bridge 中确认失效的组合，重新计算保留组合的编号，并清空当前副本中仍能由成功写入记录确认的全部编辑器战斗，包括直接写入的本地区战斗。

## 实际行为与文件

| 文件或范围 | 修改目的 |
| --- | --- |
| [BattleEncounterCatalog.Maintenance.cs](../../src/DarkestDungeonSaveEditor.Core/BattleEncounterCatalog.Maintenance.cs)、[ProfileCatalogSnapshotReader.cs](../../src/DarkestDungeonSaveEditor.Core/ProfileCatalogSnapshotReader.cs) | 检测活动战斗文件、怪物定义与清单变化，按当前加载顺序重新解析编号；文件变化不再依赖存档同时变化。 |
| [EditorBattleHistory.cs](../../src/DarkestDungeonSaveEditor.Core/EditorBattleHistory.cs)、[BattleMapSnapshot.cs](../../src/DarkestDungeonSaveEditor.Core/BattleMapSnapshot.cs)、[BattleMapSnapshotReader.cs](../../src/DarkestDungeonSaveEditor.Core/BattleMapSnapshotReader.cs)、[BattleMapEditService.cs](../../src/DarkestDungeonSaveEditor.Core/BattleMapEditService.cs) | 用成功写入备份记录战斗归属，并加入副本实例标识；核验已有备份后可读取此前的放置记录。 |
| [ManagedBattleEncounterBridgeService.Maintenance.cs](../../src/DarkestDungeonSaveEditor.Core/ManagedBattleEncounterBridgeService.Maintenance.cs) | 清理失效 Bridge 行，重算文件内序号和游戏中的编号，协调地图与 Bridge 写入及完整备份。 |
| [ManagedBattleEncounterBridgeService.MaintenanceRecovery.cs](../../src/DarkestDungeonSaveEditor.Core/ManagedBattleEncounterBridgeService.MaintenanceRecovery.cs)、[SaveCommitMarker.cs](../../src/DarkestDungeonSaveEditor.Core/SaveCommitMarker.cs)、[BattleMapWriteGuard.cs](../../src/DarkestDungeonSaveEditor.Core/BattleMapWriteGuard.cs) | 防止并发覆盖；失败时回滚，重启后可恢复未完成的维护；完整提交标记先写临时文件，再发布。 |
| [BattleMapSaveEditor.cs](../../src/DarkestDungeonSaveEditor.Core/BattleMapSaveEditor.cs) | 只清空能确认归属的战斗字段，保留奇物、宝藏及其他地图内容。 |
| [MainWindow.ProfileSync.cs](../../src/DarkestDungeonSaveEditor.App/MainWindow.ProfileSync.cs)、[BattleMapView.Commands.cs](../../src/DarkestDungeonSaveEditor.App/BattleMapView.Commands.cs) | 在档案载入、自动同步和地图写入前执行维护检查；每 10 秒检查相关内容文件；记录原因、数量、备份路径，并合并重复提示。 |
| [EncounterMaintenanceContractTests.cs](../../tests/DarkestDungeonSaveEditor.ContractTests/EncounterMaintenanceContractTests.cs)、[ProfileSyncContractTests.cs](../../tests/DarkestDungeonSaveEditor.ContractTests/ProfileSyncContractTests.cs)、测试入口 | 添加维护和内容热更新的隔离回归验证，并提供单独的维护测试入口。 |
| `README.md`、当前规则文档、`docs/change-history/` | 更新有效规则，将 7 份历史报告移到独立目录，修复引用并新增索引。 |

例如，保留的 Bridge 组合原先使用走廊编号 2；当前原生走廊表增加到 3 条，同时删除一条更早的失效 Bridge 组合后，该保留组合变为编号 3。后续放置使用 3；当前副本中已记录的编辑器战斗统一清空，不改写成新编号，也不恢复被编辑器替换掉的原战斗。

原生战斗、探索状态、队伍位置及其他地图修改保持原样。仅修改名称、描述或数值，而没有影响已记录战斗的编号、组合和可用性时，不清空战斗。游戏运行时暂缓写入，退出后自动重试。只读目录加载本身仍不写存档。

## 独立审查后修正

独立只读审查发现以下 3 个问题，均在隔离数据中验证，并增加针对性回归场景：

1. **放置记录丢失后可能仍压缩 Bridge。** 现在遇到地图上的旧 Bridge 编号却没有本副本成功放置记录时，暂缓整次清理并保留地图及包文件。既不猜测它是编辑器战斗，也不留下悬空引用后继续压缩。
2. **本地 Mod 标题无法匹配时可能误判怪物缺失。** 现在把这种情况视为活动来源尚未完整识别，暂缓清理；恢复标题识别后重新检查。
3. **提交标记写到一半可能阻断恢复。** 新标记通过临时文件发布，并验证完整性；已有缺失、截断或字段不完整的维护标记均按未完成事务恢复。已恢复的记录不会再次干扰历史读取。

以上是局部保护和恢复修正，按协作约定完成受影响检查后不递归追加审查。

## 验证与边界

维护定向测试的 16 个场景全部通过，包括本地与跨区战斗一起清空、原生战斗和附加内容保留、新副本隔离、历史记录完全或部分丢失、来源识别异常、缺失与编号变化、DSON 版本保留、游戏运行时延后、故障回滚、提交标记异常恢复及外部存档版本保护。修正审查问题后，完整回归测试通过且无跳过；正常 Release 构建为 0 警告、0 错误，输出的 Core DLL 与完整测试使用的 DLL 哈希一致。文档校验检查了 89 个相对链接，无失效链接。本地详细验证记录位于 `workspaces/bridge_invalidation_20260907/validation.md`，属于运行产物，不纳入版本控制；测试结论与审查修正摘要保留在本文中。

开发者模式已启用；实际非管理员目录/文件符号链接创建及对应清单拒绝测试均通过。移动后的 7 份历史报告正文保持原样，仅调整相对链接。

仍需保留以下边界：

- 已删除的放置历史不能重新推断本地区直接写入战斗的归属。若当前地图存在无法确认归属的旧 Bridge 编号，需要恢复记录或离开该副本后再维护。
- 无法证明的来源、体型或目标表排序会暂缓清理。旧 v3 Bridge 不在本次迁移范围内。
- 后续追加仍要求 Bridge 是目标类型的最后一个加载来源。新 Mod 改变优先级后，可能需要将 Bridge 恢复到最高优先级；本次维护不会自动调整其他 Mod 的顺序。
- 本轮只在隔离夹具中写入，实际档案仅作只读核对；未执行新版本的游戏内战斗测试。
