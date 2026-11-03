using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class QuestBoardPreview
{
    private void AddRegionHeader(Canvas panel, QuestBoardRegion region, IReadOnlyList<QuestBoardRegion> alternatives)
    {
        var layout = _assets.Layout;
        var image = _assets.QuestImage(region.HasVisibleProgression ? "dungeon_progressionbar.png" : "dungeon_no_progressionbar.png");
        var dimensions = QuestBoardPreviewAssets.PixelSize(image, 282, 84);
        var bar = QuestBoardPreviewAssets.Picture(image, dimensions.Width, dimensions.Height);
        Canvas.SetLeft(bar, layout.BackgroundOffset.X);
        Canvas.SetTop(bar, layout.BackgroundOffset.Y);
        panel.Children.Add(bar);
        if (region.HasVisibleProgression)
        {
            if (region.ProgressionLevel.HasValue)
            {
                var fill = new Rectangle
                {
                    Width = layout.ProgressSize.X * region.ProgressionFraction, Height = layout.ProgressSize.Y,
                    Fill = new SolidColorBrush(Color.FromRgb(126, 20, 20)), IsHitTestVisible = false,
                    Tag = "DungeonProgress"
                };
                Canvas.SetLeft(fill, layout.ProgressOffset.X);
                Canvas.SetTop(fill, layout.ProgressOffset.Y);
                panel.Children.Add(fill);
            }
            var level = new TextBlock
            {
                Text = region.ProgressionLevel?.ToString() ?? "?", Tag = "DungeonLevel",
                FontFamily = EditorTypography.Heading, FontSize = 24, FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("GoldBrush"), Width = 36, TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(level, layout.LevelOffset.X - 18);
            Canvas.SetTop(level, layout.LevelOffset.Y);
            panel.Children.Add(level);
        }
        if (alternatives.Count > 1)
        {
            var nameWidth = alternatives.Max(item => MeasureRegionName(item.Name, 24));
            var headerWidth = Math.Max(40, nameWidth + 40);
            var choice = new ComboBox
            {
                ItemsSource = alternatives, DisplayMemberPath = nameof(QuestBoardRegion.Name),
                SelectedValuePath = nameof(QuestBoardRegion.Id), SelectedValue = region.Id,
                Width = headerWidth, Height = 38,
                MinHeight = 0, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0),
                FontFamily = EditorTypography.Heading, FontSize = 24,
                Foreground = (Brush)FindResource("GoldBrush"), Tag = "RegionChoice",
                ToolTip = EditorText.Get("QuestBoard_RegionChoice")
            };
            Canvas.SetLeft(choice, layout.NameOffset.X + 8 - headerWidth);
            Canvas.SetTop(choice, layout.NameOffset.Y);
            BindRegionContextMenu(choice, region.Id);
            choice.SelectionChanged += (_, _) =>
            {
                if (!CanInteract() || choice.SelectedValue is not string id || id == region.Id) return;
                _regionChoices[(region.X, region.Y)] = id;
                SelectSample(_samples.FirstOrDefault(sample => sample.RegionId == id)?.Key);
            };
            panel.Children.Add(choice);
        }
        else
        {
            var name = new TextBlock
            {
                Text = region.Name, FontFamily = EditorTypography.Heading, FontSize = 27,
                Foreground = (Brush)FindResource("GoldBrush"), FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Right, Width = MeasureRegionName(region.Name, 27, FontWeights.Bold),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 1 }
            };
            var header = new Button
            {
                Style = (Style)FindResource("BoardMapButton"), Width = name.Width + 8, Height = 38,
                Content = name, ToolTip = EditorText.Format("QuestBoard_RegionHint", region.Name)
            };
            // The configured X is the text's right anchor, not a width limit.
            Canvas.SetLeft(header, layout.NameOffset.X - name.Width - 4);
            Canvas.SetTop(header, layout.NameOffset.Y);
            BindRegionContextMenu(header, region.Id);
            header.Click += (_, _) => SelectSample(_samples.FirstOrDefault(sample => sample.RegionId == region.Id)?.Key);
            panel.Children.Add(header);
        }
    }

    private static double MeasureRegionName(string name, double fontSize, FontWeight? weight = null)
    {
        var text = new TextBlock
        {
            Text = name, FontFamily = EditorTypography.Heading, FontSize = fontSize,
            FontWeight = weight ?? FontWeights.Normal
        };
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(1, Math.Ceiling(text.DesiredSize.Width));
    }

    private void BindRegionContextMenu(FrameworkElement header, string regionId)
    {
        header.MouseRightButtonUp += (_, e) =>
        {
            ShowMenu(regionId, null, header);
            e.Handled = true;
        };
    }
}
