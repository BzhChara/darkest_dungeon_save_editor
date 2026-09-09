# 地图资源旧规则修复（2026-09-09）

## 范围与依据

用户确认修复[第四轮审核](map-resource-rule-audit-2026-09-09.md)的五项问题。基线提交为 `d55a15f`。本次只修改编辑器源码、隔离契约测试和文档，没有改动真实档案、游戏资源或 Mod，也没有创建新的 Git 提交。

依据为已有 Windows x64 build 27890 只读反汇编和原生读取器研究；游戏文件哈希、函数地址及旧实现反例见审核记录。本次没有新增游戏内 A/B 实验，不将契约测试表述为实机验证。

## 实际修改

| 文件 | 修复及影响 |
| --- | --- |
| [BattleRoomAttachmentCatalog.cs](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.cs) | 仅将 `dungeons/<region>/<region>.props.darkest` 纳入地区池，按既有清单、Mod 优先级和 DLC 挂载解析有效文件；移除逐物理行、首字段和 ID 去重旧逻辑；保留原始 ID 哈希和精确名称键；预检接受合法重复项并核对实际声明位置。 |
| [NativeDarkestReader.cs](../../src/DarkestDungeonSaveEditor.Core/NativeDarkestReader.cs) | 提供带物理行号的记录读取及原始字符串槽读取。地图池采用自己的空串/点前缀/64 槽策略，人物技能等调用者继续保留各自规则。 |
| [ProfileCatalogContentFingerprint.cs](../../src/DarkestDungeonSaveEditor.Core/ProfileCatalogContentFingerprint.cs) | 刷新指纹同时包含标准池直接打开路径；不因 `_template` 等目录发现条件漏掉已读取池的内容变化。 |
| [BattleRoomAttachmentCatalog.RegionalSelection.cs](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.RegionalSelection.cs) | `.chance` 复用原生浮点字段读取；每个类型获得完整权重，重复类型和重复声明均累加；删除文件覆盖后的二次 ID/Mod 权重筛选。 |
| [BattleMapEditService.cs](../../src/DarkestDungeonSaveEditor.Core/BattleMapEditService.cs) | 地图写入日志在物理行号之外记录 `SourceRecordIndex`，区分同一行中的多条声明。 |
| [MapPropNativeContractTests.cs](../../tests/DarkestDungeonSaveEditor.ContractTests/MapPropNativeContractTests.cs) | 新增原生路径、解析、身份、刷新和写入前重验的隔离反例。 |
| [RegionalMapContentContractTests.cs](../../tests/DarkestDungeonSaveEditor.ContractTests/RegionalMapContentContractTests.cs) | 纠正旧测试中的任意文件名、权重平分、去重及双精度上限假设，增加重复项和 float 溢出验证。 |
| [BattleMapContentContractTests.cs](../../tests/DarkestDungeonSaveEditor.ContractTests/BattleMapContentContractTests.cs) | DLC 夹具采用完整标准文件覆盖；增加带空格陷阱 ID 的 JSON 和 DSON 实际事务写入。 |
| [Program.cs](../../tests/DarkestDungeonSaveEditor.ContractTests/Program.cs)、[ContractSuite.cs](../../tests/DarkestDungeonSaveEditor.ContractTests/ContractSuite.cs) | 增加 `--map-content`，运行地图组；完整套件仍按既有顺序串行运行。 |

现行规则同步至[资源重复定义规则](../resource-duplicate-semantics.md#10-map-prop-pools-and-native-identities)、[地图设计](../battle-map-editor-design.md#standalone-map-content-2026-09-06)、[内容与存档规则](../content-save-rules.md)及[测试说明](../../tests/DarkestDungeonSaveEditor.ContractTests/README.md)。

## 行为变化和保留边界

- 标准池之外的 `extra.props.darkest`、分类/备份目录副本不会再使奇物、宝箱、陷阱或障碍获得错误的池资格。已启用 Mod 仍受 `modfiles.txt` 约束；同路径覆盖保留既有原生解析器。
- 多行记录、同一行多条记录、注释、末次字段、数字前缀、百分比、NUL 和 64 个原始类型槽遵循已研究规则。第一个空字符串终止此列表；点前缀类型不终止。该行为与战斗表中的空怪物槽规则不同。
- `alpha beta / gamma` 三项具有相同 chance 时各占三分之一；`alpha alpha beta` 合计为 2:1。界面可只展示一次 alpha，自动池仍保留两份贡献。合法重复项不会因预检要求“只能出现一次”而无法写入。
- `alpha`、`ALPHA`、` alpha ` 分别保留 ID、名称请求和哈希。实际编码后的地图也使用原始 ID 哈希。真正的哈希碰撞仍被拒绝。
- 自动同步继续检测定义内容变化，即使清单没变化。刷新后按现有目录重新绑定；预览和提交均检查源文件、声明位置、资源、区域与存档。未使用的额外 props 文件不加入地图目录指纹。
- 清单列出但缺失的标准池仍参与覆盖选择：有效胜出文件无法读取时阻止加载/写入；被完整覆盖的缺失低优先级文件不妨碍可读胜出文件。这是避免采用错误下层数据的编辑器保护，尚未声称原生打开失败时的回退行为已全部实测。
- 无效 UTF-8 的池以及在原生 63 字节边界截断 UTF-8 字符的 ID 会记录原因并排除；不使用替换字符另算哈希。NUL 后字节不参与解析。纯空白 ID 仍不支持。这些是编辑器支持边界，不等于游戏必然拒绝相同字节。
- `.mash` 多文件战斗编号、Bridge 新建/复用/维护、场景保护、地图事务、人物和背包业务规则不作变更。CSV 映射、JSON 继承歧义和特殊脚本资源保护继续保留，本次没有宣称已验证所有奇物互动或继承字段。

## 验证记录

- 地图组 `--map-content` 已通过。产物：`workspaces/contract_tests/20260909_082056_412_9c6bd3faf84e49d1acc7ac0f75993866`；日志：`workspaces/map-prop-fixes-targeted-20260909.log`。
- 资源语义组 `--semantics` 已通过，覆盖人物技能/模式效果、Buff/Effect、怪癖线索、物品身份、事件依赖，以及三种战斗类型的空遭遇占号、直接/Bridge 新建替换删除和维护。产物：`workspaces/contract_tests/20260909_082331_650_2565699f7ff14c1dbdbc5b18cf78ca42`；日志：`workspaces/map-prop-fixes-semantics-20260909.log`。
- 首次新增夹具使用 Windows 保留目录名 `nul` 导致失败，已改名；补充 DSON 测试时误用 `Id` 属性导致一次编译失败，已改为实际 `TileId`。上述最终成功运行包含这两项修正。
- 完整契约套件通过（exit 0），覆盖既有目录/数量/人物、同步、地图、战斗/Bridge、维护、强制返回和事务回滚路径。产物：`workspaces/contract_tests/20260909_082506_422_c5e2a870a1bc40fdbb2ad2ae3d715c97`；日志：`workspaces/map-prop-fixes-full-20260909.log`。
- `dotnet build DarkestDungeonSaveEditor.sln -c Release --no-restore -m:1` 通过，0 警告、0 错误；日志：`workspaces/map-prop-fixes-build-20260909.log`。应用产物位于 `src/DarkestDungeonSaveEditor.App/bin/Release/net8.0-windows/`。
- `git diff --check` 通过；七份相关文档的 91 个本地文件链接通过。常规 Git 换行转换提示单独记录，不修改全仓换行策略。
- 独立只读复核发现两项局部遗漏：标准池仍受 `NativeDirectoryDiscovery` 过滤、地图记录未使用原生前缀分发。主代理分别核对过滤器和 `0x1404AF260`–`0x1404AF4CB` 的原生分支后确认，已修正并补充 Base/DLC `_template` 标准池、同大小同时间戳刷新、前缀记录与负例测试。上述初轮通过结果不作为这两项补改后的最终结果，最终重跑记录见下。

## 复核后最终验证

- 地图专项重跑通过，产物：`workspaces/contract_tests/20260909_083903_614_d5d9325b97224933952cf7df40c52581`；日志：`workspaces/map-prop-fixes-targeted-final-20260909.log`。
- 完整契约重跑通过（exit 0），包含自动同步、地图、战斗/Bridge、物品/饰品/人物和事务回归。产物：`workspaces/contract_tests/20260909_084115_259_188d81a83b3f4828a0835822a7ec75f8`；日志：`workspaces/map-prop-fixes-full-final-20260909.log`。
- 共享文本读取器本身未在复核后改变；按协作约定不为两项局部修补再递归发起复核。复核提出的两项问题均已核实修复，最终测试包含对应反例。
- 最终 Release 解决方案构建通过（exit 0），0 警告、0 错误；日志：`workspaces/map-prop-fixes-build-final-20260909.log`。Core、应用及契约测试目录中的 Core DLL SHA-256 一致，均为 `D8297F81AA8D72CFA0111A7D03D380D1702A09DE21A3C896FCB81A2BB9FBB50D`。
- 七份相关文档的本地文件链接现为 92 个，全部检查通过；最终 `git diff --check` 通过。所有发现已处理，本次改动留在工作区供提交。
