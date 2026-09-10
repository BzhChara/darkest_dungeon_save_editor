# 历史修改第五轮审核：人物身份与奇物资源，2026-09-09

本轮先按用户要求提交上一轮地图资源修复，提交为 `7c0e3a0`（`fix: align map prop loading with native resource rules`），提交后工作区干净。随后只读审核此前尚未核实的旧处理，没有修改产品源码、正式测试、真实档案或活动 Mod，也没有启动游戏。

发现五类确定问题。前两类影响人物生成结果；后三类影响奇物、陷阱等地图内容的识别、名称和可放置性。本轮仅报告，均未修复。这些隔离反例不代表已经确认当前 `profile_1` 含有相同输入或受到了影响。

## 依据与验证范围

- 对照当前实现、历史 `git blame`、既有规则和固定 Windows x64 build 27890 游戏二进制。游戏侧的新结论来自静态调用和取值路径分析，不冒充本轮实机观察。
- 游戏 `Darkest.exe` SHA-256：`35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。
- 隔离探针：[ReviewProbe.csproj](../../workspaces/historical-rule-review5-20260909/ReviewProbe.csproj)、[Program.cs](../../workspaces/historical-rule-review5-20260909/Program.cs)。直接引用上一轮构建的 Core DLL，并复用正式契约程序的独立夹具创建方法；未重建产品。所有生成、编码都针对 `workspaces/` 下新建的假档案。
- 最终 [13 组结果](../../workspaces/historical-rule-review5-20260909/runs/d58832b1ecd6420b8fd0d9fafea429cf/results.json) 包含两个正常对照、十一个边界样例，另保存了两份生成后的人物 JSON。[结果校验脚本](../../workspaces/historical-rule-review5-20260909/verify_results.py) 核对观察结果、序列化人物、游戏哈希和三个产品目录内的 Core DLL，一次最终执行通过。
- 所用 Core SHA-256：`D8297F81AA8D72CFA0111A7D03D380D1702A09DE21A3C896FCB81A2BB9FBB50D`，Core、App、ContractTests 目录一致。复现成功表示发现了缺陷，不表示缺陷测试已在修复后通过。
- 探针最终编译及执行成功，无新增依赖、无 Windows 崩溃弹窗。早期探针有自身问题：Python 文件名 `inspect.py` 遮蔽标准库，改名后正常；控制台引用测试程序集出现 WindowsBase 版本警告，补齐已有 WindowsDesktop 框架后又因缺少 `using System.IO` 编译失败，随后修正；奇物对照最初误用 `curios:` 记录名，改为 `room_curios:` 并加入正常对照断言后才计入结论。这些均不是产品故障，旧输出保留在探针目录。

## 1. P2：Buff 身份先被 Trim，导致不同 Buff 合并并写错初始 HP

位置：[JSON 字符串读取](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.TextParsing.cs)，第 14、52 行；[Buff 与怪癖解析](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.QuirkDefinitions.cs)，第 266、332 行。辅助方法至少自 `1a03bb4` 的人物目录拆分时就已存在，后来接入 Buff 最后完整定义规则时继续沿用。

当前 `ReadJsonString` 删除首尾空格，`ReadJsonStringArray` 还会忽略大小写去重。Buff 定义 ID 和怪癖的 Buff 引用均经过这些方法，后续按原始身份排除冲突的检查已经拿不到清洗前的字符串。

隔离文件依次定义：

| 原始 Buff ID | max_hp 修正 |
| --- | --- |
| `AUDIT_HP` | +10% |
| ` AUDIT_HP ` | +50% |

怪癖 `audit_hp_quirk` 明确引用 `AUDIT_HP`。游戏读取 Buff ID 后直接按原始字节计算 polynomial-53 哈希；两个 ID 的有符号哈希分别为 `2031825418`、`-1441857838`，并不是重复 ID。怪癖加载 Buff 引用时也按原始字节查找。

编辑器却先把两者变成同一个 ID，再选择末项 +50%，仍将该怪癖标记为 `Direct`。基础 HP 为 20 的生成结果，应该按所引用的 +10% 得到 22，实际预览及 `Candidate.actor.current_hp` 都是 30。

原生依据：Buff ID 原样复制及哈希位于 `0x1404A3C0D`–`0x1404A3C94`；怪癖 Buff 数组原样哈希、查表位于 `0x1404DEC20`–`0x1404DED1E`，分别存于 [Buff 反汇编](../../workspaces/historical-rule-review5-20260909/native_1404a3450.txt) 和 [怪癖反汇编](../../workspaces/historical-rule-review5-20260909/native_1404ddbf0.txt)。

建议：身份和身份引用使用原始字符串；显示、搜索可以单独清理。保留已验证的同一个原始 Buff ID 取最后完整定义规则。不能以删除未知 HP 校验代替修复身份解析，也不能据此宣称全部 Buff 重复引用、条件和大小写字段已完成验证。

## 2. P2：显式选择的怪癖被再次 Trim，可能写成另一个怪癖

位置：[初始怪癖选择解析](../../src/DarkestDungeonSaveEditor.Core/StagecoachHeroCandidateFactory.Quirks.cs)，第 16、27 行。该实现至少自 `827ca42` 的生成器拆分时保留至今。

目录能分别保留 `audit_select` 与 ` audit_select `；二者均为 `Direct`，后者带 +25% HP。调用生成器明确选择 ` audit_select `，却被 `rawId.Trim()` 改为 `audit_select`，随后匹配并生成了前者。最终人物 JSON 的 `quirks` 只有 `audit_select`，HP 为 20，未得到所选怪癖对应的 25。

这不只是大小写冲突被保守禁用，而是已经精确选中的合法目录项被静默替换。[选择窗口](../../src/DarkestDungeonSaveEditor.App/InitialQuirkSelectionDialog.xaml.cs) 传递所选定义 ID，后端生成链又调用这个方法，因此仅让目录保留原始 ID 并不足够。

建议：从选择、校验到序列化始终使用同一个精确 ID。用户搜索可以忽略大小写；不应把已选资源身份当作搜索关键字再进行模糊匹配。进化目标、互斥关系和上限统计中的其余大小写处理应单独核对，不能由这个反例推导出全部相关保护都能删除。

## 3. P2：奇物 CSV 使用通用 CSV 规则，仍会误读和漏读

位置：[奇物 CSV 读取](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.Curios.cs)，第 54–64、89–95、120–122 行。来自 `6523b66`。

当前 `TextFieldParser` 会去掉两端空白、按通用 CSV 规则处理记录；调用端按表头文字判断是否跳过，并要求映射至少五列。这不是该游戏入口的实际行为。

| 隔离样例 | 当前编辑器结果 | 原生入口结果 |
| --- | --- | --- |
| 第一列为带前导空格的 ` alpha`，地区池同时列出 `alpha` 和 `" alpha"` | 纳入不存在的 `alpha` 映射并通过校验，漏掉真正的 ` alpha` | CSV 分词器保留前导空格，映射 ID 是 ` alpha` |
| 文件没有表头，首行直接定义 alpha | 将 alpha 纳入且通过校验 | 入口无条件丢弃第一条物理行，alpha 未被该文件建立 |
| 表头后的首条数据只有 ID、Sprite、Type 三列 | 因不足五列拒绝，alpha 漏读 | Type 有效时建立映射；尚未设置的 UI 名称、音效名使用 Sprite |
| 五列齐全，但 UI 名称列为空 | alpha 可用，英文名显示 `—` | 没有旧名称时默认 Sprite；已有名称时保留旧名称 |

最后一行单独看是 P3 名称/搜索问题；前两行已会认可错误的资源身份，因此本组整体为 P2。所有样例使用未改内容的原版奇物类型库提供 `unlocked_strongbox` 类型，普通五列对照能正常显示 `Unlocked Strongbox` 并通过定义校验。

原生依据：

- [映射入口](../../workspaces/historical-rule-review5-20260909/native_1404d9cf0.txt) `0x1404D9D90`–`0x1404D9DC6` 丢弃首条物理行；之后才循环解析数据行。`0x1404D9F18` 检查分词数，随后检查第三列，不要求五列齐全。
- [CSV 分词器](../../workspaces/historical-rule-review5-20260909/native_14038dca0.txt) 在 CR/LF 结束记录，引号切换状态；只删除字段尾部 ASCII 空格，不执行 `.Trim()`。不能把 .NET 的多行、转义引号和空白处理直接当成原生规则。
- `0x1404DA068`–`0x1404DA0B7` 实现 UI 名称的显式覆盖、保留旧值和首次 Sprite 默认值。

建议：实现此入口的物理行、分词和字段缺省规则，保持映射 ID 的原生身份。短行出现在文件中间时，原生输入列缓冲区还可能保留上行未覆写的列；后续修复不能简单把所有短行补为空值，也不能把本轮首条三列样例扩大为“任何短行都与标准五列等价”。

## 4. P2：把同路径优先级再次套到重复资源 ID，同优先级又一律判歧义

位置：[奇物映射取值](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.Curios.cs)，第 64–73 行；[JSON 资源取值](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.Resources.cs)，第 40–49 行。两者来自 `6523b66`，后续接入统一文件解析器后，内部这层旧判断仍然存在。

### 奇物映射不是“一律按 Mod 优先级”，也不是整行无条件 last-match

同一 CSV 两行定义 alpha，分别使用 `old_name`、`new_name`。编辑器认为它们来自同一优先级且不同，禁用整个 alpha。游戏却按第一列原始 ID 获取已有 prop，再更新它的映射字段；这个样例的最终 UI 名称是 `new_name`。如果第二行 UI 列显式留空，原有 `old_name` 会保留，并不是被清空或禁用。

还有一个跨文件样例：

1. Base 已有 `curios/curio_props.csv`；上方 Mod 覆盖这个同路径文件，alpha 名称为 `old_name`。
2. 下方 Mod 新增不同路径 `curios/z_curio_props.csv`，也定义 alpha，名称为 `new_name`。
3. 当前共用文件解析器返回的有效顺序是：上方 Mod 覆盖后的原槽文件、下方 Mod 追加的新路径文件。探针保存了这个有序路径列表。
4. 原生 CSV 消费端逐个文件更新 prop，后一份明确名称生效；当前奇物消费者却再次比较 Mod 优先级，仍显示 `Old name`。

这里同路径覆盖已正确完成，错误在覆盖之后又用来源优先级替代资源自身的重复声明规则。原生获取/创建 prop 的 [函数](../../workspaces/historical-rule-review5-20260909/native_1404d8370.txt) 在 `0x1404D83B8`–`0x1404D83C0` 查到原对象即返回；[CSV 消费端](../../workspaces/historical-rule-review5-20260909/native_1404d9cf0.txt) 接着更新该对象。文件槽规则沿用此前已研究的 `IO_FindFiles`，本轮没有重新启动游戏验证跨文件样例。

### JSON prop 的相同默认难度重复声明又是另一种规则

同一 JSON 依次声明两个默认难度的 alpha，继承 trap，`health` 分别为 1、2。编辑器同样将其判为歧义并剔除。

原生 [注册函数](../../workspaces/historical-rule-review5-20260909/native_1404d8560.txt) `0x1404D8718`–`0x1404D8743` 将对象及难度追加到该名称的列表。[查询函数](../../workspaces/historical-rule-review5-20260909/native_1404d82b0.txt) 从列表开头查找，遇第一个精确难度匹配立即返回。所以本例相同默认难度的先声明对象生效，不是整个 ID 无法判断。

查询器还存在无精确难度时的距离选择，不能只把字典改为全局 first-match 就宣称覆盖所有难度。JSON prop、Curio CSV 是不同消费入口，不能共享一个未经区分的重复 ID 策略。

建议：保留共同的清单与同路径覆盖；移除消费者中这两处未经证实的优先级/歧义假设，分别实现可证明的重复和缺省字段规则。[既有地图内容契约](../../tests/DarkestDungeonSaveEditor.ContractTests/BattleMapContentContractTests.cs) 第 49、68–69、138–148 行还将这些重复输入视作歧义预期，修复时必须同步纠正。真实哈希碰撞、缺失引用和无法确定的难度仍需相应保护。

## 5. P2：JSON 文件级 default_data 被遗漏，合法继承的陷阱被禁用

位置：[JSON 地图资源读取](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.Resources.cs)，第 28、40、107–118 行。来自 `6523b66`。

同样定义 alpha，以下两种写法中，编辑器只接受把 `inherits_from` 写在每个 prop 内的版本；文件级缺省继承版本则报“无法确认资源属于 trap 类型”：

```json
{
  "default_data": {
    "inherits_from": { "prop_type_name": "trap" }
  },
  "props": [
    { "name": "alpha", "default_data": {} }
  ]
}
```

原生 [JSON 加载器](../../workspaces/historical-rule-review5-20260909/native_1404d76e0.txt) 先解析文件级 `default_data`，再于 `0x1404D7B20`–`0x1404D7B43` 将结果复制到每个 prop 的工作对象，之后应用条目级缺省数据及难度变化。其 [数据读取器](../../workspaces/historical-rule-review5-20260909/native_1404d5010.txt) 会解析 `inherits_from.prop_type_name` 并复制对应父对象。本例父 trap 已经由基础资源文件提供。

当前编辑器只保留 `props[]` 的单条 JSON，继承检查又只沿该条目的 `default_data` 查找，丢掉了原生已经应用的文件级默认值。因此它不是一个需要作者补齐重复字段才能正常加载的无效资源。

建议：按实际加载阶段构建有效 prop 数据，再判断类型和脚本行为；不要通过删除继承检查来放行未知类型。顶层缺省值、逐条继承、难度变化和脚本字段的全部组合，本轮并未逐项验证。

## 本轮保留与未下结论的部分

- 本轮没有证据推翻已经验证的 `modfiles.txt` 约束、标准路径与直接打开路径区别、同路径 Mod 覆盖规则。需要改的是消费端残留的身份清洗及格式/重复取值假设。
- 没有将未知 HP 条件、真实身份冲突、进化目标缺失、人物上限和无法确定的技能树保护列为可直接删除项。第 1、2 项应贯通精确身份处理，但不能借此整体取消业务校验。
- 原生 prop 库按 prop JSON、trap JSON、obstacle JSON、curio type CSV、curio mapping CSV 分阶段加载，现有 JSON 目录将三个文件族合并枚举。阶段交互、跨文件父类重新定义、难度选择和脚本字段继承值得继续检查；本轮没有为这些额外组合逐一生成反例，未宣称全部已经解决或验证完毕。
- 对物品引用、人物剩余互斥/进化检查、怪物元数据和战斗来源相关入口做了静态抽查。本轮没有取得新的完整反例去推翻战斗编号、空遭遇占号、Bridge 历史归属或保存事务保护；这不等同于全项目所有行为已无遗漏。
- 没有修改自动同步、删除战斗、强制返回或日志代码。本轮目录差异可能影响它们使用的候选或人物预览，但不据此断言那些工作流自身出现新缺陷。

## 交付状态

本轮产品代码与正式测试保持提交 `7c0e3a0` 不变，仅新增此审核记录及目录入口；隔离探针位于已忽略的 `workspaces/`。没有重跑上一轮已通过的完整产品测试，也没有把隔离反例成功执行说成产品已通过修复验收。由于此阶段没有产品代码差异，按协作规则未触发实现完成复核者。

记录及目录的 52 个本地链接已核对，`git diff --check` 通过；Git 仅提示既有 CRLF 转换策略，没有更改换行配置。

建议后续分别修复人物精确身份链和地图资源消费规则，并以本轮正常对照、反例和新增必要边界作为验收依据。五类问题目前均保留在代码中。
