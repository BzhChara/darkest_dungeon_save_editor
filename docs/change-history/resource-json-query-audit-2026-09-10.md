# 历史修改第九轮审核：饰品、地图 JSON 与资源文件查询，2026-09-10

已先提交此前验证完成的修复：`c14247a`，`fix: align JSON resource semantics and persistent raid saves`。该提交包含 JSON 字段／引用修复及持久副本 `raid_save` 子目录修复。提交后工作区干净，本轮在该版本上进行只读审核。

本轮确认三类待修复问题。没有修改产品代码、正式测试、真实存档、活动 Mod 或 Steam 配置；新增本报告和记录索引，隔离诊断文件保存在忽略目录 `workspaces/historical-rule-review9-20260910/`。

## 1. P1：饰品仍按最后一个 JSON 字段取值，会写入错误的实例次数

位置：[TrinketCatalog.cs](../../src/DarkestDungeonSaveEditor.Core/TrinketCatalog.cs) 第 94、104、118、317、324、337 行；其中第 337 行是使用次数的直接入口。来源说明中的根数组／ID 读取在同文件第 227、236 行，也保留相同问题。

`TrinketCatalog` 仍独立调用 `JsonElement.TryGetProperty`。同一对象内字段名称重复时，它返回最后一个，而游戏在此加载入口选择第一个。上一轮新增的 `NativeJsonReader` 已用于人物和物品引用，但尚未接入这个饰品解析器。

隔离资源只包含一条饰品：

```json
{"entries":[{"id":"counter_probe","limit":1,"limit":9,"price":100,"price":900,
"quest_uses":2,"quest_uses":7,"trigger_limit":3,"trigger_limit":8}]}
```

| 字段 | 原生字段选择 | 当前目录读取 | 隔离保存的结果 |
| --- | --- | --- | --- |
| limit | 1 | 9 | 影响目录及预览；此实验不证明创建数量限制有误 |
| price | 100 | 900 | 目录中价格错误 |
| quest_uses | 2 | 7 | `quest_uses_remaining = 7` |
| trigger_limit | 3 | 8 | `triggers_remaining = 8` |

后两项经过实际 `TrinketSaveEditor.AddCopies`、DSON 编码和解码，错误不只停留在显示层。保存前重新加载仍使用同一目录解析器，不能纠正这种取值差异。

另一个反例是在根对象内依次声明两个 `entries` 数组，第一个含 `first_root`，第二个含 `last_root`。当前编辑器只加载 `last_root`，原生读取第一个数组。这也说明修复不能只替换次数字段 helper，根数组、ID、职业要求和来源读取应保持一致。

原生证据来自固定 Windows x64 build 27890：

- `Trinket::Library::LoadEntriesFile`，函数 `0x1404F2E40`。`entries` 从 `0x1404F2FB4` 开始顺序查找成员。
- `limit` 在 `0x1404F3C13` 起查找，名称比较成功即跳到 `0x1404F3D3C`；失败才按 `0x28` 字节前进到下一个成员。
- `trigger_limit` 在 `0x1404F3C79` 起同样选择首次匹配。
- `quest_uses` 在 `0x1404F3FB6` 起查找，再经 `0x14028E900` 获取该字段；此处检查整数类型，不能据此扩大为允许所有小数到整数的转换。

历史来源：根数组／ID 和普通字段 helper 来自初始 `7b9a67f`，次数支持来自 `93fa9fa3`。`d55a15f` 修正饰品 ID 身份时没有改变字段查找。这是旧入口漏接，不是庭院路径修复改变了饰品规则。

建议后续修复：让饰品各个 JSON 消费入口使用已经确认的首次成员查找，保留首成员本身的类型。首字段无效时不能继续寻找后面的同名字段冒充有效值。现有“多条饰品定义同 ID 取首条完整定义”的规则不需要因此改变。

## 2. P2：地图道具 JSON 的重复成员会使整个道具目录加载失败

位置：[BattleRoomAttachmentCatalog.PropLibrary.cs](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.PropLibrary.cs) 第 62–66 行和第 125–131 行；[Resources.cs](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.Resources.cs) 第 45–51 行。

该入口使用 `JsonNode.Parse` / `JsonObject`。JSON 对象里有重复名称时，后续第一次访问触发内部字典初始化，抛出 `ArgumentException`。游戏的成员列表不会因重复名称而在这里拒绝整个对象。

最小反例为 `props/trap_definitions.json`：

```json
{"props":[{"name":"alpha","default_data":{
  "instance_type":"trap","instance_type":"obstacle"
}}]}
```

同时在标准海湾道具池中引用 `alpha`。只有第一个 `instance_type` 的对照文件可以正常得到陷阱候选 `alpha`；添加第二个字段后，生产目录加载抛出：

```text
System.ArgumentException: An item with the same key has already been added.
Key: instance_type
JsonObject.ContainsKey
BattleRoomAttachmentCatalog.ApplyPropData (...PropLibrary.cs:131)
BattleRoomAttachmentCatalog.ReadPropFile (...PropLibrary.cs:75)
BattleRoomAttachmentCatalog.ReadResources (...Resources.cs:45)
BattleRoomAttachmentCatalog.Load (...BattleRoomAttachmentCatalog.cs:151)
```

原生 `0x1404D5010` 负责应用道具数据，`instance_type` 在 `0x1404D5504` 起处理。`0x1404D5533`–`0x1404D5551` 顺序检查成员；首次匹配跳出，后续实际取值仍从成员起点查找。因此这个例子使用 `trap`，不需要把 `alpha` 当成不可解析资源。外层 `default_data` / `props` 在 `0x1404D76E0` 中也采用首次成员查找。

影响范围是地图道具目录及依赖它的战斗地图加载。主界面 [MainWindow.CatalogLoading.cs](../../src/DarkestDungeonSaveEditor.App/MainWindow.CatalogLoading.cs) 第 111–132 行捕获地图加载异常，记录“战斗地图暂时无法读取，其他目录仍已正常加载”。这不是整个编辑器必然崩溃，也没有证明当前 profile_1 含有本反例。

这段 `JsonObject` 解析来自此前地图资源修复 `78fd748a`。其中资源继承、加载顺序、难度注册等修复不因本问题而全部失效；需要替换的是不符合原生重复成员行为的对象访问入口。

建议后续修复：按原生成员语义解析外层和嵌套对象，继续保留已经验证的继承及难度规则。单独捕获 `ArgumentException` 后跳过文件不能解决问题，会丢失游戏实际可用的定义和父资源。

## 3. P2：部分文件发现仍使用旧固定后缀，会漏掉游戏查询接受的资源

位置：

- [HeroClassCatalog.SourceDiscovery.cs](../../src/DarkestDungeonSaveEditor.Core/HeroClassCatalog.SourceDiscovery.cs) 第 33、36、37 行以及第 91–107 行。
- [TrinketCatalog.cs](../../src/DarkestDungeonSaveEditor.Core/TrinketCatalog.cs) 第 258、267 行。
- [BattleRoomAttachmentCatalog.Resources.cs](../../src/DarkestDungeonSaveEditor.Core/BattleRoomAttachmentCatalog.Resources.cs) 第 12–14 行及后续资源阶段分类。

这些入口仍使用固定字符串后缀或文件通配符，不能等同于游戏传给资源查询函数的正则表达式。最直接的普通文件名反例是 `raid/camping/camping_skills.json`：游戏查询不要求 `camping_skills` 前面有点，编辑器的 `*.camping_skills.json` / `.EndsWith(".camping_skills.json")` 却要求。

此外，若原生查询中的点没有转义，该位置也能匹配其他单字符。以下是从实际调用点取得的模式，不是按文件惯例推测：

| 资源及入口 | 原生查询 | 本轮验证的漏读文件 |
| --- | --- | --- |
| 饰品，`0x1403E8333` | `.*trinkets/.*\.entries.trinkets.json` | `trinkets/a.entries.trinketsXjson` |
| Buff，`0x1404A32BF` | `.*shared/buffs/.*\.buffs.json` | `shared/buffs/z.buffsXjson` |
| 怪癖，`0x1404DDC5F` | `.*quirk_library.json`，查询根为 `shared/quirk/` | `shared/quirk/z.quirk_libraryXjson` |
| 露营技能，`0x1404A4B5A` | `.*camping_skills.json`，查询根为 `raid/camping/` | `raid/camping/camping_skills.json`、`raid/camping/z.camping_skillsXjson` |
| 嵌套陷阱定义，`0x1404D88C2` | `.*props/.*/trap_definitions.json` | `props/addon/trap_definitionsXjson` |

上述文件在模拟原版来源和明确列入 `modfiles.txt` 的本地 Mod 来源中均复现漏读。因此问题不局限于没有清单的 Mod，也不能靠再次生成清单解决。

Buff 反例包含一个基础 `a.buffs.json`，将 `hp` 定义为 +25%，以及排序靠后的有效替换文件，将同一个 Buff 定义为 +75%。基础 HP 为 20 的职业通过怪癖引用该 Buff：

| 后一文件名 | 当前有效修正 | 调用生产人物初始 HP 计算的结果 |
| --- | --- | --- |
| `z.buffs.json`，正向对照 | +75% | 35 |
| `z.buffsXjson`，同一内容 | 错误保留前一份 +25% | 25 |

这里的后果来自有效文件没有进入加载序列。已接入的“同 ID Buff 完整定义最后覆盖”及 HP 数值精度规则本身没有被此反例推翻。HP 结果调用的是生产 `GetValidatedInitialCurrentHp`，本轮没有生成真实人物或观测这组样例的游戏画面。

还有两点必须保留区别：

1. 道具根目录的三个定义文件是直接按路径打开的；只有嵌套目录走上述搜索。本例不能扩大为根目录 `props/trap_definitionsXjson` 也必然有效。
2. 升级树查询是 `.*upgrades/.*\.upgrades\.json$`，点已转义。当前相应的固定后缀不属于本问题，不能为了统一而把所有资源后缀一概放宽。

历史来源：人物侧的 Mod 后缀分类来自 `1a03bb42`，基础目录扫描来自 `73f599a3`；后来的 `37d9167` 接入了事件文件查询，但保留了旁边 Buff／怪癖／露营技能的旧过滤。饰品扫描在 `4e1db6b3` 改用物理目录枚举时仍沿用原固定后缀；地图侧则保留固定 basename 白名单。

建议后续修复：分别接入各资源真正的文件查询，贯穿基础来源、清单条目、资源阶段分类和缺失文件诊断。同路径来源选择、Mod 清单限制、物理目录过滤及各类重复 ID 规则继续保留；不应改回任意目录扫描。当前内容刷新指纹已覆盖有关目录的 `*json`，本例中的主要遗漏在消费入口。

## 验证记录与边界

- 使用已提交源代码对应的现有 Release Core，SHA-256 为 `C815B24513D037808FC628CFB61A90411E3936AACC32F99454FBC83184DD772D`；本轮没有重新编译或替换产品程序集。
- [诊断源码](../../workspaces/historical-rule-review9-20260910/Program.cs)、[最终结果](../../workspaces/historical-rule-review9-20260910/probe-results.json)、[最终运行日志](../../workspaces/historical-rule-review9-20260910/probe-hp.log)、[退出码](../../workspaces/historical-rule-review9-20260910/probe-hp.exit-code.txt)。最后一次运行目录为 `runs/103dc7c1156e4199bf869f0d20d53a80/`，共 16 个隔离场景，退出码 0。场景包含正向对照和预期复现缺陷；退出码 0 不表示这些产品问题已经修复。
- 饰品次数包含 DSON 保存往返；地图异常包含完整生产调用栈；HP 通过生产计算函数验证。所有输入和输出位于新建的隔离目录，未写真实档案。
- 原生依据为 Windows x64 build 27890 的 `Darkest.exe`，SHA-256 `35E5A653279992564809FF8406FEBD5A02A7D6961044781B1296B38A7096F59B`。本轮 `inspect_native.py` 在读取时校验该哈希；完整函数和查询调用点保存在同目录 `native_*.txt`，查询字符串在 `resource-query-strings.txt`。有关首次成员查找的通用函数证据还见第八轮的 `native_14028e980.txt`。本轮采用静态原生证据，没有启动游戏进行新的实机实验。
- 同时检查了内容快照刷新、地图历史的目录／副本身份范围、维护恢复的只读依赖检查，以及饰品保存前重新加载流程。在这些已检查路径中未确认另一项可复现回退；不代表已经穷尽所有 Mod 和全部存档状态。
- 提交所含庭院修复的既有完整测试结果为 36 组 PASS、0 FAIL、0 SKIP，并已完成独立实现复核。本轮没有改变产品，因此没有重复运行整个套件，也没有触发只读审查之外的完成复核。
- 三类问题均保持待修复。本报告与记录索引未另行提交。
