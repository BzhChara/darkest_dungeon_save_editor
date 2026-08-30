using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.Core;
using Microsoft.Win32;

namespace DarkestDungeonSaveEditor.App;

public partial class MainWindow : Window
{
    private const int TitleLogoFrameWidth = 320;
    private const int TitleLogoFrameHeight = 100;
    private const int TitleLogoFrameColumns = 10;
    private const int TitleLogoFrameCount = 120;
    private static readonly TimeSpan TitleLogoFrameInterval = TimeSpan.FromMilliseconds(100);
    private readonly ObservableCollection<TrinketRow> _visibleTrinkets = [];
    private readonly ObservableCollection<HeroRow> _visibleHeroes = [];
    private readonly ObservableCollection<HeroLevelChoice> _heroLevelChoices = [];
    private IReadOnlyList<TrinketDefinition> _allTrinkets = [];
    private IReadOnlyList<HeroClassDefinition> _allHeroes = [];
    private IReadOnlyList<string> _selectedInitialQuirkIds = [];
    private HeroClassCatalogResult? _heroCatalog;
    private TrinketStorageDefinition? _trinketStorage;
    private ActiveContentSnapshot? _activeContentSnapshot;
    private PreparedTrinketEdit? _preparedTrinketEdit;
    private PreparedStagecoachHeroEdit? _preparedHeroEdit;
    private StagecoachHeroCandidatePreview? _preparedHeroCandidatePreview;
    private SaveEditService? _editService;
    private string? _catalogProfileDirectory;
    private string? _catalogGameSaveSha256;
    private int _editRevision;
    private DispatcherTimer? _titleLogoTimer;
    private CroppedBitmap[]? _titleLogoFrames;
    private int _titleLogoFrameIndex;

    public MainWindow()
    {
        InitializeComponent();
        TrinketGrid.ItemsSource = _visibleTrinkets;
        HeroGrid.ItemsSource = _visibleHeroes;
        HeroLevelComboBox.ItemsSource = _heroLevelChoices;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeWindowTheme.ApplyDarkTitleBar(this);
    }

    private void TitleLogoImage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_titleLogoFrames is null)
        {
            var sheet = new BitmapImage();
            sheet.BeginInit();
            sheet.CacheOption = BitmapCacheOption.OnLoad;
            sheet.UriSource = new Uri(
                "pack://application:,,,/DarkestDungeonSaveEditor.App;component/Assets/dd-title-logo-loop.png",
                UriKind.Absolute);
            sheet.EndInit();
            sheet.Freeze();

            _titleLogoFrames = Enumerable.Range(0, TitleLogoFrameCount)
                .Select(frameIndex =>
                {
                    var frame = new CroppedBitmap(
                        sheet,
                        new Int32Rect(
                            frameIndex % TitleLogoFrameColumns * TitleLogoFrameWidth,
                            frameIndex / TitleLogoFrameColumns * TitleLogoFrameHeight,
                            TitleLogoFrameWidth,
                            TitleLogoFrameHeight));
                    frame.Freeze();
                    return frame;
                })
                .ToArray();
            _titleLogoFrameIndex = 0;
            TitleLogoImage.Source = _titleLogoFrames[0];
        }

        _titleLogoTimer ??= new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TitleLogoFrameInterval
        };
        _titleLogoTimer.Tick -= TitleLogoTimer_Tick;
        _titleLogoTimer.Tick += TitleLogoTimer_Tick;
        _titleLogoTimer.Start();
    }

    private void TitleLogoImage_Unloaded(object sender, RoutedEventArgs e) => _titleLogoTimer?.Stop();

    private void TitleLogoTimer_Tick(object? sender, EventArgs e)
    {
        if (_titleLogoFrames is null || _titleLogoFrames.Length == 0)
        {
            return;
        }

        _titleLogoFrameIndex = (_titleLogoFrameIndex + 1) % _titleLogoFrames.Length;
        TitleLogoImage.Source = _titleLogoFrames[_titleLogoFrameIndex];
    }

    private void Discover_Click(object sender, RoutedEventArgs e) => Discover();

    private void Discover()
    {
        try
        {
            var snapshot = SteamDiscovery.Discover();
            var game = snapshot.GameInstallations.FirstOrDefault();
            if (game is not null)
            {
                GameDirectoryTextBox.Text = game.GameDirectory;
                WorkshopDirectoryTextBox.Text = game.WorkshopDirectory;
                LocalModDirectoryTextBox.Text = game.DefaultLocalModDirectory;
            }

            var profile = SteamDiscovery.SelectDefaultProfile(snapshot.Profiles);
            if (profile is not null)
            {
                ProfileDirectoryTextBox.Text = profile.ProfileDirectory;
            }

            AppendStatus(
                $"自动发现：游戏={snapshot.GameInstallations.Count}，档案={snapshot.Profiles.Count}。" +
                (snapshot.Issues.Count == 0 ? string.Empty : $" 提示={string.Join(" | ", snapshot.Issues)}"));
        }
        catch (Exception ex)
        {
            AppendStatus($"自动发现失败：{ex.Message}");
        }
    }

    private void BrowseGame_Click(object sender, RoutedEventArgs e)
    {
        BrowseInto(GameDirectoryTextBox, "选择 DarkestDungeon 游戏目录");
    }

    private void BrowseWorkshop_Click(object sender, RoutedEventArgs e)
    {
        BrowseInto(WorkshopDirectoryTextBox, "选择 262060 工坊内容目录");
    }

    private void BrowseLocalMods_Click(object sender, RoutedEventArgs e)
    {
        BrowseInto(LocalModDirectoryTextBox, "选择本地 Mod 根目录或单个 Mod 目录");
    }

    private void BrowseProfile_Click(object sender, RoutedEventArgs e)
    {
        BrowseInto(ProfileDirectoryTextBox, "选择 profile_* 存档目录");
    }

    private static void BrowseInto(TextBox target, string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };
        if (Directory.Exists(target.Text))
        {
            dialog.InitialDirectory = target.Text;
        }

        if (dialog.ShowDialog() == true)
        {
            target.Text = dialog.FolderName;
        }
    }

    private async void LoadCatalog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CrashDiagnostics.SetStage("LoadCatalog: invalidating previous catalog");
            InvalidateCatalog();
            CrashDiagnostics.SetStage("LoadCatalog: entering busy state");
            SetBusy(true);
            var gameDirectory = RequireDirectory(GameDirectoryTextBox.Text, "游戏目录");
            var workshopDirectory = string.IsNullOrWhiteSpace(WorkshopDirectoryTextBox.Text)
                ? null
                : Path.GetFullPath(WorkshopDirectoryTextBox.Text.Trim());
            var additionalLocalModDirectory = string.IsNullOrWhiteSpace(LocalModDirectoryTextBox.Text)
                ? null
                : RequireDirectory(LocalModDirectoryTextBox.Text, "本地 Mod 目录");
            var profile = SteamDiscovery.OpenProfile(ProfileDirectoryTextBox.Text.Trim());
            var jarPath = Path.Combine(AppContext.BaseDirectory, "tools", "DDSaveEditor", "DDSaveEditor.jar");
            var codec = new DsonSaveCodec(jarPath);
            AppendStatus(
                "正在读取当前档案启用的 DLC、Workshop 与本地 Mod，再扫描饰品和人物定义……" +
                (additionalLocalModDirectory is null
                    ? string.Empty
                    : $" 本地 Mod 目录：{additionalLocalModDirectory}"));
            CrashDiagnostics.SetStage("LoadCatalog: resolving active content");
            var activeContent = await ActiveContentResolver.ResolveAsync(
                profile,
                gameDirectory,
                workshopDirectory,
                additionalLocalModDirectory,
                codec);
            CrashDiagnostics.RecordStatus(
                $"内容目录档案：ID={activeContent.Profile.ProfileId}；" +
                $"档案目录={activeContent.Profile.ProfileDirectory}；" +
                $"persist.game.json SHA-256={activeContent.SourceGameSha256}");
            CrashDiagnostics.SetStage("LoadCatalog: building content catalogs");
            var catalogs = await Task.Run(() => new
            {
                Trinkets = TrinketCatalog.Load(activeContent),
                Heroes = HeroClassCatalog.Load(activeContent)
            });
            CrashDiagnostics.SetStage("LoadCatalog: assigning catalog results");
            _allTrinkets = catalogs.Trinkets.Trinkets;
            _allHeroes = catalogs.Heroes.HeroClasses;
            _heroCatalog = catalogs.Heroes;
            _trinketStorage = catalogs.Trinkets.Storage;
            _activeContentSnapshot = activeContent;
            PopulateHeroLevels(catalogs.Heroes);
            _catalogProfileDirectory = profile.ProfileDirectory;
            _catalogGameSaveSha256 = activeContent.SourceGameSha256;
            CrashDiagnostics.SetStage("LoadCatalog: populating visible rows");
            ApplyFilter();
            CrashDiagnostics.SetStage("LoadCatalog: updating summary");
            CatalogSummaryTextBlock.Text =
                $"模式 {catalogs.Heroes.GameMode}；活动来源 {activeContent.Sources.Count} 个；饰品 {_allTrinkets.Count} 个；" +
                $"仓库槽位 {FormatStorageCapacity(_trinketStorage)}；" +
                $"人物 {_allHeroes.Count} 个，怪癖定义 {catalogs.Heroes.InitialQuirks.Count} 个，" +
                $"姓名 {catalogs.Heroes.HeroNames.Count} 个；等级 0-{Math.Max(0, catalogs.Heroes.ResolveLevelThresholds.Count - 1)}";
            var issues = activeContent.Issues
                .Concat(catalogs.Trinkets.Issues)
                .Concat(catalogs.Heroes.Issues)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            AppendStatus(
                $"内容目录完成：档案记录启用 Mod {activeContent.AppliedModCount} 个，" +
                $"成功解析来源 {activeContent.Sources.Count} 个、饰品 {_allTrinkets.Count} 个、" +
                $"仓库槽位 {FormatStorageCapacity(_trinketStorage)}、" +
                $"人物 {_allHeroes.Count} 个（其中 {_allHeroes.Count(item => item.RecruitEvents.Count > 0)} 个有招募事件，" +
                $"{_allHeroes.Count(item => item.RuntimeQuirkSignals.Count > 0)} 个有后续玩法怪癖线索；这些线索不是初始怪癖）。" +
                (issues.Length == 0 ? string.Empty : $" 读取提示 {issues.Length} 条。"));
            var issueMessages = issues
                .Select(issue => $"目录提示：{issue}")
                .ToArray();
            foreach (var issueMessage in issueMessages)
            {
                CrashDiagnostics.RecordStatus(issueMessage);
            }

            foreach (var issueMessage in issueMessages.Take(30))
            {
                AppendStatus(issueMessage, persist: false);
            }

            if (issueMessages.Length > 30)
            {
                AppendStatus($"另有 {issueMessages.Length - 30} 条目录提示仅写入完整日志。", persist: false);
            }

            CrashDiagnostics.SetStage("LoadCatalog: synchronous UI update completed");
        }
        catch (Exception ex)
        {
            CrashDiagnostics.RecordException("LoadCatalog handled exception", ex);
            AppendStatusSafely($"加载内容目录失败：{ex.Message}", "LoadCatalog failure status");
        }
        finally
        {
            try
            {
                CrashDiagnostics.SetStage("LoadCatalog: leaving busy state");
                SetBusy(false);
                CrashDiagnostics.SetStage("LoadCatalog: handler returned; waiting for Dispatcher");
                ScheduleLoadCatalogDispatcherProbes();
            }
            catch (Exception ex)
            {
                CrashDiagnostics.RecordException("LoadCatalog cleanup exception", ex);
                AppendStatusSafely($"恢复界面状态失败：{ex.Message}", "LoadCatalog cleanup status");
            }
        }
    }

    private void ScheduleLoadCatalogDispatcherProbes()
    {
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.DataBind,
            new Action(() => CrashDiagnostics.SetStage("LoadCatalog: Dispatcher DataBind reached")));
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() => CrashDiagnostics.SetStage("LoadCatalog: Dispatcher Render reached")));
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => CrashDiagnostics.SetStage("LoadCatalog: Dispatcher ContextIdle reached")));
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => CrashDiagnostics.SetStage("LoadCatalog: Dispatcher ApplicationIdle reached")));
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var keyword = SearchTextBox.Text.Trim();
        var filteredTrinkets = _allTrinkets.Where(definition =>
            string.IsNullOrWhiteSpace(keyword) ||
            definition.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            definition.LocalizedName.Chinese.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            definition.LocalizedName.English.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            definition.Rarity.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            definition.Source.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        _visibleTrinkets.Clear();
        foreach (var definition in filteredTrinkets)
        {
            _visibleTrinkets.Add(new TrinketRow(definition));
        }

        var filteredHeroes = _allHeroes.Where(definition =>
            string.IsNullOrWhiteSpace(keyword) ||
            definition.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            definition.LocalizedName.Chinese.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            definition.LocalizedName.English.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            definition.Source.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            definition.RecruitEvents.Any(item => item.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
            definition.RuntimeQuirkSignals.Any(item => item.QuirkId.Contains(keyword, StringComparison.OrdinalIgnoreCase)));

        _visibleHeroes.Clear();
        foreach (var definition in filteredHeroes)
        {
            _visibleHeroes.Add(new HeroRow(definition));
        }
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        InvalidatePreparedEdit();
        var previewRevision = _editRevision;
        try
        {
            SetBusy(true);
            var profile = SteamDiscovery.OpenProfile(ProfileDirectoryTextBox.Text.Trim());
            EnsureCatalogMatches(profile);
            var jarPath = Path.Combine(AppContext.BaseDirectory, "tools", "DDSaveEditor", "DDSaveEditor.jar");
            var editService = new SaveEditService(new DsonSaveCodec(jarPath));
            AppendStatus($"正在为 {profile.ProfileId} 创建只读副本并执行编码回环……");

            if (CatalogTabs.SelectedIndex == 0)
            {
                if (TrinketGrid.SelectedItem is not TrinketRow selected)
                {
                    throw new InvalidOperationException("请先选择一个饰品。");
                }

                if (!int.TryParse(CopiesTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var copies) ||
                    copies is < 1 or > 999)
                {
                    throw new InvalidOperationException("添加数量必须是 1 到 999 的整数。");
                }

                if (_trinketStorage is null)
                {
                    PreviewWarningTextBlock.Text =
                        "未能从当前活动内容解析唯一有效的饰品仓库上限。为避免写入超过实际容量，饰品预览与应用已禁用。";
                    PreviewWarningTextBlock.Visibility = Visibility.Visible;
                    AppendStatus($"饰品预览已阻止：{PreviewWarningTextBlock.Text}");
                    throw new InvalidOperationException("当前饰品仓库容量未知，无法安全生成预览。");
                }

                if (_activeContentSnapshot is null)
                {
                    throw new InvalidOperationException("活动内容快照不可用，请重新加载内容目录。");
                }

                var preparedEdit = await editService.PrepareTrinketEditAsync(
                    profile,
                    selected.Definition,
                    copies,
                    _trinketStorage,
                    _activeContentSnapshot);
                if (previewRevision != _editRevision || CatalogTabs.SelectedIndex != 0)
                {
                    AppendStatus("预览期间目录状态发生变化，已丢弃本次结果。");
                    return;
                }

                _editService = editService;
                _preparedTrinketEdit = preparedEdit;
                var preview = preparedEdit.Preview;
                PreviewSummaryTextBlock.Text =
                    $"{preview.TrinketId} · 仓库内该饰品 {preview.ExistingCopies} → {preview.ResultingCopies} " +
                    $"（定义上限 {FormatDefinitionLimit(preview.DefinitionLimit)}） · " +
                    $"仓库总槽位 {preview.ExistingInventoryEntries} → {preview.ResultingInventoryEntries} / " +
                    $"{FormatStorageCapacity(preview.StorageCapacity)}";
                if (preview.ExceedsDefinitionLimit)
                {
                    PreviewWarningTextBlock.Text =
                        $"已超过该饰品的定义持有上限 {preview.DefinitionLimit}。编辑器会按控制台模式保留写入能力，" +
                        "但游戏之后不会再正常奖励该饰品；已装备副本未计入这里的仓库数量。";
                    PreviewWarningTextBlock.Visibility = Visibility.Visible;
                    AppendStatus($"饰品预览提示：{PreviewWarningTextBlock.Text}");
                }
                ApplyButton.IsEnabled = true;
                AppendStatus($"饰品预览通过，工作区：{preparedEdit.WorkspaceDirectory}");
                return;
            }

            if (HeroGrid.SelectedItem is not HeroRow selectedHero || _heroCatalog is null)
            {
                throw new InvalidOperationException("请先选择一个人物职业。");
            }
            if (_activeContentSnapshot is null)
            {
                throw new InvalidOperationException("活动内容快照不可用，请重新加载内容目录。");
            }

            var generated = StagecoachHeroCandidateFactory.Generate(
                _heroCatalog,
                selectedHero.Definition,
                RandomNumberGenerator.GetInt32(int.MaxValue),
                GetSelectedHeroLevel(),
                _selectedInitialQuirkIds);
            var preparedHeroEdit = await editService.PrepareStagecoachHeroEditAsync(
                profile,
                generated,
                _heroCatalog,
                _activeContentSnapshot);
            if (previewRevision != _editRevision || CatalogTabs.SelectedIndex != 1)
            {
                AppendStatus("预览期间目录状态发生变化，已丢弃本次结果。");
                return;
            }

            _editService = editService;
            _preparedHeroEdit = preparedHeroEdit;
            _preparedHeroCandidatePreview = generated.Preview;
            PreviewWarningTextBlock.Visibility = Visibility.Collapsed;
            PreviewWarningTextBlock.Text = string.Empty;
            var heroPreview = generated.Preview;
            PreviewSummaryTextBlock.Text =
                $"{heroPreview.Name} / {heroPreview.HeroClass}：{heroPreview.ResolveLevel}级，XP {heroPreview.ResolveXp}，" +
                $"武器/护甲 {heroPreview.WeaponRank}/{heroPreview.ArmourRank}，HP {heroPreview.CurrentHp.ToString("0.##", CultureInfo.InvariantCulture)}，" +
                $"怪癖 [{FormatSelectedQuirks(heroPreview)}]，" +
                $"技能 {heroPreview.CombatSkills.Count}+{heroPreview.CampingSkills.Count}，" +
                $"个人升级记录 {preparedHeroEdit.Preview.UpgradePurchaseCount}；" +
                $"马车 {preparedHeroEdit.Preview.ExistingCandidates} → {preparedHeroEdit.Preview.ResultingCandidates}，" +
                $"GUID {preparedHeroEdit.Preview.CandidateGuid}。";
            ApplyButton.IsEnabled = true;
            AppendStatus(
                $"人物预览通过：{heroPreview.Name} / {heroPreview.HeroClass} / {heroPreview.ResolveLevel}级；" +
                $"XP {heroPreview.ResolveXp}；武器/护甲 rank {heroPreview.WeaponRank}/{heroPreview.ArmourRank}；" +
                $"正面怪癖 [{string.Join(", ", heroPreview.PositiveQuirks)}]；" +
                $"负面怪癖 [{string.Join(", ", heroPreview.NegativeQuirks)}]；" +
                $"疾病 [{string.Join(", ", heroPreview.Diseases)}]；" +
                $"战斗技能 [{string.Join(", ", heroPreview.CombatSkills)}]；" +
                $"露营技能 [{string.Join(", ", heroPreview.CampingSkills)}]；" +
                $"个人升级记录 {preparedHeroEdit.Preview.UpgradePurchaseCount} 条。");
            foreach (var warning in heroPreview.Warnings)
            {
                AppendStatus($"人物预览提示：{warning}");
            }
            AppendStatus($"人物预览工作区：{preparedHeroEdit.WorkspaceDirectory}");
        }
        catch (Exception ex)
        {
            AppendStatus($"生成预览失败：{ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_editService is null || (_preparedTrinketEdit is null && _preparedHeroEdit is null))
        {
            return;
        }

        var isHeroEdit = _preparedHeroEdit is not null;
        var profileDirectory = isHeroEdit
            ? _preparedHeroEdit!.Profile.ProfileDirectory
            : _preparedTrinketEdit!.Profile.ProfileDirectory;
        var changeSummary = isHeroEdit
            ? $"向普通马车加入 {_preparedHeroCandidatePreview?.Name} / {_preparedHeroCandidatePreview?.HeroClass} " +
              $"（{_preparedHeroCandidatePreview?.ResolveLevel}级，XP {_preparedHeroCandidatePreview?.ResolveXp}，" +
              $"武器/护甲 rank {_preparedHeroCandidatePreview?.WeaponRank}/{_preparedHeroCandidatePreview?.ArmourRank}；" +
              $"个人升级记录 {_preparedHeroEdit!.Preview.UpgradePurchaseCount} 条；" +
              $"GUID {_preparedHeroEdit!.Preview.CandidateGuid}；" +
              $"初始怪癖 [{FormatSelectedQuirks(_preparedHeroCandidatePreview)}]）"
            : $"加入 {_preparedTrinketEdit!.Preview.RequestedCopies} 个 {_preparedTrinketEdit.Trinket.Id}";
        var trinketLimitWarning = !isHeroEdit && _preparedTrinketEdit!.Preview.ExceedsDefinitionLimit
            ? $"\n\n注意：写入后仓库内该饰品将有 {_preparedTrinketEdit.Preview.ResultingCopies} 个，" +
              $"超过定义上限 {_preparedTrinketEdit.Preview.DefinitionLimit}。"
            : string.Empty;
        var confirmation = MessageBox.Show(
            $"将对以下档案执行：{changeSummary}\n\n" +
            $"{profileDirectory}" + trinketLimitWarning + "\n\n" +
            "程序会先完整备份 profile 中所有 persist*.json。确认游戏已经关闭并继续吗？",
            "确认应用存档修改",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            AppendStatus("已取消应用，真实存档未修改。");
            return;
        }

        try
        {
            SetBusy(true);
            var backupDirectory = isHeroEdit
                ? (await _editService.CommitAsync(_preparedHeroEdit!)).BackupDirectory
                : (await _editService.CommitAsync(_preparedTrinketEdit!)).BackupDirectory;
            AppendStatus($"应用成功。备份：{backupDirectory}");
            MessageBox.Show(
                $"存档修改成功。\n\n备份目录：\n{backupDirectory}",
                "完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            InvalidatePreparedEdit();
        }
        catch (Exception ex)
        {
            AppendStatus($"应用失败：{ex.Message}");
            MessageBox.Show(ex.Message, "应用失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void TrinketGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => InvalidatePreparedEdit();

    private void HeroGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        InvalidatePreparedEdit();
        _selectedInitialQuirkIds = [];
        UpdateInitialQuirkSelectionSummary();
        UpdateCatalogMode();
    }

    private void HeroLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        InvalidatePreparedEdit();

    private void SelectInitialQuirks_Click(object sender, RoutedEventArgs e)
    {
        if (HeroGrid.SelectedItem is not HeroRow selectedHero || _heroCatalog is null)
        {
            AppendStatus("请先选择一个人物职业。");
            return;
        }

        try
        {
            var dialog = new InitialQuirkSelectionDialog(
                _heroCatalog,
                selectedHero.Definition,
                _selectedInitialQuirkIds)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var selectedIds = dialog.SelectedQuirkIds.ToArray();
            if (_selectedInitialQuirkIds.SequenceEqual(selectedIds, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            _selectedInitialQuirkIds = selectedIds;
            InvalidatePreparedEdit();
            UpdateInitialQuirkSelectionSummary();
            AppendStatus($"已选择初始怪癖：{FormatSelectedQuirks(_selectedInitialQuirkIds)}。");
        }
        catch (Exception ex)
        {
            AppendStatus($"打开初始怪癖选择失败：{ex.Message}");
        }
    }

    private void CatalogTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, CatalogTabs))
        {
            return;
        }

        InvalidatePreparedEdit();
        UpdateCatalogMode();
    }

    private void InputPath_TextChanged(object sender, TextChangedEventArgs e) => InvalidateCatalog();

    private void InvalidateCatalog()
    {
        InvalidatePreparedEdit();
        _catalogProfileDirectory = null;
        _catalogGameSaveSha256 = null;
        _allTrinkets = [];
        _allHeroes = [];
        _selectedInitialQuirkIds = [];
        _heroCatalog = null;
        _trinketStorage = null;
        _activeContentSnapshot = null;
        _heroLevelChoices.Clear();
        _visibleTrinkets.Clear();
        _visibleHeroes.Clear();
        if (TrinketGrid is not null)
        {
            TrinketGrid.SelectedItem = null;
        }
        if (HeroGrid is not null)
        {
            HeroGrid.SelectedItem = null;
        }
        if (CatalogSummaryTextBlock is not null)
        {
            CatalogSummaryTextBlock.Text = string.Empty;
        }
        if (PreviewButton is not null)
        {
            PreviewButton.IsEnabled = false;
        }
        UpdateInitialQuirkSelectionSummary();
    }

    private void InvalidatePreparedEdit()
    {
        unchecked
        {
            _editRevision++;
        }
        _preparedTrinketEdit = null;
        _preparedHeroEdit = null;
        _preparedHeroCandidatePreview = null;
        _editService = null;
        if (ApplyButton is not null)
        {
            ApplyButton.IsEnabled = false;
        }
        if (PreviewSummaryTextBlock is not null)
        {
            PreviewSummaryTextBlock.Text = string.Empty;
        }
        if (PreviewWarningTextBlock is not null)
        {
            PreviewWarningTextBlock.Text = string.Empty;
            PreviewWarningTextBlock.Visibility = Visibility.Collapsed;
        }
    }

    private void SetBusy(bool busy)
    {
        PathInputsBorder.IsEnabled = !busy;
        CatalogTabs.IsEnabled = !busy;
        SearchTextBox.IsEnabled = !busy;
        CopiesTextBox.IsEnabled = !busy && CatalogTabs.SelectedIndex == 0;
        HeroLevelComboBox.IsEnabled = !busy &&
            CatalogTabs.SelectedIndex == 1 &&
            _heroCatalog is not null &&
            HeroGrid.SelectedItem is HeroRow;
        InitialQuirksButton.IsEnabled = !busy &&
            CatalogTabs.SelectedIndex == 1 &&
            _heroCatalog is not null &&
            HeroGrid.SelectedItem is HeroRow;
        PreviewButton.IsEnabled = !busy && CanPreviewCurrentTab();
        ApplyButton.IsEnabled = !busy &&
            (_preparedTrinketEdit is not null || _preparedHeroEdit is not null);
    }

    private void EnsureCatalogMatches(SaveProfile profile)
    {
        if (_catalogProfileDirectory is null || _catalogGameSaveSha256 is null ||
            (_allTrinkets.Count == 0 && _heroCatalog is null))
        {
            throw new InvalidOperationException("请先为当前档案加载内容目录。");
        }

        if (!Path.GetFullPath(profile.ProfileDirectory)
                .Equals(Path.GetFullPath(_catalogProfileDirectory), StringComparison.OrdinalIgnoreCase))
        {
            InvalidateCatalog();
            throw new InvalidOperationException("档案目录已变化，请重新加载内容目录。");
        }

        var gameSavePath = Path.Combine(profile.ProfileDirectory, "persist.game.json");
        if (!File.Exists(gameSavePath) ||
            !ComputeSha256(gameSavePath).Equals(_catalogGameSaveSha256, StringComparison.OrdinalIgnoreCase))
        {
            InvalidateCatalog();
            throw new InvalidOperationException("当前档案的 Mod/DLC 配置已变化，请重新加载内容目录。");
        }
    }

    private bool CanPreviewCurrentTab()
    {
        return _catalogProfileDirectory is not null &&
               (CatalogTabs.SelectedIndex == 0
                   ? _allTrinkets.Count > 0
                   : CatalogTabs.SelectedIndex == 1 && _heroCatalog is not null &&
                     _allHeroes.Count > 0 && HeroLevelComboBox.SelectedItem is HeroLevelChoice);
    }

    private void UpdateCatalogMode()
    {
        if (PreviewButton is null || CopiesTextBox is null || CopiesLabel is null)
        {
            return;
        }

        var isHeroTab = CatalogTabs.SelectedIndex == 1;
        PreviewButton.Content = isHeroTab ? "生成候选人物安全预览" : "生成饰品安全预览";
        CopiesLabel.Visibility = isHeroTab ? Visibility.Collapsed : Visibility.Visible;
        CopiesTextBox.Visibility = isHeroTab ? Visibility.Collapsed : Visibility.Visible;
        HeroLevelLabel.Visibility = isHeroTab ? Visibility.Visible : Visibility.Collapsed;
        HeroLevelComboBox.Visibility = isHeroTab ? Visibility.Visible : Visibility.Collapsed;
        HeroLevelComboBox.IsEnabled = isHeroTab &&
            _heroCatalog is not null &&
            HeroGrid.SelectedItem is HeroRow;
        InitialQuirksButton.Visibility = isHeroTab ? Visibility.Visible : Visibility.Collapsed;
        InitialQuirkSelectionSummaryTextBlock.Visibility = isHeroTab ? Visibility.Visible : Visibility.Collapsed;
        InitialQuirksButton.IsEnabled = isHeroTab &&
            _heroCatalog is not null &&
            HeroGrid.SelectedItem is HeroRow;
        UpdateInitialQuirkSelectionSummary();
        PreviewButton.IsEnabled = CanPreviewCurrentTab();
    }

    private void PopulateHeroLevels(HeroClassCatalogResult catalog)
    {
        _heroLevelChoices.Clear();
        var thresholds = catalog.ResolveLevelThresholds.Count > 0
            ? catalog.ResolveLevelThresholds
            : new[] { 0 };
        for (var level = 0; level < thresholds.Count; level++)
        {
            _heroLevelChoices.Add(new HeroLevelChoice(level, thresholds[level]));
        }

        HeroLevelComboBox.SelectedIndex = _heroLevelChoices.Count > 0 ? 0 : -1;
    }

    private int GetSelectedHeroLevel()
    {
        return HeroLevelComboBox.SelectedItem is HeroLevelChoice choice
            ? choice.ResolveLevel
            : throw new InvalidOperationException("请选择要生成的人物等级。");
    }

    private void UpdateInitialQuirkSelectionSummary()
    {
        if (InitialQuirksButton is null || InitialQuirkSelectionSummaryTextBlock is null)
        {
            return;
        }

        InitialQuirksButton.Content = $"选择初始怪癖… ({_selectedInitialQuirkIds.Count})";
        if (HeroGrid is null || HeroGrid.SelectedItem is not HeroRow)
        {
            InitialQuirksButton.IsEnabled = false;
            InitialQuirkSelectionSummaryTextBlock.Text = "请先选择人物职业；默认生成空白怪癖。";
            return;
        }

        InitialQuirksButton.IsEnabled = CatalogTabs is { IsEnabled: true, SelectedIndex: 1 } &&
            _heroCatalog is not null;

        var positiveCount = 0;
        var negativeCount = 0;
        var diseaseCount = 0;
        if (_heroCatalog is not null)
        {
            foreach (var id in _selectedInitialQuirkIds)
            {
                var definition = _heroCatalog.InitialQuirks.FirstOrDefault(
                    quirk => quirk.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                diseaseCount += definition?.IsDisease == true ? 1 : 0;
                positiveCount += definition is { IsDisease: false, IsPositive: true } ? 1 : 0;
                negativeCount += definition is { IsDisease: false, IsPositive: false } ? 1 : 0;
            }
        }

        InitialQuirkSelectionSummaryTextBlock.Text =
            $"已选 +{positiveCount}/-{negativeCount}/疾病 {diseaseCount}：" +
            FormatSelectedQuirks(_selectedInitialQuirkIds);
    }

    private static string FormatSelectedQuirks(StagecoachHeroCandidatePreview? preview)
    {
        return preview is null
            ? "未知"
            : FormatSelectedQuirks(
                preview.PositiveQuirks.Concat(preview.NegativeQuirks).Concat(preview.Diseases));
    }

    private static string FormatSelectedQuirks(IEnumerable<string> quirkIds)
    {
        var ids = quirkIds.ToArray();
        return ids.Length == 0 ? "空白" : string.Join(", ", ids);
    }

    private static string FormatDefinitionLimit(int? limit)
    {
        return limit switch
        {
            0 => "无限",
            null => "未知",
            _ => limit.Value.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string FormatStorageCapacity(TrinketStorageDefinition? storage)
    {
        return storage is null
            ? "未知"
            : $"{storage.MaxSlots.ToString(CultureInfo.InvariantCulture)}（{storage.ContentSource.DisplayName}）";
    }

    private static string FormatStorageCapacity(int? capacity)
    {
        return capacity?.ToString(CultureInfo.InvariantCulture) ?? "未知";
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string RequireDirectory(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || !Directory.Exists(value.Trim()))
        {
            throw new DirectoryNotFoundException($"{label}不存在：{value}");
        }

        return Path.GetFullPath(value.Trim());
    }

    private void AppendStatus(string message, bool persist = true)
    {
        if (persist)
        {
            CrashDiagnostics.RecordStatus(message);
        }

        StatusTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        StatusTextBox.ScrollToEnd();
    }

    private void AppendStatusSafely(string message, string diagnosticSource)
    {
        try
        {
            AppendStatus(message);
        }
        catch (Exception ex)
        {
            CrashDiagnostics.RecordException(diagnosticSource, ex, message);
        }
    }

    private sealed record TrinketRow(TrinketDefinition Definition)
    {
        public string Id => Definition.Id;
        public string ChineseName => FormatLocalizedName(Definition.LocalizedName.Chinese);
        public string EnglishName => FormatLocalizedName(Definition.LocalizedName.English);
        public string Rarity => Definition.Rarity;
        public string Source => string.IsNullOrWhiteSpace(Definition.SourceLabel)
            ? Definition.Source
            : Definition.SourceLabel;
        public string LimitDisplay => FormatDefinitionLimit(Definition.Limit);
        public bool IsStateful => Definition.IsStateful;
        public bool HasProviderConflict => Definition.HasProviderConflict;
        public string StatefulFieldSummary => string.Join(", ", Definition.StatefulFields);
    }

    private sealed record HeroLevelChoice(int ResolveLevel, int ResolveXp)
    {
        public string DisplayName => $"{ResolveLevel}级 / XP {ResolveXp}";
    }

    private sealed record HeroRow(HeroClassDefinition Definition)
    {
        public string Id => Definition.Id;
        public string ChineseName => FormatLocalizedName(Definition.LocalizedName.Chinese);
        public string EnglishName => FormatLocalizedName(Definition.LocalizedName.English);
        public string Source => string.IsNullOrWhiteSpace(Definition.SourceLabel)
            ? Definition.Source
            : Definition.SourceLabel;
        public string GenerationMode => Definition.Generation switch
        {
            null => "生成模板缺失",
            { IsEnabled: true } => "游戏自然 / 编辑器手动",
            { IsEnabled: false } => "仅编辑器手动",
            _ => "自然状态未知 / 编辑器手动"
        };
        public bool HasProviderConflict => Definition.HasProviderConflict;
        public double? BaseHp => Definition.BaseHp;
        public string LevelSummary => string.IsNullOrWhiteSpace(Definition.ProgressionUnsupportedReason)
            ? $"0-{Math.Max(0, Definition.LevelProfiles.Count - 1)}级完整"
            : $"仅可用 {string.Join(",", Definition.LevelProfiles.Select(profile => profile.ResolveLevel))}级：" +
              Definition.ProgressionUnsupportedReason;
        public int ColourVariationCount => Definition.ColourVariationCount;
        public string QuirkRange => Definition.Generation is not { } generation
            ? "未知"
            : $"正面 {FormatRange(generation.PositiveQuirksMin, generation.PositiveQuirksMax)} / " +
              $"负面 {FormatRange(generation.NegativeQuirksMin, generation.NegativeQuirksMax)}";
        public string CombatSkillSummary =>
            $"定义 {Definition.CombatSkillIds.Count} / 必选 {Definition.GuaranteedCombatSkillIds.Count} / " +
            $"选择上限 {Definition.SelectedCombatSkillsMax?.ToString(CultureInfo.InvariantCulture) ?? "未知"}";
        public string CampingSkillSummary =>
            $"职业 {Definition.ClassCampingSkillIds.Count} / 共享 {Definition.SharedCampingSkillIds.Count}";
        public string RecruitEventSummary => string.Join(", ", Definition.RecruitEvents.Select(item => item.Id));
        public string RuntimeQuirkSummary => string.Join(
            ", ",
            Definition.RuntimeQuirkSignals
                .Select(item => item.QuirkId)
                .Distinct(StringComparer.OrdinalIgnoreCase));

        private static string FormatRange(int? minimum, int? maximum)
        {
            if (minimum is null && maximum is null)
            {
                return "?";
            }

            return minimum == maximum || maximum is null
                ? minimum?.ToString(CultureInfo.InvariantCulture) ?? maximum!.Value.ToString(CultureInfo.InvariantCulture)
                : $"{minimum?.ToString(CultureInfo.InvariantCulture) ?? "?"}–{maximum.Value.ToString(CultureInfo.InvariantCulture)}";
        }
    }

    private static string FormatLocalizedName(string value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;
}
