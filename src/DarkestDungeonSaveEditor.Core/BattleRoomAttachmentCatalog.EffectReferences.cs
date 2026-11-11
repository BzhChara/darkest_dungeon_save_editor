using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

public static partial class BattleRoomAttachmentCatalog
{
    internal static IReadOnlyList<string> ReadTrapEffectReferences(IReadOnlyList<ActiveContentSource> sources, List<string> issues)
    {
        var resources = new PropResources();
        foreach (var file in ResolveEffectiveResourceFiles(sources, issues))
        {
            try { ReadPropFile(file, resources, validateReferenceStructure: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            { issues.Add($"Trap Effect sources could not be read: {file.Path}: {error.Message}"); }
        }
        // Curio registration follows JSON props and can change an existing
        // object's instance type. Such an object no longer runs trap effects.
        try
        {
            ReadCurioResources(ResolveEffectiveCurioFiles(sources, issues), resources, issues,
                ContentFileOverlay.GetEnabledDlcPrefixes(sources));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { issues.Add($"Trap/curio resource registration could not be verified: {error.Message}"); }
        // Use the same eager parent copy and first/nearest difficulty lookup
        // as placement. Shadowed definitions must not become loot roots.
        var result = new List<string>();
        foreach (var group in resources.Ids.GroupBy(NativeResourceIdentity.HashCString))
        {
            if (group.Count() > 1) issues.Add("Trap prop identities collide under the native hash.");
            foreach (var id in group)
                foreach (var resource in Enumerable.Range(1, 7).Select(level => resources.Find(id, level)).Distinct())
                {
                    if (resource is null) continue;
                    var data = resource.Data;
                    if (data.Rejection is not null) issues.Add(data.Rejection);
                    if (data.InvalidEffects) issues.Add($"Invalid trap Effect list: {id}");
                    if (data.InstanceType != "trap") continue;
                    result.AddRange(data.SuccessEffects);
                    result.AddRange(data.FailEffects);
                }
        }
        return result;
    }
}
