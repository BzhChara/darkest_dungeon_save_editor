using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;
using DarkestDungeonSaveEditor.Core;
using Microsoft.Win32;

namespace DarkestDungeon.ModOrder.App;

public partial class MainWindow : Window
{
    private bool _chinese;
    private bool _ready;
    private bool _busy;
    private ConversionPlan? _plan;
    private string? _lastExport;
    private readonly string _languageSettingsPath;
    private readonly CultureInfo _systemCulture;
    private string _languagePreference;

    private bool CanExportPreview => !_busy && _lastExport is null && _plan?.CanExport == true;

    public MainWindow() : this(Path.Combine(SaveEditorLocations.CreateDefault().ApplicationDataDirectory,
        "mod-order-language.json"), CultureInfo.CurrentUICulture) { }

    public MainWindow(string languageSettingsPath, CultureInfo systemCulture)
    {
        _languageSettingsPath = languageSettingsPath;
        _systemCulture = systemCulture;
        _languagePreference = EditorLanguageSettings.Load(languageSettingsPath, out var languageError);
        _chinese = EditorText.ResolveCulture(_languagePreference, _systemCulture).TwoLetterISOLanguageName == "zh";
        InitializeComponent();
        SourceInitialized += (_, _) => DarkestDungeonSaveEditor.App.NativeWindowTheme.ApplyDarkTitleBar(this);
        _ready = true;
        Translate();
        if (languageError is not null) Details.Text = languageError;
    }

    private string T(string chinese, string english) => _chinese ? chinese : english;

    private void Translate()
    {
        Title = T("暗黑地牢 Mod 顺序转换工具", "Darkest Dungeon Mod Order Converter");
        Resources["BodyFont"] = new FontFamily(T("SimHei, Microsoft YaHei UI, Segoe UI", "Segoe UI, Microsoft YaHei UI"));
        Resources["HeadingFont"] = new FontFamily(T("SimSun, Microsoft YaHei UI, Segoe UI", "Segoe UI, Microsoft YaHei UI"));
        Heading.Text = Title;
        LanguageButton.Content = T("语言", "Language");
        LanguageButton.ToolTip = T("立即切换界面语言", "Change the interface language immediately");
        LanguageButton.ContextMenu.FontFamily = (FontFamily)Resources["BodyFont"];
        SystemLanguage.Header = T("跟随系统", "Follow system");
        UpdateLanguageMenuChecks();
        ToMo2.Content = T("存档 → MO2", "Save → MO2");
        ToSave.Content = T("MO2 → 原生存档", "MO2 → Native save");
        SaveLabel.Text = T("目标存档 persist.game.json", "Save: persist.game.json");
        ListLabel.Text = T("MO2 配置的 modlist.txt", "MO2 profile: modlist.txt");
        ModsLabel.Text = T("MO2 的 mods 文件夹", "MO2 mods directory");
        ModsPath.ToolTip = T("只读取 meta.ini / project.xml 等身份信息；标准目录结构下可自动推断。", "Reads identity metadata only. Suggested automatically for the standard MO2 directory layout.");
        foreach (var button in new[] { SaveBrowse, ListBrowse, ModsBrowse })
            button.Content = T("浏览…", "Browse…");
        PreviewHeading.Text = T("转换预览 · 优先级从高到低", "Preview · Highest priority first");
        OnlyEnabled.Content = T("只显示启用项", "Enabled only");
        string[] headers = _chinese ? ["顺序", "启用", "MO2 文件夹", "原生来源", "原生 ID / 标题", "问题 / 说明"]
            : ["Order", "Enabled", "MO2 folder", "Native source", "Native ID / title", "Issue / detail"];
        for (var i = 0; i < headers.Length; i++) PreviewGrid.Columns[i].Header = headers[i];
        PreviewButton.Content = T("预览转换", "Preview conversion");
        ExportButton.Content = T("导出新文件…", "Export new files…");
        RefreshStatus();
    }

    private void Input_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _plan = null;
        _lastExport = null;
        ExportButton.IsEnabled = false;
        PreviewGrid.ItemsSource = null;
        Details.Text = "";
        RefreshStatus();
    }

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        LanguageButton.ContextMenu.PlacementTarget = LanguageButton;
        LanguageButton.ContextMenu.IsOpen = true;
    }

    private void UpdateLanguageMenuChecks()
    {
        foreach (var item in LanguageButton.ContextMenu.Items.OfType<MenuItem>())
            item.IsChecked = item.Tag is string preference && preference == _languagePreference;
    }

    private void LanguageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string preference }) return;
        try
        {
            if (preference != _languagePreference) EditorLanguageSettings.Save(_languageSettingsPath, preference);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            UpdateLanguageMenuChecks();
            MessageBox.Show(this, T("无法保存语言设置：", "Could not save the language preference: ") + ex.Message,
                T("语言设置", "Language preference"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _languagePreference = preference;
        _chinese = EditorText.ResolveCulture(preference, _systemCulture).TwoLetterISOLanguageName == "zh";
        Translate();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var kind = (string)((Button)sender).Tag;
        if (kind is "save" or "list")
        {
            var dialog = new OpenFileDialog
            {
                Filter = kind == "save" ? "Darkest Dungeon save|persist.game.json|JSON|*.json" : "MO2 modlist|modlist.txt|Text|*.txt",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true) return;
            if (kind == "save") SavePath.Text = dialog.FileName;
            else
            {
                ListPath.Text = dialog.FileName;
                // Only suggest the standard instance/profiles/<profile>/modlist.txt layout.
                var profiles = Directory.GetParent(Path.GetDirectoryName(dialog.FileName)!);
                if (ModsPath.Text.Length == 0 && profiles?.Name.Equals("profiles", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var candidate = Path.Combine(profiles.Parent!.FullName, "mods");
                    if (Directory.Exists(candidate)) ModsPath.Text = candidate;
                }
            }
        }
        else
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog(this) == true)
                ModsPath.Text = dialog.FolderName;
        }
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        var request = new ConversionRequest(ToSave.IsChecked == true ? ConversionDirection.Mo2ToSave : ConversionDirection.SaveToMo2,
            SavePath.Text, ListPath.Text, ModsPath.Text);
        _plan = null;
        _lastExport = null;
        PreviewGrid.ItemsSource = null;
        Details.Text = "";
        SetBusy(true);
        Status.Text = T("正在读取存档和 Mod 元数据…", "Reading save and Mod metadata…");
        try
        {
            _plan = await Task.Run(() => Converter.PreviewAsync(request));
            FilterRows();
            Details.Text = string.Join(Environment.NewLine, _plan.Errors);
        }
        catch (Exception ex) { Details.Text = ex.Message; }
        finally { SetBusy(false); RefreshStatus(); }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!CanExportPreview || _plan is not { } plan) return;
        var dialog = new OpenFolderDialog { Title = T("选择导出位置；将在其中新建文件夹", "Choose a parent directory; a new export folder will be created") };
        if (dialog.ShowDialog(this) != true) return;
        var output = Path.Combine(dialog.FolderName, $"DD-ModOrder-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        _lastExport = null;
        SetBusy(true);
        Status.Text = T("正在重新检查输入并验证导出…", "Rechecking inputs and verifying export…");
        try
        {
            _lastExport = await Task.Run(() => Converter.ExportAsync(plan, output));
            Details.Text = _lastExport;
            MessageBox.Show(this, T("已导出到：\n", "Exported to:\n") + _lastExport + "\n\n" +
                (plan.Request.Direction == ConversionDirection.SaveToMo2
                    ? T("关闭 MO2 和游戏，备份原 modlist.txt 后再用导出的同名文件替换。重新打开对应 MO2 配置检查排序。",
                        "Close MO2 and the game. Back up the original modlist.txt, then replace it with the export. Reopen the target profile and check its order.")
                    : T("关闭游戏和 MO2，备份整个存档目录后，只替换 persist.game.json，再直接启动游戏。导出中的 original 文件夹保留了原文件。",
                        "Close the game and MO2. Back up the entire save profile, replace only persist.game.json, then launch the game directly. The original file is also kept in the export's original folder.")),
                Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { _plan = null; Details.Text = ex.Message; }
        finally { SetBusy(false); RefreshStatus(); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        InputPanel.IsEnabled = DirectionPanel.IsEnabled = PreviewButton.IsEnabled = LanguageButton.IsEnabled = !busy;
        ExportButton.IsEnabled = CanExportPreview;
    }

    private void RefreshStatus()
    {
        if (_busy) return;
        Status.Text = _lastExport is not null ? T("导出完成，原文件未修改。", "Exported. Source files unchanged.")
            : _plan is null ? (Details.Text.Length > 0 ? T("预览失败，详见上方说明。", "Preview failed. See details above.")
                : T("选择输入文件后预览。", "Select inputs, then preview."))
            : _plan.CanExport ? T($"可导出：{_plan.EnabledCount} 个启用 Mod。", $"Ready: {_plan.EnabledCount} enabled Mods.")
            : T($"存在 {_plan.Errors.Count} 个问题，暂不能导出。", $"{_plan.Errors.Count} issue(s). Export blocked.");
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) { if (_ready) FilterRows(); }
    private void FilterRows() => PreviewGrid.ItemsSource = _plan?.Rows.Where(row => OnlyEnabled.IsChecked != true || row.Enabled).ToArray();
    private void Row_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && PreviewGrid.SelectedItem is PreviewRow row)
            Details.Text = string.Join(Environment.NewLine, new[] { row.Detail, row.SourceDirectory }.Where(s => s.Length > 0));
    }
}
