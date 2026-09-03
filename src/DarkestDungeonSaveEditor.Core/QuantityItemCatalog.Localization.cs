using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DarkestDungeonSaveEditor.Core;

public static partial class QuantityItemCatalog
{
    private static IEnumerable<string> GetLocalizationKeys(QuantityItemDefinition definition)
    {
        foreach (var key in ContentLocalizationCatalog.GetInventoryItemKeys(
                     definition.InventoryType,
                     definition.ItemId))
        {
            yield return key;
        }

        if (definition.IsSaveOnly && definition.StorageKind == QuantityItemStorageKind.Wallet)
        {
            foreach (var key in ContentLocalizationCatalog.GetInventoryItemKeys(
                         "heirloom",
                         definition.PersistedType))
            {
                yield return key;
            }
        }
    }

    private static BilingualContentName ResolveLocalizedName(
        ContentLocalizationCatalog localization,
        QuantityItemDefinition definition)
    {
        var result = localization.GetInventoryItemName(definition.InventoryType, definition.ItemId);
        if (!definition.IsSaveOnly || definition.StorageKind != QuantityItemStorageKind.Wallet)
        {
            return result;
        }

        var heirloom = localization.GetInventoryItemName("heirloom", definition.PersistedType);
        return new BilingualContentName(
            string.IsNullOrWhiteSpace(result.Chinese) ? heirloom.Chinese : result.Chinese,
            string.IsNullOrWhiteSpace(result.English) ? heirloom.English : result.English);
    }

}
