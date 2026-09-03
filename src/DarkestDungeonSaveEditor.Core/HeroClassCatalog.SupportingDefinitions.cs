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
        var threshold = int.MaxValue;
        if (document.RootElement.TryGetProperty("configuration", out var configuration))
        {
            threshold = ReadJsonInt(configuration, "class_specific_number_of_classes_threshold") ?? threshold;
        }

        if (!document.RootElement.TryGetProperty("skills", out var skills) ||
            skills.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in skills.EnumerateArray())
        {
            var id = ReadJsonString(item, "id");
            var heroClasses = ReadJsonStringArray(item, "hero_classes");
            if (string.IsNullOrWhiteSpace(id) || heroClasses.Count == 0)
            {
                continue;
            }

            var isCanonicalShared = id is "encourage" or "first_aid" or "pep_talk";
            yield return new CampingSkillDefinition(
                id,
                heroClasses,
                isCanonicalShared || heroClasses.Count > threshold);
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
