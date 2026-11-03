using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DarkestDungeonSaveEditor.App;

// Native task textures include transparent padding that can overlap adjacent
// tasks. Keep the normal button behavior, but only claim painted artwork.
internal sealed class QuestBoardTaskButton : Button
{
    internal required Func<Point, bool> ContainsArtwork { get; init; }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        // The shared template still renders hover/focus feedback. Its rectangle
        // must not intercept input before this control's artwork hit test runs.
        if (VisualTreeHelper.GetChildrenCount(this) > 0 && VisualTreeHelper.GetChild(this, 0) is UIElement root)
            root.IsHitTestVisible = false;
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters) =>
        ContainsArtwork(parameters.HitPoint) ? new PointHitTestResult(this, parameters.HitPoint) : null;
}
