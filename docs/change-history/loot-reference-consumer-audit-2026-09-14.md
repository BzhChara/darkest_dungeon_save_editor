# 历史修改第二十五轮审核：掉落引用的权重与同名表选择（2026-09-14）

先按要求将上一轮物品保存身份、饰品次数修复提交为 `670e4e4`（`fix: validate inventory save identities and native trinket counters`）。提交前核对 23 份文件与上一轮验证记录的 SHA-256 全部一致，暂存差异检查通过；提交后工作区干净。上一轮 Release 构建零警告、零错误，完整合同测试 85 PASS、0 FAIL、0 SKIP，独立完成复核没有确认阻塞问题。这些属于上一轮结果，没有算作本轮新验证。

本轮只读审核产品代码。新增本报告、记录索引及忽略目录中的隔离探针；没有修改产品实现、正式测试、真实档案、活动 Mod 或 Steam 配置，没有启动游戏。按协作约定，只读审核不另外触发代码完成复核。

## 确认问题一：忽略掉落条目的权重，误认物品有用途（P2）

位置：[`QuantityItemReferenceAnalyzer.LootParsing.cs`](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.LootParsing.cs) 第 62–90 行。当前只检查条目的 `type`、`data`，随后把物品或子表加入可达图，完全没有读取 `chances`。

游戏的 LootTable 加载器 `0x140442EA0` 在读取物品／子表之前先检查权重：

- `0x14044335C–0x1404433C3` 查找首个精确 `chances` 成员。字段缺失时记录诊断并在 `0x1404433AF` 进入下一条，当前条目不建立引用。
- `0x1404433CB` 调用原生数字转换，`0x1404433D3` 转成 float32；`0x1404433D7–0x1404433DA` 比较零并跳过非正权重。
- 因此 `0`、`-1` 和转成 float32 后为零的 `1e-50` 都不会注册该条目的物品／子表。正数 `0.25` 会保留。重复成员 `chances: 0, chances: 9` 仍取首个零；反向顺序取 9。

例如，一个已注册的奇物 Loot 入口指向 `audit_root`，该表仅有：

```json
{
  "id": "audit_root", "difficulty": 0, "dungeon": "",
  "entries": [
    { "type": "item", "chances": 0,
      "data": { "type": "estate", "id": "loot_a", "amount": 1 } }
  ]
}
```

物品本身有有效定义、不能手动配给、存档也没有它。在隔离内容里没有其他引用路径：游戏跳过这一条，编辑器却返回 `ConfirmedActive`，使其进入默认物品列表。把这个零权重条目改成 `type: table` 指向一张正常子表，也会错误地把子表中的物品标为有效。

这属于用途识别和默认筛选错误，不是把该物品写成另一个 ID。修复建议是在添加物品和子表边之前遵守原生权重读取及跳过规则；保留首成员、float32 精度与独立有效引用。不能据此禁止所有手动写入，也不能修改 Bridge 战斗的零权重规则：后者仍可通过有效战斗编号显式放置，使用的是另一套消费者。

## 确认问题二：同名掉落表被无条件合并（P2）

位置：同一文件第 44–54 行及第 78–90 行；[`QuantityItemReferenceAnalyzer.InternalModels.cs`](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.InternalModels.cs) 的 `LootTableNode` 只保留物品／子表集合，没有表的加载顺序、难度、地区、感染阶段及周数范围。

原生行为分为注册和查询：

1. `0x140441795` 逐条加载完整表；`0x14044179A–0x1404418CE` 按 ID 哈希找到分组，并将每条完整表追加到其向量，不把同名表的 entries 合并。
2. `0x140441960–0x140441A53` 查询该向量。检查地区（`+0x48`）、难度（`+0x44`）、感染阶段（`+0x4C`）及周数区间（`+0x50/+0x54`）；前三者的零值允许通用匹配。**第一个满足条件的表立即返回**，只有条件不匹配才前进到下一条。
3. `0x14043F3A7` 的实际掉落调用使用这个查询。若选中的表没有有效条目，后续在 `0x14043F3E9` 离开本次表处理，不回头选择后一个同名表。

具体例子：按照实际文件加载顺序，先有通用表 `T → loot_a`，再有另一个通用表 `T → loot_b`，均为正权重，且没有其他物品来源。游戏查询 T 得到第一份，只能从这条路径取得 `loot_a`；编辑器把两份表合并，错误确认 `loot_b` 也有用途。交换两份表，游戏可达物品随之交换，编辑器仍同时确认二者。同一 JSON 数组中的重复表、同一 Mod 的不同有效文件，均已复现。

补充边界：

- 先通用表、后难度 3 专用表：后者被前者遮住，不能仅因它声明了物品就确认引用。
- 难度 1 与难度 3 两个互不覆盖的表：两者在各自条件下都可能被用到，不能统一删除后一个同名 ID。隔离对照保留了这类情况。
- 被遮住的表可以引用子表；误认会继续传播到子表物品。
- 第一份通用表的条目全是零权重时，它仍能首先匹配；游戏不会因此改用第二份表。编辑器同时误认两个物品。

建议保留有序表变体及条件，再判定每条引用是否存在可满足的加载／选择路径。已证明完全被遮住的变体不应提供有效用途证据；尚未覆盖的条件应保留“不确定”，不能把“所有同名表合并”或“所有同名表只保留第一份”作为通用修复。此处沿用当前“可能在该场景类型下有用途”的目录目标，不额外收紧成“只显示本次副本难度能掉落的物品”。

## 历史来源与受影响路径

两处逻辑都已经存在于 `fb7b0fd`（2026-09-02，扩充物品工作流）的 `QuantityItemReferenceAnalyzer.cs`。`d1d18f6` 后续拆分到独立文件；`d55a15f` 修正字符串身份，其他提交修正清单、目录与 JSON 成员规则，但上述权重遗漏和集合合并仍保留至本轮 HEAD。这是早期引用分析的遗漏，不是刚提交的保存身份／饰品次数修复回退。

实际影响为 `ReferenceStatus`、`ReferenceEvidence`、默认列表和隐藏列表、已有残留条目的说明，以及同步重载后的这些结果。[`Models.cs`](../../src/DarkestDungeonSaveEditor.Core/Models.cs) 的 `IsHiddenByDefault` 和 [`MainWindow.CatalogInteraction.cs`](../../src/DarkestDungeonSaveEditor.App/MainWindow.CatalogInteraction.cs) 的筛选会直接使用错误状态。只重新扫描文件仍会得到同样的误判。

正式测试中也有未声明 `chances`、却期待掉落链提供 `ConfirmedActive` 的夹具，例如 [`ResourceEligibilityContractTests.cs`](../../tests/DarkestDungeonSaveEditor.ContractTests/ResourceEligibilityContractTests.cs) 的 `rf_known` 表。修复时应为正常引用对照补齐有效权重，并单独验证缺失／零权重分支，不能保留不符合原生条件的旧断言。

这些问题不要求更改有效物品定义的 first-match、同路径文件优先级、存档 ID、数量分配或饰品规则。6 次隔离写入预览均保持选择的 ID，并按每格上限 2 将总数 3 分成两格；这只能证明本次样例中的保存路径，没有把引用误判变成写错 ID，不能泛化为所有物品／Mod 的数值都已验证。

Bridge 持久副本延期维护、历史归属校验、资源指纹与保存前重验做了相关调用链复查，本轮未在这些路径确认新的缺陷，没有据此删除它们的保护。

## 新验证、证据及范围

执行：

```powershell
dotnet run --project workspaces/historical-rule-review25-20260914/Probe.csproj -c Release -- .
python -B workspaces/historical-rule-review25-20260914/native_evidence.py
python -B workspaces/historical-rule-review25-20260914/verify.py
```

- 当前产品 Core SHA-256：`1933171a25cc4722a32b900192290776771e338dad07d30d306ae0c48a468614`，探针直接引用提交 `670e4e4` 对应的 Release DLL，并在入口校验哈希。
- 游戏为固定 Windows x64 build 27890，EXE SHA-256：`35e5a653279992564809ff8406febd5a02a7d6961044781b1296b38a7096f59b`。重新提取 6 份原生片段，验证权重过滤、表追加、首个条件匹配返回及空表处理的关键指令，并保留字段字面量和片段哈希。
- 54 个场景覆盖本地 Mod、工坊 Mod、启用 DLC 前缀下的 Mod 资源，全部走 `modfiles.txt`；每类 18 个。39 个场景稳定复现上述误判，15 个正常／孤立／互不覆盖变体对照符合预期。这里的 PASS 表示成功确认当前缺陷和对照结果，不是修复后测试通过。
- 6 次实际 `PrepareQuantityItemEditAsync` 预览经 DSON 编码及回读，原始合成存档哈希保持不变；未调用提交接口。
- 最初 DLC 夹具使用人物声明，没有建立有效的引用入口而停止。改用已有合同覆盖的奇物类型与 props 映射入口后，正权重对照和全部 DLC 场景通过；初始失败日志保留，不算入最终 54 场景。该调整验证的是本轮掉落消费者，不能当作人物 DLC 加载已完成实测。
- 游戏选择结果来自固定二进制的静态控制流；编辑器结果来自真实调用当前 Core。没有新一轮游戏内掉落实测，没有统计当前全部活动 Mod 有多少物品受影响，也未逐项覆盖错误 JSON 类型、所有掉落奖励子类和全部条件组合。
- 结果见 [probe-evidence.json](../../workspaces/historical-rule-review25-20260914/probe-evidence.json)、[probe.log](../../workspaces/historical-rule-review25-20260914/probe.log)、[native-evidence.json](../../workspaces/historical-rule-review25-20260914/native-evidence.json) 和 [verification.json](../../workspaces/historical-rule-review25-20260914/verification.json)。探针和二进制摘录为本机忽略产物，Git 中保留本报告的结论及关键地址。

上述两项在本次审核结束时尚未修改。后续实现及验证见 [2026-09-15 修复记录](loot-reference-fixes-2026-09-15.md)；本报告保留审核当时的代码状态和复现结果。
