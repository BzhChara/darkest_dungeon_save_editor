# 历史修改第四轮审核：地图资源旧规则（2026-09-09）

后续状态：用户已确认修复下列五项；代码变化及最终验证另记于[地图资源旧规则修复](map-resource-rules-fix-2026-09-09.md)。本文保留审核时点的旧行为和证据。

## 提交与范围

按要求先提交上一轮已验证的物品、饰品身份修复：`d55a15f`（`fix: preserve exact inventory and trinket identities`）。提交后工作区干净。本轮是审核，没有修改产品代码、正式测试或现行规则实现；仅新增此记录和目录入口。

对照历史提交、当前调用链和既有研究，检查人物/怪癖/HP、地图资源、战斗历史维护、自动同步及强制返回。新增的确定问题集中在 `BattleRoomAttachmentCatalog`：它仍保留早于原生规则研究的独立读取方式。没有据此否定此前已经验证的 `.mash.darkest` 多文件战斗表规则。

## 依据与验证方式

- 对同一 Windows x64 build 27890 游戏文件作只读反汇编，SHA-256：`35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。检查脚本先验证此哈希。本轮未启动游戏，没有新的游戏内 A/B 实验。
- `0x1404AF1EC`–`0x1404AF23C` 用地区 ID 构造 `dungeons/%s/%s.props.darkest`，交给文件读取器；并非枚举该地区的任意 `*.props.darkest`。`arena` 有单独跳过分支，不能据此为其猜测普通资源池。
- `0x1404AF249`、`0x1404AF512` 调用已研究的 `LineReader::ReadNextLine`（`0x14028E1C0`），其记录/注释行为适用于这里。
- 奇物类池进入 `0x1404AF800`，陷阱/障碍池进入 `0x1404AFC50`。两者分别在 `0x1404AFA8D`、`0x1404AFEDE` 使用浮点字段读取器 `0x14036C270` 读取 `.chance`；在 `0x1404AFA9C`、`0x1404AFEED` 使用最后字段查找器 `0x14036B300` 找 `.types`。
- 这两个池读取器最多读取 64 个原始字符串槽，每槽为 64 字节缓冲区。消费到首个空字符串结束。陷阱/障碍循环的 `0x1404B0004`–`0x1404B003A` 为每个条目追加相同的完整 chance，并累加总权重，没有按本行类型数相除，也没有先去重。
- 陷阱池字符串复制回调 `0x1404B0D20`–`0x1404B0D45` 原样复制四个 16 字节块，未删除 ID 两端空格。
- 隔离控制台探针位于 `workspaces/historical-rule-review-20260909/`，引用已构建的 Core DLL，构造独立资源和假档案；不写真实存档或 Mod。最终 14 组样例运行正常，结果在 `results.json`，样例目录为 `runs/677e69570c5c40848ec5702348b07f76`。该探针记录错误的实际表现，运行成功不表示产品已修好。
- 探针所用 Core DLL SHA-256 为 `6F6D9C498A119647BDEA6A4FA6A238F4B366AE6946AC40CA1E67515194375C4C`，与上一轮最终验证产物一致；本轮未修改产品源码。记录及目录中的 35 个本地链接、`git diff --check` 已检查通过。
- 探针初次编译误用了不可公开访问的本地化类型；改用已读源码中的常量后重新运行成功。一次 PowerShell 结果展示因字典含大小写不同的键失败，改用 `ConvertFrom-Json -AsHashtable` 后成功。这两项是探针/展示问题，不作为产品缺陷。

## 确定发现

### 1. P2：任意名称的 props 文件被当成本地区有效资源池

位置：[地图资源文件发现](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.cs)，`ResolveEffectivePropFiles` / `EnumeratePropFiles`。历史实现来自 `a5f38b8`，后续 `6523b66` 将它扩展到陷阱/障碍；接入统一文件覆盖后仍未收紧实际打开路径。

隔离样例：Base 的 `dungeons/weald/weald.props.darkest` 只有 `alpha`，Mod 的清单列出另一个 `dungeons/weald/extra.props.darkest`，其中只有 `beta`。当前目录将两者都作为荒野陷阱，自动选择各占一半，且 `ValidateDefinition` 接受 `beta`。

原生地区池入口只打开 `weald/weald.props.darkest`，因此这个额外文件不能证明 `beta` 属于荒野陷阱池。即便 `beta` 的底层资源在别处有效，也不能拿未被该池读取的文件证明其地区、普通/宝箱分类或随机权重。

建议：按实际地区构造资源池路径，在该路径上应用已确认的清单、Mod 和 DLC 挂载规则；移除任意文件扩充地区池的旧假设。保留哈希资源可显式放置与地区随机池资格之间的区别。

### 2. P2：props 仍按物理行和首个字段解析，既漏读也误读

位置：[地图资源解析](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.cs)，`ParseFile` / `StripComment`，以及 [权重读取](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.RegionalSelection.cs)，`ReadRegionalWeight`。逐行解析来自 `a5f38b8`，权重形状限制来自 `6523b66`。

| 输入 | 当前编辑器实际结果 | 原生入口的规则 |
| --- | --- | --- |
| `traps:` 后换行写 `.chance`、`.types alpha` | 完全漏掉 alpha | 属于同一记录，可以读取 |
| 块注释内有完整 alpha 声明，注释外有 beta | 两者都进入池，alpha 也通过定义校验 | 注释内声明不生效 |
| 两条 `traps:` 声明共用一行 | 只留下首项定义，重复 chance 又使自动池为空 | 分别读取两条记录 |
| `.types alpha .types beta` | 使用 alpha | 最后一次字段为 beta |
| `.chance 1 .chance 2 .types alpha` | 禁止参与自动选择 | 使用末值 2 |
| `.chance 50% .types alpha` | 禁止参与自动选择 | 浮点读取器将其解析为 0.5 |
| `.types alpha "" beta` | 跳过空串后继续加入 beta | 遇首个空串停止，beta 不参与 |
| `.types` 前 64 项为 alpha，第 65 项为 beta | 去重后仍加入 beta | 只读取前 64 个槽 |

建议：复用已经研究的记录与数值字段规则，并实现这里特有的列表长度、字符串缓冲区和结束条件。不能直接套用“战斗组合的空怪物位置仍占位”规则；那是另一种读取器。

### 3. P2：陷阱/障碍权重被平分、重复条目被去重

位置：[地图资源解析](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.cs)，`ParseFile` 的 `.Distinct(...)` 和 `ReadRegionalWeight(...) / ids.Length`。两处均来自 `6523b66`。

样例一：

```text
traps: .chance 4 .types alpha beta
traps: .chance 4 .types gamma
```

游戏池的三个条目权重为 4、4、4，各占三分之一。编辑器将第一行拆成 2、2，第二行为 4；1200 个均匀取样结果为 300、300、600，即 25%、25%、50%。

样例二：`.chance 4 .types alpha alpha beta`。游戏保留两份 alpha，合计 alpha:beta 为 2:1；编辑器去重后得到 1:1。

影响右键自动新建/替换陷阱或障碍时的候选概率；不影响 `.mash` 战斗编号。建议保留每个原始有效条目的完整权重和重复次数，展示目录可以去重，随机池不能去重。现有 [区域资源测试](../../tests/DarkestDungeonSaveEditor.ContractTests/RegionalMapContentContractTests.cs) 第 75 行恰好把 25%、25%、50% 当成预期，且夹具用了任意 props 文件名，后续修复必须同时纠正这些测试，不能以旧断言为原生行为依据。

### 4. P2：地图资源 ID 的空格被删除后计算哈希，写入校验也认可错误哈希

位置：[哈希计算](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.cs)，`ComputePropHash` 第 334 行。来自 `a5f38b8`。

原始 `.types " alpha "` 和 JSON 资源名 `" alpha "` 可以在当前目录中精确对应，但 `ComputePropHash` 对 ID 执行 `Trim()`，得到 `alpha` 的哈希 `781775590`；原始字符串的 UTF-8 polynomial-53 哈希是 `-925614370`（按有符号 32 位表示）。目录和 `ValidateDefinition` 都接受前者。

[地图内容写入](../../src/DarkestDungeonSaveEditor.Core/BattleMapSaveEditor.Content.cs) 又调用同一个哈希函数校验，随后直接将此值写入 `trap` 等字段，因此重复验证不能发现错误。建议保留原始身份再计算哈希，并继续处理真正的原生哈希碰撞、缓冲区长度及空值边界，不应只删除校验。

### 5. P3：地图名称请求仍忽略大小写去重，漏掉有效名称

位置：[地图目录本地化请求](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.cs)，第 219–223 行。

隔离文件同时定义 `alpha`、`ALPHA`，XML 中分别有 `Lower trap`、`Upper trap`。共享本地化读取器已按精确键工作，但地图入口先执行 `Distinct(OrdinalIgnoreCase)`，导致 ALPHA 的名称根本没有被请求。结果两个资源都保留、哈希也不同，ALPHA 名称却显示 `—`。这也是上一轮共享名称键修复后尚未贯通的调用端。

建议将请求去重改为精确键；显示排序和用户搜索仍可忽略大小写。该问题影响名称展示/按名称搜索，不改变战斗编号。

## 暂不作确定结论的项目

- 另一个隔离样例证实：高优先级 Mod 清单列出但物理文件缺失时，地图扫描器记录 issue 后继续使用 Base 同路径文件，并允许定义校验。它与容量目录中保留缺失覆盖候选的保护不一致；本轮尚未追完游戏在“已注册文件打开失败”时的底层回退行为，不能把它直接描述成已经验证的游戏读取差异。应在后续修正来源解析时单独核对。
- `curio_props.csv`、JSON prop 继承等重复定义的完整取值规则，仍不能无依据地全部改成 first-match 或 last-match。本轮验证的是地区 props 池入口、字段与权重，不是所有奇物互动机制。
- 人物/怪癖剩余的保守身份冲突、未知 HP 条件、进化目标校验，本轮未取得足够证据将其判定为可直接删除的错误规则。

## 保留边界及交付状态

本轮没有发现新的证据推翻已验证的战斗多文件排序、空遭遇占号、Bridge 旧编号及历史归属保护。自动同步仍在资源轮询时重新计算内容指纹；保存操作仍执行场景、源文件与存档哈希、游戏进程和事务校验。这里只说明本轮检查范围，不能解释为完整游戏语义已无遗漏。

此轮新增确认的五项均未修复。建议下一次修改集中处理地图内容目录的来源、解析、原始身份及区域池，并同步纠正旧契约。当前无产品代码差异，因此按协作约定未触发实现完成复核者；本轮证据为主审的只读原生检查和隔离可执行反例。
