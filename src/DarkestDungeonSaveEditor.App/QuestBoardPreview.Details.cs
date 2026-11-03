using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class QuestBoardPreview
{
    private void SizeRewardPanel()
    {
        var detail = _assets.Layout.Details;
        var rewards = _assets.Layout.Rewards;
        var artSize = QuestBoardPreviewAssets.PixelSize(DetailBackdrop.Source, 400, 843);
        DetailBackdrop.Width = artSize.Width;
        DetailBackdrop.Height = artSize.Height;

        void Position(FrameworkElement element, QuestBoardPoint point)
        {
            Canvas.SetLeft(element, point.X);
            Canvas.SetTop(element, point.Y);
        }
        void Center(TextBlock text, QuestBoardPoint point, double width)
        {
            text.Width = Math.Max(1, width);
            text.TextAlignment = TextAlignment.Center;
            Position(text, new(point.X - text.Width / 2, point.Y));
        }
        Center(QuestName, detail.Name, artSize.Width - 52);
        Position(QuestDescription, detail.Description);
        QuestDescription.Width = detail.DescriptionWidth;
        // Both embedded and full-map views retain the configured text areas.
        // Overflow ends in an ellipsis, without shifting the artwork or adding a scrollbar.
        QuestDescription.Height = TextAreaHeight(QuestDescription, Math.Min(detail.Camping.Y, detail.Specifics.Y) - detail.Description.Y - 12);
        Position(CampingText, detail.Camping);
        Center(QuestSpecs, detail.Specifics, 248);
        Position(ObjectiveHeading, detail.Goals);
        Position(QuestObjective, detail.GoalDescription);
        QuestObjective.Width = Math.Max(1, artSize.Width - detail.GoalDescription.X - 30);
        QuestObjective.Height = TextAreaHeight(QuestObjective, detail.RewardHeading.Y - detail.GoalDescription.Y - 12);
        Center(RewardHeading, detail.RewardHeading, artSize.Width - 52);

        var count = RewardPanel.Children.Count;
        var columns = Math.Min(rewards.Columns, count);
        var images = RewardPanel.Children.OfType<Canvas>().SelectMany(slot => slot.Children.OfType<Image>()).ToArray();
        var rowHeight = images.Select(image => image.Height).DefaultIfEmpty(144).Max();
        var leftOverflow = Math.Max(0, -images.Select(Canvas.GetLeft).DefaultIfEmpty(0).Min());
        var rightOverflow = Math.Max(0, images.Select(image => Canvas.GetLeft(image) + image.Width)
            .DefaultIfEmpty(rewards.Spacing.X).Max() - rewards.Spacing.X);
        RewardPanel.ItemWidth = rewards.Spacing.X;
        RewardPanel.ItemHeight = rewards.Spacing.Y;
        RewardPanel.Width = columns * rewards.Spacing.X;
        RewardPanel.Margin = new Thickness(leftOverflow, 0, rightOverflow, 0);
        RewardPanel.Height = count == 0 ? 0 : (Math.Ceiling(count / (double)rewards.Columns) - 1) * rewards.Spacing.Y + rowHeight;
        for (var index = 0; index < count; index++)
        {
            var rowCount = Math.Min(rewards.Columns, count - index / rewards.Columns * rewards.Columns);
            // Native centering applies to every row, including a partial final row.
            RewardPanel.Children[index].RenderTransform = new TranslateTransform(
                rewards.Centered ? (columns - rowCount) * rewards.Spacing.X / 2 : 0, 0);
        }
        var centeredOffset = rewards.Centered ? (rewards.Columns - columns) * rewards.Spacing.X / 2 : 0;
        Position(RewardInventoryScroll, new(detail.RewardInventory.X + rewards.Start.X + centeredOffset - leftOverflow,
            detail.RewardInventory.Y + rewards.Start.Y));
        // Reserve the vertical scrollbar instead of wrapping an extra column when it appears.
        RewardInventoryScroll.Width = RewardPanel.Width + leftOverflow + rightOverflow + SystemParameters.VerticalScrollBarWidth + 2;
        RewardInventoryScroll.Height = rowHeight + 2;

        // Retain the editor's independent, width-fitted panel. Its scrollable extent
        // follows configured content rather than cropping at the vanilla y=750.
        var bounds = new Rect(0, 0, artSize.Width, 0);
        foreach (var element in new FrameworkElement[] { QuestName, QuestDescription, CampingText, QuestSpecs,
                     ObjectiveHeading, QuestObjective, RewardHeading, RewardInventoryScroll })
        {
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            bounds.Union(new Rect(Canvas.GetLeft(element), Canvas.GetTop(element), element.DesiredSize.Width, element.DesiredSize.Height));
        }
        var top = EstateMapHeader.Visibility == Visibility.Visible ? Math.Min(0, bounds.Top) :
            Math.Min(detail.Name.Y, Math.Min(detail.Description.Y, Math.Min(detail.Camping.Y, detail.Goals.Y)));
        DetailsCanvas.Width = DetailsViewport.Width = bounds.Right - bounds.Left;
        DetailsCanvas.Height = Math.Max(artSize.Height, bounds.Bottom + 14);
        Canvas.SetLeft(DetailsCanvas, -bounds.Left);
        Canvas.SetTop(DetailsCanvas, -top);
        DetailsViewport.Height = Math.Max(1, bounds.Bottom + 14 - top);
    }

    private static double TextAreaHeight(TextBlock text, double available) =>
        Math.Max(0, Math.Floor(available / text.LineHeight)) * text.LineHeight;

    private Canvas CreateRewardSlot(string iconPath, string? amount = null, string? rarity = null)
    {
        var spacing = _assets.Layout.Rewards.Spacing;
        var slot = new Canvas { Width = spacing.X, Height = spacing.Y, Background = Brushes.Transparent, Tag = iconPath };
        void AddImage(string path)
        {
            var source = _assets.Image(path);
            var size = QuestBoardPreviewAssets.PixelSize(source, 72, 144);
            var icon = QuestBoardPreviewAssets.Picture(source, size.Width, size.Height);
            Canvas.SetLeft(icon, (spacing.X - size.Width) / 2);
            slot.Children.Add(icon);
        }
        if (rarity is not null) AddImage($"panels/icons_equip/trinket/rarity_{rarity}.png");
        AddImage(iconPath);
        if (amount is not null)
        {
            var text = new TextBlock
            {
                Text = amount, FontFamily = EditorTypography.Heading, FontSize = 22, FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("GoldBrush"),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 2, ShadowDepth = 1 }
            };
            Canvas.SetLeft(text, 14);
            Canvas.SetTop(text, 4);
            slot.Children.Add(text);
        }
        return slot;
    }
}
