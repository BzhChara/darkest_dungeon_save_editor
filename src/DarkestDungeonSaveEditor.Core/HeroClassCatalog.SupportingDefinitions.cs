using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace DarkestDungeonSaveEditor.Core;

public static partial class HeroClassCatalog
{
    private static IEnumerable<CampingSkillDefinition> ReadCampingSkills(string path)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(path),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        uint threshold = 0;
        if (document.RootElement.TryGetProperty("configuration", out var configuration) && configuration.ValueKind == JsonValueKind.Object)
        {
            threshold = unchecked((uint)(ReadJsonInt(configuration, "class_specific_number_of_classes_threshold") ?? 0));
        }

        if (!document.RootElement.TryGetProperty("skills", out var skills) ||
            skills.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in skills.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var idNode) ||
                idNode.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(idNode.GetString()))
            {
                continue;
            }
            var id = idNode.GetString()!;
            var hasClasses = item.TryGetProperty("hero_classes", out var classNodes) && classNodes.ValueKind == JsonValueKind.Array;
            var heroClasses = hasClasses ? classNodes.EnumerateArray()
                .Where(node => node.ValueKind == JsonValueKind.String)
                .Select(node => node.GetString()!).ToArray() : [];
            yield return new CampingSkillDefinition(
                id,
                heroClasses,
                hasClasses ? (uint)classNodes.GetArrayLength() > threshold : null);
        }
    }

    private static IEnumerable<RecruitEventGroup> ReadRecruitEvents(string path, string source)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(path),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        if (!document.RootElement.TryGetProperty("events", out var events) ||
            events.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var eventNode in events.EnumerateArray())
        {
            if (eventNode.ValueKind != JsonValueKind.Object ||
                !eventNode.TryGetProperty("id", out var idNode) ||
                idNode.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(idNode.GetString()))
            {
                continue;
            }

            var eventId = idNode.GetString()!;
            var recruits = new List<HeroRecruitEventDefinition>();
            if (eventNode.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var dataNode in data.EnumerateArray())
                {
                    if (dataNode.ValueKind != JsonValueKind.Object ||
                        !ReadJsonString(dataNode, "type").Equals("bonus_recruit", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var heroClass = ReadJsonString(dataNode, "string_data");
                    if (string.IsNullOrWhiteSpace(heroClass))
                    {
                        continue;
                    }

                    recruits.Add(new HeroRecruitEventDefinition(
                        eventId,
                        heroClass,
                        ReadJsonDouble(dataNode, "number_data"),
                        source,
                        Path.GetFullPath(path)));
                }
            }

            yield return new RecruitEventGroup(eventId, recruits, source, Path.GetFullPath(path));
        }
    }

}
