# 资源目录与人物／怪物注册修复

日期：2026-09-11。对应[第十二轮审核](resource-directory-and-actor-query-audit-2026-09-11.md)，用户已确认修复。本次没有提交 Git；上一提交为 `55e214c`。

## 修改内容

1. **人物／怪物注册使用原生文件名查询。** `NativeResourceFileRules` 区分英雄 `.*\.info.darkest$` 与怪物 `.*\.info\.darkest$`；两者都区分文件名大小写。`ContentFileDiscovery` 增加查询过滤入口，在检查清单文件是否缺失之前应用查询。`NativeContentFileResolver` 统一人物／怪物发现及原生的两次去点后缀 ID 提取。人物目录、饰品职业要求、怪物存在／体型／Boss 读取采用这套发现结果。
2. **Mod 目录查询保留清单原始大小写。** 物品定义、背包及饰品容量、饰品、Buff／怪癖、Effect／露营技能、奇物、嵌套 prop 与既有 Loot／JSON 引用查询按来源选择目录规则。清单的 `Inventory/`、`Shared/buffs/`、`shared/Buffs/` 或大小写错误的 DLC 前缀不会被误当成游戏请求的目录；正确小写清单仍可打开 Windows 上使用大写名称的实体目录。
3. **人物伴生物品与注册保持一致。** 物品引用只采用已经注册的人物／怪物的标准 info/art/override 文件，保持原生记录类型限制。如果另一个有效种子发现了 ID，标准文件的物理目录排除规则不会再错误阻止直接打开。发现失败仍报告扫描不完整，不把访问失败当成无引用。
4. **严格目录筛选先于物理路径去重。** 清单同时列出 `Loot/query.loot.json` 和 `loot/query.loot.json` 时，必须保留能够通过原生目录查询的后者；不能先按 Windows 大小写合并，再把唯一剩下的错误别名排除。物品引用入口先筛选、后去重，两种排列顺序结果一致。

## 可观察的结果

- 唯一的 `alpha_A.info.DARKEST` 不会注册 alpha。两只 alpha 的组合仍占 0，正常 bravo 位于 1，Bridge 下一条为 2；不会根据错误读取的 size 3 把第一条跳过、把 bravo 写成 0。hall／room／boss 都覆盖。
- 仅清单大小写发生变化也会触发刷新；若清单改为 `alpha_A.info.darkest`，Windows 打开同一个实体文件后才会注册 alpha，按已确认的体型规则重新计数。旧战斗选择不能绕过写入前检查。
- 基础 HP 20、有效 +25% Buff 时生成并保存 HP 25；清单错误目录中的 +75% 不再把它误改成 35。正确清单／物理别名的 +75% 仍得到 35。
- 有效堆叠上限 2、背包容量 4 时，8 件物品实际写成四堆 2，40 件被拒绝。错误目录里的上限 9／容量 99 不再允许越界写入。

## 保持不变与验证边界

仍然通过 `modfiles.txt` 读取活动 Mod；没有恢复无清单扫描，也没有改写用户的清单、Mod、地图或存档。Base／模式／官方 DLC 保持 Windows 目录规则。标准 actor 文件、根级 prop 文件和 roster.variables 的直接打开没有一律改成大小写敏感；宽范围诊断和刷新指纹也没有被缩窄。没有更改同路径覆盖优先级、物品／饰品／Buff／怪癖的重复 ID 规则、空组合占号规则及已知超体型行跳过规则。

依据为固定 Windows x64 build 27890 的原生查询与上一轮隔离反例；本轮运行产品契约和真实 DSON 编码／解码，没有进行新的游戏实战。此前 `_template` 命名干扰的两个探索样例仍保留为未采信证据；新增通配点测试使用不含该排除标记的独立种子和大写标准文件。

## 初次验证（独立复核前）

日志集中在 `workspaces/resource-query-fixes-20260911/`。初次测试中修正了新夹具误用“预期失败”帮助函数、假定缺失奇物仍有目录条目，以及配给 JSON 键名三个测试问题；未掩盖失败日志。

- 定向查询契约：`queries-third.log`，4 组 PASS，包含物品／HP DSON、六种来源、三种战斗类型及 Bridge 安装／替换／删除。后续增加 DLC 前缀与子目录大小写诊断案例，由完整套件验证。
- Release 串行构建：`build-final.log`，退出码 0，0 警告、0 错误。
- 首次完整契约：`full-contracts.log`，退出码 0，43 组 PASS、0 FAIL、0 SKIP；产物位于 `workspaces/contract_tests/20260911_020446_387_0107597fb4344c67b905f3fd78c3322b`。当时新增 63 组目录／注册／引用对照，额外增加三种战斗类型的未注册怪物后续 Direct／Bridge 创建、替换、删除事务。
- 首次验证时 Core、App 和测试输出内的 Core DLL SHA-256 一致：`3487B7D80BF80100015ABB2D4D806CF50813C59C9449F76075510C3ED76BB1EE`。此哈希对应去重顺序补充修复前的版本。

## 独立复核

使用一名全新上下文的只读审查员检查本轮产品差异、未跟踪测试及原生证据，发现一项 P2：物品引用在严格目录判断前已经忽略大小写去重，错误别名先出现时会丢掉合法记录。主执行者补充实际目录加载契约，确认 `local/loot/query.loot.json` 在错误大小写条目排在前面时失败；见 `reviewer-alias-red.log`。

已将两处提前去重移除，统一在合法性筛选后去重，并新增 24 组本地／Workshop／DLC 清单顺序对照，覆盖 Loot、Curio、配给 JSON 与 DLC 前缀。审查员没有报告其他阻断项。该局部修正按协作规则不再触发递归复核；修正后重新执行构建和完整回归，全部通过。

## 最终验证（包含复核修正）

- `dotnet build DarkestDungeonSaveEditor.sln -c Release --no-restore -m:1`：退出码 0，0 警告、0 错误；见 `build-reviewed.log`。
- `dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests -c Release --no-build -- .`：退出码 0，43 组 PASS、0 FAIL、0 SKIP；见 `full-reviewed-contracts.log`。产物为 `workspaces/contract_tests/20260911_021827_582_8c9658471d35416092364292970340d3`，包含本轮合计 87 组新增目录／注册／引用对照及新增的三类战斗事务。
- Core、App 和正式测试输出的 Core DLL SHA-256 一致：`9E05EE0A2B7996C7595649870CABF41C427332720A7102B03D0F69DEEF055D11`。
- `verify_completion.py`／`completion-verification.json` 核对最终日志、三处产物、报告链接及 `git diff --check`。失败复现日志和首次验证结果均保留，没有用最终成功结果覆盖失败记录。
- 代码、测试和有效规则文档已更新；没有新的实际游戏运行验证，没有操作真实存档／Mod，也没有提交本轮修改。
