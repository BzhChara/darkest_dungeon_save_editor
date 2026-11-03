using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using DarkestDungeonSaveEditor.App;

internal static partial class ContractSuite
{
    public static async Task RunBattleMapViewportOnlyAsync(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "artifacts", "battle-map-viewport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await RunWpfContractsAsync(() =>
        {
            VerifyBattleMapViewport(root);
            return Task.CompletedTask;
        });
        Console.WriteLine("PASS: Fitted 100%, zoom/pan reset, resize, refreshed topology, reload, language and unavailable states.");
        Console.WriteLine("Artifacts: " + root);
    }

    private static void VerifyBattleMapViewport(string root)
    {
        // Reduced map topology exercises presentation only; no profile is read or written.
        BattleMapSnapshot Snapshot(int span, string identity)
        {
            var areas = new[] { (0, 0), (span / 2, 8), (span, 16) }.Select((position, index) =>
                new BattleMapAreaSnapshot("roo" + index, index, BattleMapAreaKind.Room,
                    BattleMapTileKnowledge.Scouted, 2, false,
                    [new BattleMapTileSnapshot("tile0", 0, position.Item1, position.Item2, 3, 0, 0,
                        BattleMapTileKnowledge.Scouted, 2, BattleMapTileContent.Nothing, 0, 0, 0, -1, 7, false)]))
                .ToArray();
            return new BattleMapSnapshot(root, "map", "raid", "mapHash", "raidHash", "crypts", 1, 1,
                0, "roo0", null, null, 0, "roo0", 0, 0, "roo0", false, [], areas, [], DateTime.UtcNow)
                { RaidIdentity = identity };
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var view = new BattleMapView { UsesSharedProfileMonitor = true };
        var window = new Window
        {
            Content = view, Width = 1120, Height = 720, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual
        };
        T Named<T>(string name) => (T)view.FindName(name);
        var canvas = Named<Canvas>("MapCanvas");
        var viewport = Named<Border>("MapViewport");
        var scale = Named<ScaleTransform>("MapScaleTransform");
        var offset = Named<TranslateTransform>("MapTranslateTransform");
        var percent = Named<TextBlock>("ZoomTextBlock");
        var reset = Named<Button>("ResetZoomButton");
        void Layout()
        {
            window.UpdateLayout();
            FlushLanguageBindings();
            window.UpdateLayout();
        }
        void Render(BattleMapSnapshot snapshot, bool fit)
        {
            typeof(BattleMapView).GetMethod("RenderSnapshot", flags)!.Invoke(view, [snapshot, fit]);
            Layout();
        }
        double ExpectedFit() => Math.Clamp(Math.Min((viewport.ActualWidth - 56) / canvas.Width,
            (viewport.ActualHeight - 56) / canvas.Height), 0.45, 1.25);
        Point Offset() => new(offset.X, offset.Y);
        void CheckDefault()
        {
            var fit = ExpectedFit();
            Assert(Math.Abs(scale.ScaleX - fit) < 0.000001 && scale.ScaleY == scale.ScaleX && percent.Text == "100%",
                "The existing fitted drawing size must remain unchanged and be displayed as 100%.");
            Assert(Math.Abs(offset.X - (viewport.ActualWidth - canvas.Width * fit) / 2) < 0.000001 &&
                   Math.Abs(offset.Y - (viewport.ActualHeight - canvas.Height * fit) / 2) < 0.000001,
                "The default view must center the current map in both directions.");
        }
        void Click(string name)
        {
            Named<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout();
        }
        var longMap = Snapshot(80, "long");
        var shortMap = Snapshot(32, "short");
        try
        {
            window.Show();
            Layout();
            Assert(!reset.IsEnabled && percent.Text == "100%" && view.FindName("FitMapButton") is null,
                "The empty map must disable the single reset control and have no duplicate Fit button.");
            foreach (var language in new[] { "en", "zh-CN" })
            {
                EditorText.Initialize(language, EditorText.SystemCulture);
                view.RefreshLanguage();
                window.Width = 1120;
                window.Height = 720;
                Render(longMap, fit: true);
                CheckDefault();
                var originalFit = scale.ScaleX;
                RenderContractWindow(window, new Size(1120, 720), Path.Combine(root, language + "-default.png"));

                Click("ZoomInButton");
                Assert(Math.Abs(scale.ScaleX - originalFit * 1.15) < 0.000001 && percent.Text == "115%",
                    "Zoom-in must report the magnification relative to the fitted default.");
                var anchor = new Point(viewport.ActualWidth / 2, viewport.ActualHeight / 2);
                typeof(BattleMapView).GetMethod("SetZoom", flags)!.Invoke(view, [originalFit * 2, anchor]);
                Assert(percent.Text == "200%", "Twice the default drawing size must read 200%.");
                offset.X -= 37;
                offset.Y += 19;
                var manualScale = scale.ScaleX;
                var manualOffset = Offset();
                window.Width = 1360;
                window.Height = 840;
                Layout();
                Assert(scale.ScaleX == manualScale && Offset() == manualOffset &&
                       percent.Text == $"{manualScale / ExpectedFit() * 100:0}%" && percent.Text != "200%",
                    "A resized manual view must keep its transform while refreshing the fit-relative percentage.");

                window.Content = null;
                Layout();
                window.Content = view;
                Layout();
                Assert(scale.ScaleX == manualScale && Offset() == manualOffset,
                    "Unloading and reloading the map control must retain a manually chosen view.");
                view.RefreshLanguage();
                Assert(scale.ScaleX == manualScale && Offset() == manualOffset,
                    "Refreshing localized visuals must retain zoom and pan.");

                Render(shortMap, fit: false);
                Assert(scale.ScaleX == manualScale && Offset() == manualOffset &&
                       percent.Text == $"{manualScale / ExpectedFit() * 100:0}%",
                    "A refreshed map with different bounds must update the reference without changing manual framing.");
                Click("ResetZoomButton");
                CheckDefault();
                window.Width = 1120;
                window.Height = 720;
                Layout();
                CheckDefault();

                for (var index = 0; index < 30; index++) Click("ZoomOutButton");
                Assert(scale.ScaleX == 0.45 && percent.Text == $"{0.45 / ExpectedFit() * 100:0}%",
                    "The existing lower drawing-scale limit must remain unchanged, including values below the fitted 100%.");
                for (var index = 0; index < 30; index++) Click("ZoomInButton");
                Assert(scale.ScaleX == 2.4 && percent.Text == $"{2.4 / ExpectedFit() * 100:0}%",
                    "The existing upper drawing-scale limit must remain unchanged.");
                Render(longMap, fit: true);
                CheckDefault();

                Render(longMap with { IsWaveBattle = true }, fit: false);
                Assert(!reset.IsEnabled && percent.Text == "100%", "Wave state must disable reset and discard a stale percentage.");
                Render(longMap with { IsTutorialRaid = true }, fit: false);
                Assert(!reset.IsEnabled, "Tutorial state must disable reset.");
                Render(shortMap, fit: true);
                Assert(reset.IsEnabled, "An ordinary map must restore navigation after an unavailable state.");
                CheckDefault();
                view.ClearProfile();
                Assert(!reset.IsEnabled && percent.Text == "100%", "Clearing the profile must reset the disabled percentage label.");
            }
        }
        finally
        {
            view.ClearProfile();
            window.Close();
        }
    }
}
