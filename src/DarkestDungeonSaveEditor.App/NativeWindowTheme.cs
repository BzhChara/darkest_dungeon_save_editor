using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DarkestDungeonSaveEditor.App;

internal static class NativeWindowTheme
{
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    public static void ApplyDarkTitleBar(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var darkModeEnabled = 1;
            if (DwmSetWindowAttribute(
                    handle,
                    DwmwaUseImmersiveDarkMode,
                    ref darkModeEnabled,
                    sizeof(int)) != 0)
            {
                _ = DwmSetWindowAttribute(
                    handle,
                    DwmwaUseImmersiveDarkModeBefore20H1,
                    ref darkModeEnabled,
                    sizeof(int));
            }

            var captionColor = ToColorRef(red: 0x0B, green: 0x08, blue: 0x08);
            var textColor = ToColorRef(red: 0xD8, green: 0xD4, blue: 0xC7);
            var borderColor = ToColorRef(red: 0x3B, green: 0x15, blue: 0x16);
            _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmwaBorderColor, ref borderColor, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // DWM is unavailable only on unsupported Windows environments; keep the native default.
        }
        catch (EntryPointNotFoundException)
        {
            // Older Windows builds may not expose this entry point; keep the native default.
        }
    }

    private static int ToColorRef(byte red, byte green, byte blue) =>
        red | green << 8 | blue << 16;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
