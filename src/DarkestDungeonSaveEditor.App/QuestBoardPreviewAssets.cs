using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

// Decode art resolved by the active-content snapshot. Direct roots are only used by UI fixtures.
internal sealed class QuestBoardPreviewAssets
{
    private readonly Dictionary<string, ImageSource?> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ImageSource, byte[]> _alphaMasks = new(ReferenceEqualityComparer.Instance);
    private string? _gameDirectory;
    private string[] _roots = [];
    private IReadOnlyDictionary<string, string>? _resolvedImages;
    private const string QuestArt = "campaign/town/quest_select/";
    internal QuestBoardLayout Layout { get; set; } = new();

    internal bool Configure(string? gameDirectory)
    {
        var path = gameDirectory?.Trim();
        if (string.Equals(_gameDirectory, path, StringComparison.OrdinalIgnoreCase)) return false;
        _gameDirectory = path;
        _images.Clear();
        _alphaMasks.Clear();
        _resolvedImages = null;
        _roots = string.IsNullOrWhiteSpace(path) ? [] :
        [
            path,
            Path.Combine(path, "dlc/580100_crimson_court/features/crimson_court"),
            Path.Combine(path, "dlc/735730_color_of_madness")
        ];
        return true;
    }

    internal ImageSource? QuestImage(string name) => Image(QuestArt + name);

    internal void SetResolvedImages(IReadOnlyDictionary<string, string>? paths)
    {
        if (ReferenceEquals(_resolvedImages, paths)) return;
        _resolvedImages = paths;
        _images.Clear();
        _alphaMasks.Clear();
    }

    internal ImageSource? Image(string relativePath)
    {
        if (_images.TryGetValue(relativePath, out var cached)) return cached;
        ImageSource? result = null;
        var paths = _resolvedImages is null ? _roots.Select(root => Path.Combine(root, relativePath)) :
            _resolvedImages.TryGetValue(relativePath, out var resolved) ? new[] { resolved } : [];
        foreach (var candidate in paths)
        {
            try
            {
                var path = Path.GetFullPath(candidate);
                if (!File.Exists(path)) continue;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                result = bitmap;
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException or FileFormatException)
            {
                // Missing/incomplete game artwork leaves a labeled, usable UI preview.
            }
        }
        _images[relativePath] = result;
        return result;
    }

    private (ImageSource? Length, ImageSource? Type) QuestImages(QuestBoardChoice choice)
    {
        var length = choice.SavedQuest is { } savedLength ? Image(savedLength.LengthIconPath) :
            QuestImage($"quest_select_length_{(choice.IsGenerated ? "generated" : "plot")}_{choice.Length}.png");
        var type = choice.SavedQuest is { } savedIcon ? Image(savedIcon.IconPath) : QuestImage(choice.Icon.StartsWith("plot_", StringComparison.Ordinal)
            ? $"quest_select_{choice.Icon}.png"
            : $"quest_select_{choice.Icon}_{choice.Difficulty}.png");
        type ??= MapMenuIcons.Create(choice.IsGenerated ? MapMenuIcon.Battle : MapMenuIcon.Boss,
            file => Image("panels/icons_map/" + file)).Source;
        return (length, type);
    }

    internal static Size PixelSize(ImageSource? image, double fallbackWidth, double fallbackHeight) => image switch
    {
        BitmapSource bitmap => new(bitmap.PixelWidth, bitmap.PixelHeight),
        not null => new(image.Width, image.Height),
        _ => new(fallbackWidth, fallbackHeight)
    };

    internal Rect QuestIconBounds(QuestBoardChoice choice)
    {
        var (length, type) = QuestImages(choice);
        var bounds = Rect.Empty;
        void Include(ImageSource? image, QuestBoardPoint offset, double fallback)
        {
            var size = PixelSize(image, fallback, fallback);
            bounds.Union(new Rect(offset.X - size.Width / 2, offset.Y - size.Height / 2, size.Width, size.Height));
        }
        if (length is not null) Include(length, Layout.LengthIconOffset, 96);
        Include(type, Layout.TypeIconOffset, 48);
        return bounds;
    }

    internal Grid QuestIcon(QuestBoardChoice choice, double? size = null)
    {
        var (length, type) = QuestImages(choice);
        var bounds = QuestIconBounds(choice);
        var scale = size.HasValue ? size.Value / Math.Max(bounds.Width, bounds.Height) : 1;
        var visual = new Grid { Width = bounds.Width * scale, Height = bounds.Height * scale, IsHitTestVisible = false };
        void Add(ImageSource? image, QuestBoardPoint offset, double fallback)
        {
            var dimensions = PixelSize(image, fallback, fallback);
            var layer = Picture(image, dimensions.Width * scale, dimensions.Height * scale);
            layer.RenderTransform = new TranslateTransform((offset.X - bounds.X - bounds.Width / 2) * scale,
                (offset.Y - bounds.Y - bounds.Height / 2) * scale);
            visual.Children.Add(layer);
        }
        if (length is not null) Add(length, Layout.LengthIconOffset, 96);
        Add(type, Layout.TypeIconOffset, 48);
        return visual;
    }

    internal Func<Point, bool> QuestHitTest(QuestBoardChoice choice)
    {
        var (length, type) = QuestImages(choice);
        var bounds = QuestIconBounds(choice);
        Func<Point, bool> Layer(ImageSource? image, QuestBoardPoint offset)
        {
            if (image is null) return _ => false;
            var size = PixelSize(image, 48, 48);
            var rect = new Rect(offset.X - size.Width / 2 - bounds.Left,
                offset.Y - size.Height / 2 - bounds.Top, size.Width, size.Height);
            if (image is not BitmapSource bitmap) return rect.Contains;
            if (!_alphaMasks.TryGetValue(image, out var mask))
            {
                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                converted.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                mask = new byte[bitmap.PixelWidth * bitmap.PixelHeight];
                for (var i = 0; i < mask.Length; i++) mask[i] = pixels[i * 4 + 3];
                _alphaMasks.Add(image, mask);
            }
            return point =>
            {
                var x = (int)Math.Floor(point.X - rect.X);
                var y = (int)Math.Floor(point.Y - rect.Y);
                return x >= 0 && x < bitmap.PixelWidth && y >= 0 && y < bitmap.PixelHeight &&
                    mask[y * bitmap.PixelWidth + x] != 0;
            };
        }
        var lengthHit = Layer(length, Layout.LengthIconOffset);
        var typeHit = Layer(type, Layout.TypeIconOffset);
        return point => lengthHit(point) || typeHit(point);
    }

    internal static System.Windows.Controls.Image Picture(ImageSource? source, double width, double height)
    {
        var image = new System.Windows.Controls.Image
        {
            Source = source, Width = width, Height = height, Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }
}
