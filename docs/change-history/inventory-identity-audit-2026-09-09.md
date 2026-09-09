# 历史修改第三轮审核：物品、饰品身份（2026-09-09）

## 提交点与范围

按用户要求，先将上一轮已验证修改提交为 `27a6c17`（`fix: remove obsolete hero and item parsing heuristics`），提交后工作区干净。本报告记录提交后的审查和修正，未自动进行第二次提交。

本轮对照历史提交、现行解析和存档调用链、已记录的原生查询规则及隔离契约测试。没有启动游戏、修改真实存档或 Mod，也没有新增依赖。实验证据沿用 Windows x64 build 27890，SHA-256 `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`；本轮新增的是编辑器端可执行反例及回归。

## 发现与修正

| 历史规则 | 原意及问题 | 当前处理 |
| --- | --- | --- |
| 物品 `CatalogKey.ToUpperInvariant()`，可追溯到 `fb7b0fd`；饰品目录的忽略大小写字典来自初始实现 | 方便合并与查找，但把不同原生哈希的 ID 合成一项 | 物品类型/ID、饰品 ID 精确比较；目录键使用类型长度前缀，避免分隔符产生另一种碰撞 |
| `4e1db6b` 对被上述饰品字典合并的大小写变体加“不可用”保护 | 防止误选，但没有修正目录、计数和写入链上的错误合并 | 删除这种合并后禁用；保留真正的原生哈希碰撞与文件来源不明保护 |
| 数量与饰品计数忽略 ID 大小写，且经 `JsonSupport.ReadString` 去除两端空白 | 不同 ID 的数量被合计；只有一条活动定义、其余为存档残留时，旧目录仍会允许修改这项合计 | 专用原始字符串读取，仅精确匹配所选物品；数量减少及副本删除不触碰其他 ID |
| 引用索引及掉落图对 ID 去空格、忽略大小写 | 一个 ID 的获取途径可能被当作另一个 ID 的引用 | 精确匹配物品、掉落表及引用键；相关 JSON 字段名按大小写匹配，未修改重复字段取值顺序 |
| 本地化请求、LOC/LOC2 返回字典与名称缓存忽略大小写；XML 键去空格 | 二进制读到了不同哈希的名称，返回/缓存时又混用 | 保留精确键及原始 XML ID；名称展示格式、来源/格式优先级和搜索方式保持原有策略 |
| 主规则文档仍写“引号内保护注释” | 与上一轮已经接入的原生文本读取器矛盾，可能让后续修改重新引入旧行为 | 修正文档并指向当前原生记录/字段规则 |

具体反例：副本三个堆叠分别为 `ni_case` ×2、`NI_CASE` ×5、` ni_case ` ×8。旧数量匹配会合计为 15，把第一种设为 0 会删除三种堆叠；现在只删除第一种，另外两种及其原始 ID 保留。小镇减少数量有同类问题。定义同时存在时旧冲突保护可能阻止 UI 写入；只有小写定义存在、另外两种为存档残留时，该保护无法解决问题。因此测试同时覆盖这一可写入口，而不仅调用底层转换函数。

修改覆盖以下文件组：

- `QuantityItemCatalog*`、`Models.cs`：定义、存档残留、内存键、类型分类与自动刷新。
- `QuantityItemReferenceAnalyzer*`：物品和掉落表引用身份。
- `TrinketCatalog.cs`、`TrinketSaveEditor.cs`：首条定义、数量和新增实例。
- `JsonSupport.cs`、`QuantityItemSaveEditor.cs`、`RaidInventorySaveEditor.cs`、`SaveEditService.QuantityItems.cs`、`SaveEditService.Trinkets.cs`：原始 ID 读取、选择匹配、编码和提交前重验。
- `ContentLocalizationCatalog.cs`、`LocLocalizationReader.cs`、`Loc2LocalizationReader.cs`、`MainWindow.EditWorkflow.cs`、`MainWindow.Presentation.cs`：名称键与写入后界面刷新/类型提示。
- `InventoryIdentityContractTests.cs` 及测试入口/既有饰品夹具：大小写、空格、分隔符、存档残留、二进制名称与真实保存路径；旧“大小写冲突”夹具改为有数学依据的原生哈希碰撞。

## 复核后继续保留的规则

- `modfiles.txt` 资格、同相对路径覆盖、标准人物/怪物路径：文件路径与资源 ID 是不同层次，不把 Windows 路径比较一并改为大小写敏感。
- 物品/饰品 first-match、怪癖/Buff/升级树各自已确认的规则：精确识别 ID 不改变加载序列。
- 物品缺引用、扫描不完整和存档残留的区分：不因为修正 ID 就删除引用分析，也不向饰品追加“必须显式引用”条件。
- 战斗分类、空组合占号、未知体型/顺序保护、直接及 Bridge 写入前重验、历史归属与维护事务：本轮检查没有取得推翻这些保护的新证据。没有修改 Bridge 文件格式、编号计算或清理范围。
- 人物/怪癖/Effect 目录中尚存的保守身份冲突保护继续保留；本轮没有只删除这些保护而放开其余尚未统一的调用链。不宣称所有资源都已支持全部大小写变体。
- 自动同步的来源指纹、场景分离、旧快照拒写、原子替换、事务回滚与强制返回保护继续有效。

## 验证

- 新隔离反例在产品修正前失败：分别计数、减少/删除保留其他 ID、目录数量及本地化等断言暴露了旧归一化。测试搭建时曾遗漏本地化输出目录，随后修正；该搭建错误不作为产品缺陷证据。
- 新定向契约已通过，包含预览、真实 DSON 编码与提交，小镇增加、副本精确删除、饰品初始实例、错误大小写请求拒绝，以及既有人物/Effect/空遭遇契约。产物 `workspaces/contract_tests/20260909_062555_995_c24aad2c349540e394d3c5bb22204155`。
- 完整契约已通过，日志 `workspaces/inventory-identity-audit-full-20260909.log`，产物 `workspaces/contract_tests/20260909_062727_045_004e8519b2034bb89512ece56b9dcfe6`。涵盖目录、数量与饰品、人物、自动同步、直接/Bridge 新建替换删除、编号与维护、回滚和强制返回。
- Release 解决方案构建通过，0 警告、0 错误。
- 补充的存档残留可写入口、类型/ID 分隔符、LOC 与 XML 精确键契约通过；最新 `--semantics` 产物为 `workspaces/contract_tests/20260909_063236_830_65514f84dbde4357bab6a581bbb9559c`。该次只增加测试覆盖，产品代码与完整契约通过时一致。
- 61 个本地文档链接与 `git diff --check` 通过；Release App/Core 的核心 DLL SHA-256 一致，为 `6F6D9C498A119647BDEA6A4FA6A238F4B366AE6946AC40CA1E67515194375C4C`。
- 一位新建、独立简报的只读复核者完成了相对 `27a6c17` 的最终差异检查（含未跟踪测试和报告），未发现有依据的 P1/P2 实质问题。复核者另行运行现有 Release 产物的 `--semantics`，结果通过，产物 `workspaces/contract_tests/20260909_063756_312_e10cd4c0ea50430795b54a97528b1cbc`；未修改代码或真实游戏数据。没有未解决的本轮复核发现。

当前规则见 [资源身份规则](../resource-duplicate-semantics.md#9-inventory-identities-and-localization-keys) 和 [内容与存档规则](../content-save-rules.md)。这些结论不等于完整复现所有游戏脚本、畸形 ID 或未实测的资源机制。
