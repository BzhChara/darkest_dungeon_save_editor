using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.Core;
using Microsoft.Win32;

namespace DarkestDungeonSaveEditor.App;

public partial class MainWindow : Window
{
    private void TitleLogoImage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_titleLogoFrames is null)
        {
            var sheet = new BitmapImage();
            sheet.BeginInit();
            sheet.CacheOption = BitmapCacheOption.OnLoad;
            sheet.UriSource = new Uri(
                "pack://application:,,,/DarkestDungeonSaveEditor.App;component/Assets/dd-title-logo-loop.png",
                UriKind.Absolute);
            sheet.EndInit();
            sheet.Freeze();

            _titleLogoFrames = Enumerable.Range(0, TitleLogoFrameCount)
                .Select(frameIndex =>
                {
                    var frame = new CroppedBitmap(
                        sheet,
                        new Int32Rect(
                            frameIndex % TitleLogoFrameColumns * TitleLogoFrameWidth,
                            frameIndex / TitleLogoFrameColumns * TitleLogoFrameHeight,
                            TitleLogoFrameWidth,
                            TitleLogoFrameHeight));
                    frame.Freeze();
                    return frame;
                })
                .ToArray();
            _titleLogoFrameIndex = 0;
            TitleLogoImage.Source = _titleLogoFrames[0];
        }

        _titleLogoTimer ??= new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TitleLogoFrameInterval
        };
        _titleLogoTimer.Tick -= TitleLogoTimer_Tick;
        _titleLogoTimer.Tick += TitleLogoTimer_Tick;
        _titleLogoTimer.Start();
    }

    private void TitleLogoImage_Unloaded(object sender, RoutedEventArgs e) => _titleLogoTimer?.Stop();

    private void TitleLogoTimer_Tick(object? sender, EventArgs e)
    {
        if (_titleLogoFrames is null || _titleLogoFrames.Length == 0)
        {
            return;
        }

        _titleLogoFrameIndex = (_titleLogoFrameIndex + 1) % _titleLogoFrames.Length;
        TitleLogoImage.Source = _titleLogoFrames[_titleLogoFrameIndex];
    }

}
