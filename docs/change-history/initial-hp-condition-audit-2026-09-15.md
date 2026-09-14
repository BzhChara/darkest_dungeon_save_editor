# 历史修改第二十七轮审核：人物初始 HP 条件（2026-09-15）

先按要求提交上一轮升级购买码与费用引用修复：`c281549034bd043871330625d9681e96da9d8d36`，标题为 `fix: resolve final upgrade requirements and cost references`。提交前重新执行上一轮证据校验，123 个定向场景、12 次 DSON 验证、18 项语义检查和 89 项完整套件结果均符合已记录的通过状态；提交后工作区干净。这些是上一轮修复的验证，不是下面新问题的通过证明。

本轮只读审核产品实现，新增审核记录、索引和 Git 忽略目录中的隔离探针；未修改产品代码、正式测试、真实档案、活动 Mod 或 Steam 配置，没有启动游戏。按协作约定，只读审核不另行触发完成复核。下列问题尚未修复。

## 确认问题：反向折磨条件未计入新人物初始 HP（P2）

位置：[`StagecoachHeroCandidateFactory.HitPoints.cs`](../../src/DarkestDungeonSaveEditor.Core/StagecoachHeroCandidateFactory.HitPoints.cs) 第 156–164 行，`IsActiveAtGeneration`；第 23–32 行将其结果用于候选的初始生命计算。

当前生成条件只把 `always`、`no_trinkets` 的原始值设为 true，其他条件一律设为 false，再返回：

```csharp
return rawCondition && !modifier.IsFalseRule;
```

这不是通用的“条件取反”。一个新生成、未陷入折磨的人物，其 `afflicted` 原始条件是 false；若 Buff 指定 `is_false_rule: true`，原生判定应为 true。当前代码却把正向、反向 `afflicted` 都排除在初始 HP 计算之外。

例如，基础 HP 为 20，只有一个所选怪癖，分别引用下列有效 Buff：

| 条件及修正 | 原生条件下对应的满血值 | 编辑器预览、候选和 DSON 回读值 |
| --- | ---: | ---: |
| 未陷入折磨时，最大 HP +50% | 30 | 20 |
| 未陷入折磨时，最大 HP −25% | 15 | 20 |
| 未陷入折磨时，最大 HP +4 | 24 | 20 |

这里的“对应满血值”来自原生条件分支及最大 HP 修正计算，结合编辑器已有的“新候选按当前有效最大生命生成满血”约定；不是本轮游戏内录屏或游戏刚生成候选的存档采样。20、0.5、−0.25、4 均能用二进制浮点精确表示，这三个差异不能用舍入解释。

目录已经完整读到 Buff 的 `RuleType`、`IsFalseRule`、修正类型和数值，怪癖也标为可直接写入。遗漏发生在候选生成，不是清单、文件覆盖、怪癖 last-match 或 Buff 选择错误。现有可达状态安全检查 `IsActiveAtRuntime` 已按 `condition != modifier.IsFalseRule` 处理反向条件；它只负责拒绝危险组合，并不替生成方法决定初始 HP。

影响路径为所选怪癖 → 人物候选预览 → 候选 `actor.current_hp` → 马车插入 → DSON 保存。没有把怪癖 Buff 再写入 `actor.buff_group`，也没有发现编解码改变数值：本例是错误的初始值被原样保存。不能由此宣称游戏必然崩溃、一定显示超上限血条，或当前 profile_1 一定存在受影响怪癖；本轮未验证这些情况。

## 原生依据

固定 Windows x64 build 27890，游戏 EXE SHA-256：`35e5a653279992564809ff8406febd5a02a7d6961044781b1296b38a7096f59b`。

1. `0x140077F00` 的规则初始化中，`afflicted` 对应表项 `0x142AA4EA0`；相对表基址 `0x142AA4D40`、步长 `0x2C`，枚举值为 8。
2. `0x1404736A0` 的适用性检查按枚举 8 进入 `0x1404737D4`。Hero 虚表 `0x140E790F0` 的 `+0x148` 槽指向返回 true 的 `0x14026D000`，随后分支到 `0x14047380B`，设置适用标志为 true。该 Hero 条件不需要副本环境。
3. 条件值分支 `0x140473FEA` 调用 `0x14047DEE0`；其 Hero 分支 `0x14047DF5E` 比较人物 `+0x11E0` 的折磨状态是否非零。对未陷入折磨的新人物，原始条件为 false。
4. 返回后 `0x140473FFF` 检查 Buff `+0x60` 的反向标记；反向时 `0x140474009` 使用 `sete` 取反。因此未折磨且反向为 true 时，最终条件为 true。
5. 活动 Buff 收集 `0x140473570` 使用上述条件函数；人物刷新 `0x1405BFA50` 在没有副本上下文时也会调用 `0x1404799A0`，后者收集活动 Buff 并通过 `0x1404746F0` 累计固定及百分比修正。`0x1404B9B10` 读取最终属性值。这证明反向折磨条件不会因人物在副本外就统一失效。

上述跳转表、虚表指针、关键指令和片段哈希由本机脚本核对，未执行、注入或修改任何游戏函数。`in_mode`、`lightabove` 具有不同的适用性前置条件，不能把本结论推广为“所有缺少上下文的条件取反后都应生效”。

## 为什么之前改过 HP，这次仍有问题

- `git log -S` 可追溯到 `fb7b0fd`：未拆分的候选工厂中已经有相同的生成条件逻辑。`827ca42` 拆分文件时原样搬迁，不能把搬迁提交误称为错误起点。
- `37d9167` 修正了 HP 输入单精度、条件字符串及模式哈希等问题，保留了这段生成条件选择。它解决的是不同层次的问题，没有覆盖此次反向折磨遗漏。
- 刚提交的 `c281549` 处理升级树购买码和费用引用，不涉及这段 HP 条件，也没有导致本次问题回归。
- [`HeroCandidateContractTests.cs`](../../tests/DarkestDungeonSaveEditor.ContractTests/HeroCandidateContractTests.cs) 第 302–310 行只用正向 `afflicted`、模式和光照夹具验证“运行时条件不预先应用”。这个测试可以通过，却没有覆盖反向折磨。
- [`content-save-rules.md`](../content-save-rules.md) 第 445 行和 [`resource-duplicate-semantics.md`](../resource-duplicate-semantics.md) 第 346 行的运行时条件概述范围过宽，后续修复应同步澄清。当前审核没有提前把规则文档改写成已经实现。

建议后续修改生成条件选择：对已确认的新人物状态求值，再执行反向标记；缺少必要上下文的条件仍应保持不适用。补充正向／反向折磨、正负百分比、固定值和保存回读回归，保留光照／模式上下文、未知条件、非正 HP、非有限值以及禁止重复写入 Buff 等独立保护。无需增加旧存档兼容或迁移。

## 隔离验证

探针直接引用上一轮已验证的 Core Release DLL，SHA-256 为 `85bf03ddeb54341464ab1fc56468b138334c036613dbb9dc94864e502a8d9a60`；没有重新编译或替换产品。

```powershell
python -B workspaces/historical-rule-review27-20260915/verify_native.py
dotnet build workspaces/historical-rule-review27-20260915/Probe.csproj -c Release --no-restore --nologo -m:1 -nr:false -p:UseSharedCompilation=false
dotnet workspaces/historical-rule-review27-20260915/bin/Release/net8.0-windows/Probe.dll .
python -B workspaces/historical-rule-review27-20260915/verify_results.py
```

- 6 类合成资源来源：base、mode、dlc-package、dlc-feature、local、workshop。两类 Mod 均有显式 `modfiles.txt`；原版、模式及官方 DLC 保持对应目录设备规则。这是加载通道覆盖，不是 6 份真实玩家档案或工坊 Mod 的实机测试。
- 每类 8 个场景，共 48 个；18 个稳定复现上述遗漏，30 个正常对照符合预期。对照包括 `always`、`no_trinkets` 的正反向，以及正向 `afflicted`。探针 PASS 表示复现与对照符合预期，不表示产品缺陷已修复。
- 对 18 个复现及 6 个无条件正向对照，实际调用 `StagecoachHeroSaveEditor.AddCandidate`，编码完整合成 town 文档后解码，共 24 次 DSON 往返；整份文档相等、GUID 894 的初始 HP 保留、原始输入对象未修改。
- 初次探针因夹具未声明任何 0 级战斗技能而被已有保护拒绝；补齐夹具技能后再运行。最终探针构建 0 警告、0 错误，执行退出码 0；失败只涉及忽略目录中的夹具，没有修改产品来绕过保护。
- 证据复核核对 Core／EXE／原生片段哈希、48 个结果和 24 份解码文档，并检查本轮跟踪差异仅为报告及索引。本轮未另跑新的全量产品回归；产品源码和正式测试没有改动。

本机证据：[probe-evidence.json](../../workspaces/historical-rule-review27-20260915/probe-evidence.json)、[native-evidence.json](../../workspaces/historical-rule-review27-20260915/native-evidence.json)、[verification.json](../../workspaces/historical-rule-review27-20260915/verification.json)、[最终探针日志](../../workspaces/historical-rule-review27-20260915/probe-final.log)。本机探针和二进制摘录位于 Git 忽略目录，仓库保留报告和关键地址。

## 本轮没有删除依据的旧保护

- 露营技能文件内等级阈值从 0 开始、不能沿用另一文件的阈值：与已记录的原生文件级状态一致，保留。
- 缺失必填 amount／is_false_rule 等字段的 HP Buff：原生读取存在错误分支，不能武断按 0／false 补齐后宣称有效，保留未验证状态。
- 购买目标哈希冲突、DSON 购买码边界、战斗编号不确定和保存前内容重验：本轮没有提供可删除的依据。

另复查了物品数量与保存身份、人物生成及升级消费者、资源覆盖和 Bridge 编号相关路径；除上述初始 HP 条件之外，没有新增已复现的结论。本轮是有范围的历史审核，不能据此宣称全项目不存在其他错误。

用户随后确认修复。实现与验证见 [2026-09-15 修复记录](initial-hp-condition-fixes-2026-09-15.md)，本报告保留审核时的代码状态与复现结果。
