# 历史修改第二十四轮审核：饰品次数与物品保存身份（2026-09-14）

先按要求将上一轮容量、资源大小写及 Bridge 身份修复提交为 `b25205e`（`fix: preserve resource identities across catalogs and saves`）。提交前核对全部 40 份差异文件与上一轮验证记录一致，暂存差异检查通过；提交后工作区干净。上一轮最终验证为 82 PASS、0 FAIL、0 SKIP，Release 构建零警告、零错误，并已经完成独立复核。本轮没有把这些旧结果计为新测试。

本轮为只读代码审核。新增的内容只有本报告、记录索引及忽略目录中的复现程序和证据；没有修改产品代码、正式测试、真实档案、活动 Mod 或 Steam 配置，也没有启动游戏。按协作约定，只读审核不另行触发代码完成复核。

## 确认问题一：饰品次数字段的正整数限制过严（P2）

位置：[`TrinketCatalog.cs` 的 `ReadPositiveInstanceCounter`](../../src/DarkestDungeonSaveEditor.Core/TrinketCatalog.cs)，以及 [`TrinketSaveEditor.cs` 的 `CreatePristineInstance`](../../src/DarkestDungeonSaveEditor.Core/TrinketSaveEditor.cs)。前者当前第 341–348 行要求 `value > 0`，后者第 125–128 行再次拒绝非正数；预览和提交还会检查 `UnsupportedStateFields`。

该限制来自 `93fa9fa`（2026-09-03，新建未消耗的有状态饰品）。后来的 `831b962` 修正了“JSON 对象取第一个同名成员”，但保留了“第一项不是正整数就禁用整件饰品”的旧假设，正式测试和现行文档也沿用了它。第一成员规则本身仍正确。

本轮重新提取固定游戏二进制，确认原生行为：

- 计数定义默认是 `-1`，位于 `0x1404F31CE`（trigger）和 `0x1404F31E2`（quest）。
- `0x1404F3D7D–0x1404F3D9F` 和 `0x1404F4012–0x1404F4034` 查找首个 JSON 成员，通过数值类型检查时直接赋值，**没有要求大于零**。显式整数 `0` 保留为 `0`。
- 对 `null` 或字符串这类不满足数值检查的首成员，这两个可选字段的分支跳过赋值，保留默认 `-1`，不会改用后一个同名成员，也不会在这里丢弃整件饰品。
- 实例加载在存档未提供计数时从定义的 `+0x8C` / `+0x94` 初始化；保存分支 `0x1405D0820–0x1405D0874` 写出大于 `-1` 的值，包含 `0`。因此不能把“零次”和“未启用计数”混为一谈。

具体例子：`quest_uses: 0`、`trigger_limit: 0` 会在目录标记为无效，尚未进入 DSON 编码就被拒绝。`quest_uses: null, quest_uses: 4` 的正确解释是首成员没有赋值、保留原生默认值；当前实现却把整件饰品禁用。反向对照 `quest_uses: 2, quest_uses: 0` 应继续取 2，当前也确实取 2。

建议修复时同时处理目录、初始实例、预览/提交校验和旧测试：恢复已证明的零值及缺省语义。不能只把比较符从 `> 0` 改成 `>= 0`，也不能改成取后一个合法成员。本轮没有声称所有负数、超范围 JSON 数字、耗尽后的变形/销毁机制都已逐项验证；这些边界应与已确定的零值和默认值分开。

## 确认问题二：长物品／饰品 ID 能通过保存，但游戏会截短（P2）

位置：[`TrinketCatalog.cs`](../../src/DarkestDungeonSaveEditor.Core/TrinketCatalog.cs) 第 113 行附近保留完整 JSON ID；[`QuantityItemCatalog.Definitions.cs`](../../src/DarkestDungeonSaveEditor.Core/QuantityItemCatalog.Definitions.cs) 第 216 行读取完整物品 ID。它们没有核对保存身份的字节边界，之后由 [`TrinketSaveEditor.cs`](../../src/DarkestDungeonSaveEditor.Core/TrinketSaveEditor.cs)、[`QuantityItemSaveEditor.cs`](../../src/DarkestDungeonSaveEditor.Core/QuantityItemSaveEditor.cs) 和 [`RaidInventorySaveEditor.cs`](../../src/DarkestDungeonSaveEditor.Core/RaidInventorySaveEditor.cs) 完整写入 `id`。

完整 ID 读取和保存来自早期数量/饰品实现；`4e1db6b` 接入文本读取器、`d55a15f` 修正精确 ID 后，仍未补上游戏存档加载边界。这里不是要求恢复旧大小写归一化，而是发现另一层独立的保存约束。

原生证据区分定义身份与保存身份：

1. 饰品定义在 `0x1404F3422–0x1404F3463` 把名称复制进 64 字节缓冲，但哈希循环仍读取原始输入字符串。物品在 `0x1404C85F9–0x1404C86E1` 分别读取 64 字节保存名称和 512 字节的哈希输入。两个原始长短 ID 可以因此得到不同的定义哈希，不能简单改名合并。
2. 通用物品存档加载器 `0x1405D0910` 读取 `id` 和 `type` 时分别传入 `0x40` 大小，在 `0x1405D0A02–0x1405D0A1C` / `0x1405D09E5–0x1405D09FF` 可见。
3. `open_RestoreData` 检查 `0xB101` 魔数后实例化二进制读取器。其 vtable `0x140E28330 + 0x78` 指向 `0x1402370D0`，继续调用 `0x140237480`。后者只复制 `min(源长度, 目标大小 - 1)` 字节，再补 NUL：64 字节目标最多保留 63 字节。该函数另有长字符串诊断/断言分支，不应承诺长 ID 在所有运行配置中都无提示。
4. `0x1405D0C70–0x1405D0C93` 对已读入缓冲的 ID 计算哈希，再按该哈希找定义。JSON 存档路径也使用带 `0x40` 大小的 `strncpy_s(..., _TRUNCATE)`，不是 DSON 编解码工具自己的无限长字符串规则。

隔离例子使用两条不同定义：63 个 `a` 的 ID 和同一字符串后加 `b` 的 64 字节 ID。饰品前者价格 10，后者价格 99；物品前者堆叠上限 2，后者上限 9。当前目录没有冲突标记，选择后者可通过完整保存预览，DDSaveEditor 的 DSON 回读也仍保留全部 64 字节。按上述游戏读取器截为 63 字节后，查找的是前者的哈希。副本测试中编辑器按后者上限创建一格 5 个，游戏所绑定的另一条定义上限却为 2。

另有中文对照：21 个“饰”为 63 个 UTF-8 字节，末尾加 `b` 后只有 22 个字符，却已经超过字节上限。检查 `string.Length <= 63` 不能解决它。

建议核对“读取后的保存 ID 及哈希能否仍指向所选定义”，对无法原样保留的写入给出明确限制；不要直接截短后继续写入，因为可能变成另一件道具。目录身份、引用身份和保存身份应按各自消费者处理，不能把 63 字节截断机械应用到所有资源定义。此次证实的是新生成/修改时的边界，不需要加入旧档迁移或兼容回填。

## 复现、验证及限制

执行命令：

```powershell
dotnet run --project workspaces/historical-rule-review24-20260914/Probe.csproj -c Release -- .
python -B workspaces/historical-rule-review24-20260914/native_evidence.py
```

- 产品程序集固定为本次已提交版本。Core SHA-256 为 `39f93056d99b67362c5cd4ca09a99fe8fb7b0df2a3ded7119c87eb51edc54f85`。
- 原生证据固定 Windows x64 build 27890，EXE SHA-256 为 `35e5a653279992564809ff8406febd5a02a7d6961044781b1296b38a7096f59b`。脚本重新校验了 8 份反汇编、字段字面量、二进制/JSON vtable 槽位和 `strncpy_s` 导入。
- 三类隔离来源分别为 Base、本地 Mod、工坊 Mod，后两者均有 `modfiles.txt`。27 次计数预检中，18 次稳定复现错误拒绝，9 次缺省/正数/第一成员对照通过。12 次饰品 ID 预览包含 6 次恰好 63 字节的正常对照及 6 次 64 字节的误放行；另有小镇物品、副本物品各 6 次长 ID 误放行。
- 合计 51 次完整预检调用，33 次成功生成的预览经过真实 DSON 编码回读，18 次在编码前被旧计数保护拒绝；这是一组**确认现存缺陷的反例**，不是修复后测试。没有提交这些合成预览，原始合成存档保持不变。
- 编辑器行为来自实际调用当前 Core；文中的游戏截短、哈希绑定和默认值结果来自固定二进制的静态证据及独立小型计算，不冒充新一轮游戏内实测。没有验证耗尽后的具体动画、触发、销毁或全量 Mod 的长 ID 分布。
- 同步资源指纹、目录重载、保存前重验、Bridge 历史归属与持久副本维护做了相关调用链复核。本轮未确认这些路径有新增问题；没有据此删除原有保护，也没有重复执行上一轮完整 82 项回归。
- 证据和源文件校验汇总见 [verification.json](../../workspaces/historical-rule-review24-20260914/verification.json)，原始预检结果见 [probe-evidence.json](../../workspaces/historical-rule-review24-20260914/probe-evidence.json)，最终日志见 [probe-final.log](../../workspaces/historical-rule-review24-20260914/probe-final.log)，原生位置见 [native-evidence.json](../../workspaces/historical-rule-review24-20260914/native-evidence.json)。

上述两项待用户确认后修改。本报告暂不改写现行规则正文，避免把“建议修复的目标行为”描述成当前产品已经实现。
