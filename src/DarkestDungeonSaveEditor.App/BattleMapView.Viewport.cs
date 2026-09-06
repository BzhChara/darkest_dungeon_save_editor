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
    private void BattleMapView_Loaded(object sender, RoutedEventArgs e)
    {
        StartProfileMonitoring();
        if (_isRaidAvailable)
        {
            FitMapToViewport();
        }
        if (!UsesSharedProfileMonitor && _profileDirectory is not null)
        {
            _ = RefreshLiveSnapshotAsync(_profileGeneration);
        }
    }

    private void BattleMapView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isRaidAvailable && _fitToView)
        {
            FitMapToViewport();
        }
    }

    private void BattleMapView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_isRaidAvailable)
        {
            return;
        }

        var factor = e.Delta > 0 ? 1.12 : 1 / 1.12;
        SetZoom(MapScaleTransform.ScaleX * factor, e.GetPosition(MapViewport));
        _fitToView = false;
        e.Handled = true;
    }

    private void BattleMapView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isRaidAvailable)
        {
            return;
        }

        _isRightButtonDown = true;
        _isPanning = false;
        _rightButtonOrigin = e.GetPosition(MapViewport);
        _panOrigin = new Point(MapTranslateTransform.X, MapTranslateTransform.Y);
    }

    private void BattleMapView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isRaidAvailable || !_isRightButtonDown || e.RightButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(MapViewport);
        var delta = current - _rightButtonOrigin;
        if (!_isPanning && Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y) >= PanThreshold)
        {
            _isPanning = true;
            _fitToView = false;
            MapViewport.Cursor = Cursors.SizeAll;
            _ = Mouse.Capture(MapViewport, CaptureMode.Element);
        }

        if (!_isPanning)
        {
            return;
        }

        MapTranslateTransform.X = _panOrigin.X + delta.X;
        MapTranslateTransform.Y = _panOrigin.Y + delta.Y;
        e.Handled = true;
    }

    private void BattleMapView_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isRightButtonDown = false;
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        MapViewport.Cursor = Cursors.Arrow;
        if (ReferenceEquals(Mouse.Captured, MapViewport))
        {
            Mouse.Capture(null);
        }

        e.Handled = true;
    }

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        ZoomAroundViewportCenter(1 / 1.15);
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        ZoomAroundViewportCenter(1.15);
    }

    private void ResetZoomButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isRaidAvailable)
        {
            return;
        }

        _fitToView = false;
        CenterMapAtScale(1);
    }

    private void FitMapButton_Click(object sender, RoutedEventArgs e)
    {
        _fitToView = true;
        FitMapToViewport();
    }

    private void ZoomAroundViewportCenter(double factor)
    {
        if (!_isRaidAvailable)
        {
            return;
        }

        var center = new Point(MapViewport.ActualWidth / 2, MapViewport.ActualHeight / 2);
        SetZoom(MapScaleTransform.ScaleX * factor, center);
        _fitToView = false;
    }

    private void SetZoom(double requestedScale, Point anchor)
    {
        var oldScale = MapScaleTransform.ScaleX;
        var newScale = Math.Clamp(requestedScale, MinimumZoom, MaximumZoom);
        if (Math.Abs(newScale - oldScale) < 0.0001)
        {
            return;
        }

        var contentX = (anchor.X - MapTranslateTransform.X) / oldScale;
        var contentY = (anchor.Y - MapTranslateTransform.Y) / oldScale;
        MapScaleTransform.ScaleX = newScale;
        MapScaleTransform.ScaleY = newScale;
        MapTranslateTransform.X = anchor.X - contentX * newScale;
        MapTranslateTransform.Y = anchor.Y - contentY * newScale;
        UpdateZoomText();
    }

    private void FitMapToViewport()
    {
        if (!_isRaidAvailable || MapViewport.ActualWidth <= 0 || MapViewport.ActualHeight <= 0)
        {
            return;
        }

        var horizontalScale = (MapViewport.ActualWidth - 56) / MapCanvas.Width;
        var verticalScale = (MapViewport.ActualHeight - 56) / MapCanvas.Height;
        CenterMapAtScale(Math.Clamp(Math.Min(horizontalScale, verticalScale), MinimumZoom, 1.25));
    }

    private void CenterMapAtScale(double scale)
    {
        scale = Math.Clamp(scale, MinimumZoom, MaximumZoom);
        MapScaleTransform.ScaleX = scale;
        MapScaleTransform.ScaleY = scale;
        MapTranslateTransform.X = (MapViewport.ActualWidth - MapCanvas.Width * scale) / 2;
        MapTranslateTransform.Y = (MapViewport.ActualHeight - MapCanvas.Height * scale) / 2;
        UpdateZoomText();
    }

    private void UpdateZoomText()
    {
        ZoomTextBlock.Text = $"{MapScaleTransform.ScaleX * 100:0}%";
    }

}
