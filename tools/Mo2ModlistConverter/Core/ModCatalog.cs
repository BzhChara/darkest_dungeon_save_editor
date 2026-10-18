using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace DarkestDungeon.ModOrder;

internal static class ModCatalog
{
    internal static List<ModlistEntry> ReadModlist(byte[] bytes)
    {
        var result = new List<ModlistEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = InputReader.Text(bytes).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line[0] is not ('+' or '-'))
                throw new InvalidDataException($"modlist.txt line {i + 1}: unsupported entry '{line}'. Only managed + / - entries are supported.");
            var name = line[1..].Trim();
            if (!IsFolderName(name) || !seen.Add(name))
                throw new InvalidDataException($"modlist.txt line {i + 1}: invalid or duplicate folder '{name}'.");
            result.Add(new(name, line[0]));
        }
        return result;
    }

    private static bool IsFolderName(string name) => name.Length > 0 && name is not ("." or "..") &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.EndsWith('.') && !name.EndsWith(' ') &&
        !Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static List<InstalledMod> ReadInstalled(string root, InputReader reader)
    {
        if (!reader.DirectoryExists(root)) throw new DirectoryNotFoundException($"MO2 mods directory is missing: {root}");
        var result = new List<InstalledMod>();
        foreach (var directory in reader.Directories(root))
        {
            var name = Path.GetFileName(directory);
            if (name.EndsWith("_separator", StringComparison.Ordinal) ||
                Regex.IsMatch(name, @"backup[0-9]*$", RegexOptions.CultureInvariant)) continue;
            try { result.Add(ReadMod(directory, reader)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or XmlException or ArgumentException)
            { result.Add(new(name, [], ex.Message)); }
        }
        return result;
    }

    private static InstalledMod ReadMod(string directory, InputReader reader)
    {
        var folder = Path.GetFileName(directory);
        var plain = reader.Read(Path.Combine(directory, "project.xml"));
        var hidden = reader.Read(Path.Combine(directory, "project.xml.mohidden"));
        var project = plain is null ? hidden is null ? (Title: "", PublishedId: "") : InputReader.Project(hidden) : InputReader.Project(plain);
        if (plain is not null && hidden is not null && project != InputReader.Project(hidden))
            throw new InvalidDataException("project.xml and project.xml.mohidden have conflicting identities.");
        var metaBytes = reader.Read(Path.Combine(directory, "meta.ini"));
        var meta = ReadSyncSettings(metaBytes is null ? "" : InputReader.Text(metaBytes));
        bool? isWorkshop = null;
        if (meta.TryGetValue("isworkshop", out var kind))
            isWorkshop = bool.TryParse(kind, out var flag) ? flag
                : throw new InvalidDataException("Invalid sync-tool isworkshop flag in meta.ini.");
        var metaId = meta.GetValueOrDefault("id", "");
        var projectId = ValidWorkshopId(project.PublishedId) ? project.PublishedId : "";
        if (isWorkshop != false && metaId.Length > 0 && !ValidWorkshopId(metaId))
            throw new InvalidDataException("Invalid sync-tool Workshop id in meta.ini.");
        if (isWorkshop != false && metaId.Length > 0 && projectId.Length > 0 && metaId != projectId)
            throw new InvalidDataException("Workshop id in meta.ini disagrees with project.xml.");
        var workshopId = metaId.Length > 0 ? metaId : projectId;
        var candidates = new List<NativeId>();
        // Explicit local metadata wins even when the copied project retains a Workshop ID.
        if (isWorkshop == false)
        {
            if (project.Title.Length > 0) candidates.Add(new("mod_local_source", project.Title));
        }
        else if (isWorkshop == true || metaId.Length > 0)
        {
            if (workshopId.Length > 0) candidates.Add(new("Steam", workshopId));
        }
        else
        {
            // Unsynced/manual installations may represent either source. The input save
            // (forward) can disambiguate them. Reverse conversion requires origin metadata.
            if (project.Title.Length > 0) candidates.Add(new("mod_local_source", project.Title));
            if (workshopId.Length > 0) candidates.Add(new("Steam", workshopId));
        }
        return new(folder, candidates, candidates.Count == 0
            ? "No usable native identity. Sync this Mod with the Darkest Dungeon sync tool or supply valid project metadata." : null);
    }

    private static Dictionary<string, string> ReadSyncSettings(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1]; continue; }
            if (section != "Plugins" || line.StartsWith(';') || line.StartsWith('#')) continue;
            var equals = line.IndexOf('=');
            if (equals < 0) continue;
            var key = Uri.UnescapeDataString(line[..equals].Trim());
            const string prefix = "Darkest Dungeon Mod Sync Tool\\";
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            key = key[prefix.Length..];
            if (key is not ("id" or "isworkshop")) continue;
            var value = line[(equals + 1)..].Trim().Trim('"');
            if (!result.TryAdd(key, value)) throw new InvalidDataException($"Duplicate sync-tool setting: {key}");
        }
        return result;
    }

    internal static bool ValidWorkshopId(string value) => value.All(c => c is >= '0' and <= '9') &&
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 &&
        value == id.ToString(CultureInfo.InvariantCulture);
}
