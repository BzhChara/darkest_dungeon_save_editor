# 赞助入口：2026-09-07

主界面右上角增加“赞助”按钮，点击打开软件内的“赞助支持”窗口。按钮使用次级样式；弹窗沿用深色边框与金色标题，展示用户提供的支付宝 ¥10 收款码，以及“赞助完全自愿，不影响任何功能”的说明。

## 修改文件

- [MainWindow.xaml](../../src/DarkestDungeonSaveEditor.App/MainWindow.xaml)：标题区域的“赞助”按钮。
- [MainWindow.Support.cs](../../src/DarkestDungeonSaveEditor.App/MainWindow.Support.cs)：点击后打开属于主窗口的模态弹窗。
- [SupportDialog.xaml](../../src/DarkestDungeonSaveEditor.App/SupportDialog.xaml)、[SupportDialog.xaml.cs](../../src/DarkestDungeonSaveEditor.App/SupportDialog.xaml.cs)：说明、原图、金额、关闭按钮、暗色标题栏；根据主窗口所在显示器的工作区和 WPF 缩放限制尺寸。正文可滚动，关闭按钮固定在底部；支持 Enter 和 Esc。
- [support-alipay.jpg](../../src/DarkestDungeonSaveEditor.App/Assets/support-alipay.jpg)、[项目资源配置](../../src/DarkestDungeonSaveEditor.App/DarkestDungeonSaveEditor.App.csproj)：将原图作为程序资源打包，不依赖用户的临时文件路径。

原图大小为 1080 × 1620；SHA-256 为 `76AF8FCDFFD8E215AAE1948F5CC043A338C0F59F6DD6AD8326F9EF42DF6B12E4`。没有裁剪、重新生成或修改收款码。此入口不读取或改写档案，不加入支付 API、账号或付款验证逻辑。

## 验证

- 最终 Release 构建通过，0 警告、0 错误。
- 实际 WPF 烟测通过：在未加载档案时，从主界面按钮连续打开 3 次，分别由关闭按钮、Esc、Enter 关闭；验证窗口归属与完整资源加载。
- 原始图片与打包资源的字节哈希一致。
- 主窗口 1120 × 720、赞助弹窗正常高度和 540 像素高度均完成渲染检查；正常高度无滚动条，短窗口正文可滚动且关闭按钮可见。
- 现有 `RunUiContracts` 及其包含的 `RunUiLayoutContracts` 通过。本次没有修改 Core 或存档逻辑，未重复执行整套存档回归。
- 独立只读审查发现一个副屏适配问题，已核实并修正：不再直接使用仅代表主显示器的 `SystemParameters.WorkArea`，改用主窗口的显示器工作区并换算 WPF 缩放。修正后重新通过界面烟测与现有 UI 回归。系统属性的范围见 [Microsoft 文档](https://learn.microsoft.com/en-us/dotnet/api/system.windows.systemparameters.workarea?view=windowsdesktop-10.0)。
- 结果与截图位于 `workspaces/support_dialog_20260907/`，独立审查结果记录在该目录的 `validation.md`。

中间检查曾因测试入口错误、测试键盘范围设置错误而失败，已修正测试程序并重新通过。一次构建受正在运行的编辑器占用影响，解除占用后已重新构建。未执行真实扫码付款验证；不同 DPI 的多显示器组合没有逐一实机覆盖。
