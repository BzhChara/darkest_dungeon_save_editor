# 人物皮肤与初始技能选择修复

日期：2026-09-14。对应[第十九轮审核](hero-generation-selection-audit-2026-09-14.md)，基线 `da780a5`。用户确认修复三项生成规则，并明确不增加旧候选兼容逻辑。

## 改动

- `StagecoachHeroCandidateFactory.Progression.cs`：`generation_guaranteed` 改为初始结果至少包含一个标记技能；先抽取，再按原生分支替换首个临时选择位置。战斗池、职业露营池不足时少取并提示，两类露营池不互相补位。
- `HeroSkinDirectoryDiscovery.cs`：统一收集全部活动挂载的直接皮肤目录，复用 DLC 前缀资格与物理发现规则；不再按 info/art 提供者单独从 A 连续计数。Mod 使用清单虚拟目录，本体、模式、官方 DLC 使用物理目录入口。皮肤正则、目录大小写和深度分别处理；返回的目录名按原生精确比较去重。
- `HeroClassCatalog.cs`、`HeroClassCatalog.HeroDefinitions.cs`：从公共目录查询取得皮肤数量；删除旧的提供者最大值／连续字母／PNG 存在性分支。`ProfileCatalogContentFingerprint.cs` 也使用目录入口触发刷新。
- `StagecoachHeroCandidateFactory.cs`、`MainWindow.RowModels.cs`：更新缺皮肤原因与“标记 N（至少选一项）”显示；生成预览保留少取提示。
- 两份新增契约测试覆盖抽取、目录、来源、预检和序列化；更新旧皮肤断言及重复资源测试对私有解析器的反射调用参数。后者仍验证重复 Effect 列表的追加顺序，不撤销其断言。
- [当前规则](../hero-generation-selection.md)、内容规则、重复语义文档和 README 已同步；本记录留在修改历史目录。

影响人物目录、自动刷新、生成可用性预检及新候选的技能／皮肤值。升级购买、全部露营技能解锁、初始怪癖与 HP、物品饰品及战斗／Bridge 保持原有实现。没有修改真实存档、Mod、游戏或系统配置，没有新增依赖。

## 验证

定向 `--hero-selection` 通过六组检查：

- 42 个战斗案例：六类来源，至少一个标记技能的所有合法子集、标记数超过目标、技能池不足、选择上限；每例检查七级预检，512 个确定种子核对可达子集。
- 24 个露营案例：两类池各自不足或为空、提示、全部可用露营技能的解锁。
- 六项原有拒绝条件，以及不可自行选择技能的职业保留全部技能。
- 36 个皮肤组合案例：A/B、A/C、仅 C、独立皮肤、分离 info/art 提供者、未列清单对照及自动刷新。
- 84 个皮肤查询案例：名称正则、大小写、深度、无 PNG、空物理目录、清单虚拟目录、缺失载荷、禁用 DLC 前缀。
- 同皮肤在根挂载／DLC／清单重复项之间只占一个位置，以及完全缺皮肤时仍禁止创建。

其中 17 组本地 Mod 案例执行 town、roster、upgrades 的 **51 次**完整 DSON 往返，校验整份 JSON、预览中的皮肤与技能 ID、保存的零值技能映射。定向产物：`workspaces/contract_tests/20260914_005001_006_d87edf3396624868bc1774cb570d8aea/`。

首次新增测试构建因误用可用性记录属性 `Reason` 失败，修正为 `UnavailableReason`；测试未因此被跳过。首次默认并行方案构建退出 1，但输出没有具体编译诊断；随后按仓库 README 的串行方式构建成功，0 警告、0 错误，见 `workspaces/hero-selection-fixes-20260914/build-serial.log`。

首轮 `--catalogs` 在已有重复资源测试失败：该测试反射调用旧的私有方法签名，仍传入来源字典。已更新为新的皮肤数量参数，并移除 override 的旧参数，保持原有语义断言，见 `catalogs.log`。第二轮 `catalogs-final.log` 运行至旧的可用性断言失败：仍要求职业露营池为空时禁用人物。已按批准的第 3 项改为检查七级可创建、保留共享选择并带少取提示，其余预检断言未删除。产品代码在这两处测试适配期间保持不变。

最终串行构建记录于 `build-validated.log`，0 警告、0 错误。最终 `--catalogs` 全部通过，**40 PASS / 0 FAIL / 0 SKIP**，进程退出 0，见 `catalogs-validated.log`；涵盖目录、名称、同步、人物、物品饰品和保存契约，没有选择独立战斗测试组。产物位于 `workspaces/contract_tests/20260914_010013_963_f43ed5c733454b288c7a19b799bd2737/`。

独立 Python 脚本 `verify_artifacts.py` 先复核定向运行、再复核上述完整运行中的 51 对 JSON、17 个候选的技能与皮肤值，并核对 Core、App、契约测试输出的 Core DLL 一致。完整回归阶段 Core SHA-256 为 `b713d26b6e01b10ce71f4bd9e03c00d87922476cb3d8aee950efa18645183326`；下述补充修正后重新复核并更新了 `artifact-verification.json`。`verify_scope.py` 记录 15 份最终源码／测试输入哈希，并验证本轮七份文档中的 151 处本地链接，见 `final-scope-verification.json`；`git diff --check` 通过。

## 独立审查补充修正

全新只读审查者 `Epicurus`（`01a09d6b-7e07-75e1-b135-451496a279a0`）指出两项遗漏：P2，同清单的 `Pack_selection_C`／`pack_selection_C` 被公共文件覆盖器的大小写无关键合并；P3，请求 4、池内 3、选择上限 2 时漏掉少取提示。

主流程重新核对 `0x140247C90 → 0x140B91D12 → IAT 0x140C63378 → strncmp` 调用链，确认返回路径模式 0 的目录名比较区分大小写；旧反汇编和导入表分别在 `workspaces/multifile_encounters_20260906/disasm_140247970.txt`、`disasm_140b91d0c.txt` 与 `workspaces/native_loading_20260907/imports.txt`。审查者没有成功执行探针，不把其静态结论写成游戏实测。

主流程随后用捕获异常且不生成 apphost 的隔离控制台探针成功复现两项：`edges-red.json` 末行记录皮肤数量 1（预期 2），以及选中数 2 但没有短缺提示，两项均为 false，退出 1。该探针首建出现 WindowsBase 引用版本警告；已让探针使用与正式契约测试一致的 WPF 框架引用，不新增依赖或展示窗口。

修正仅限目录名精确去重、原始请求短缺提示及定向测试；公共文件覆盖器保持原样。新增 18 个六来源大小写目录对照，以及低选择上限与短缺同时出现的检查。属于局部审查修正，没有递归启动第二位审查者；审查者已关闭。此前 40 PASS 的完整回归早于这两处补充修正，不宣称随后重复运行了完整套件。

最终验证：

- `build-reviewed.log`：Release 全方案构建通过，0 警告、0 错误。
- `selection-reviewed.log`：补充修正后的 `--hero-selection` **6 PASS / 0 FAIL / 0 SKIP**，退出 0；包含原有六组矩阵及新增大小写／低上限边界。
- `build-edge-reviewed.log` 与 `edges-green.json`：隔离探针构建零警告／错误，运行退出 0；皮肤数量恢复为 2，选中数仍为 2 且正确提示请求 4、可用 3，两项均为 true。
- 最新定向产物 `workspaces/contract_tests/20260914_011418_456_e2aed3134c264694874901fc8e195667/` 中的 51 对 JSON、17 个候选再次经独立 Python 验证。三个输出位置的最终 Core SHA-256 一致，为 `f7359d5fab0d8a212760fcb97599ca460d18f136ac6634fcd3790ad22c256b55`。
- 15 份源码／测试哈希、151 个本地文档链接与差异空白检查已在收尾重新核对。所有已确认的本轮问题均已处理。

本次依据固定原生分支和隔离可执行测试，没有新启动游戏实测。目录计数正确不等于验证了每个皮肤的贴图／骨骼／动画完整性；也不宣称随机种子与游戏的随机数发生器一一对应。
