# 历史修改第二十八轮审核：非人物文本的物品引用

按用户要求，先提交上一轮人物初始 HP 条件修复：**fc30df8**，完整提交 `fc30df889f5db4bc3095dba0ad064eda80f33f7d`，标题 `fix: apply unafflicted HP modifiers to new heroes`。提交后工作区干净，未推送。

随后只读核对历史规则。本轮确认 **1 个旧规则遗漏，存在两种表现**：非人物／怪物目录的 `.darkest` 文件仍可凭字段名称被当成已确认的物品来源；同一宽泛筛选还会把游戏不消费的缺失文件当成引用分析失败。尚未修改产品代码或正式测试。

## P2：未确认消费者的文本文件仍能确定物品引用

相关位置（行号对应 fc30df8）：

- [文件候选筛选](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.SourceDiscovery.cs)，`IsEligibleReferencePath`，第 264–275 行。
- [泛文本引用解析](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.RootParsing.cs)，`ParseRootFile` 第 61–68 行、`ParseDarkestRoot` 第 108–116 行。
- [缺失文件诊断](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.SourceDiscovery.cs)，`AuditManifestReferenceFiles`，第 175–191 行。

### 表现一：忽略文件里的字段被算成有效引用

隔离 Mod 定义两个 `estate` 物品：`audit_token` 和 `unused_control`，每格上限均为 2，`estate_can_be_provision` 均为 false。两者没有出现在样例存档里，没有其他小镇／副本引用。

Mod 清单包含下面这个文件：

```text
campaign/provision/notes.darkest
```

文件内容为：

```text
notes: .type estate .id audit_token
```

当前编辑器把 `audit_token` 判为 **ConfirmedActive（已确认引用）**，放入副本默认物品列表。将相同文件放到 `campaign/town_events/notes.darkest`，它又会被判为小镇的已确认引用。

这两个文件都不符合相应的游戏加载查询。本轮重新读取已安装 build 27890 的 EXE，核对查询字符串、引用字符串的指令和 `IO_FindFiles` 调用：

| 原生入口 | 请求目录 | 原生文件表达式 |
| --- | --- | --- |
| 配给 `0x1404503C0` | `campaign/provision/` | `.*provision.json` |
| 事件 `0x1403E877E` | `campaign/town_events/` | `.*campaign/town_events/.*\.town_events.events.json` |

这两类加载入口没有采用 `notes.darkest`。清单列出文件，只使其有资格被挂载；不能让它越过具体资源的文件查询并产生配给／事件引用。样例没有提供会另外读取这些文件的脚本或其他入口。

[配给查询反汇编](../../workspaces/historical-rule-review28-20260915/native_140450411.txt)与[事件查询反汇编](../../workspaces/historical-rule-review28-20260915/native_1403e875a.txt)是本轮从固定 EXE 重新导出的片段。完整加载／解析链亦见既有[JSON 消费规则研究](json-reference-rule-audit-2026-09-10.md)。

问题不局限于 `.type/.id`。以下内容分别单独测试，都在 `notes.darkest` 中产生错误确认：

| 文件内容 | 当前结果 |
| --- | --- |
| `notes: .type estate .id audit_token` | 确认该物品引用 |
| `notes: .item_id audit_token` | 确认该物品引用 |
| `notes: .use_item_id audit_token` | 确认该物品引用 |
| `loot: .code audit_root` | 沿两层 Loot 表确认最终物品引用 |

对应的反向对照都正确：文件不列入清单、把内容写成注释、或使用不匹配相应资源查询的 `notes.json`，物品保持 SuspectedUnused 并默认隐藏。合法配给／事件 JSON 仍能建立引用。

### 表现二：无关缺失文件导致整场景引用分析报不完整

清单仍列出 `campaign/provision/notes.darkest`，但文件实际不存在时，编辑器产生 `Quantity-item reference file listed by active Mod is missing`，将副本的 `scanComplete` 设为 false。

结果是 `audit_token` **和从未在任何引用中出现的 `unused_control`** 都变为 AnalysisIncomplete，均取消默认隐藏。事件目录的相同样例影响小镇。另一个场景的状态保持正确。

缺失文件提示本身应保留，但判断对象应是相应消费者会请求的文件。当前错误来自前置资格筛选过宽，不应通过删除所有缺失文件诊断来修复。

### 原因与历史来源

`NativeResourceFileRules.IsEligibleReferenceFile` 对已有 JSON／Loot 查询做了筛选；普通 `.darkest` 则仍然返回允许。`SourceDiscovery` 只对人物／怪物的标准打开路径追加约束，其他文本进入名为 `open` 的未验证来源组。

随后 `ParseDarkestRoot` 的非人物／怪物分支直接把 `.type/.id`、`.item_id`、`.use_item_id` 和部分 `.code` 写入**确定引用**，没有再验证文件消费者、记录种类。上游注释虽称为未验证来源，这里却没有保留“不确定”的语义。

历史追踪确认这是旧假设残留：

- 泛字段引用判断最早存在于 `d1d18f66`；`27a6c17c` 迁移到 `NativeDarkestReader` 时保留了当前形式。
- `55e214c`（第十一轮修复）增加了 `if (hero || monster)` 分支，并正确排除标准人物／怪物文件中的未知记录；原来的通用分支仍在它后面。
- 本轮再次用标准人物文件做对照：`notes: .type estate .id audit_token` 不产生引用，`extra_battle_loot: .code audit_root` 正常产生副本引用。**之前修过的分支仍然正确；遗漏在其他目录，不是 fc30df8 的 HP 修复导致回退。**

建议修复范围：统一引用候选、缺失文件诊断和文本消费的资格判断；已知消费者按其查询和记录种类处理。尚未验证的真实资源结构只能保留分析不确定性，不能仅凭同名字段确认物品被使用。不要只排除 `notes.darkest` 这个名字，也不要一并删掉合法人物伴生掉落或其他已经验证的入口。

## 可见影响与未发现异常的对照

- 默认物品搜索列表使用 `IsHiddenByDefault`；错误确认和错误“不完整”都会让条目离开隐藏列表。小镇／副本归属及日志中的默认／隐藏统计也受影响。
- 该路径只提供引用证据，不改变物品定义的 ID、类型或每格上限。四次隔离保存对照中，写入数量 5 后，小镇保留一条数量 5；副本按上限 2 分成 `2、2、1`，DSON 编解码后均保持正确。
- 将两个本地样例的错误引用改为注释，内容指纹变化，重新加载后恢复为 SuspectedUnused。这里问题在重新计算时采用的规则，不是这组样例的内容刷新遗漏。未运行 GUI 自动同步计时器。
- 已读取怪癖生成校验及 HP 条件相关路径，未取得额外错误规则的证据。奇物同 ID 的互动合并细节没有在本轮完整证明，不作为已确认问题，也没有据此提出删除保护。

## 验证记录

提交前重新核对上一轮冻结结果：人物初始 HP 132 个场景、DSON 对照、资源语义套件 19 PASS，以及完整契约套件 **90 PASS / 0 FAIL / 0 SKIP**。详见[上一轮修复记录](initial-hp-condition-fixes-2026-09-15.md)。这些验收不覆盖此次新反例。

本轮隔离探针直接引用当前 Release Core，不重新编译或替换产品 DLL：

- Core SHA-256：`B6DFFED56DA076BE2383783703D7D0E25B2EC35E6CEBF9B722D35B445DC9C068`。
- 游戏 EXE SHA-256：`35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。
- 60 个独立内容样例，各读取小镇、副本一次，共 **120 次目录观测**。覆盖 local、workshop、启用 DLC 前缀的 Mod。
- 其中 **24 次错误确认、6 次错误“不完整”**，其余 **90 次对照正确**。六次“不完整”均同时影响两个测试物品。
- **4 次隔离 DSON 保存往返、2 次指纹／重载对照**通过。
- 原生查询的字符串地址、相对调用目标和样例名称匹配检查通过。

证据位于忽略目录 [historical-rule-review28-20260915](../../workspaces/historical-rule-review28-20260915/)：

- [探针](../../workspaces/historical-rule-review28-20260915/Probe.cs)、[探针项目](../../workspaces/historical-rule-review28-20260915/Probe.csproj)。
- [原始结果](../../workspaces/historical-rule-review28-20260915/probe-results.json)、[执行日志](../../workspaces/historical-rule-review28-20260915/probe.log)。
- [校验脚本](../../workspaces/historical-rule-review28-20260915/verify.py)、[校验摘要与文件哈希](../../workspaces/historical-rule-review28-20260915/verification.json)。

校验通过表示上述缺陷及对照稳定复现，不表示产品已修复。没有启动游戏，没有修改实际存档、已启用 Mod 或 Steam 配置。数量验证直接调用保存编辑器及编码器，没有运行完整保存服务事务或游戏内领取流程。

本轮仅新增此报告、更新审查索引，产品和正式测试未修改，因此未重复执行全量契约套件，也不触发实现完成审查员。新增报告尚未提交；上述问题待用户决定是否修复。
