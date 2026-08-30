using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DarkestDungeonSaveEditor.Core;

public static partial class SteamDiscovery
{
    public static DiscoverySnapshot Discover()
    {
        var issues = new List<string>();
        var steamRoots = DiscoverSteamRoots(issues);
        var libraries = DiscoverLibraries(steamRoots, issues);
        var games = DiscoverGameInstallations(steamRoots, libraries);
        var profiles = DiscoverProfiles(steamRoots, issues);
        return new DiscoverySnapshot(games, profiles, issues);
    }

    public static SaveProfile? SelectDefaultProfile(IReadOnlyList<SaveProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        return profiles.FirstOrDefault(profile =>
                   profile.ProfileId.Equals("profile_0", StringComparison.OrdinalIgnoreCase))
               ?? profiles.FirstOrDefault();
    }

    private static IReadOnlyList<string> DiscoverSteamRoots(List<string> issues)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        TryAddRegistryPath(candidates, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        TryAddRegistryPath(candidates, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        TryAddRegistryPath(candidates, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));

        var roots = candidates
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (roots.Length == 0)
        {
            issues.Add("Steam installation was not discovered automatically.");
        }

        return roots;
    }

    private static IReadOnlyList<string> DiscoverLibraries(IReadOnlyList<string> steamRoots, List<string> issues)
    {
        var libraries = new HashSet<string>(steamRoots, StringComparer.OrdinalIgnoreCase);
        foreach (var steamRoot in steamRoots)
        {
            var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
            {
                continue;
            }

            try
            {
                var text = File.ReadAllText(vdfPath);
                foreach (Match match in LibraryPathRegex().Matches(text))
                {
                    var value = match.Groups[1].Value.Replace("\\\\", "\\", StringComparison.Ordinal);
                    if (Directory.Exists(value))
                    {
                        libraries.Add(Path.GetFullPath(value));
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add($"Failed to read Steam library list '{vdfPath}': {ex.Message}");
            }
        }

        return libraries.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<GameInstallation> DiscoverGameInstallations(
        IReadOnlyList<string> steamRoots,
        IReadOnlyList<string> libraries)
    {
        var steamRoot = steamRoots.FirstOrDefault() ?? string.Empty;
        var results = new List<GameInstallation>();
        foreach (var library in libraries)
        {
            var game = Path.Combine(library, "steamapps", "common", "DarkestDungeon");
            if (!File.Exists(Path.Combine(game, "_windows", "win64", "Darkest.exe")))
            {
                continue;
            }

            results.Add(new GameInstallation(
                steamRoot,
                library,
                game,
                Path.Combine(library, "steamapps", "workshop", "content", "262060")));
        }

        return results;
    }

    private static IReadOnlyList<SaveProfile> DiscoverProfiles(IReadOnlyList<string> steamRoots, List<string> issues)
    {
        var profiles = new List<SaveProfile>();
        foreach (var steamRoot in steamRoots)
        {
            var userdata = Path.Combine(steamRoot, "userdata");
            if (!Directory.Exists(userdata))
            {
                continue;
            }

            try
            {
                foreach (var userDirectory in Directory.EnumerateDirectories(userdata))
                {
                    var remote = Path.Combine(userDirectory, "262060", "remote");
                    if (!Directory.Exists(remote))
                    {
                        continue;
                    }

                    foreach (var profileDirectory in Directory.EnumerateDirectories(remote, "profile_*"))
                    {
                        var estate = Path.Combine(profileDirectory, "persist.estate.json");
                        if (!File.Exists(estate))
                        {
                            continue;
                        }

                        profiles.Add(new SaveProfile(
                            Path.GetFileName(profileDirectory),
                            Path.GetFullPath(profileDirectory),
                            estate,
                            Path.GetFileName(userDirectory),
                            File.GetLastWriteTimeUtc(estate)));
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add($"Failed to inspect Steam userdata '{userdata}': {ex.Message}");
            }
        }

        return profiles
            .DistinctBy(profile => profile.ProfileDirectory, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(profile => profile.LastWriteTimeUtc)
            .ThenBy(profile => profile.ProfileDirectory, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static SaveProfile OpenProfile(string profileDirectory)
    {
        profileDirectory = Path.GetFullPath(profileDirectory);
        var estate = Path.Combine(profileDirectory, "persist.estate.json");
        if (!File.Exists(estate))
        {
            throw new FileNotFoundException("The selected directory does not contain persist.estate.json.", estate);
        }

        var profileId = Path.GetFileName(profileDirectory);
        var userId = Directory.GetParent(Directory.GetParent(Directory.GetParent(profileDirectory)!.FullName)!.FullName)?.Name
            ?? string.Empty;
        return new SaveProfile(profileId, profileDirectory, estate, userId, File.GetLastWriteTimeUtc(estate));
    }

    private static void TryAddRegistryPath(
        HashSet<string> candidates,
        RegistryKey root,
        string keyPath,
        string valueName)
    {
        try
        {
            using var key = root.OpenSubKey(keyPath);
            if (key?.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value))
            {
                candidates.Add(value.Replace('/', Path.DirectorySeparatorChar));
            }
        }
        catch
        {
            // Discovery is best effort; callers can always choose a directory manually.
        }
    }

    [GeneratedRegex("\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPathRegex();
}
