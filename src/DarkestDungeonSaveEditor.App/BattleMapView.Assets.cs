using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class BattleMapView : UserControl
{
    private void ConfigureOriginalMapAssets(string? gameDirectory)
    {
        string? normalizedDirectory = null;
        if (!string.IsNullOrWhiteSpace(gameDirectory))
        {
            try
            {
                normalizedDirectory = Path.GetFullPath(gameDirectory.Trim());
            }
            catch (Exception)
            {
                normalizedDirectory = null;
            }
        }

        if (!string.Equals(_gameDirectory, normalizedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _gameDirectory = normalizedDirectory;
            _mapAssetCache.Clear();
        }

        var panel = TryLoadGameImage(MapPanelAsset);
        var panelBrush = CreateOriginalMapBackdrop(panel);
        var room = TryLoadMapIcon("room_empty.png");
        var hall = TryLoadMapIcon("hall_clear.png");
        var party = TryLoadMapIcon("indicator.png");
        _usesOriginalMapAssets = panelBrush is not null && room is not null && hall is not null && party is not null;

        MapViewport.Background = panelBrush is null
            ? new SolidColorBrush(Color.FromRgb(3, 3, 3))
            : panelBrush;

        UpdateMapBadge();
    }

    private void UpdateMapBadge()
    {
        PrototypeBadgeTextBlock.Text = EditorText.Get("BattleMapView_Assets_001");
        PrototypeBadge.ToolTip = _usesOriginalMapAssets
            ? null
            : EditorText.Get("BattleMapView_Assets_002");
    }

    private static ImageBrush? CreateOriginalMapBackdrop(ImageSource? source)
    {
        if (source is not BitmapSource bitmap || bitmap.PixelWidth < 660 || bitmap.PixelHeight < 344)
        {
            return null;
        }

        // panel_map.png also contains the game's map/inventory tab buttons on its right edge.
        // Crop the neutral grid interior in source pixels so DPI metadata cannot shrink it into a corner.
        var gridTexture = new CroppedBitmap(bitmap, new Int32Rect(12, 18, 648, 326));
        gridTexture.Freeze();
        return new ImageBrush(gridTexture)
        {
            Viewbox = new Rect(0, 0, 1, 1),
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewport = new Rect(0, 0, 1, 1),
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            TileMode = TileMode.None,
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            Opacity = 0.92
        };
    }

    private ImageSource? TryLoadMapIcon(string fileName) =>
        TryLoadGameImage(Path.Combine(MapIconDirectory, fileName));

    private ImageSource? TryLoadGameImage(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(_gameDirectory))
        {
            return null;
        }

        if (_mapAssetCache.TryGetValue(relativePath, out var cached))
        {
            return cached;
        }

        ImageSource? source = null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(Path.Combine(_gameDirectory, relativePath), UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            source = image;
        }
        catch (Exception)
        {
            // The editor remains usable when an installation is incomplete or customized.
        }

        _mapAssetCache[relativePath] = source;
        return source;
    }

}
