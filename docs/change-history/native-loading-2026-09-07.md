# 原生资源加载核对与接入：2026-09-07

本轮按用户确认，将已验证的 Windows 原生加载规则接入离线目录解析，并补全仅修改资源定义时的自动刷新。静态检查只读取游戏 EXE；运行时检查只请求查询与读取进程内存权限。没有注入、挂钩、内存写入，也没有修改真实存档或 Mod 文件。

## 已实现的行为

- 提取 `NativeContentFileResolver`：以原生文件枚举槽位重放路径覆盖。替换文件保留原槽位，新文件按来源的目录深度与区分大小写的文件名顺序追加。战斗目录原有的多 DLC、同优先级来源与无法证明编号的限制保留。
- 数量物品按 `(type,id)` 的第一个已加载定义取值；饰品也取第一个 ID 匹配项，不再把后续同 ID 条目的任务次数、触发次数等字段合入。怪癖取最后一个已加载的精确 ID 定义，包括完整进化字段。遗留目录键不能区分的大小写冲突仍禁止写入。
- 角色 ID 的发现与定义文件的读取分开：人物使用 `heroes/<id>/<id>` 的 info、art、override；怪物从 ID 去掉最后两个字节得到 family，再读 `monsters/<family>/<id>/<id>` 的 info、art。分类目录中的同名文件不能直接提供 HP、体型或 Boss 标签。
- 标准路径打开仍受 Mod 清单约束。已发现的 ID 不会使未列出的同名标准文件自动覆盖原版；这与此前人物清单 A/B 实测一致。无清单设备的标准路径打开与目录发现过滤分开。
- 怪物体型按原生构造默认值 0 和后续最后字段读取。重复 `.size`、整数前缀和带引号数字的处理与物品原生读取共用 `NativeDarkestReader`。大小写不同的怪物 ID 仍分别存在，但 Windows 标准路径可能解析到同一覆盖文件。
- 缺少标准 info 和 art 的空怪物类仍以原生体型 0 参与编号计算，但不可新建/替换或充当 Bridge 来源。自动维护也按无有效定义处理。标准定义明确写了体型 0 的单位不会因此被排除；无法解析的体型、字节 ID 或哈希碰撞保持未知，暂停相关编号/清理而不当作缺失删除。
- 新增原生 ID 哈希碰撞保护。例如 `Az` 与 `BE` 均为 3567；这类物品、饰品、人物和怪癖不再作为两个独立可写定义使用，指向碰撞怪癖的进化链也受保护。怪物碰撞使相关编号保持不可证明。无冲突资源不受影响。
- 饰品的 `hero_class_requirements` 必须全部满足。此前编辑器多出的七件职业限定饰品因此不再出现。
- 物理扫描分支跳过点号开头和含 `_template` 的路径组件，并复现原生 C locale 下的路径转换失败。清单枚举与标准路径打开不使用这个过滤条件。诊断用全量文件盘点仍保留所有文件。
- 新增 `ProfileCatalogContentFingerprint` 覆盖目录定义、引用关系、角色辅助文件和本地化文件。即使 Mod 清单、文件长度和修改时间没有变化，定义内容变化仍触发自动刷新；耗时读取在后台执行。发布重建的界面目录前再次检查内容指纹。

这轮没有改动物品/饰品写入位置、人物生成存档结构、怪癖限额/互斥与进化链安全检查、Bridge 格式、地图操作事务和原生战斗责任边界。改进后的目录与编号仍进入这些既有写入前校验。广义目录刷新不等于清理战斗；自动维护仍按战斗绑定变化决定是否清理。

## 原生依据

适用 EXE：本机 Windows x64 build 27890，SHA-256 `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。下列地址是首选基址地址，运行时读取按实际模块基址换算。

| 地址 | 已核对的行为 |
| --- | --- |
| `0x140247D20`、`0x140247970` | base 先枚举，再倒序应用已注册挂载；flags=0 保留替换槽位 |
| `0x140374460`、`0x1402396F0` | 物理设备与清单设备的枚举、深度及文件名排序 |
| `0x140374790`、`0x140375090` | 模板/点号目录过滤；`wcstombs_s` 返回非零时不加入文件结果 |
| `0x1404F1590`、`0x1404F6480` | 物品、饰品的首次匹配查询 |
| `0x1404ABD00` | 怪癖扫描全表，最后一次 ID 哈希匹配保留 |
| `0x1404F39A0` | 饰品要求的所有职业逐个查找，缺少任意一个就跳过饰品 |
| `0x1404C3230`、`0x1404CD210` | 人物、怪物的标准路径构造及 info/art/override 顺序 |
| `0x140248040`、`0x1402480DC–0x140248100`、`0x1402393C0` | 读取/存在性检查经过清单查找，未列出的 Mod 路径跳过该提供方；UTF-8 打开不会绕过清单 |
| `0x1404804D0`、`0x1404CDA30` | Actor 体型初始化为 0，display 字段后续覆盖 |

`wcstombs_s` 的错误处理与 locale 依赖参见 [Microsoft 文档](https://learn.microsoft.com/en-us/cpp/c-runtime-library/reference/wcstombs-s-wcstombs-s-l?view=msvc-170)。隔离 UCRT C-locale 调用确认：中文路径返回 EILSEQ（42），Latin-1 字符能转换为单字节。后者不等于 UTF-8 路径往返正确；未验证编码路径不能扩展为“任意 Unicode 目录都能有效加载”的承诺。

## 真实档案的逐项对比

对比 `profile_1` 已捕获的两份稳定运行时表与相同活动来源的离线目录。除了数量，还核对 ID、物品堆叠上限、怪物体型，以及可直接写入的战斗类型/编号/有序怪物序列。

| 对象 | 结果 |
| --- | --- |
| 怪物 | 1,449 个 ID 与各自体型匹配，无多出或缺少 |
| 数量物品 | 编辑器 259 个 `(type,id)` 全部找到运行时对应项，堆叠上限匹配 |
| 职业 | 50 个 ID 集合匹配 |
| 怪癖 | 567 个唯一 ID 集合匹配；运行时捕获未包含全部进化字段，不能据此声称字段逐项相等 |
| 饰品 | 1,144 件战役饰品都在运行时表；运行时额外 103 件来自 PvP，不进入战役目录 |
| 荒野难度 1 | 走廊 65、房间 69、Boss 4，共 138 个类型/编号/有序组合逐条匹配 |

最初离线多出的 15 个怪物 ID 全部来自无清单的 `Shuiyue_Monster_Enhancement`，位于 `monsters/游荡/...` 下：`bulrush_E/F`、`snake_cobra_D/E/F`、`snake_rattler_D/E/F`、`virginian_A/B/C`、`99pkcrab_C`、`99pkdullahan_C`、`99pkfish_C`、`99pkpistolman_C`。接入物理目录转换规则后，这 15 个差异消失。不能把这一结果套用到清单中明确列出的中文路径。

验证期间七个被监控存档文件的读取前后哈希一致。真实档案没有执行编辑器战斗生成/替换；这些写入通过隔离合同测试验证。

## 验证记录与限制

- 完整合同测试已覆盖所有 Bridge 类型、地图创建/替换/删除、自动维护、旧编号保护、写入前竞态、DSON 往返、回滚、物品数量、人物与怪癖、饰品、强制返回以及自动同步。新增测试覆盖原生文件槽位、首次/最后 ID 查询、标准路径、物理扫描过滤和同长度同时间戳的定义更新。
- 实际档案运行时表只加载了荒野难度 1；其他地区使用相同解析器并通过隔离案例，但不能宣称已对所有地区实机逐项核对。
- DLC 的完整注册顺序、空/异常清单的原生分支、部分语义资源的重复 ID 策略、非默认 locale 和特殊路径仍需独立证据。保留相应既有保护，不把有限反汇编结果写成完整游戏源码还原。
- 当前完整验证日志和对比结果位于忽略的 `workspaces/native_loading_20260907/`。任务开始前已有的修复由 `baseline/` 单独记录，复核使用 `task.diff`，不混入此前工作。

## 独立复核后的修正

独立只读复核确认了清单打开路径与之前实机 A/B 的一致性，并发现标准路径旁路清单、空怪物类被当成可用组合，以及字符串分组遗漏原生哈希碰撞的问题。主代理核实后修正，补充了以下可执行案例：

- 清单列出分类目录的同名文件，但未列出标准覆盖文件时，仍采用有效的原版标准文件。
- 缺少标准文件的战斗保留编号 0 但不能放置；后续正常战斗仍使用编号 1；定义明确写了体型 0 的条目保持可用。
- `Az/BE` 的不同堆叠和有状态饰品定义被实际预览服务拒绝，未创建存档备份；正常条目、怪癖安全状态和进化引用同时检查。

复核后是针对已证实问题的局部修正，按项目协作规则复跑相关和完整检查，没有再次启动重复独立审阅。审阅者特别指出，中文路径加 ASCII ID 的案例不代表已经支持任意 Unicode 怪物 ID；现有字节 ID 限制保留。

## 涉及文件

- `NativeContentFileResolver.cs`、`ContentFileDiscovery.cs`、`NativeDirectoryDiscovery.cs`、`NativeDarkestReader.cs`、`NativeResourceIdentity.cs`：共享文件顺序、扫描、字段和身份规则；原物品专用解析文件的实现合并到共享读取器。
- `QuantityItemCatalog.*`、`TrinketCatalog.cs`、`HeroClassCatalog.*`、`BattleEncounterCatalog.*`、`BattleRoomAttachmentCatalog.*`：各目录接入；存储/引用/本地化扫描器使用同一物理发现过滤。
- `ManagedBattleEncounterBridgeService.Maintenance.cs`：缺少标准定义的历史组合不再当成可用类；原生编号计算仍保留其空槽影响。
- `ProfileCatalogContentFingerprint.cs`、`ProfileCatalogSnapshotReader.cs`、`MainWindow.CatalogLoading.cs`、`MainWindow.ProfileSync.cs`：广义资源指纹、后台读取、界面重建及最终一致性检查。
- `NativeResourceResolutionContractTests.cs` 和受影响的目录/地图/维护/同步测试及样例：新增边界验证；把旧的“任意分类目录文件可作定义”“所有同 ID 都按 Mod 优先级覆盖”等假设调整为已验证规则。
- 本记录、修改记录索引、`content-save-rules.md`、`encounter-runtime-order.md`：记录实现与当前规则，保留历史报告的时点含义。

## 最终检查

- `dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests/DarkestDungeonSaveEditor.ContractTests.csproj -c Release --no-build -- <仓库绝对路径>`：退出码 0，无失败或跳过；日志 `contracts-verified.log`，样例目录 `workspaces/contract_tests/20260907_115443_845_8c60f330be8847c58afeaff466c2edb8`。
- `dotnet build DarkestDungeonSaveEditor.sln -c Release --no-restore -m:1`：退出码 0，0 警告、0 错误；日志 `build-final.log`。
- 最终代码再次只读核对真实内容：`native-comparison.json` 的怪物/物品/职业/怪癖/战役饰品集合、体型/堆叠和 138 条遭遇比对保持上述结果，`ProfileUnchanged=true`。这是稳定的既有运行时快照对照，不是再次触发所有真实战斗。
- `git diff --check` 按仓库换行配置检查通过。一度临时禁用 `core.autocrlf` 的检查把 CRLF 行尾当作空白，已按原配置重跑；未修改 Git 持久设置，也未因此改写项目行尾。

本轮没有提交 Git。所有真实游戏文件保持只读；合同测试、反汇编输出和检查日志仅写入被忽略的工作区。
