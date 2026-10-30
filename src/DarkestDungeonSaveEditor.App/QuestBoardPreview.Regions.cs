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
        // Keep the shipped bar at its native size. Its right-hand skull and
        // level label share the same placement as the game's dungeon layout.
        var bar = QuestBoardPreviewAssets.Picture(_assets.QuestImage(region.HasVisibleProgression
            ? "dungeon_progressionbar.png" : "dungeon_no_progressionbar.png"), 282, 84);
        Canvas.SetLeft(bar, -5);
        Canvas.SetTop(bar, -10);
        panel.Children.Add(bar);
        if (region.HasVisibleProgression)
        {
            if (region.ProgressionLevel.HasValue)
            {
                var fill = new Rectangle
                {
                    Width = 194 * region.ProgressionFraction, Height = 8,
                    Fill = new SolidColorBrush(Color.FromRgb(126, 20, 20)), IsHitTestVisible = false,
                    Tag = "DungeonProgress"
                };
                Canvas.SetLeft(fill, 14);
                Canvas.SetTop(fill, 32);
                panel.Children.Add(fill);
            }
            var level = new TextBlock
            {
                Text = region.ProgressionLevel?.ToString() ?? "?", Tag = "DungeonLevel",
                FontFamily = EditorTypography.Heading, FontSize = 24, FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("GoldBrush"), Width = 36, TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(level, 219 - 18);
            Canvas.SetTop(level, 20);
            panel.Children.Add(level);
        }
        if (alternatives.Count > 1)
        {
            var choice = new ComboBox
            {
                ItemsSource = alternatives, DisplayMemberPath = nameof(QuestBoardRegion.Name),
                SelectedValuePath = nameof(QuestBoardRegion.Id), SelectedValue = region.Id,
                Width = region.HasVisibleProgression ? 198 : 258, Height = 38,
                MinHeight = 0, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0),
                FontFamily = EditorTypography.Heading, FontSize = 24,
                Foreground = (Brush)FindResource("GoldBrush"), Tag = "RegionChoice",
                ToolTip = EditorText.Get("QuestBoard_RegionChoice")
            };
            Canvas.SetTop(choice, -8);
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
                TextAlignment = TextAlignment.Right, Width = 190,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 1 }
            };
            var header = new Button
            {
                Style = (Style)FindResource("BoardMapButton"), Width = 198, Height = 38,
                Content = name, ToolTip = EditorText.Format("QuestBoard_RegionHint", region.Name)
            };
            Canvas.SetTop(header, -8);
            header.Click += (_, _) => SelectSample(_samples.FirstOrDefault(sample => sample.RegionId == region.Id)?.Key);
            panel.Children.Add(header);
        }
    }
}
