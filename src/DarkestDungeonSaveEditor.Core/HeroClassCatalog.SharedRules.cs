using System.Text.Json;

namespace DarkestDungeonSaveEditor.Core;

public static partial class HeroClassCatalog
{
    private static HeroInitialQuirkLimits ReadInitialQuirkLimits(
        IReadOnlyList<EffectiveContentFile> files, List<string> issues)
    {
        var limits = HeroInitialQuirkLimits.Default;
        // Same-path providers are already resolved. Each file updates only the
        // explicitly present fields, in IO_FindFiles order (0x1404E81F0).
        foreach (var file in files)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(file.Path), new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Shared rules must be a JSON object.");

                int? ReadLimit(string field, int? previous)
                {
                    if (!NativeJsonReader.TryGetProperty(document.RootElement, field, out var value)) return previous;
                    if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var limit) && limit >= 0)
                        return limit;
                    issues.Add($"Initial quirk limit '{field}' is not a supported nonnegative integer in '{file.Path}'.");
                    return null;
                }
                limits = new HeroInitialQuirkLimits(
                    ReadLimit("quirks_max_positive", limits.Positive),
                    ReadLimit("quirks_max_negative", limits.Negative),
                    ReadLimit("quirks_max_diseases", limits.Diseases));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                // This unreadable slot could set any of the three fields. Later
                // valid fields may establish values again, just as native ordering does.
                limits = new HeroInitialQuirkLimits(null, null, null);
                issues.Add($"Failed to read shared quirk limits '{file.Path}': {ex.Message}");
            }
        }
        return limits;
    }
}
