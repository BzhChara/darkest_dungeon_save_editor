# 历史修改第二十六轮审核：升级要求覆盖与费用引用（2026-09-15）

先按要求把上一轮掉落引用修复提交为 `c5bb049`（`fix: honor native loot weights and ordered table variants`）。提交前核对 15 份文件与上一轮验证记录的 SHA-256 一致，暂存差异检查通过，提交后工作区干净。

本轮只读审核产品实现，新增本报告、记录索引和忽略目录下的隔离探针；没有修改产品代码、正式测试、真实档案、活动 Mod 或 Steam 配置，没有启动游戏。依据协作约定，只读审核没有另外触发完成复核。下列两项在本轮结束时尚未修复。

## 确认问题一：树内重复购买码被错误地当作冲突（P2）

位置：[`HeroClassCatalog.Progression.cs`](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.Progression.cs) 第 236–238 行，`ReadHeroUpgradeRequirements`。

当前代码允许同一购买码重复声明相同的等级要求，但只要等级要求不同，就抛出 `conflicting resolve prerequisites`。调用方保留该树的来源并设置 `UnsupportedReason`，随后人物候选预检和实际生成都被阻止。

例如，一个具有 0、1 两档 `alpha` 技能的职业，其有效升级树为：

```json
{
  "id": "audit_hero.alpha",
  "tags": [],
  "requirements": [
    { "code": "0", "prerequisite_resolve_level": 0,
      "currency_cost": [], "prerequisite_requirements": [] },
    { "code": "1", "prerequisite_resolve_level": 3,
      "currency_cost": [], "prerequisite_requirements": [] },
    { "code": "1", "prerequisite_resolve_level": 1,
      "currency_cost": [], "prerequisite_requirements": [] }
  ]
}
```

原生结果：购买码 `1` 最终要求人物等级 1；前面的等级 3 被覆盖。交换这两条后，最终要求等级 3。因此既不是取最小值，也不是取最大值，更不是冲突后整树失效。

编辑器结果：上述两种顺序都把该人物的 0–6 级生成标记为不可用；实际调用候选工厂也拒绝。把隔离输入中被覆盖的要求移除后，当前产品立即正常生成，且等级 1 和等级 3 的购买码正确变化。这证明阻止生成的是重复码校验，样本本身没有缺少人物姓名、皮肤、基础 HP、等级阈值等前置条件。

### 原生证据

固定 Windows x64 build 27890：

- `0x140470460` 逐项加载一个树对象的 `requirements`；`0x140470691` 读取 `code` 的首字节。
- `0x140470775` 调用 `0x140470CD0`，在树的 map 中按购买码查找或插入。查找命中时，`0x140470D3A` 返回原节点，`0x140470D3D` 设置“没有新插入”，跳至返回路径；不会建立第二个同码节点，也不报冲突。
- 调用方不使用“是否插入”来跳过赋值：`0x1404707AF` 替换费用向量，`0x1404707D0` 替换前置要求向量，`0x1404707D8` 无条件写入本条等级要求。之后进入下一条。
- 费用赋值函数 `0x140470FB0` 更新目标向量的末尾指针；新的空列表也会清除旧费用，不是只追加新费用。

建议修改为同一有效树内的同码要求取最后一次赋值，并同步更正正式测试。这里仅确认重复码的覆盖行为；不能以此删除 DSON 购买码可表示性、购买目标哈希冲突、缺少真实技能档位等其他独立保护。

[`HeroUpgradeTreeContractTests.cs`](../../tests/DarkestDungeonSaveEditor.ContractTests/HeroUpgradeTreeContractTests.cs) 第 148 行还把 `Tree(alpha, ("0", 0), ("0", 2))` 放在“应拒绝”的夹具列表中。这个断言固化了旧假设，现有测试通过不能证明此处符合游戏。

## 确认问题二：升级费用分析保留了已被覆盖的物品引用（P2）

位置：[`QuantityItemReferenceAnalyzer.JsonRoots.cs`](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.JsonRoots.cs) 第 185–189 行。

`Upgrades` 分支逐条遍历所有有效文件的 `trees`、所有 `requirements`，把每个 `currency_cost` 都加入小镇用途证据，没有执行两层选择：

1. 同名树按有效文件及文件内顺序取最后一份完整树；不把旧树要求合并进新树。
2. 在最后一份树中，同一购买码取最后一条要求；不累计被覆盖的旧费用。

第一层规则此前已有[古董商技能树实测](antiquarian-upgrade-probe-2026-09-08.md)和[人物升级树修复](hero-upgrade-trees-2026-09-08.md)记录。本轮重新提取 `0x1404703D0`：遍历完整树向量时，`0x1404703F3` 对每次匹配更新结果指针，`0x1404703F7–0x1404703FE` 继续遍历直到末尾，因此保留最后匹配的完整树。第二层由本轮上述 map 赋值证据确认。

具体例子：只有 Mod 自定义货币 `currency_a`、`currency_b` 的定义，没有存档条目或其他用途；一棵被人物技能引用的树，较早的完整定义消耗 A，较晚的同名完整定义只消耗 B。

| 场景 | 原生最终有效费用 | 当前编辑器确认有用途的货币 |
|---|---|---|
| 同文件同名树先 A 后 B | B | A、B |
| 同一 Mod 的不同文件先 A 后 B | B | A、B |
| 同名树先 B 后 A | A | A、B |
| 最后同名树的要求列表为空 | 无 | A |
| 同一树、同一码先 A 后 B | B | A、B |
| 同一码最后费用列表为空 | 无 | A |
| 两个不同码分别消耗 A、B | A、B | A、B，正确对照 |
| 两个不同且已绑定的树分别消耗 A、B | A、B | A、B，正确对照 |
| 同路径高优先级文件 B 覆盖 A | B | B，正确对照 |

另有对照确认：同路径高优先级替换保留原枚举位置，后续另一有效文件中的同名树仍可能成为最终结果；未列在清单上的物理文件没有参与引用分析。问题发生在取得有效文件之后，不需要回退清单、目录或同路径优先级规则。

实际影响是小镇物品目录的 `ReferenceStatus`、`ReferenceEvidence`、默认可见性，以及自动同步重新载入后的同样结果。保存中没有这些物品时，被覆盖的旧货币本应没有这条用途证据，当前却被标为 `ConfirmedActive`，留在默认列表中。参见 [`Models.cs`](../../src/DarkestDungeonSaveEditor.Core/Models.cs) 的 `IsHiddenByDefault` 和 [`MainWindow.CatalogInteraction.cs`](../../src/DarkestDungeonSaveEditor.App/MainWindow.CatalogInteraction.cs) 的筛选。

建议先解析完整的有效升级树及其最终购买码要求，再为最终费用记录用途证据。保留其他真实用途；最后空树、空费用应阻断旧定义回退。此处不要求禁用“显示隐藏物品”后的手动写入，也不需要更改物品定义 first-match、保存身份或数量算法。

六次实际物品写入预览均将所选货币的正确 `type` 和数量 3 写入合成钱包，原始合成存档哈希不变。因此本次发现是用途识别错误，样本未出现写错物品或数量；这不代表重新验证了所有库存场景。

## 历史来源

- 重复购买码的“不同等级就报冲突”在最早可查代码基线 `7b9a67f` 的 `HeroClassCatalog.cs` 中已经存在。之后 `1a03bb4` 拆分职责，`3d2bd9c` 改为按最后匹配树解析时搬迁并保留了这段校验。当前 `git blame` 指向搬迁提交，不能误称是该次研究新增了整个错误规则。
- 升级费用的当前逐树、逐要求分支来自 `c14247a`；更早版本使用通用 JSON 递归分析。虽然后来改成明确的 `Upgrades` 消费结构，仍未加入树和购买码的最终选择。
- 两项都不是刚提交的 `c5bb049` 掉落引用修复引入。此前已修正“同名树取最后一份”不等于树内记录和其他消费者也已同步正确。

## 验证与边界

执行：

```powershell
python -B workspaces/historical-rule-review26-20260915/native_evidence.py
dotnet build workspaces/historical-rule-review26-20260915/Probe.csproj -c Release --nologo
dotnet workspaces/historical-rule-review26-20260915/bin/Release/net8.0-windows/DarkestDungeonSaveEditor.ContractTests.dll .
python -B workspaces/historical-rule-review26-20260915/verify.py
```

- 当前 Core SHA-256：`6fc5413bc5179bf5441473ea2420910a705fa3170b0418b8aed0a933a102393f`。探针直接引用已提交修复对应的 Release DLL，启动时校验哈希，没有重新编译或替换产品。
- 游戏 EXE SHA-256：`35e5a653279992564809ff8406febd5a02a7d6961044781b1296b38a7096f59b`。提取 7 份带字面量说明的原生片段，校验关键指令并记录片段哈希。
- 60 个场景：本地 Mod、工坊 Mod、启用 DLC 前缀中的 Mod 资源各 20 个，全部使用显式 `modfiles.txt`。每类有 15 个费用场景和 5 个人物生成场景；36 个复现上述缺陷，24 个正常对照符合预期。PASS 表示探针成功复现当前错误与对照，并不表示两项已修复。
- 12 次 DSON 验证：6 次 `PrepareQuantityItemEditAsync` 钱包预览；6 次把最终有效要求作为独立对照输入后生成候选，并将购买记录编码回读。后者没有改产品，也没有把原始重复要求输入误称为已经能够生成。
- 初始探针存在接口名称、候选类型、哈希常量抄写和路径分隔符比较错误；只修改忽略目录中的探针后，最终构建为 0 警告、0 错误，以上 60 场景完整执行。初始失败不算入最终样本，也没有据此修改产品。
- 游戏规则证据来自固定二进制静态控制流，编辑器结果来自真实调用当前 Core。本轮没有新的游戏内重复码实测，没有统计当前 profile 的全部 Mod 中有多少个受影响实例，也没有逐项验证缺失字段、错误 JSON 类型、非 ASCII 购买码、全部建筑或剧情触发条件。
- 相关调用链复查没有为清单加载、同路径覆盖、Bridge 维护、保存前内容重验等已有保护提供新的删除依据。此次发现不足以宣称整个项目已无其他错误。
- 上一轮最终构建及受影响测试已记录在[掉落引用修复记录](loot-reference-fixes-2026-09-15.md)。本轮没有把上一轮完整测试数算作本轮新验证，也没有修改正式测试后再跑全套。

本机证据：[probe-evidence.json](../../workspaces/historical-rule-review26-20260915/probe-evidence.json)、[native-evidence.json](../../workspaces/historical-rule-review26-20260915/native-evidence.json)、[verification.json](../../workspaces/historical-rule-review26-20260915/verification.json)。探针和二进制摘录保留在 Git 忽略目录，仓库中保留本报告及关键地址。当前有效规则文档没有提前改写为已实现。

用户随后确认修复。实现与验证见 [2026-09-15 修复记录](upgrade-reference-fixes-2026-09-15.md)，本报告保留审核时的代码状态与复现结果。
