# 饰品、地图 JSON 与资源文件查询修复：2026-09-10

本次在 `c14247a` 上修复[第九轮审核](resource-json-query-audit-2026-09-10.md)确认的三项问题。修改产品解析入口和相应契约测试，更新规则文档；没有新增依赖，没有修改真实存档、活动 Mod 或游戏配置。当前变更尚未提交。

## 实现范围

| 文件 | 修改目的 |
| --- | --- |
| `TrinketCatalog.cs` | 根数组、ID、职业要求、普通字段、实例计数和来源说明统一使用 `NativeJsonReader` 的首成员查找；文件发现使用原生饰品查询 |
| `BattleRoomAttachmentCatalog.PropLibrary.cs` | 将资源解析改为 `JsonDocument` / `JsonElement`，避免重复成员触发唯一键字典异常；所有相关层级使用首成员查找 |
| `NativeResourceFileRules.cs` | 集中定义饰品、Buff、怪癖、露营技能的查询，以及地图道具根路径／嵌套搜索的阶段分类 |
| `HeroClassCatalog.SourceDiscovery.cs` | 基础目录及 Mod 清单分类采用上述查询，删除这些资源的旧固定后缀分支 |
| `BattleRoomAttachmentCatalog.Resources.cs` | 文件纳入和加载阶段排序共用分类，保持根 prop→obstacle→trap、嵌套 prop→trap→obstacle 的次序 |
| `BattleRoomAttachmentCatalog.cs` | 为现有枚举 helper 增加可选消费过滤，在判断缺失文件之前排除未消费路径；其他调用沿用原行为 |

### 饰品重复字段

同一对象中 `quest_uses: 2` 后接 `quest_uses: 7`，现在读取并保存 2；`trigger_limit: 3` 后接 8，读取并保存 3。这包括实际实例创建及 DSON 往返。无效的首计数继续禁止生成，后面的有效字段不能替代它。普通数值字段先检查类型，不再因首字段非数字而中断同文件后续饰品读取。

根数组、ID、职业要求和来源说明也采用同一规则，避免只纠正计数却仍选错条目或虚报原版来源。多条资源定义出现同一饰品 ID 时，仍选择首条完整定义；没有改为拼接字段，也没有改变既有数量上限提示策略。

### 地图道具重复字段

读取时保留 JSON 成员序列，按首次匹配访问，不再把重复成员插入唯一键字典。覆盖文件和条目的 `default_data`、`props`、`name`、`inherits_from.prop_type_name`、普通属性、脚本布尔值、`difficulty_variations` 和 `level`。

`instance_type: "trap"` 后接 `"obstacle"` 的反例现在作为陷阱正常进入目录。首字段类型错误仍按对应元数据／结构规则拒绝，不能借后续重复成员绕过检查。已加载父资源复制、难度版本追加与查询、脚本排除和地图写入保护均保留。

### 文件查询

编辑器按每类已确认的原生查询识别文件，不再用统一的字面后缀替代。`raid/camping/camping_skills.json` 和原生允许的 `*.buffsXjson` 等名称现在进入目录。地图嵌套查询使用相同分类排序，父资源阶段不会因文件名排序而落在子资源之后。

仍保留两类差别：根 `props/prop_definitions.json` 等三条路径是精确打开，不能将根 `trap_definitionsXjson` 当作嵌套查询结果；升级树仍要求字面 `.upgrades.json`。Mod 清单、停用 DLC 排除、同路径上方 Mod 优先、Buff 最后完整定义覆盖等规则均保留。

缺失文件诊断在消费过滤后进行。刷新指纹原本已覆盖有关目录的 `*json`；新增回归确认它能够感知这些新纳入文件的变化，并使旧地图选项失效。没有新增另一套自动同步逻辑。

## 验证

三个新增测试模块分别为 `TrinketJsonMemberContractTests.cs`、`CatalogFileQueryContractTests.cs`、`MapPropJsonContractTests.cs`，已接入原有目录／人物／地图测试入口，并登记到测试 README。

- 饰品：重复根数组／ID／要求／普通字段／计数、错误首值、同 ID 定义仍取首条、被覆盖来源归属、两份实例的 DSON 往返。
- 文件查询：原版、模式、DLC、本地 Mod、创意工坊 Mod、Mod 内 DLC 路径六类来源；合法及未消费文件、未列清单与停用 DLC、同路径覆盖、缺失文件诊断、相同文件大小和时间戳下的内容刷新。
- 人物：由新纳入的最后一份 Buff 将 +25% 改为 +75%，基础 HP 20 的真实生成候选为 35，写入小镇 DSON 后仍为 35。
- 地图：重复成员不崩溃、错误首值、继承和难度变体、根／嵌套加载次序、缺失文件提示、资源改变后旧选择拒绝以及刷新后重新选择可用。

| 检查 | 结果 | 日志 |
| --- | --- | --- |
| Release 解决方案构建 | 成功，0 警告、0 错误 | `fix-test-build.log` |
| `--catalogs` | exit 0，19 条 PASS 汇总，0 FAIL、0 SKIP | `fix-catalogs.log` |
| `--map-content` | exit 0，4 条 PASS 汇总，0 FAIL、0 SKIP | `fix-map-content.log` |
| 完整套件 | exit 0，39 条 PASS 汇总，0 FAIL、0 SKIP | `fix-full-contracts.log` |
| 差异及文档链接 | `git diff --check` 通过；相关文档本地链接可解析 | 最终检查 |

日志和[机器可读测试汇总](../../workspaces/historical-rule-review9-20260910/fix-checks.json)位于 `workspaces/historical-rule-review9-20260910/`。完整套件产物位于 `workspaces/contract_tests/20260910_092733_159_60295bd4083e4470a6a7ec18fcafbf9a/`。以上计数是测试输出的 PASS 汇总行数，不是将每个断言或每个矩阵场景分别计数。

首次默认构建被 MSBuild 取消，日志为 `fix-build-initial.log`，不计为通过。随后采用仓库建议的单进程 `-m:1` 并关闭共享编译，构建和全部验证成功。没有跳过失败测试，也没有为此调整项目配置。

本次测试的 Core SHA-256 为 `7AE71991B48FC228C13860C04EAA74BF7B71E431C2848B90C689183B85834206`，应用 Release 输出目录中的 Core 与之相同。完整套件同时覆盖了此前庭院子目录、Bridge 新建／替换／删除、维护、回滚、自动同步和强制回城路径。

## 复核与边界

已完成一次独立、重新提供任务背景的只读复核（Sagan，`01a08aa7-2f47-76c0-b9b6-22d4c1358ea9`），无实质发现。复核覆盖产品差异、三个新增正式测试、规则文档及相关 helper／来源解析／保存／刷新调用链，并核对了两份饰品实例的 2/3 计数与人物 HP 35 的实际 DSON 往返产物。复核没有修改文件或重复启动测试；目前没有该次复核遗留的待处理项。

当前依据为固定游戏版本的原生静态证据和隔离执行验证；本轮没有启动游戏进行新的实机对照，没有声称完整模拟所有资源效果或脚本。界面布局、存档字段格式、战斗编号算法和真实存档内容未作修改。有效规则已补入 `docs/resource-duplicate-semantics.md` 第 16 节，并从物品／饰品规则和地图设计文档交叉引用。
