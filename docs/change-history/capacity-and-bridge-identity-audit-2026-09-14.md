# 历史修改第二十三轮审核：容量旧保护与 Bridge 地区身份（2026-09-14）

后续状态：用户已授权修复并要求集中检查同类大小写问题；实现与验证见[修复记录](case-policy-and-capacity-fixes-2026-09-14.md)。下文“尚未修复”等描述保留为原审核时点的状态。

本轮先提交上一轮修复：`5c8fba618648666ef30b120603b5e2ee5c80372c`，标题 `fix: align encounter collection queries and source validation`，共 16 个文件。提交前核对上一轮验证记录中的 16 份文件和 3 份 Core 输出，提交后工作区干净。上一轮最终完整测试为 77 PASS、0 FAIL、0 SKIP，Release 构建零警告、零错误，独立审查未发现遗留问题；本轮没有对未变化的代码重新运行整套测试。

随后只读审核确认 **2 个 P2，尚未修复**。本轮只新增审核记录与目录索引，未修改产品源码、正式测试、现行规则文档或 Release 程序。隔离探针和合成档案位于被忽略的 `workspaces/historical-rule-review23-20260914/`，没有操作真实存档、已启用的 Mod、Steam 配置或游戏窗口。

## 证据范围

- 探针直接引用当前 Release Core，SHA-256 为 `4374df8f819931ed0354a7d70cb4040321f20b8c97b0f3123575b3620ec1fcfa`，与 Core、App 和正式测试目录里的 Core 输出一致；没有重编译产品。
- 固定 Windows x64 build 27890 的游戏 EXE，SHA-256 为 `35e5a653279992564809ff8406febd5a02a7d6961044781b1296b38a7096f59b`。本轮重新提取容量加载器、LineReader 与 MashGuide 三个函数的反汇编。
- 原生预期来自该 EXE 的分支及已核实的目录查询规则；编辑器行为来自当前 DLL 的实际执行。**本轮没有重新进入游戏实测，也没有确认 profile_1 是否含有这些触发条件。**
- 复核范围包括容量读取到保存预览、Bridge 建表与既有索引检查，以及关联的地区身份、维护和分类路径。没有将尚未证明的 DLC 排序限制或运行时机制列作错误。

## P2：含 NUL 的容量文件仍触发整份禁用，误拦有效容量

位置：`InventorySystemConfigCatalog.cs:43–50`；实际写入入口为 `SaveEditService.QuantityItems.cs:83–87`、`SaveEditService.Trinkets.cs:35–51`。

`ReadCapacity` 只要在任一有效容量文件中发现字节 0，就设置不可恢复的 `unresolved`。这既丢弃 NUL 前已写明的容量，也使其他独立文件的有效容量失效。此处 NUL 指文件实际的 `0x00` 字节，不是文本中的反斜杠和数字零。

例如文件依次包含以下内容，`<NUL>` 表示实际终止符：

```text
inventory_system_config: .type raid .max_slots 2
inventory_system_config: .type trinket_storage .max_slots 2
<NUL>
inventory_system_config: .type raid .max_slots 100
inventory_system_config: .type trinket_storage .max_slots 100
```

游戏读取并保留前面的 2，到 NUL 停止。编辑器的共用 `NativeDarkestReader` 现在也能得到 2，但容量入口在调用共用读取器之前便返回不可用。实际背包物品修改和饰品添加预览都因此报容量无法解析。

### 历史与原生依据

- `29e0a88` 在修复容量解析时加入整份拒绝。当时托管读取可能把 NUL 后的 100 当成最终值，参见[当时的审核记录](obsolete-rule-audit-2026-09-09.md)。这是一项当时有明确理由的保守处理。
- 后续 `27a6c17` 已在 `NativeDarkestReader.cs:26–29` 统一截断 NUL；[后续记录](legacy-compatibility-audit-2026-09-09.md)也明确写明容量仍保留严格拒绝。因此本轮发现的是未清理的旧保护，不是声称之前已经采用了正确的容量截断行为。
- 原生 `0x14028E1F5–0x14028E1F7` 检查当前 NUL，跳到 `0x14028E2D9–0x14028E2E5` 返回结束。容量加载器 `0x1404C83EF–0x1404C8407` 在每条有效记录中更新容量；`0x1404C8444–0x1404C8451` 循环结束时不撤销已更新值。

### 当前程序复现

分别使用原版物理源、本地 Mod 清单源、工坊 Mod 清单源执行以下五种输入，共 15 组；Mod 文件均明确列在 `modfiles.txt` 中：

| 输入 | 原生规则与共用读取器所得容量 | 当前两个容量目录 |
| --- | ---: | --- |
| 正常文件最终赋值 2 | 2 | 2 |
| 有效 2 后跟 NUL，再跟 100 | 2 | 不可用 |
| 第一份赋值 4，第二份以 NUL 开始 | 4 | 不可用 |
| 第一份赋值 4，第二份只有其他库存类型后跟 NUL | 4 | 不可用 |
| 含 NUL 文件之后，另一独立文件重新赋值 6 | 6 | 不可用 |

另用合成二进制 DSON 档案执行四次真实保存准备：正常配置下背包物品与饰品各一次成功；仅追加 NUL 及无效尾部之后，两者分别在容量检查处被拒绝。四次准备前后合成存档哈希均不变。

建议删除这项整份拒绝，让共用读取器消费 NUL 前内容，仍对完整文件字节计算变更指纹；保留有效文件无法读取、类型哈希碰撞、最终容量非正数等独立保护。正式测试中将 NUL 样例一律预期为 `null` 的旧断言也应同步修正。

影响范围是需要这些容量的背包数量编辑和小镇饰品添加；小镇 estate 资源数量编辑不使用这一容量检查。没有证据表明这处问题会造成存档损坏。

## P2：Bridge 把大小写不同的地区 ID 合并，第二个地区无法追加

位置：`ManagedBattleEncounterBridgeService.cs:150–155`、`:448–450`；专用文件名生成在 `BattleEncounterCatalog.RuntimeOrder.cs:96–102`。

目录查询已按原生请求保留地区 ID。`cove` 与 `Cove` 对应不同的文件名匹配条件；Windows 物理目录打开可以忽略目录大小写，并不意味着这两个资源 ID 相同。详见[地区目录规则的既有验证](resource-reference-and-region-fixes-2026-09-11.md)。

但 Bridge 的 manifest 表查找和旧索引校验仍对 `DungeonId` 使用 `OrdinalIgnoreCase`，专用文件路径也按忽略大小写比较。结果如下：

1. 同一档案先在 `cove` 难度 2 追加一个来源为遗迹的组合，成功得到索引 1。
2. 切换到 ID 为 `Cove` 的合法自定义地区，难度仍为 2；当前目录能识别它自己的正常组合。
3. 再追加同一遗迹组合，旧 manifest 的 `cove` 表被当成当前 `Cove` 表使用。
4. `ValidateManifestIndexes` 在当前地区查询中找不到那条旧记录，抛出“托管 Bridge 的已有索引与当前多文件遭遇表不一致，本次不会写入。”

这不是一般性的跨地区失败。hall、room、boss 分别执行同一过程，三个类型都复现上述误拦；另有两个对照：首次在 `MistyGrove` 生成、同档案从 `cove` 切到 `weald` 生成，三个类型全部成功。共完成 15 次 Bridge 调用，12 次对照或首次生成成功、3 次预期误拦；失败前后地图、游戏配置及已安装测试 Bridge 的文件集合和哈希均未改变。

### 为什么不能只改一个比较器

旧比较来自早期 `a5f38b8` 的地区身份假设。当前文件生成器虽保留地区大小写，但会产生：

```text
dungeons/cove/ddse_managed.cove.2.mash.darkest
dungeons/Cove/ddse_managed.Cove.2.mash.darkest
```

这两条物理路径在普通 Windows 文件系统上指向同一个文件。单把 manifest 的地区比较改成 `Ordinal`，仍会在新建文件时冲突。

建议按精确地区 ID 管理表和旧索引，同时为大小写不同的地区生成物理上不同的专用文件名，保留原生查询所需的地区后缀和清单目录拼写。关联维护路径中 `DungeonId.ToLowerInvariant()` 的键也应一并核对；**本轮只证明追加误拦，没有证明这些维护键已误删真实地图，不能据此宣称存在数据损坏。** Windows 文件打开和文件哈希等确需忽略大小写的比较不应被全局替换。

## 验证记录与限制

- [容量目录及 DSON 准备结果](../../workspaces/historical-rule-review23-20260914/capacity-evidence.json)：15 组目录输入、4 次保存准备，均完成预定对照与反例断言。
- [Bridge 结果](../../workspaces/historical-rule-review23-20260914/bridge-evidence.json)：9 个独立生命周期场景、15 次调用，覆盖三个战斗类型，保留失败堆栈和无写入断言。
- [原生证据索引](../../workspaces/historical-rule-review23-20260914/native-evidence.json)：三个函数，其中 LineReader 包含相邻的结束返回片段。
- [最终核对记录](../../workspaces/historical-rule-review23-20260914/verification.json)：提交、结果数量、产物哈希及文档链接检查。
- 初版容量探针误用了不存在的 `SaveProfile.GameSavePath`，编译失败；已改为真实的档案路径后重新执行成功。此失败发生于隔离探针，不是产品构建失败；没有把初次运行计入验证结果。
- 本轮没有产品代码差异，按只读审核任务规则不另行启动完成审查者。上述问题尚待用户授权修复；当前程序仍是提交 `5c8fba6` 对应的版本。
