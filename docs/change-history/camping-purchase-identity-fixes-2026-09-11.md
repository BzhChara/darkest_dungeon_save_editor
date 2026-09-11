# 露营技能购买编号修复

日期：2026-09-11。基于 `6e7cdb9`，对应[第十四轮审核](camping-purchase-identity-audit-2026-09-11.md)。用户已确认实施。

## 修改结果

此前总长超过 63 个 UTF-8 字节的 `<职业 ID>.<露营技能 ID>` 会按完整字符串写入购买编号，无法对应游戏实际查询的目标。现在先按原生缓冲区取前 63 字节，再由既有购买写入器计算 hash。保留候选人物完整的技能 ID，购买码仍为原生解锁／拥有检查使用的 `0`。

实际产品修改集中在 [StagecoachHeroCandidateFactory.Progression.cs](../../src/DarkestDungeonSaveEditor.Core/StagecoachHeroCandidateFactory.Progression.cs)：

- 新增露营购买目标构造方法，只有这类目标采用 63 字节边界。
- 两个不同技能截断到同一目标时明确拒绝；购买计划中不同目标出现相同 native hash 时也在预检拒绝。保存端原有的碰撞与重复购买检查继续保留。
- 对 NUL 标识或截断切开 UTF-8 字符的情况给出具体限制原因，不用替代字符计算错误编号。同一技能的重复声明／引用仍生成一条购买记录。

目录可用性、人物生成和保存预览都复用该购买计划，因此限制会在生成前显示。修正对应资源后，内容指纹变化及目录重载能够移除先前限制。没有更改通用 `HashName`、`HeroUpgradePurchase` 数据合同、DSON 格式、战斗／装备购买规则或当前技能选择数量；存档编辑服务继续对最新目录重新生成并核对计划。

这次没有重写既有档案中的购买记录，没有修改真实存档或 Mod，也没有启动游戏。

## 验证

- 先增加回归测试，旧实现明确失败于 `base/ascii-64` 的购买目标断言，确认测试能抓到原问题。
- Release 解决方案构建：**0 警告、0 错误**。
- `--semantics`：**14 PASS / 0 FAIL / 0 SKIP**。
- 完整回归：**46 PASS / 0 FAIL / 0 SKIP**，进程退出 0。覆盖物品／饰品编辑、人物生成、战斗与 Bridge、地图操作、自动维护及保存事务等既有流程。

新增 [CampingPurchaseIdentityContractTests.cs](../../tests/DarkestDungeonSaveEditor.ContractTests/CampingPurchaseIdentityContractTests.cs)，接入完整人物测试和 `--semantics`：

1. **36 组来源／边界对照**：Base、模式、单个官方 DLC、本地 Mod、工坊 Mod、DLC 前缀内的 Mod，各覆盖短 ID、ASCII 总长 63／64 字节、UTF-8 总长 63／66 字节和定义中 code `A`。检查目录可用性、完整技能 ID、原生购买目标及其他购买项。Mod 的未列清单文件继续排除。
2. **5 组保护／重复声明场景**：UTF-8 断字符、不同技能同一截断目标、截断后 hash 碰撞、NUL、同一技能重复声明。前四组检查所有等级预检和实际生成同样拒绝，并在修正定义、保持清单不变后重新变为可用。
3. 本地 Mod 的 6 组分别对 town、roster、upgrades 做实际编码／解码，合计 **18 次 DSON 往返**。新人物仍保留完整露营技能 ID，升级文件包含正确目标 hash；原来的超长完整字符串 hash 不再写入。HP、XP、护甲、战斗技能购买保持正确。
4. 独立验证脚本从最终 DSON 产物取出购买记录，并用本机 `__stdio_common_vsnprintf_s` 的 64 字节格式化过程重新计算原生目标，6 组比较一致。此检查不调用产品截断函数，也不操作游戏进程。

证据位于 [本轮验证工作区](../../workspaces/camping-purchase-identity-fixes-20260911/)：`red.log`、`build.log`、`semantics.log`、`full-contracts.log`、`verify_fix.py`、`semantics-verification.json`、`full-contracts-verification.json`。完整回归产物为 `workspaces/contract_tests/20260911_044803_765_c65b23de66444823a520576cfd557fae`；针对性执行和完整执行各覆盖上述 36+5 场景、18 次本功能 DSON 往返，统计未重复累加。工作区文件不随 Git 分发；正式测试代码随项目保留。

Core、App 和测试输出的 Core DLL 哈希一致：`D563B6BC6572D7A29A310421C22C047C9CC04969F2D1BB5D2FB285D8BE3FC505`，与审核前的旧 DLL 不同。没有出现构建或测试被旧 DLL 占用的问题。

## 边界与复核

原生证据仍为 Windows x64 build 27890。截断切开 UTF-8、NUL 和歧义目标目前明确不支持；这些是编辑器的支持边界，不代表游戏对所有相同输入一定报错。没有扩展为“所有资源都截到 63 字节”，也没有声称验证了所有过长职业 ID、露营训练界面或实机战斗行为。

完成一次全新上下文的独立只读复核，未发现可复现的实质问题。复核者检查产品最终差异、新测试与钩子、文档、四份原生证据，以及目录预检、生成、预览重算和保存提交校验；核对了输入快照和三处 DLL 哈希、构建／测试日志。额外的 12 项内存边界探针、6 组 UCRT 目标 hash 对照和 18 份往返 JSON 比较通过；这些独立检查未重复计入正式套件的 46 项摘要。

独立探针首次因异常包装层读取方式误报，修正探针后通过；不是产品失败。复核者未修改文件、未重跑全套或启动游戏，没有额外验证真实档案的提交或所有超长职业 ID。本轮主代理最终核对源码与正式测试仍匹配送审快照。复核结果保存在验证工作区的 `review-result.md`。
