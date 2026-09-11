# 人物成长资源筛选与 DLC 经验配置修复：2026-09-11

用户确认修复[第十三轮审核](hero-progression-resource-audit-2026-09-11.md)的两项问题。本轮以 `cd4a3ac` 为基线，只修改相关资源读取、回归测试和文档，未提交 Git。开始时已有的审查报告及索引条目保留。

## 改动与行为

- `NativeResourceFileRules.cs` 增加共用 `IsUpgradeFile` 谓词，沿用既有的大小写敏感、两个字面点的文件名查询。人物升级树定义和升级资源引用使用同一谓词。
- `HeroClassCatalog.SourceDiscovery.cs` 的 Base／模式／官方 DLC 物理发现和 Mod 清单发现均接入该谓词。清单里的 `.upgrades.JSON`、`.UPGRADES.json` 不能再修改人物装备和 HP；清单小写而磁盘文件名大写的有效别名继续可读。先过滤再去重，错误别名不能排挤其后的正确记录。
- `HeroClassCatalog.cs` 将启用的 DLC 前缀传给经验表读取；`HeroClassCatalog.Progression.cs` 对有效文件的挂载后路径进行标准路径比较。有效 DLC 经验表可以提供等级及 XP，实际读文件和诊断仍保留原始来源路径。
- `HeroProgressionResourceContractTests.cs` 新增行为回归，`TextResourceQueryContractTests.cs` 将其接入定向查询和完整合同测试。两份有效规则文档同步更新，修改历史集中保存在本目录。

影响的流程为：重载目录、人物等级选项、生成预检、生成的 XP／武器与护甲等级／初始 HP，以及对应的升级购买记录。原样例的无效大写升级文件现在被排除，1 级人物恢复护甲 1 级、HP 40 和购买码 `0`；有效 DLC 经验表 `[0,7,14,21,28,35,42]` 能生成 1 级 XP 7、6 级 XP 42 的人物。

同路径 Mod 优先级、树 ID 最后匹配、整树替换、技能隐式升级规则及异常模板／哈希保护保持原样。经验表仍须匹配挂载后的标准根路径；不读取 `notes/roster.variables.json`、未列入清单或位于未启用 DLC 内的文件。异常胜出经验表仍只允许 0 级模板，不会回退暴露较低优先级的正常表。

没有修改游戏安装、真实 Mod、真实档案或已有英雄；没有新增依赖、改变存档格式或修改战斗编号与 Bridge 行为。

## 回归覆盖

- **48 组升级树文件查询**：六种来源分别覆盖标准文件、两种大写后缀、两处非字面点、物理大小写别名、大写目录、错误清单别名在正确别名之前。另覆盖未列入清单的无效 JSON 不能参与解析，以及三种 Mod 来源修正清单后的内容指纹与目录重载。
- **18 组经验配置查询**：六种来源分别覆盖标准根路径、嵌套同名文件、物理大小写别名；核对七个等级的可生成状态，以及 1 级和 6 级的实际 XP。
- DLC 经验表额外覆盖未列入清单、未启用挂载、根 Mod 覆盖、四类异常阈值及同文件修复后的重载。异常报告必须指向实际胜出文件，不得误报根文件不存在。
- 七组人物生成执行实际 `StagecoachHeroSaveEditor.AddCandidate`，分别往返 town、roster 和 upgrades，合计 **21 次 DSON 编码／解码往返**。全文件比较之外，显式核对 HP、XP、护甲等级和按原生树哈希保存的护甲购买码。

## 验证过程

日志保存在 Git 忽略的 `workspaces/hero-progression-resource-fixes-20260911/`。

1. `red-queries.log`：新测试先在未修复的 Core 上运行，按预期失败于 `base/upper-extension` 的护甲／HP／购买记录断言。
2. `queries.log`、`queries-diagnostic.log`：升级树 48 组通过；经验表物理大小写别名对照因测试要求所有诊断为空而失败。采集值确认经验阈值为正确的 `[0,7,14,21,28,35,42]`，仅存在既有的“挂载路径仅大小写不同”诊断。测试改为仅允许该明确诊断，继续检查全部等级、XP 和实际生成；没有关闭或改写产品诊断来规避测试。
3. `build.log`：Release 串行构建通过，0 警告、0 错误。
4. `queries-final.log`：定向查询测试退出码 0，6 组 PASS，无 FAIL／SKIP；包含本轮 66 组文件／来源矩阵及补充保护、刷新和 DSON 验证。
5. `full-contracts.log`：完整合同测试退出码 0，**45 组 PASS、0 FAIL、0 SKIP**。产物为 `workspaces/contract_tests/20260911_030324_269_f2920537a975440cbeb074cd98b037ac`，同时覆盖物品／饰品数量、人物／怪癖、三类战斗 Direct／Bridge、地图操作、自动维护、同步、强制返回和写入回滚。
6. `verify_completion.py`／`completion-verification.json` 核对完整／定向日志、三处 DLL 哈希、48＋18 组矩阵目录、21 份 DSON 往返结果、报告链接及 `git diff --check`，全部通过。

Core、App 和正式测试输出的 Core DLL 已核对一致：`8ED90F1A216BDABAACBDB33E29B7EC9DC8D6D20134FD1B99AD1CD59D3918CDE8`。

本轮没有重新启动游戏进行实机验证；原生依据沿用审核中固定的 Windows x64 build 27890。

## 独立复核

完成上述验证后，将本轮最终差异、原始需求、验收条件与验证记录交给一名全新上下文的只读审查员。审查员未发现有证据的实质问题，确认升级树清单／物理目录区别、有效别名、筛选与去重顺序，以及经验表挂载路径、来源和异常保护符合验收条件。

审查员只读核对了 45 PASS／0 FAIL／0 SKIP 日志、三处 DLL 哈希和 21 份 DSON 产物，没有重跑完整套件或进行实机验证，没有修改文件。主执行者随后确认产品与测试文件仍是送审版本，仅补录本节结论；未产生需要修正或再次复核的发现。原始结论保存在忽略工作区的 `review-result.md`。
