# 怪癖计数与概率精度修复：2026-09-14

基线为 `11ab3eb`。本轮处理[第十八轮审核](hero-quirk-classification-audit-2026-09-14.md)确认的两项问题：正面疾病漏计正面数量、怪癖概率未按原生单精度解析。

用户进一步明确不需要旧候选的额外处理，因此最终没有增加“旧候选搭配新目录”的 HP 检查，也没有迁移或修复既有候选记录。`SaveEditService.StagecoachHeroes.cs` 与基线一致；现有预览指纹、活动上限与进化倒计时校验保留。

## 最终改动

### 正面疾病同时参与两类数量检查

- `StagecoachHeroCandidateFactory.Quirks.cs`：正面数量由 `is_positive=true` 决定，包含正面疾病；负面仍是非正面且非疾病，疾病仍由 `is_disease=true` 决定。
- `StagecoachHeroCandidateFactory.cs`：候选摘要的正面列表使用相同条件。正面疾病同时出现在正面与疾病摘要中，实际候选 JSON 只保存一个该 ID 的记录。
- `InitialQuirkSelectionDialog.xaml.cs` 与 `.xaml`：选择摘要采用相同计数，并提示正面疾病同时占用两个上限。目录的疾病显示分类保留。
- `MainWindow.Presentation.cs`：主窗口选择摘要也采用双重计数；汇总预览及保存确认按精确 ID 去重，同一正面疾病只显示一次，大小写不同的 ID 保持独立。

例如正面上限 1、疾病上限 1 时，“普通正面 + 正面疾病”会因正面数量 2 被拒绝；只选正面疾病可正常创建。正面上限为 0 或无法确定时，也不能借疾病分类绕过正面检查。

原生依据为固定 x64 build 27890 的三个计数谓词：`0x1404E0D50`（正面）、`0x1404E0D10`（负面且非疾病）、`0x1404E0BF0`（疾病）。原有按记录条数而非 `slot_size` 求和的规则不变。

### 概率按原生 float32 解析

`HeroClassCatalog.QuirkDefinitions.cs` 复用已有 `ReadJsonFloat` 读取 `random_chance`，与原生 `0x1404DE5E7` 的 `cvtsd2ss` 转换一致。

- `1e-50` 变为 0，与显式 `0` 使用相同的特殊怪癖分类与技能线索筛选；不再误标为自然随机。
- `0.2` 等普通值也保留单精度转换后的数值。
- 超出单精度范围的值表示为未确定概率（`null`），不改成 0，也不把无穷大放入目录 JSON，避免目录指纹序列化失败。这类概率不参与已确定的自然随机／运行时特殊怪癖线索判断；显式手动选择仍依照原有可写性规则，因为概率不会写入人物存档。
- 首个精确 JSON 属性匹配、重复怪癖 ID 取最后定义、同路径覆盖、Mod 清单资格和 Effect 合并规则均不变。

概率文件变化仍会被自动刷新指纹捕获；原生解析意义相同的 `0` 与 `1e-50` 得到相同目录指纹，不会无故使原有保存校验失效。

## 测试与文档

新增 `HeroQuirkClassificationContractTests.cs` 和 `HeroQuirkClassificationGuardContractTests.cs`，接入原有 `--quirk-rules`、`--catalogs` 与完整套件。

- 六类来源覆盖本体、mode、DLC feature、本地 Mod、Workshop Mod、启用 DLC 子路径的 Mod。
- 检查正面疾病双重计数、普通类别对照、零／未知上限、摘要列表和只保存一次；18 次临时 town／roster／upgrades DSON 往返。
- 78 个概率场景覆盖零、下溢、普通小数、负数、float/double 溢出、错误类型、字段大小写及重复字段；同时核对技能线索、手动生成与目录指纹。
- 通过真实保存准备／提交入口验证正面疾病上限、原有内容变更保护、概率文件刷新以及等价概率下的成功保存；两组三文件事务成功后重新解码检查。
- `README.md` 和 `docs/content-save-rules.md` 更新对应规则；本文件与审查报告留在独立修改记录目录。

修改前，新计数测试在 `p0 + pd` 应拒绝的断言失败，证明旧实现能绕过上限；记录于 `workspaces/quirk-classification-fixes-20260914/red.log`。移除额外 HP 检查后的最终构建与回归以 `build-scoped.log`、`catalogs-scoped.log` 为准，早期中间构建／定向日志不代表最终范围。

## 验证结果

- 核心修复及范围调整后的 Release 全方案构建成功，0 警告、0 错误；`build-scoped.log`。
- 核心修复及范围调整后的 `--catalogs` 回归 **34 PASS / 0 FAIL / 0 SKIP**，退出 0；`catalogs-scoped.log`。包含新增三组检查以及现有目录、同步、人物、物品饰品和保存契约；没有重新运行独立战斗测试组。此运行早于下述两处界面局部修正，不将它计为界面修正后再次完整运行。
- 独立 JSON 读取复核 18 份 proposed／roundtrip 文档一致，六类来源的正面疾病都只保存一次；两组完整提交产物重新解码后正常。`verify_final.py` 与 `artifact-verification.json` 保存核对结果。
- Core、App、测试输出的 Core DLL 哈希一致，9 份本轮源代码／测试输入哈希已保存于 `review-source-hashes.json`；保存服务文件与基线无差异。
- 最终 Core DLL SHA-256：`4D538B5EB8F812B5E4F2D2A81E611CB155ED6B8A76206FD9A711830CF25E6BD9`。复核开始后再次确认 9 份源代码／测试输入未变化，4 份本轮文档中的 105 处本地引用有效。
- 全新只读审查者 `01a09c1f-7a7e-7ec1-8560-bcb0d543e864` 提出两项显示问题：主窗口的属性模式仍漏计正面疾病（P2），汇总预览直接拼接正面与疾病列表导致同一 ID 重复显示（P3）。未发现任务范围内其他可行动问题；审查者已关闭。

主流程核对调用链，并用引用实际 App/Core DLL 的隔离 UI 探针复现这两项发现：六个显示对照中四例不符、两个空白对照正常，记录于 `ui-red.json`。探针最初误把嵌套的 `HeroRow` 当成顶级类型而启动失败（`ui-before.json`）；修正探针反射定位后才得到上述复现结果，没有更改产品来绕过探针错误。

随后只在 `MainWindow.Presentation.cs` 做两处局部修正：正面计数去除非疾病条件，聚合显示使用 `Distinct(StringComparer.Ordinal)`。最终 Release 全方案构建通过，0 警告、0 错误（`build-reviewed.log`）；实际编译后方法的六个显示用例以及原有 `RunUiContracts` 全部通过（`ui-reviewed.json`）。覆盖正面疾病、混合选择、空白选择、重复显示及大小写独立 ID。探针使用不显示的隔离控件，不启动真实窗口或读取用户档案；它验证方法输出，不构成屏幕视觉或游戏实测。

这属于审查意见的局部修正，未递归启动第二位审查者。原先九份核心／界面／测试输入保持不变，新增核对 `MainWindow.Presentation.cs` 后，最终十份输入记录在 `final-source-hashes.json`；Core DLL 保持上述哈希。所有已确认的本轮问题均已处理。

本轮没有新的游戏实测，也未修改真实游戏、Mod、存档或配置。未增加依赖；HP 公式、旧候选处理、物品饰品数量及地图／Bridge 功能均保持原有实现。
