# 历史修改第十六轮审核：装备购买要求、可达档位与购买目标，2026-09-11

后续状态：用户已授权修复，实施与验证另见[修复记录](equipment-upgrade-consumer-fixes-2026-09-11.md)。下文保留审查当时 `c624056` 的实现、行号与结论。

## 提交与范围

按用户要求，先提交上一轮已经验证并独立复核的战斗技能修复：`c624056`，标题 `fix: align combat skill purchase targets and unlock validation`，共 22 个文件。提交后工作区干净；15 份代码／测试文件与上轮复核输入哈希一致，Core、App、正式测试输出的 Core DLL 均为 `80A38BE5433C794030902FAAAD9732C27CB37FB42973A1862DF0744AC34417BF`。

随后只读审查历史实现，重点追踪人物装备资源、等级模板、生成预检、购买计划和保存记录。本轮确认四项条件性问题，尚未修复产品代码。新增内容仅为本报告、修改记录索引和 Git 忽略工作区中的隔离工具／证据。没有修改真实存档、游戏文件、用户 Mod 或配置。

| 优先级 | 已确认问题 | 影响 |
| --- | --- | --- |
| P2 | 按满足人物等级的最高装备档位取值，越过未购买的中间档 | 生成的武器／护甲等级与原生重新查询结果不同，护甲还会影响初始 HP |
| P2 | 将装备 `upgradeRequirementCode` 当普通字符串读取 | 引号被错误剥离；多字符值未按原生单字节规则解释，分别造成错误放行和误拦 |
| P2 | 强制每个高档装备都需要购买码，并要求升级树条目全部对应装备 | 原生无需购买的装备或无关的额外升级条目使高等级生成被误拦 |
| P2 | 装备购买目标仍使用完整 `<职业>.weapon/armour` | 超过原生查询缓冲区时写入错误目标的购买记录，或漏绑真实目标树 |

这些问题沿用旧实现的假设，并非此次提交新增。它们发生在装备分支，不能用已经修正的战斗技能购买逻辑代替核对。

## 验证方式与结果

- 原生依据固定为 Windows x64 build 27890；游戏 EXE SHA-256 为 `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。没有新开游戏实测或执行游戏内存写入。
- 隔离程序直接引用当前 Release Core DLL，使用 `UseAppHost=false`，顶层捕获异常，通过 `dotnet` 运行。没有重编产品工程。
- **90 组目录至生成／保存对照**：Base、模式、DLC、本地 Mod、工坊 Mod、带启用 DLC 前缀的 Mod，六类来源各 15 组。所有 Mod 样例具备清单。
- **36 组正常对照、54 组反例**：其中 24 组能生成，但保存的装备等级与原生消费规则推导的结果不同；30 组被误拦。这里的反例数是隔离样例数，不是用户档案中的受影响职业数量。
- 能生成的本地样例完成 **30 次实际 DSON 往返**，每例核对 town、roster、upgrades；确认 `weapon_rank`、`armour_rank`、`current_hp`、购买 hash／code／GUID 已进入保存内容。
- 独立验证脚本从生成的定义文件和保存产物重新读取原始购买码、目标和购买状态，完成 **180 次本机 UCRT 64 字节格式化对照**，再次核对全部 30 份往返产物。脚本退出 0。
- 原生装备消费过程是依据反汇编建立的模型，并未在游戏里调用该函数；UCRT 格式化是直接调用本机库。不能将这些结果描述为新的游戏实测。
- 上一轮完整回归为 **48 PASS / 0 FAIL / 0 SKIP**，构建为 0 警告／0 错误；本轮核对对应输入与输出一致，没有重新运行整个产品回归套件。

证据位于 `workspaces/historical-rule-review16-20260911/`：[隔离程序](../../workspaces/historical-rule-review16-20260911/Program.cs)、[最终日志](../../workspaces/historical-rule-review16-20260911/probe.log)、[原始结果](../../workspaces/historical-rule-review16-20260911/probe-results.json)、[验证脚本](../../workspaces/historical-rule-review16-20260911/verify_audit.py)、[验证结果](../../workspaces/historical-rule-review16-20260911/verification.json)。这些文件不随 Git 分发。

最初的隔离程序错误地假定中文职业目录也会从 Base 的物理目录枚举进入目录，因找不到该职业而退出 1，日志保留为 `probe-initial.log`。这属于测试前提错误：已有原生目录规则会排除这种物理枚举条目。最终矩阵改用 ASCII 职业 ID，保留六种来源和 63／64 字节目标边界，没有修改产品目录规则来让样例通过。本轮不据此声称完整验证了非 ASCII 职业身份。

## 1. 装备必须按顺序可达，不能只选满足等级条件的最高档

当前 [HeroClassCatalog.Progression.cs](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.Progression.cs) 第 283–288 行，对各档分别判断 `MinimumResolveLevel <= level`，然后 `MaxBy(rank.Rank)`。

但原生正常装备重算入口 `0x1405C7540` 按向量顺序遍历。武器在 `0x1405C7681` 读取该档购买码，未购买时由 `0x1405C76C9` 跳出；护甲对应 `0x1405C77A0`、`0x1405C77E8`。只有连续通过的档位才能更新人物 rank。见[完整入口](../../workspaces/historical-rule-review16-20260911/native_1405c7540.txt)。竞技场及全购买调试分支不在本结论的普通运行条件内。

隔离样例同时设置武器／护甲三个档位：

| 装备档位 | 护甲基础 HP | 所需购买码 | 该码要求的人物等级 |
| --- | --- | --- | --- |
| rank 0 | 20 | 无 | 无 |
| rank 1 | 40 | `a` | 3 |
| rank 2 | 70 | `b` | 1 |

生成 1 级人物时，编辑器购买 `b`，保存武器 rank 2、护甲 rank 2 和 `current_hp=70`。原生检查通过 rank 0 后，发现 rank 1 的 `a` 未购买，即停止；实际查询选中的护甲基础 HP 为 20。不是保存 codec 丢失了 `b`，而是不能绕过中间档。

人物等级升至 3、`a/b` 都能购买的对照则正确得到 rank 2。相同购买码可供多个连续装备档使用的对照也正常，不能反过来要求每个装备档必须使用不同 code。

该 `MaxBy` 实现可追溯至初始基线 `7b9a67f`；`1a03bb4` 拆分文件后保留。建议用同一份实际购买计划逐档推导装备 rank，再计算护甲 HP。可以保留已有符合等级要求的购买记录，但不能把尚不可达的装备当成已装备。

## 2. 装备购买码是原始单字节字段，不是普通字符串

[HeroClassCatalog.InternalModels.cs](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.InternalModels.cs) 第 204 行调用 `NativeDarkestReader.ReadString(body, ".upgradeRequirementCode")`。

原生武器 `0x1404868ED`、护甲 `0x1404869AF` 调用的却是 `0x14036CC50`：查找最后一次字段出现，跳过空白，将随后**一个原始字节**写入装备 `+0x110`。它不解引号，也不读取完整字符串。字段省略时不写，保留原值。见[单字节读取器](../../workspaces/historical-rule-review16-20260911/native_14036cc50.txt)、[武器调用](../../workspaces/historical-rule-review16-20260911/native_1404867e0.txt)、[护甲调用](../../workspaces/historical-rule-review16-20260911/native_140486910.txt)。

| 文本字段 | 原生购买码 | 编辑器当前解释 | 对照结果 |
| --- | --- | --- | --- |
| `.upgradeRequirementCode a` | `a` | `a` | 正常 |
| `.upgradeRequirementCode "a"` | 双引号字节 `0x22` | `a` | 树中有 `a` 时错误放行，保存的 `a` 无法满足原生装备的要求 |
| `.upgradeRequirementCode alpha` | `a` | `alpha` | 树中有 `a` 时，原生可以满足，编辑器反而判无法解析 |

带引号的护甲样例保存 rank 1、HP 40，但原生查询只能停在 rank 0。武器独立样例复现相同问题，避免只凭护甲推断。普通单字符、大小写不同 code、重复末字段的对照均通过。

早期实现使用 `ReadString(attributes, "upgradeRequirementCode")`；`27a6c17` 改为共用文本读取器时仍保留字符串假设。因此不能回滚整个文本解析修复，应为这个实际使用单字节的消费字段采用相应读取语义。JSON 升级树注册、普通 ID 字段以及通用字符串读取不应一并改成单字节。

## 3. 缺购买码和额外升级条目不应一律使高等级模板失效

[HeroClassCatalog.Progression.cs](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.Progression.cs) 第 334–351 行要求多档装备必须有升级模板、每个高档装备必须提供可匹配的 code；第 358–363 行还要求树中不能存在未对应装备档的 code。这些限制也可追溯至 `7b9a67f`。

原生新装备的购买码初始化为 `0x00`，武器位于 `0x1404C5E95`、护甲位于 `0x1404C5B55`；见[武器初始化](../../workspaces/historical-rule-review16-20260911/native_1404c5e30.txt)、[护甲初始化](../../workspaces/historical-rule-review16-20260911/native_1404c5af0.txt)。这里的 **`0x00` 是未填写的零字节，不是文本购买码 `"0"`**。

装备重算在该字节为零时直接允许继续，因此以下两个样例没有当前代码假定的不可解析问题：

- 两档装备均不声明购买码，升级树 requirements 为空：原生顺序消费可到 rank 1，编辑器拒绝生成 1 级人物，理由为“多个 rank，但缺少有效 upgrade 模板”。
- rank 1 使用 `a`，树中同时有 `a` 和未被装备引用的 `z`：原生装备查询只检查实际装备的 code，`z` 不影响 rank 1；编辑器因为存在未对应 rank 的 `z` 而拒绝生成 1 级人物。

目前这些错误会把等级模板缩回基础档，不能表述为所有情况下职业都完全消失。建议删除这两类无原生依据的全量对应限制，按装备实际要求逐档计算；仍保留非零购买码无法确定、异常生效树、身份冲突和无有效 HP 等真正需要的限制。

## 4. 装备目标存在独立的 63 字节查询边界

[HeroClassCatalog.Progression.cs](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.Progression.cs) 第 114–115 行以完整 `<职业>.weapon/armour` 绑定树。[StagecoachHeroCandidateFactory.Progression.cs](../../src/DarkestDungeonSaveEditor.Core/StagecoachHeroCandidateFactory.Progression.cs) 随后按该完整树 ID 生成装备购买记录，writer 原样计算 hash。

原生正常装备重算则先用 `0x14036B3A0` 的 64 字节缓冲区格式化目标：武器 `0x1405C75F4`、护甲 `0x1405C771C`。最多保留 63 字节，末尾 NUL，随后对该结果计算 hash。见[格式化 helper](../../workspaces/historical-rule-review16-20260911/native_14036b3a0.txt)。

本轮用正常 ASCII 职业 ID 隔离复现：

- 56 字节职业 ID 加 `.weapon`／`.armour`，总长 63 字节：目录、购买 hash 和原生 rank 一致。
- 57 字节职业 ID，加后缀后总长 64 字节，仅有完整名称的树：编辑器保存 rank 1、HP 40；购买 hash 对应完整名称，原生重算查询的是截断目标，因此回到 rank 0。
- 同一职业仅定义原生 63 字节目标树：该树和对应购买要求可被原生查询使用，编辑器绑定时却找不到完整名称，拒绝生成 1 级人物。

不能因此全局截断所有升级树 ID：定义注册仍使用完整原始 ID，而且游戏自身的部分装备升级路径也不同，`0x1405C8E86`／`0x1405C8F76` 使用 128 字节 helper `0x14036B750`。见[自动升级片段](../../workspaces/historical-rule-review16-20260911/native_1405c8c72.txt)、[护甲后续片段](../../workspaces/historical-rule-review16-20260911/native_1405c8f60.txt)、[128 字节 helper](../../workspaces/historical-rule-review16-20260911/native_14036b750.txt)。

后续修复至少应阻止生成“保存 rank／HP 与最终购买查询不一致”的候选；若支持这类长目标，应区分原始注册树、实际查询目标和游戏其他调用路径，对目标合并／碰撞明确报告限制，不能简单把完整树改名。上一轮战斗修复明确没有改装备，因此此次发现不否定战斗分支已经完成的验证。

## 影响链、保留内容与边界

生成可用性直接调用实际生成器，以上条件会同时影响等级可选性和预览。候选序列化写入装备 rank 与 `current_hp`；提交前检查重用相同目录等级模板及购买计划，没有独立的原生装备可达性校验，因此不会仅因这些语义错误而自动修正。该结论来自路径追踪；隔离程序执行了目录／生成／writer／DSON 往返，没有对真实档案执行提交事务。

本轮正常对照支持继续保留大小写敏感购买码、装备名称首次插入槽位、同一码用于多个连续档、末字段生效，以及资源清单和目录限制。未发现应整体撤回之前文本解析、同路径覆盖、升级树逐 ID 最后匹配或战斗技能修复的证据。

本轮深查范围集中在装备消费，不代表重新完整实测物品、饰品、怪癖进化、战斗／Bridge、自动同步及地图编辑。没有检查用户当前 `profile_1` 是否包含上述定义，不能断言该档案已经受影响。其他技能声明边界仍需分项验证，不从本轮装备规则推广结论。

四项发现均待用户授权后修复。由于本轮没有产品代码修改，按照只读审查要求未启动新的完成审查者；最初提交沿用上一轮已完成的独立复核。本报告和索引是提交后的新文档，尚未再次提交。
