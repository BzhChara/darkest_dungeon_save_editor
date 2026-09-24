using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DarkestDungeonSaveEditor.App;

public partial class BattleMapView
{
    private enum BattleMenuIcon
    {
        Add, Replace, Remove, MoveParty, Attachment, Battle, Boss, Curio, Treasure, Trap, Obstacle
    }

    private static readonly IReadOnlyDictionary<BattleMenuIcon, ImageSource> MenuIconFallbacks =
        Enum.GetValues<BattleMenuIcon>().ToDictionary(icon => icon, CreateMenuIconFallback);

    private Image CreateMenuIcon(BattleMenuIcon icon)
    {
        // Read the same installed artwork used by the map. Do not bundle game assets.
        var asset = icon switch
        {
            BattleMenuIcon.Battle => "marker_battle.png",
            BattleMenuIcon.Boss => "room_boss.png",
            BattleMenuIcon.Curio => "marker_curio.png",
            BattleMenuIcon.Treasure => "room_treasure.png",
            BattleMenuIcon.Trap => "marker_trap.png",
            BattleMenuIcon.Obstacle => "marker_obstacle.png",
            BattleMenuIcon.MoveParty => "indicator.png",
            _ => null
        };
        var image = new Image
        {
            Source = (asset is null ? null : TryLoadMapIcon(asset)) ?? MenuIconFallbacks[icon],
            Width = 22,
            Height = 22,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private static ImageSource CreateMenuIconFallback(BattleMenuIcon icon)
    {
        var bone = new SolidColorBrush(Color.FromRgb(207, 197, 173));
        var gold = new SolidColorBrush(Color.FromRgb(193, 165, 103));
        var ink = new SolidColorBrush(Color.FromRgb(18, 17, 14));
        var line = new Pen(gold, 1.35) { LineJoin = PenLineJoin.Miter };
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            // A shared view box keeps every symbol independent of font metrics and UI language.
            context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 24, 24));
            void Shape(string path, Brush? fill = null, Pen? stroke = null) =>
                context.DrawGeometry(fill, stroke, Geometry.Parse(path));
            switch (icon)
            {
                case BattleMenuIcon.Add:
                    Shape("M11,3 L13,3 13,11 21,11 21,13 13,13 13,21 11,21 11,13 3,13 3,11 11,11 Z", gold);
                    break;
                case BattleMenuIcon.Replace:
                    Shape("M4,10 C5,4 14,2 19,7 M20,14 C19,20 10,22 5,17", stroke: line);
                    Shape("M15,7 L20,8 20,3 Z M9,17 L4,16 4,21 Z", gold);
                    break;
                case BattleMenuIcon.Remove:
                    Shape("M5,4 L12,10.5 19,4 20,5 13.5,12 20,19 19,20 12,13.5 5,20 4,19 10.5,12 4,5 Z", bone);
                    break;
                case BattleMenuIcon.MoveParty:
                    Shape("M11,2 C14,7 19,8 16,13 L12,16 8,13 C5,9 10,7 11,2 Z", gold);
                    Shape("M10,14 L14,14 13,22 11,22 Z", bone);
                    Shape("M9,16 L15,16", stroke: line);
                    break;
                case BattleMenuIcon.Attachment:
                    Shape("M4,4 L15,4 15,15 4,15 Z M7,7 L12,7 12,12 7,12 Z", stroke: line);
                    Shape("M18,12 L18,22 M13,17 L23,17", stroke: line);
                    break;
                case BattleMenuIcon.Battle:
                    Shape("M3,2 L7,4 17,16 15,18 4,7 Z M21,2 L20,7 9,18 7,16 17,4 Z", bone);
                    Shape("M4,15 L10,21 M14,21 L20,15 M7,18 L4,21 M17,18 L20,21", stroke: line);
                    break;
                case BattleMenuIcon.Boss:
                    Shape("F0 M5,5 L9,2 15,2 19,5 20,13 16,16 16,20 8,20 8,16 4,13 Z " +
                          "M6,9 L10,8 10,12 7,12 Z M14,8 L18,9 17,12 14,12 Z " +
                          "M12,12 L10,15 14,15 Z", bone);
                    Shape("M10,17 L10,20 M13,17 L13,20", stroke: new Pen(ink, 1));
                    break;
                case BattleMenuIcon.Curio:
                    Shape("M12,2 L21,12 12,22 3,12 Z", stroke: line);
                    Shape("M8,9 C8,5 16,5 16,9 C16,11 12,11 12,14 M12,16 L12,18", stroke: new Pen(bone, 1.5));
                    break;
                case BattleMenuIcon.Treasure:
                    Shape("M3,10 L5,5 19,5 21,10 20,20 4,20 Z", stroke: line);
                    Shape("M3,11 L21,11 M6,14 L6,18 M18,14 L18,18", stroke: line);
                    Shape("M10,10 L14,10 14,15 10,15 Z", bone);
                    break;
                case BattleMenuIcon.Trap:
                    Shape("M12,2 L22,12 12,22 2,12 Z", stroke: line);
                    Shape("M5,14 L8,9 11,15 14,9 17,15 20,12", stroke: new Pen(bone, 1.4));
                    break;
                case BattleMenuIcon.Obstacle:
                    Shape("M3,20 L3,12 8,10 7,5 15,3 20,8 19,14 22,20 Z", stroke: line);
                    Shape("M3,12 L11,14 10,20 M8,10 L14,9 19,14 M14,9 L15,3", stroke: line);
                    break;
            }
        }
        var source = new DrawingImage(drawing);
        source.Freeze();
        return source;
    }
}
