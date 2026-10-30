using System.Windows.Controls;

namespace DarkestDungeonSaveEditor.App;

public partial class BattleMapView
{
    private Image CreateMenuIcon(MapMenuIcon icon) => MapMenuIcons.Create(icon, TryLoadMapIcon);
}
