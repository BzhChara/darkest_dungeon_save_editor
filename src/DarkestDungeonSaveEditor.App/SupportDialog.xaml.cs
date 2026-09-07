using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DarkestDungeonSaveEditor.App;

public partial class SupportDialog : Window
{
    public SupportDialog(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        InitializeComponent();
        Owner = owner;
        var workArea = GetOwnerWorkArea(owner);
        MaxHeight = workArea.Height;
        MaxWidth = workArea.Width;
        Loaded += (_, _) => CloseButton.Focus();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeWindowTheme.ApplyDarkTitleBar(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static Size GetOwnerWorkArea(Window owner)
    {
        const uint monitorDefaultToNearest = 2;
        var monitor = MonitorFromWindow(new WindowInteropHelper(owner).Handle, monitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea.Size;

        // Native monitor bounds are pixels; use the owner's WPF scale for the
        // dialog that will be centered on that monitor, including a smaller secondary screen.
        var dpi = VisualTreeHelper.GetDpi(owner);
        return new Size((info.Work.Right - info.Work.Left) / dpi.DpiScaleX,
            (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
