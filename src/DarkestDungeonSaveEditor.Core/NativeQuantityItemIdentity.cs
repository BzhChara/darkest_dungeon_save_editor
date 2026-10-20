using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

// Catalog keys retain authored names for references and stale-definition guards.
// Saved items instead bind through bounded native type/ID hashes.
internal readonly record struct NativeQuantityItemIdentity(
    QuantityItemStorageKind StorageKind, uint Type, uint Id)
{
    internal static NativeQuantityItemIdentity FromSaved(
        QuantityItemStorageKind kind, string type, string id) =>
        new(kind, NativeInventoryIdentity.HashSavedField(type),
            kind == QuantityItemStorageKind.Wallet ? 0 : NativeInventoryIdentity.HashSavedField(id));

    internal static NativeQuantityItemIdentity FromDefinition(QuantityItemDefinition definition) =>
        definition.IsSaveOnly
            ? FromSaved(definition.StorageKind, definition.PersistedType, definition.PersistedId)
            : new(definition.StorageKind, NativeResourceIdentity.HashCString(definition.PersistedType),
                definition.StorageKind == QuantityItemStorageKind.Wallet
                    ? 0 : NativeResourceIdentity.HashCString(definition.PersistedId));

    internal bool Matches(JsonObject item) =>
        this == FromSaved(StorageKind, JsonSupport.ReadRawString(item, "type"),
            JsonSupport.ReadRawString(item, "id"));

    internal static bool IsSavedTrinketType(string type) =>
        NativeInventoryIdentity.HashSavedField(type) == NativeResourceIdentity.HashCString("trinket");
}
