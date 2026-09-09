# 内容读取兼容盘点（2026-09-06）

## 状态与范围

本报告保留兼容盘点和后续决定。2026-09-06 用户已确认删除 **A1 清单外 XML 补读**与 **A2 错误 XML 恢复**，并同时增加受清单约束的旧 `.loc` 读取。A1/A2 下方的机制说明是删除前记录，不再代表当前行为。其他 A3/A4/A5、B/C 类规则未随之删除。

随后用户确认将“结构错误拒绝整表、可定位的单条文本错误仅跳过该条”应用到 LOC、LOC2 和 XML。该规则不恢复 A1/A2：XML 编码或语法损坏仍拒绝整份，仅完整解析后可明确定位的无效项单独跳过。当前实现与验证见本文第三阶段。

范围是内容发现、本地化、人物依赖解析及物品引用分析。存档二进制格式、地图状态、备份恢复、UI 和文件监视等不是本次“Mod 读取兼容”的删除候选。

关键区分：支持游戏已有文件格式、兼容不同字段/目录写法、绕过主清单补读文件、从损坏数据中恢复文本，不是同一件事。名称能够显示，也不能证明对应业务定义会被游戏加载。

## 是否专门去 old 文件或目录寻找资源

**没有找到“正常位置读不到，就专门搜索 old/backup/unused”的补救分支，也没有为 Ruler、Eos_Nyx、Rurutia 等 ID 写死的读旧目录特判。**

但以下行为确实可能读到名字带 `old` 的文件：

1. 清单明确列出的路径只要通过目录、类型、DLC 和存在性等检查，就会成为对应资源候选；名字含 `old` 不是排除依据。
2. 无清单扫描对标准内容目录递归，因此 `inventory/old/example.inventory.items.darkest`、`heroes/backup/...info.darkest` 等仍可能被发现。它不是回退找旧文件，而是普通递归覆盖到了子目录。不能把“排除 Mod 根目录旁的 project-backup”误解成“排除任意深度的 backup 子目录”。
3. 本地化 XML 在无清单分支递归读取，在有清单分支接受符合类型的已列 XML，因此也可能读到 `localization/unused/*.string_table.xml`。这与既有文档“不递归 unused”的笼统表述有差异；该差异本轮仍未修改，等待用户决定。
4. **已删除的历史行为：** 清单外 XML 补读只取有效根的 `localization` 直接子文件，不递归其 `old` 子目录。现在有清单时不再补读这些文件。
5. `.string_table.xml.unused`、`.loc.unused`、`.loc2.unused` 不是受支持的最终扩展名。LOC/LOC2 只取有效本地化根的直接文件，不递归其旧目录。

删除“清单外 XML 补读”并不能自动消除第 1、2、3 项。若要按目录名字再过滤，那是另一项资源有效性策略，需要单独决定，不能假定目录名证明文件废弃。

## A. 会放宽名称读取的兼容

### A1. 清单外顶层 XML 补读（已删除，以下为历史机制）

- **触发：** Mod 有 `modfiles.txt`，但有效根的 `localization/*.string_table.xml` 没有列入清单。
- **行为：** 在 Mod 根、已启用 DLC 包/feature 根下补读这些直接 XML。并非只在发现旧 `.loc` 时才触发：代码没有这个前置判断。
- **实际例子：** Ruler（工坊 `1596685165`）清单列出 `1596685165_english.loc`、`1596685165_schinese.loc`，没有列出 `localization/JoanofArc.string_table.xml`。删除前双语名称 `Ruler / Ruler` 来自该顶层 XML。现在直接读取清单内的两个 LOC，也能得到同样名称。
- **影响：** 只提供已请求 ID 的名字，不会把清单外的英雄、怪癖或饰品定义变成可生成内容。同一来源有效 LOC2 的已提供名称优先于 XML。
- **风险：** 作者遗留的 XML 可能与编译表或游戏实际显示不一致；没有 LOC2 对应条目时，旧 XML 可能填上游戏未提供的名称。
- **当前结果：** 没有其他有效来源的语言显示 `—`；Ruler 已有清单内 LOC 名称，不需要此兼容。不能只归因于 Mod 不规范：此前编辑器缺少这种二进制格式的读取能力。定义和生成资格不因此自动消失。
- **位置：** [ContentLocalizationCatalog.cs](../../src/DarkestDungeonSaveEditor.Core/ContentLocalizationCatalog.cs)，`EnumerateLocalizationFiles`；`AddDirectAuthoringStringTables` 已删除。

### A2. 不规范 XML 的两级恢复解析（已删除，以下为历史机制）

- **触发：** 正常 XML 解析抛出 `XmlException`。
- **第一级：** 重新读取文本，去掉 XML 声明、注释和部分非法控制字符，再尝试作为 XML 解析；该文本读取采用容错 UTF-8 解码。
- **第二级：** 如果仍不能解析，用正则从 `<language>`、`<entry>` 片段抽取键和值，处理 CDATA 或实体编码。条目正则并不要求严格匹配 `</entry>`，所以可能接受错误闭合标签。
- **影响：** 所有共享本地化目录的显示名，以及从 XML 读取的随机人物姓名池。
- **风险：** 恢复出的文本不等于游戏必然能解析同一份 XML；损坏结构可能使文本边界或归属不可靠。
- **当前结果：** 两级均删除。格式错误的 XML 产生读取提示，该文件不贡献任何译文或随机姓名；有效 XML、LOC、LOC2 不受影响。
- **位置：** [ContentLocalizationCatalog.cs](../../src/DarkestDungeonSaveEditor.Core/ContentLocalizationCatalog.cs)，`ReadLanguageEntries`；`LoadDocument`、`ReadSanitizedText` 已删除。`Contract Lenient` 等样本改为验证严格拒绝与正常文件隔离。

### A3. 名称键的下划线变体

- **行为：** 饰品同时查询 `str_inventory_title_trinket<ID>` 和 `str_inventory_title_trinket_<ID>`；物品也查询类型和 ID 直接拼接或加下划线的两个键。每种语言先取主键，缺少时才取变体键。
- **边界：** 只查已选文件里的精确候选键，不新增文件路径、不模糊匹配、不用另一种语言冒充缺失翻译。
- **删除后：** 只使用另一种键写法的名称可能变空；资源定义仍然存在。
- **位置：** [ContentLocalizationCatalog.cs](../../src/DarkestDungeonSaveEditor.Core/ContentLocalizationCatalog.cs)，`GetTrinketName`、`GetInventoryItemKeys`。

### A4. 仅存档钱包条目的名称推断

- **触发：** 钱包中存在某种货币，但当前内容没有对应定义，即 `IsSaveOnly` 且存放位置为钱包。
- **行为：** 除按钱包类型查询，还用 `heirloom` 类型和该持久化类型作为 ID 查询名称。
- **理由：** 钱包持久化结构并不总保留普通物品定义中的完整 `(type, id)` 形态。
- **删除后：** 这类已有货币可能只剩内部 ID/空名称；数量、场景和存档条目不会因此改变。
- **位置：** [QuantityItemCatalog.Localization.cs](../../src/DarkestDungeonSaveEditor.Core/QuantityItemCatalog.Localization.cs)。

### A5. 随机人物姓名池的语言选择

- **行为：** XML 的 `hero_name_*` 按语言分组，优先英文，没有英文组时采用首个可用语言组。
- **边界：** 只决定随机生成的个人姓名。人物职业、怪癖、饰品、物品的中英文字段仍不会跨语言强行填充。
- **删除后：** 不含英文姓名组的姓名文件不再贡献随机姓名。
- **位置：** [ContentLocalizationCatalog.cs](../../src/DarkestDungeonSaveEditor.Core/ContentLocalizationCatalog.cs)，`ReadHeroNames`。

## B. 不是“找旧文件”的格式和目录支持

### B1. LOC/LOC2 编译本地化及色码处理

- 初次盘点时只读取 XML 和 `.loc2`，当时 Ruler 的“旧 `.loc` 兼容”实际是 A1 的 XML 补读。后经用户确认增加 `LocLocalizationReader`，按旧格式的两偏移表头、直接哈希值组和字符串表解析；不将 LOC 文件当成 LOC2，不恢复清单外扫描。
- 同一来源、同一语言按 `.loc2 > .loc > 有效 XML` 选择非空名称，来源之间仍按原优先级覆盖。未提供的另一语言保持空白。Ruler 的两个清单内 LOC 都写着 `Ruler`，这不是编辑器把英文填进中文。
- Eos_Nyx 的十二件饰品名称来自 LOC2 支持；Rurutia 相关修复包括正确剥离编译色码。色码的开始/结束可能跨条目，不能要求每条字符串内成对出现。
- LOC/LOC2 表偏移、记录长度、索引、所有值的 NUL 结尾与去掉色码后的 UTF-8 均校验。结构或边界损坏拒绝整表；边界有效但 UTF-8/编译色码无效时仅跳过该值，保留同表其他有效名称，按文件汇总计数与最多三个示例，不用宽松解码吞掉损坏。读取器只使用哈希记录查名称，不利用 bucket 索引；不声称能发现未使用的 bucket/metadata 字段错误或支持所有未知旧格式。
- 移除此项会损失正常编译本地化，不只是停止兼容不规范 Mod。Rurutia 第九件饰品缺英文仍保持缺失，没有自动编译、翻译或跨语言填补。
- **位置：** [LocLocalizationReader.cs](../../src/DarkestDungeonSaveEditor.Core/LocLocalizationReader.cs)、[Loc2LocalizationReader.cs](../../src/DarkestDungeonSaveEditor.Core/Loc2LocalizationReader.cs)、[RealModLocalizationContractTests.cs](../../tests/DarkestDungeonSaveEditor.ContractTests/RealModLocalizationContractTests.cs)。格式与样本哈希见[规则文档 §3.1](../content-save-rules.md#31-legacy-loc-evidence-and-implementation-boundary)。

### B2. 人物升级文件的两种目录位置

- 支持 `upgrades/heroes/.../*.upgrades.json`，同时支持 `upgrades/*.upgrades.json` 的直接文件；有效 DLC 根也应用这两种位置。
- 有清单时仍须列在清单内，没有新增清单外升级文件搜索。
- Ruler 的清单本身就列着 `upgrades/JoanofArc.upgrades.json`。这是目录布局支持，不是旧文件恢复。
- 删除顶层支持可能使该类英雄丢失技能/装备升级规则；没有完整升级依据时会受现有等级或生成安全规则限制。
- **位置：** [HeroClassCatalog.SourceDiscovery.cs](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.SourceDiscovery.cs)，`IsHeroUpgradeManifestPath`、`EnumerateHeroUpgradeFiles`。

### B3. 注释、尾逗号和清单省略长度

- 多处游戏 JSON 读取允许注释和尾逗号；`.darkest` 使用对应文本语法解析。这是已有数据格式支持，不读取额外目录。
- 清单解析保留纯路径行，同时接受末尾字节数。此次修复只解决扩展名片段误截断，不要求所有历史清单必须带长度。
- 去掉这些语法支持可能拒绝现有文件，不能仅据其不符合严格 JSON/上传器完整行格式就认定为废弃内容。
- **位置：** [HeroClassCatalog.Progression.cs](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.Progression.cs)、[ModManifestPath.cs](../../src/DarkestDungeonSaveEditor.Core/ModManifestPath.cs) 等相应解析入口。

### B4. 无清单、DLC、Mod 来源识别

> 历史状态说明（2026-09-09）：本节的无清单资源读取已被后续清单生成/严格清单读取取代；下文 C1 的升级模板“最优匹配消歧”也已改成原生的逐树 ID 最后定义规则。请以 [当前规则](../content-save-rules.md) 为准，不应按本报告恢复旧策略。进一步清理见 [第二轮细查](legacy-compatibility-audit-2026-09-09.md)。

- 无清单标准目录递归、已启用 DLC 条件、Mod 优先级、按 `project.xml/Title` 匹配本地 Mod，是已经确认的主读取规则，不是某个 Mod 的特殊补丁。
- 本地 Mod 的项目发现支持默认 mods、游戏 dlc 和用户额外目录，递归发现项目并避免把项目内部嵌套项目当成另一个独立来源。同名多候选不会随意选一个。
- 不能因为删除 A1/A2 就顺带去掉这些规则。扫描盘点发现清单外业务文件，也不会自动将它们纳入生成目录。
- **位置：** [ActiveContentResolver.cs](../../src/DarkestDungeonSaveEditor.Core/ActiveContentResolver.cs)、[ContentFileOverlay.cs](../../src/DarkestDungeonSaveEditor.Core/ContentFileOverlay.cs)。

## C. 会影响业务判断的回退，不应与翻译兼容一起删除

### C1. 人物升级模板消歧和单级技能处理

- 同一有效优先级出现多个升级模板时，依据当前英雄技能 ID 和装备 rank 校验匹配度；只有唯一、无硬性失败的最优模板才会被选择，无法消歧时保持未确定。
- 对明确只有 level 0 的技能，允许其没有多级升级树，并生成已有规则验证的 code `0` 基础购买记录。不是为缺少任意升级树的技能臆造整套等级。
- 若高等级资料缺失但 level 0 的基础 HP 等条件仍有效，可保留可证明的 0 级模板；高等级不会因此被强行放行。
- 这些行为影响技能解锁和生成资格，不只是名称。删除需要单独审核人物功能，不能当作“删旧文件兼容”的顺带动作。
- **位置：** [HeroClassCatalog.Progression.cs](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.Progression.cs)、[StagecoachHeroCandidateFactory.Progression.cs](../../src/DarkestDungeonSaveEditor.Core/StagecoachHeroCandidateFactory.Progression.cs)、[HeroCatalogContractTests.cs](../../tests/DarkestDungeonSaveEditor.ContractTests/HeroCatalogContractTests.cs)。

### C2. 物品引用分析不完整时避免误隐藏

- 活动引用/掉落文件无法完整解析时，记录分析不完整；必要时从文本匹配精确物品身份来保留不确定证据，而不是把它声称为已确认的正常掉落链。
- 当前 `scanComplete` 为假会让没有明确引用的 Mod 定义保持“分析不完整”，不判为确定的疑似未使用。这可能让更多已有定义可见，但不凭空创建定义。
- 存档中本来存在的数量条目即使来源缺失或数量为零，也继续显示。这是存档编辑与恢复能力，而不是找旧文件。
- 删除这些回退可能重新误隐藏合法物品；写入仍须通过各自的场景、数量、容量及存档校验。
- 本轮清单读取失败的处理不沿用这项可见性回退：清单决定有效文件范围，读取失败直接停止对应目录请求，不把不完整清单当成可写依据。
- **位置：** [QuantityItemReferenceAnalyzer.cs](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.cs)、[QuantityItemReferenceAnalyzer.LootParsing.cs](../../src/DarkestDungeonSaveEditor.Core/QuantityItemReferenceAnalyzer.LootParsing.cs)、[QuantityItemCatalog.SavedEntries.cs](../../src/DarkestDungeonSaveEditor.Core/QuantityItemCatalog.SavedEntries.cs)。

## 已作决定与仍独立的边界

**A1/A2 已按确认删除，同时增加清单内 LOC 读取。** 有效 XML、原 LOC2 支持、名称键变体及业务规则不随之删除。清单外 XML 的盘点字段和日志中的“顶层补读范围”也已去掉，清单外文件只作为差异候选记录。

A3/A4/A5 是名称查找或姓名池回退，可分别选择；B 类主要是正常格式/目录支持；C 类涉及业务判断，应另行评估。`old/backup/unused` 的嵌套递归范围还需另作决定，不能声称删掉 A1 后便不再读到这些文件。

## 第一阶段：发现规则修复与验证（历史记录）

- DLC 奇物/宝箱扫描统一有效根选择，验证有/无清单覆盖结果与写入指纹一致，并覆盖禁用 DLC、无关备份根。
- 复杂清单路径不再在中间扩展名处截断；保留最终扩展名筛选和纯路径行支持。
- 共用清单读取器先完整读取、解析选定类型的路径，再返回条目；九个目录入口的文件独占、非法路径测试验证明确失败，不返回半份目录或回退扫描。释放占用后可重新加载。
- 完整合同测试通过：`workspaces/contract_tests/20260905_193445_506_a1683013f38c410ea105d2043591049d`。
- 首次 Release 构建时关闭编辑器被权限拒绝；经权限批准关闭已核对路径的进程后，Release 重建通过，0 警告、0 错误。没有启动游戏或修改真实 Mod、存档。
- 本轮 C# 文件的 `dotnet format whitespace --verify-no-changes --no-restore` 在沙箱命名管道权限失败后，经权限批准重试通过；`git diff --check` 通过。
- 独立只读 reviewer 对比 13 个变更前快照和 3 个新增文件，未发现实质问题，补充的编译产物只读解析检查也通过；未重复执行完整测试。
- 额外尝试按既有 profile_0/profile_1 来源盘点对真实清单逐行复查新解析器，并检查本文链接，但执行进程创建遭到权限拒绝，权限重试仍失败。这项未完成，不计为通过；本轮未作新增游戏实机验证，也未声称穷尽所有 Mod 兼容影响。

## 第二阶段：严格 XML 与旧 LOC 支持的验证（历史记录）

- 已删除 A1/A2，新增通用 `LocLocalizationReader`；没有为 Ruler ID 硬编码名称或路径。所有名称类型共用发现、覆盖和格式优先级；随机个人姓名仍只读有效 XML。
- 新增 `LocalizationPolicyContractTests` 和 `LegacyLocalizationContractTests`，覆盖有/无清单的两个 Mod 渠道、已启用/禁用 DLC、嵌套二进制排除、有效嵌套 XML 保留、清单外三种格式不纳入、空清单、语言缺失、来源与格式优先级、精确路径替换、两级 XML 恢复移除，以及 LOC 的偏移/索引/长度/NUL/UTF-8/色码损坏。当时未请求的损坏值也会导致整表拒绝；第三阶段已将可定位的 UTF-8/色码错误改为单值隔离，结构错误仍整表拒绝。
- 首次测试编译发现新增测试中的空字典集合表达式不适用 `IReadOnlyDictionary`，已改为显式字典；后续编译和测试通过。
- 完整合同测试通过，产物：`workspaces/contract_tests/20260905_200402_386_0958173cd18d401dbb58558c09f942ef`。既有符号链接盘点用例因系统缺少创建链接权限被明确跳过，本轮新增名称读取用例没有跳过。
- 同次完整测试启用了三个真实 Mod 的只读探针：Ruler 从清单内 LOC 显示 `Ruler / Ruler`，Eos_Nyx 的十二件饰品保持双语，Rurutia 的九件饰品保持已有语言、缺失的第九件英文仍为空。Ruler 名称文件与清单的读前/读后哈希一致。
- Solution Release 构建通过：0 警告、0 错误。运行版与测试版的 Core DLL 哈希一致；本轮 C# 文件的 whitespace 格式验证、`git diff --check` 均通过。
- 新的独立只读 reviewer 对比了 14 个变更前文件和 3 个新增文件，未发现实质代码问题，并独立核对 Ruler 的清单、两个表头和样本哈希。其指出的旧数值脚本报告现行措辞已补充历史限定及新规则链接，该报告另存了修改前快照。
- Reviewer 未重复完整测试或运行游戏；其额外反射探针在创建进程阶段被权限拒绝，没有执行，不计为通过。上述可执行验证来自主执行流程，独立审查使用代码和直接文件证据。
- 未启动游戏，未修改真实 Mod、清单、存档或其他兼容规则，未自动提交。嵌套 XML 的 `old/backup/unused` 名称过滤仍是单独的待决定策略。

## 第三阶段：按可靠边界隔离无效条目

- 用户确认 LOC/LOC2/XML 统一遵循“结构错误拒绝整份、可明确定位的无效条目仅跳过该条、缺名保持空白”。不恢复清单外 XML 补读，不猜测编码，也不清洗或正则恢复损坏 XML。
- LOC 和 LOC2 在各自表结构与索引校验之后共用值读取器。全部记录仍接受边界、长度和 NUL 检查；即便先遇到可跳过的文字错误，后来发现结构错误，也不会返回部分名称或发布“部分读取成功”提示。合法同语言来源的既有回退和来源/格式优先级不变。
- 完整 XML 文档解析成功后，缺少条目 ID/语言 ID 或嵌套条目/语言归属不明的项单独跳过；正常条目与随机姓名保留。编码、声明、注释、标签和 CDATA 错误仍拒绝整份，不能借单项隔离恢复 A2。
- 新增 `LocalizationEntryIsolationContractTests`：覆盖 LOC/LOC2 的无效 UTF-8、截断色码、多值组保留下一有效值、空译文、同语言回退、批量坏值的摘要上限、后置结构错误整表拒绝；另覆盖有效 XML 的局部无效项、随机姓名一致性，以及后置 XML 语法/编码错误不得泄漏前面的有效条目。夹具确认源文件字节不变。
- 完整合同测试通过，产物：`workspaces/contract_tests/20260905_202934_934_ce8e6e1bd2cc489b9c2b4bf015e73aab`。既有符号链接盘点用例因缺少系统权限明确跳过；本轮新增用例没有跳过。
- 同次启用四个真实 Mod 的只读探针。工坊 `1143685298` 简中 LOC 的 43 个无效文字值被跳过，仍正确读取 `少女`、`天顶枪兵`，未虚构英文；Ruler 继续显示 `Ruler / Ruler`；Eos_Nyx 十二件饰品的双语与 Rurutia 九件饰品的已有语言保持正确，缺失的第九件英文仍为空。汉化 LOC 与 Ruler 源文件的哈希检查通过。
- Solution Release 构建通过，0 警告、0 错误。本轮 C# 格式化首次因沙箱命名管道权限失败，批准在沙箱外重试后成功，随后的只读格式验证也通过。
- 运行版与测试版 Core DLL 的 SHA-256 相同（`101C204BCEC5E648B1E95FE2D5F073150F523BC404D308D55B69435E6B1CBE91`）；`git diff --check` 与本轮规则/审计/测试文档的本地链接检查通过。
- 独立只读 reviewer 对比本轮 13 个文件快照与 3 个新增文件，未发现实质问题。其额外反射探针首次和权限重试均在进程创建阶段被拒，未执行，不计为通过；没有重复完整测试，结论依据静态复核与主流程的可执行检查。
- 未启动游戏，未修改真实 Mod、清单或存档，没有提交。当前规则仍不能恢复结构/编码已损坏的 XML；这属于明确保留的拒绝边界，不是本轮待修复项。
