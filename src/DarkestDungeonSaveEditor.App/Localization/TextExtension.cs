using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Data;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

[MarkupExtensionReturnType(typeof(object))]
public sealed class TextExtension(string key) : MarkupExtension
{
    public string Key { get; } = key;
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        LocalizedUi.CreateBinding(() => EditorText.Get(Key)).ProvideValue(serviceProvider);
}

[MarkupExtensionReturnType(typeof(object))]
public sealed class FontExtension(string kind) : MarkupExtension
{
    public string Kind { get; } = kind;
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        LocalizedUi.CreateBinding(() => Kind == "Heading" ? EditorTypography.Heading : EditorTypography.Body)
            .ProvideValue(serviceProvider);
}

// Binding owns each display callback, so closing a window releases its state.
// Only this process-wide notification source subscribes to the Core event.
internal sealed class LocalizedUi : INotifyPropertyChanged
{
    private static readonly LocalizedUi Instance = new();
    public int Revision { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    private LocalizedUi() => EditorText.CultureChanged += (_, _) =>
    {
        void Refresh()
        {
            Revision++;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Revision)));
        }
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Refresh();
        else if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke((Action)Refresh);
    };

    internal static Binding CreateBinding(Func<object> value) => new(nameof(Revision))
    {
        Source = Instance,
        Mode = BindingMode.OneWay,
        Converter = new DisplayConverter(value)
    };

    internal static void Set(DependencyObject target, DependencyProperty property, Func<object> value) =>
        BindingOperations.SetBinding(target, property, CreateBinding(value));

    private sealed class DisplayConverter(Func<object> value) : IValueConverter
    {
        public object Convert(object input, Type targetType, object parameter, CultureInfo culture) => value();
        public object ConvertBack(object input, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

public static class EditorTypography
{
    public static FontFamily Body => new(EditorText.IsChinese
        ? "SimHei, Microsoft YaHei UI, Segoe UI" : "Segoe UI, Microsoft YaHei UI");
    public static FontFamily Heading => new(EditorText.IsChinese
        ? "SimSun, Microsoft YaHei UI, Segoe UI" : "Segoe UI, Microsoft YaHei UI");
}

internal static class EditorNameColumns
{
    internal static void Apply(DataGrid grid)
    {
        var chinese = grid.Columns.OfType<DataGridBoundColumn>()
            .FirstOrDefault(column => column.Binding is Binding { Path.Path: "ChineseName" });
        var english = grid.Columns.OfType<DataGridBoundColumn>()
            .FirstOrDefault(column => column.Binding is Binding { Path.Path: "EnglishName" });
        if (chinese is not null && english is not null)
        {
            // WPF leaves DisplayIndex at -1 until it initializes the column map.
            var targetIndex = Math.Min(
                chinese.DisplayIndex >= 0 ? chinese.DisplayIndex : grid.Columns.IndexOf(chinese),
                english.DisplayIndex >= 0 ? english.DisplayIndex : grid.Columns.IndexOf(english));
            (EditorText.IsChinese ? chinese : english).DisplayIndex = targetIndex;
        }
    }
}
