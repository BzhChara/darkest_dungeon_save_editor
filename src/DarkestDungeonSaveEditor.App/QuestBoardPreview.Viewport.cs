using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class QuestBoardPreview
{
    // Keep the current town framing: native content runs from the Darkest
    // Dungeon header at Y=90 to the town's first quest row ending at Y=884.
    // Region choices and quest counts must not move this vertical reference.
    private const double EmbeddedVerticalCenter = 487;

    private enum MapViewMode { Default, Fit, Manual }
    private MapViewMode _viewMode = MapViewMode.Default;
    private bool _rightDown;
    private bool _panning;
    private Point _dragOrigin;
    private Point _offsetOrigin;

    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_ready) return;
        RefreshMapView();
    }

    private void RefreshMapView()
    {
        if (MapViewport.ActualWidth <= 0 || MapViewport.ActualHeight <= 0) return;
        if (_viewMode == MapViewMode.Default) CenterMap(MinimumScale);
        else if (_viewMode == MapViewMode.Fit || MapScale.ScaleX < MinimumScale) FitBoard();
        else { ClampMapOffset(); UpdateZoomControls(); }
    }

    private double WholeMapScale => Math.Min(MapViewport.ActualWidth / MapSurface.Width,
        MapViewport.ActualHeight / MapSurface.Height);

    // Only the full-map window can fit below 50%. The embedded view keeps its
    // centered crop as the minimum, including Fit and wheel/button zoom-out.
    private double MinimumScale => _expandedWindow is null ? Math.Max(0.5, WholeMapScale) : WholeMapScale;

    private Rect MapContentBounds()
    {
        var bounds = Rect.Empty;
        foreach (var panel in MapCanvas.Children.OfType<Canvas>())
        {
            // Direct children are the region bar/header and quest buttons. Do not
            // include transparent hit areas or the rotating selection splatter.
            foreach (var child in panel.Children.OfType<FrameworkElement>())
            {
                if (!(child.Width > 0 && child.Height > 0)) continue;
                var left = Canvas.GetLeft(child);
                var top = Canvas.GetTop(child);
                bounds.Union(new Rect(Canvas.GetLeft(panel) + (double.IsNaN(left) ? 0 : left),
                    Canvas.GetTop(panel) + (double.IsNaN(top) ? 0 : top), child.Width, child.Height));
            }
        }
        return bounds.IsEmpty ? new Rect(0, 0, MapSurface.Width, MapSurface.Height) : bounds;
    }

    private Point CenteredMapOffset(double scale)
    {
        var content = _expandedWindow is null ? MapContentBounds() :
            new Rect(0, 0, MapSurface.Width, MapSurface.Height);
        var x = MapViewport.ActualWidth / 2 - (content.Left + content.Width / 2) * scale;
        var centerY = _expandedWindow is null ? EmbeddedVerticalCenter : MapSurface.Height / 2;
        var y = MapViewport.ActualHeight / 2 - centerY * scale;
        // Keep the background covering the viewport where it is large enough;
        // the full-map window retains its original whole-image centering.
        var width = MapSurface.Width * scale;
        var height = MapSurface.Height * scale;
        return new Point(width <= MapViewport.ActualWidth ? (MapViewport.ActualWidth - width) / 2 :
                Math.Clamp(x, MapViewport.ActualWidth - width, 0),
            height <= MapViewport.ActualHeight ? (MapViewport.ActualHeight - height) / 2 :
                Math.Clamp(y, MapViewport.ActualHeight - height, 0));
    }

    private void ClampMapOffset()
    {
        if (MapViewport.ActualWidth <= 0 || MapViewport.ActualHeight <= 0) return;
        // At minimum scale there is no pan range. After zooming, every edge of
        // that centered rectangle must stay covered, even when it is cropped.
        var fitWidth = MapSurface.Width * MinimumScale;
        var fitHeight = MapSurface.Height * MinimumScale;
        var origin = CenteredMapOffset(MinimumScale);
        var left = origin.X;
        var top = origin.Y;
        MapOffset.X = Math.Clamp(MapOffset.X, Math.Min(left, left + fitWidth - MapSurface.Width * MapScale.ScaleX), left);
        MapOffset.Y = Math.Clamp(MapOffset.Y, Math.Min(top, top + fitHeight - MapSurface.Height * MapScale.ScaleY), top);
    }

    private void UpdateZoomControls()
    {
        ZoomText.Text = $"{MapScale.ScaleX:P0}";
        ZoomOutButton.IsEnabled = MapScale.ScaleX > MinimumScale + 0.000001;
        ZoomInButton.IsEnabled = MapScale.ScaleX < Math.Max(2.4, MinimumScale) - 0.000001;
    }

    private void FitBoard()
    {
        if (MapViewport.ActualWidth <= 0 || MapViewport.ActualHeight <= 0) return;
        _viewMode = MapViewMode.Fit;
        CenterMap(MinimumScale);
    }

    private void CenterMap(double scale)
    {
        MapScale.ScaleX = MapScale.ScaleY = scale;
        var origin = CenteredMapOffset(scale);
        MapOffset.X = origin.X;
        MapOffset.Y = origin.Y;
        UpdateZoomControls();
    }

    private void SetZoom(double scale, Point anchor)
    {
        if (!CanInteract()) return;
        CloseMenu();
        if (MinimumScale <= 0) return;
        scale = Math.Clamp(scale, MinimumScale, Math.Max(2.4, MinimumScale));
        if (scale <= MinimumScale + 0.000001) { FitBoard(); return; }
        var old = MapScale.ScaleX;
        MapOffset.X = anchor.X - (anchor.X - MapOffset.X) / old * scale;
        MapOffset.Y = anchor.Y - (anchor.Y - MapOffset.Y) / old * scale;
        MapScale.ScaleX = MapScale.ScaleY = scale;
        _viewMode = MapViewMode.Manual;
        ClampMapOffset();
        UpdateZoomControls();
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Zoom(1 / 1.15);
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Zoom(1.15);
    private void ResetZoom_Click(object sender, RoutedEventArgs e) =>
        SetZoom(1, new Point(MapViewport.ActualWidth / 2, MapViewport.ActualHeight / 2));
    private void Zoom(double factor) => SetZoom(MapScale.ScaleX * factor, new Point(MapViewport.ActualWidth / 2, MapViewport.ActualHeight / 2));
    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        if (!CanInteract()) return;
        FitBoard();
    }
    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        SetZoom(MapScale.ScaleX * (e.Delta > 0 ? 1.12 : 1 / 1.12), e.GetPosition(MapViewport));
        e.Handled = true;
    }

    private void Viewport_RightDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanInteract()) return;
        _rightDown = true;
        _panning = false;
        _dragOrigin = e.GetPosition(MapViewport);
        _offsetOrigin = new Point(MapOffset.X, MapOffset.Y);
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_rightDown || e.RightButton != MouseButtonState.Pressed) return;
        if (PanMap(e.GetPosition(MapViewport) - _dragOrigin)) e.Handled = true;
    }

    private bool PanMap(Vector delta)
    {
        if (!_panning && delta.Length > 5)
        {
            CloseMenu();
            _panning = true;
            // A drag with no travel range must retain its automatic view mode.
            if (MapScale.ScaleX > MinimumScale + 0.000001) _viewMode = MapViewMode.Manual;
            Mouse.Capture(MapViewport);
            MapViewport.Cursor = Cursors.SizeAll;
        }
        if (!_panning) return false;
        MapOffset.X = _offsetOrigin.X + delta.X;
        MapOffset.Y = _offsetOrigin.Y + delta.Y;
        ClampMapOffset();
        return true;
    }

    private void Viewport_RightUp(object sender, MouseButtonEventArgs e)
    {
        if (_panning) e.Handled = true;
        EndPan();
    }

    private void Viewport_LostMouseCapture(object sender, MouseEventArgs e) => EndPan();
    private void EndPan()
    {
        _rightDown = _panning = false;
        MapViewport.Cursor = Cursors.Arrow;
        if (ReferenceEquals(Mouse.Captured, MapViewport)) Mouse.Capture(null);
    }

    private void SetExpandedDetails(bool expanded)
    {
        EstateMapHeader.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        DetailsViewport.Height = expanded ? 750 : 610;
        Canvas.SetTop(DetailsCanvas, expanded ? 0 : -140);
        RewardsScroll.ScrollToTop();
    }

    private void FitExpandedWindow(Window window)
    {
        // Size the opening window around a whole 16:9 map rectangle. Measure the
        // actual chrome/toolbars instead of assuming a particular Windows DPI.
        var mapUnits = Math.Floor(Math.Min(MapViewport.ActualWidth / 16, MapViewport.ActualHeight / 9));
        if (mapUnits <= 0) return;
        var oldWidth = window.ActualWidth;
        var oldHeight = window.ActualHeight;
        window.Width = Math.Max(window.MinWidth, oldWidth - MapViewport.ActualWidth + mapUnits * 16);
        window.Height = Math.Max(window.MinHeight, oldHeight - MapViewport.ActualHeight + mapUnits * 9);
        window.Left += (oldWidth - window.Width) / 2;
        window.Top += (oldHeight - window.Height) / 2;
    }

    private void Expand_Click(object sender, RoutedEventArgs e)
    {
        if (_expandedWindow is not null) { _expandedWindow.Close(); return; }
        if (!CanInteract()) return;
        CloseMenu();
        var owner = Window.GetWindow(this);
        var previousMode = _viewMode;
        var scale = MapScale.ScaleX;
        var offset = new Point(MapOffset.X, MapOffset.Y);
        Content = null;
        var window = new Window
        {
            Owner = owner, Title = EditorText.Get("QuestBoard_WindowTitle"),
            Width = 1600, Height = 820, MinWidth = 900, MinHeight = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, Content = PreviewRoot,
            Background = (System.Windows.Media.Brush)FindResource("AppBackgroundBrush")
        };
        // The detached root still needs the view's shared styles for menus and dynamically added buttons.
        window.Resources.MergedDictionaries.Add(Resources);
        window.SourceInitialized += (_, _) => NativeWindowTheme.ApplyDarkTitleBar(window);
        window.PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape) { window.Close(); args.Handled = true; } };
        _expandedWindow = window;
        SetExpandedDetails(true);
        LocalizedUi.Set(ExpandButton, System.Windows.Controls.ContentControl.ContentProperty,
            () => EditorText.Get("QuestBoard_Collapse"));
        _viewMode = MapViewMode.Fit;
        window.Loaded += (_, _) => { FitExpandedWindow(window); FitBoard(); };
        try { window.ShowDialog(); }
        finally
        {
            CloseMenu();
            window.Content = null;
            Content = PreviewRoot;
            _expandedWindow = null;
            SetExpandedDetails(false);
            LocalizedUi.Set(ExpandButton, System.Windows.Controls.ContentControl.ContentProperty,
                () => EditorText.Get("QuestBoard_Expand"));
            _viewMode = previousMode;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_viewMode != MapViewMode.Manual) RefreshMapView();
                else if (scale < MinimumScale) FitBoard();
                else
                {
                    MapScale.ScaleX = MapScale.ScaleY = scale;
                    MapOffset.X = offset.X; MapOffset.Y = offset.Y;
                    ClampMapOffset();
                    UpdateZoomControls();
                }
            }));
        }
    }
}
