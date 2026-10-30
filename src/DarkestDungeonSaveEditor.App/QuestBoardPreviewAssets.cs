using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DarkestDungeonSaveEditor.App;

// Decode art resolved by the active-content snapshot. Direct roots are only used by UI fixtures.
internal sealed class QuestBoardPreviewAssets
{
    private readonly Dictionary<string, ImageSource?> _images = new(StringComparer.OrdinalIgnoreCase);
    private string? _gameDirectory;
    private string[] _roots = [];
    private IReadOnlyDictionary<string, string>? _resolvedImages;
    private const string QuestArt = "campaign/town/quest_select/";

    internal bool Configure(string? gameDirectory)
    {
        var path = gameDirectory?.Trim();
        if (string.Equals(_gameDirectory, path, StringComparison.OrdinalIgnoreCase)) return false;
        _gameDirectory = path;
        _images.Clear();
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

    internal Grid QuestIcon(QuestBoardChoice choice, double size)
    {
        var visual = new Grid { Width = size, Height = size, IsHitTestVisible = false };
        var length = choice.SavedQuest is { } savedLength ? Image(savedLength.LengthIconPath) :
            QuestImage($"quest_select_length_{(choice.IsGenerated ? "generated" : "plot")}_{choice.Length}.png");
        var type = choice.SavedQuest is { } savedIcon ? Image(savedIcon.IconPath) : QuestImage(choice.Icon.StartsWith("plot_", StringComparison.Ordinal)
            ? $"quest_select_{choice.Icon}.png"
            : $"quest_select_{choice.Icon}_{choice.Difficulty}.png");
        if (length is not null) visual.Children.Add(Picture(length, size, size));
        if (type is not null) visual.Children.Add(Picture(type, size, size));
        else visual.Children.Add(MapMenuIcons.Create(choice.IsGenerated ? MapMenuIcon.Battle : MapMenuIcon.Boss,
            file => Image("panels/icons_map/" + file)));
        return visual;
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
