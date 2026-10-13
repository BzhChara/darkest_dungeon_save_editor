using DarkestDungeonSaveEditor.Core;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

try
{
    if (args.Length == 4 && args[0] == "--real")
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var content = Mo2Compatibility.Prepare(args[1], args[2], args[3]);
        var first = watch.Elapsed;
        var cached = Mo2Compatibility.Prepare(args[1], args[2], args[3]);
        var cachedElapsed = watch.Elapsed - first;
        Assert(content == cached, "real snapshot reuse");
        var plan = Mo2ContentPlan.Capture(args[1], args[2], args[3], default);
        var settings = Mo2Settings.Read(args[1]);
        Assert(plan.Files.All(file => new[] { settings.ModsDirectory, settings.OverwriteDirectory }.Any(root =>
            file.SourcePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))),
            "real providers come only from MO2 mods or overwrite");
        Assert(plan.Files.All(file => File.ReadAllBytes(file.SourcePath).SequenceEqual(
            File.ReadAllBytes(Path.Combine(content, file.RelativePath)))), "real source copies are byte-identical");
        var profile = new SaveProfile("read-only-probe", Path.GetDirectoryName(args[1])!, "", "", DateTime.UtcNow);
        var snapshot = new ActiveContentSnapshot(profile, "base", [new("local:merge mod", "merge mod", "local", content, 1000)],
            [], "", "", 1, "");
        var inventory = ContentFileInventory.Scan(snapshot);
        Assert(inventory.IsComplete && inventory.Mods.Single().Files.All(file =>
            file.ManifestMatch != ContentManifestMatch.Missing && !file.HasLengthMismatch), "real inventory completeness");
        Console.WriteLine($"PASS real integration: {plan.Files.Count} files, first {first.TotalSeconds:F2}s, cached {cachedElapsed.TotalSeconds:F2}s; {content}");
        return;
    }
    if (args.Length == 2 && args[0] == "--api")
    {
        var context = new AssemblyLoadContext("ordinary-core", isCollectible: true);
        var original = context.LoadFromAssemblyPath(Path.GetFullPath(args[1]));
        var patched = typeof(ActiveContentResolver).Assembly;
        Assert(original.GetName().Name == patched.GetName().Name && original.GetName().Version == patched.GetName().Version,
            "assembly identity");
        Assert(Api(original).SequenceEqual(Api(patched)), "public API parity");
        var ordinaryVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(args[1]).FileVersion;
        Assert(ordinaryVersion == System.Diagnostics.FileVersionInfo.GetVersionInfo(patched.Location).FileVersion, "file version");
        Console.WriteLine("PASS ordinary/replacement Core assembly identity, public API and file version");
        return;
    }

    var tests = new (string Name, Action<Fixture> Run)[]
    {
        ("enabled order, MO2 content and authoritative whitelist", f =>
        {
            f.Mod("Low"); f.Mod("High"); f.Mod("Disabled");
            f.File("Low", "inventory/test.darkest", "low!");
            f.File("High", "inventory/test.darkest", "HIGH");
            f.File("Disabled", "inventory/test.darkest", "nope");
            f.File("High", "inventory/unlisted.darkest", "not visible");
            f.List("+High", "-Disabled", "+Low");
            f.Manifest(("inventory/test.darkest", "HIGH"));
            var snapshot = f.Prepare();
            Assert(File.ReadAllText(Path.Combine(snapshot, "inventory/test.darkest")) == "HIGH", "higher priority wins");
            Assert(!File.Exists(Path.Combine(snapshot, "inventory/unlisted.darkest")), "whitelist enforced");
            Assert(!File.Exists(Path.Combine(f.ModDirectory("High"), "modfiles.txt")), "no MO2 manifest generated");
        }),
        ("overwrite merge retains all IDs in the ordinary catalog", f =>
        {
            f.Mod("A"); f.Mod("B"); f.List("+B", "+A");
            f.File("A", "trinkets/test.entries.trinkets.json", Trinkets("x"));
            f.File("B", "trinkets/test.entries.trinkets.json", Trinkets("y"));
            var merged = Trinkets("x", "y");
            f.Overwrite("trinkets/test.entries.trinkets.json", merged);
            f.Manifest(("trinkets/test.entries.trinkets.json", merged));
            var snapshot = f.Content(f.Prepare());
            var result = TrinketCatalog.Load(snapshot);
            Assert(result.Trinkets.Select(t => t.Id).SequenceEqual(new[] { "x", "y" }), "merged IDs reach native reader");
            Assert(result.Trinkets.All(t => t.Source == "local:merge mod"), "one provider");
        }),
        ("missing generated merge cannot expose the last input", f =>
        {
            f.Mod("A"); f.Mod("B"); f.List("+B", "+A");
            f.File("A", "trinkets/test.entries.trinkets.json", Trinkets("x"));
            f.File("B", "trinkets/test.entries.trinkets.json", Trinkets("y"));
            f.Manifest(("trinkets/test.entries.trinkets.json", Trinkets("y")));
            Throws<InvalidDataException>(() => f.Prepare(), "Generated merged file is missing");
        }),
        ("virtual aliases keep each provider and overwrite index", f =>
        {
            f.Mod("+Speed"); f.Mod("High"); f.List("+High", "++Speed");
            f.File("+Speed", "effects/test.effects.darkest", "low");
            f.File("High", "effects/test.effects.darkest", "high");
            f.Overwrite("effects/test.effects.darkest", "generated");
            f.Manifest(("effects/999000test.effects.darkest", "low"), ("effects/999001test.effects.darkest", "high"),
                ("effects/999002test.effects.darkest", "generated"));
            var snapshot = f.Prepare();
            Assert(File.ReadAllText(Path.Combine(snapshot, "effects/999000test.effects.darkest")) == "low", "low alias");
            Assert(File.ReadAllText(Path.Combine(snapshot, "effects/999001test.effects.darkest")) == "high", "high alias");
            Assert(File.ReadAllText(Path.Combine(snapshot, "effects/999002test.effects.darkest")) == "generated", "overwrite alias");
            Assert(!File.Exists(Path.Combine(snapshot, "effects/test.effects.darkest")), "original name excluded");
        }),
        ("last-only quirk alias uses generated overwrite", f =>
        {
            f.Mod("A"); f.Mod("B"); f.List("+B", "+A");
            f.File("A", "shared/quirk/base.quirk_library.json", "{}");
            f.File("B", "shared/quirk/base.quirk_library.json", "{}");
            f.Overwrite("shared/quirk/base.quirk_library.json", "{\"quirks\":[]}");
            f.Manifest(("shared/quirk/999002base.quirk_library.json", "{\"quirks\":[]}"));
            Assert(File.Exists(Path.Combine(f.Prepare(), "shared/quirk/999002base.quirk_library.json")), "last-only alias");
        }),
        ("rarity aliases follow dependencies instead of Mod index", f =>
        {
            f.Mod("Dependent"); f.Mod("Provider"); f.List("+Provider", "+Dependent");
            var dependent = "{\"rarities\":[{\"id\":\"child\",\"insert_before\":\"parent\"}]}";
            var provider = "{\"rarities\":[{\"id\":\"parent\",\"insert_before\":\"common\"}]}";
            f.File("Dependent", "trinkets/custom.rarities.trinkets.json", dependent);
            f.File("Provider", "trinkets/custom.rarities.trinkets.json", provider);
            f.Manifest(("trinkets/999000custom.rarities.trinkets.json", provider), ("trinkets/999001custom.rarities.trinkets.json", dependent));
            var snapshot = f.Prepare();
            Assert(File.ReadAllText(Path.Combine(snapshot, "trinkets/999000custom.rarities.trinkets.json")) == provider, "dependency first");
            Assert(File.ReadAllText(Path.Combine(snapshot, "trinkets/999001custom.rarities.trinkets.json")) == dependent, "dependent second");
            // The supplied Windows mapper retains this original because its exclusion key
            // uses forward slashes while scanner keys use backslashes before normalization.
            f.Manifest(("trinkets/custom.rarities.trinkets.json", provider),
                ("trinkets/999000custom.rarities.trinkets.json", provider), ("trinkets/999001custom.rarities.trinkets.json", dependent));
            snapshot = f.Prepare();
            Assert(File.ReadAllText(Path.Combine(snapshot, "trinkets/custom.rarities.trinkets.json")) == provider,
                "original rarity provider retained when listed alongside aliases");
        }),
        ("rarity cycles fail", f =>
        {
            f.Mod("A"); f.List("+A");
            f.File("A", "trinkets/one.rarities.trinkets.json", "{\"rarities\":[{\"id\":\"a\",\"insert_before\":\"b\"}]}");
            f.File("A", "trinkets/two.rarities.trinkets.json", "{\"rarities\":[{\"id\":\"b\",\"insert_before\":\"a\"}]}");
            f.Manifest(); Throws<InvalidDataException>(() => f.Prepare(), "cycle");
        }),
        ("hidden resources stay hidden and metadata need not be restored", f =>
        {
            f.Mod("A"); f.List("+A");
            f.File("A", "inventory/hidden.darkest", "hidden");
            System.IO.File.Move(Path.Combine(f.ModDirectory("A"), "inventory/hidden.darkest"),
                Path.Combine(f.ModDirectory("A"), "inventory/hidden.darkest.mohidden"));
            Fixture.Write(Path.Combine(f.ModDirectory("A"), "project.xml.mohidden"), "<project/>");
            f.Manifest(("inventory/hidden.darkest", "hidden"));
            Throws<InvalidDataException>(() => f.Prepare(), "No enabled provider");
            f.Manifest(); Assert(!File.Exists(Path.Combine(f.Prepare(), "inventory/hidden.darkest")), "no unhide");
            // The actual scanner retains hidden resource names in regenerated manifests.
            f.Manifest(("inventory/hidden.darkest.mohidden", "hidden"), ("inventory/hidden.mohidden/child.darkest", "data"),
                ("inventory/.git/config", "data"));
            var snapshot = f.Prepare();
            Assert(!Directory.Exists(Path.Combine(snapshot, "inventory")), "hidden paths never copied");
            Assert(File.ReadAllText(Path.Combine(snapshot, "modfiles.txt")) == "", "hidden requests excluded from private manifest");
            Assert(File.ReadAllText(Path.Combine(f.OverwriteRoot, "modfiles.txt")).Contains(".mohidden"), "MO2 manifest left intact");
        }),
        ("MO2-only Mods need no original paths, metadata or Workshop directory", f =>
        {
            f.Mod("海蓝"); f.List("+海蓝");
            f.File("海蓝", "inventory/test.darkest", "local");
            f.Manifest(("inventory/test.darkest", "local"));
            var snapshot = Mo2Compatibility.Prepare(f.Config, f.Game, null);
            Assert(File.ReadAllText(Path.Combine(snapshot, "inventory/test.darkest")) == "local", "MO2-only content loaded");
            Assert(!File.Exists(Path.Combine(f.ModDirectory("海蓝"), "meta.ini")), "metadata is not required or created");
        }),
        ("original files and sync metadata are not content inputs", f =>
        {
            f.Mod("A"); f.List("+A"); f.File("A", "inventory/test.darkest", "LIVE");
            f.Manifest(("inventory/test.darkest", "LIVE"));
            var original = Path.Combine(f.Workshop, "1/inventory/test.darkest");
            var metadata = Path.Combine(f.ModDirectory("A"), "meta.ini");
            Fixture.Write(original, "OLD!");
            Fixture.Write(metadata, "[Plugins]\nDarkest%20Dungeon%20Mod%20Sync%20Tool\\source_path=%WORKSHOP%\\\\1\n");
            var snapshot = f.Prepare();
            Assert(File.ReadAllText(Path.Combine(snapshot, "inventory/test.darkest")) == "LIVE", "MO2 bytes win over same-size original bytes");
            Fixture.Write(original, "NEW!");
            Assert(f.Prepare() == snapshot, "original changes do not invalidate the MO2 cache");
            File.Delete(original);
            Assert(f.Prepare() == snapshot, "missing original file has no effect");
            Fixture.Write(metadata, "[Plugins]\nDarkest%20Dungeon%20Mod%20Sync%20Tool\\source_path=%WORKSHOP%\\\\missing\n" +
                "Darkest%20Dungeon%20Mod%20Sync%20Tool\\source_path=invalid\\escape\n");
            Assert(f.Prepare() == snapshot, "missing sources and malformed metadata have no effect");
            File.Delete(metadata);
            Assert(f.Prepare() == snapshot, "removed metadata has no effect");
        }),
        ("missing MO2 providers never fall back to original files", f =>
        {
            f.Mod("A"); f.List("+A"); f.File("A", "inventory/test.darkest", "one");
            Fixture.Write(Path.Combine(f.Workshop, "1/inventory/test.darkest"), "one");
            Fixture.Write(Path.Combine(f.ModDirectory("A"), "meta.ini"),
                "[Plugins]\nDarkest%20Dungeon%20Mod%20Sync%20Tool\\source_path=%WORKSHOP%\\\\1\n");
            f.Manifest(("inventory/test.darkest", "one"));
            File.Delete(Path.Combine(f.ModDirectory("A"), "inventory/test.darkest"));
            Throws<InvalidDataException>(() => f.Prepare(), "No enabled provider");
            f.List("+Missing");
            Throws<InvalidDataException>(() => f.Prepare(), "Directory not found");
        }),
        ("manifest sizes and paths are checked before copying", f =>
        {
            f.Mod("A"); f.List("+A"); f.File("A", "inventory/test.darkest", "a");
            Fixture.Write(Path.Combine(f.OverwriteRoot, "modfiles.txt"), "inventory/test.darkest 9\n");
            Throws<InvalidDataException>(() => f.Prepare(), "size differs");
            foreach (var path in new[] { "../escape", "C:/outside", "inventory/file:stream", "inventory/NUL", "inventory/file.", "inventory//file" })
            {
                Fixture.Write(Path.Combine(f.OverwriteRoot, "modfiles.txt"), path + " 0\n");
                Throws<InvalidDataException>(() => f.Prepare());
            }
            f.Manifest(("inventory/test.darkest", "a"), ("Inventory/test.darkest", "a"));
            Throws<InvalidDataException>(() => f.Prepare(), "case-ambiguous");
        }),
        ("unknown aliases and unmanaged modlist entries fail", f =>
        {
            f.Mod("A"); f.List("+A"); f.File("A", "effects/test.effects.darkest", "a");
            f.Manifest(("effects/999004test.effects.darkest", "a"));
            Throws<InvalidDataException>(() => f.Prepare(), "No enabled provider");
            f.List("*A"); Throws<InvalidDataException>(() => f.Prepare(), "Unsupported modlist");
        }),
        ("duplicate Mod names fail", f =>
        {
            f.Mod("A"); f.List("+A", "-a"); f.Manifest();
            Throws<InvalidDataException>(() => f.Prepare(), "Duplicate MO2");
        }),
        ("cache reuse and unchanged-timestamp definition changes", f =>
        {
            f.Mod("A"); f.List("+A"); f.File("A", "inventory/test.darkest", "one"); f.Manifest(("inventory/test.darkest", "one"));
            var before = f.Prepare(); var timestamp = Directory.GetLastWriteTimeUtc(before);
            Assert(before == f.Prepare() && Directory.GetLastWriteTimeUtc(before) == timestamp, "no repeated copy");
            var source = Path.Combine(f.ModDirectory("A"), "inventory/test.darkest"); var stamp = File.GetLastWriteTimeUtc(source);
            Fixture.Write(source, "two"); File.SetLastWriteTimeUtc(source, stamp);
            var after = f.Prepare();
            Assert(before != after && System.IO.File.ReadAllText(Path.Combine(before, "inventory/test.darkest")) == "one", "immutable snapshot invalidation");
            Assert(File.ReadAllText(Path.Combine(after, "inventory/test.darkest")) == "two", "updated MO2 bytes reach the new snapshot");
        }),
        ("cache corruption cannot be loaded", f =>
        {
            f.Mod("A"); f.List("+A"); f.File("A", "inventory/test.darkest", "one"); f.Manifest(("inventory/test.darkest", "one"));
            var snapshot = f.Prepare(); Fixture.Write(Path.Combine(snapshot, "inventory/test.darkest"), "two");
            Throws<InvalidDataException>(() => f.Prepare(), "cache is incomplete or changed");
        }),
        ("cache cannot overlap MO2 or game input", f =>
        {
            f.List(); f.Manifest(); f.WriteConfig(cache: f.OverwriteRoot);
            Throws<InvalidDataException>(() => f.Prepare(), "cacheDirectory must be separate");
        }),
        ("cancellation never returns a partial snapshot", f =>
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Throws<OperationCanceledException>(() => Mo2Compatibility.Prepare(f.Config, f.Game, f.Workshop, cancellation.Token));
            Assert(!Directory.Exists(f.Cache), "no cache created");
        }),
        ("resolver preserves saved Mod slot and rejects stale preview bindings", f =>
        {
            f.Mod("A"); f.List("+A"); f.File("A", "inventory/test.darkest", "one"); f.Manifest(("inventory/test.darkest", "one"));
            Fixture.Write(Path.Combine(f.Game, "mods/Other/project.xml"), "<project><Title>Other</Title></project>");
            Fixture.Write(Path.Combine(f.Game, "mods/Other/modfiles.txt"), "");
            var appConfig = Path.Combine(AppContext.BaseDirectory, "mo2-compat.json");
            Assert(!System.IO.File.Exists(appConfig), "test directory has no user config");
            System.IO.File.Copy(f.Config, appConfig);
            try
            {
                var content = f.Resolve("{\"0\":{\"name\":\"Other\",\"source\":\"mod_local_source\"},\"3\":{\"name\":\"merge mod\",\"source\":\"mod_local_source\"}}");
                Assert(content.Sources.Count == 3 && content.Sources[2].Id == "local:merge mod" && content.Sources[2].LoadOrder == 1003, "saved slot preserved");
                Assert(content.Sources[1].Directory == Path.Combine(f.Game, "mods", "Other"), "other local source unchanged");
                ActiveContentResolver.ValidateSourceBindings(content.Resolution, content.Sources);
                Fixture.Write(Path.Combine(f.Game, "mods/!!MOD/project.xml"), "<project><Title>merge mod</Title></project>");
                ActiveContentResolver.ValidateSourceBindings(content.Resolution, content.Sources);
                Fixture.Write(Path.Combine(f.Game, "mods/Collision/project.xml"), "<project><Title>merge mod</Title></project>");
                Throws<InvalidDataException>(() => ActiveContentResolver.ValidateSourceBindings(content.Resolution, content.Sources), "title ambiguity");
                Throws<InvalidDataException>(() => f.Resolve("{\"0\":{\"name\":\"merge mod\",\"source\":\"mod_local_source\"}}"), "title ambiguity");
                Fixture.Write(Path.Combine(f.Game, "mods/Collision/project.xml"), "<project><Title>Different</Title></project>");
                Fixture.Write(Path.Combine(f.ModDirectory("A"), "inventory/test.darkest"), "two");
                Throws<InvalidOperationException>(() => ActiveContentResolver.ValidateSourceBindings(content.Resolution, content.Sources));
            }
            finally { System.IO.File.Delete(appConfig); }
        }),
        ("ordinary saves need no MO2 configuration", f =>
        {
            var content = f.Resolve("{}"); Assert(content.Sources.Count == 1, "ordinary base source");
            ActiveContentResolver.ValidateSourceBindings(content.Resolution, content.Sources);
            Throws<InvalidDataException>(() => f.Resolve("{\"0\":{\"name\":\"merge mod\",\"source\":\"mod_local_source\"}}"), "Configuration is missing");
        }),
        ("ordinary English and Chinese resources are preserved", f =>
        {
            EditorText.Initialize("en", CultureInfo.InvariantCulture); var english = EditorText.Get("ActiveContentResolver_001");
            EditorText.Initialize("zh-CN", CultureInfo.InvariantCulture); var chinese = EditorText.Get("ActiveContentResolver_001");
            Assert(english != chinese && chinese.Any(ch => ch > 127), "satellite resource lookup");
        })
    };
    var root = args.Length == 1 ? Path.GetFullPath(args[0]) : Path.Combine(AppContext.BaseDirectory, "workspaces");
    root = Path.Combine(root, "mo2-contracts-" + Guid.NewGuid().ToString("N"));
    foreach (var (name, run) in tests)
    {
        var fixture = new Fixture(Path.Combine(root, Guid.NewGuid().ToString("N")));
        run(fixture);
        Console.WriteLine("PASS " + name);
    }
    Console.WriteLine($"PASS {tests.Length} MO2 contracts; isolated evidence: {root}");
}
catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }

static void Assert(bool condition, string message) { if (!condition) throw new Exception("FAIL " + message); }
static void Throws<T>(Action action, string? fragment = null) where T : Exception
{
    try { action(); }
    catch (T error) when (fragment is null || error.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase)) { return; }
    throw new Exception("Expected " + typeof(T).Name + ": " + fragment);
}
static string Trinkets(params string[] ids) => JsonSerializer.Serialize(new
{
    entries = ids.Select(id => new { id, rarity = "common", price = 1, limit = 0, buffs = Array.Empty<string>() })
});
static string[] Api(Assembly assembly) => assembly.GetExportedTypes().SelectMany(type =>
    new[] { type.FullName + " : " + type.BaseType?.FullName }.Concat(type.GetMembers(
            BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
        .Select(member => type.FullName + "|" + member.MemberType + "|" + member))).Order(StringComparer.Ordinal).ToArray();

internal sealed class Fixture
{
    internal string Root { get; }
    internal string Game => Path.Combine(Root, "game");
    internal string Workshop => Path.Combine(Root, "workshop");
    internal string Mods => Path.Combine(Root, "mo2/mods");
    internal string Profile => Path.Combine(Root, "mo2/profiles/Default");
    internal string OverwriteRoot => Path.Combine(Root, "mo2/overwrite");
    internal string Cache => Path.Combine(Root, "cache");
    internal string Config => Path.Combine(Root, "mo2-compat.json");
    internal Fixture(string root)
    {
        Root = root;
        foreach (var directory in new[] { Game, Workshop, Mods, Profile, OverwriteRoot }) Directory.CreateDirectory(directory);
        WriteConfig(); List(); Manifest();
        Write(Path.Combine(OverwriteRoot, "project.xml"), "<project><Title>merge mod</Title></project>");
    }
    internal void WriteConfig(string? cache = null) => Write(Config, JsonSerializer.Serialize(new
    { profileDirectory = Profile, modsDirectory = Mods, overwriteDirectory = OverwriteRoot, cacheDirectory = cache ?? Cache }));
    internal string ModDirectory(string mod) => Path.Combine(Mods, mod);
    internal void Mod(string name) => Directory.CreateDirectory(ModDirectory(name));
    internal void File(string mod, string relative, string text)
        => Write(Path.Combine(ModDirectory(mod), relative), text);
    internal void Overwrite(string relative, string text) => Write(Path.Combine(OverwriteRoot, relative), text);
    internal void List(params string[] lines) => Write(Path.Combine(Profile, "modlist.txt"), "# MO2 highest priority first\n" + string.Join('\n', lines));
    internal void Manifest(params (string Path, string Text)[] files) => Write(Path.Combine(OverwriteRoot, "modfiles.txt"),
        string.Join('\n', files.Select(file => file.Path + " " + Encoding.UTF8.GetByteCount(file.Text))));
    internal string Prepare() => Mo2Compatibility.Prepare(Config, Game, Workshop);
    internal ActiveContentSnapshot Content(string directory) => new(new("test", Root, "", "", DateTime.UtcNow), "base",
        [new("base", "base", "base", Game, 0), new("local:merge mod", "merge mod", "local", directory, 1000)], [], "", "", 1, "");
    internal ActiveContentSnapshot Resolve(string applied)
    {
        var decoded = Path.Combine(Root, "decoded-game.json");
        Write(decoded, "{\"base_root\":{\"applied_ugcs_1_0\":" + applied + "}}");
        return ActiveContentResolver.ResolveDecoded(new("test", Root, "", "", DateTime.UtcNow), Game, Workshop, null, Root, decoded, "test");
    }
    internal static void Write(string path, string text)
    { Directory.CreateDirectory(Path.GetDirectoryName(path)!); System.IO.File.WriteAllText(path, text, new UTF8Encoding(false)); }
}
