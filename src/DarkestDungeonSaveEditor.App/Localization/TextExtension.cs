using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Data;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

// Language changes take effect on the next startup, so each window resolves text once.
[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension(string key) : MarkupExtension
{
    public string Key { get; } = key;
    public override object ProvideValue(IServiceProvider serviceProvider) => EditorText.Get(Key);
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
        if (EditorText.IsChinese) return;
        var chinese = grid.Columns.OfType<DataGridBoundColumn>()
            .FirstOrDefault(column => column.Binding is Binding { Path.Path: "ChineseName" });
        var english = grid.Columns.OfType<DataGridBoundColumn>()
            .FirstOrDefault(column => column.Binding is Binding { Path.Path: "EnglishName" });
        if (chinese is not null && english is not null)
        {
            // WPF leaves DisplayIndex at -1 until it initializes the column map.
            var targetIndex = chinese.DisplayIndex >= 0 ? chinese.DisplayIndex : grid.Columns.IndexOf(chinese);
            english.DisplayIndex = targetIndex;
        }
    }
}
