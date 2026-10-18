using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace DarkestDungeon.ModOrder;

internal sealed class InputReader
{
    private readonly SortedDictionary<string, string> _observed = new(StringComparer.Ordinal);
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal byte[]? Read(string path, bool required = false)
    {
        path = Path.GetFullPath(path);
        byte[]? bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
        _observed["file:" + path] = bytes is null ? "missing" : Convert.ToHexString(SHA256.HashData(bytes));
        if (required && bytes is null) throw new FileNotFoundException("Required input is missing.", path);
        return bytes;
    }

    internal bool DirectoryExists(string path)
    {
        var exists = Directory.Exists(path);
        _observed["directory:" + Path.GetFullPath(path)] = exists.ToString();
        return exists;
    }

    internal string[] Directories(string path)
    {
        var dirs = DirectoryExists(path) ? Directory.GetDirectories(path).Order(StringComparer.Ordinal).ToArray() : [];
        _observed["children:" + Path.GetFullPath(path)] = JsonSerializer.Serialize(dirs);
        return dirs;
    }

    internal string Fingerprint(ConversionRequest request) => Convert.ToHexString(SHA256.HashData(
        Utf8.GetBytes(JsonSerializer.Serialize(new { request, inputs = _observed }))));

    internal static string Text(byte[] bytes) => Utf8.GetString(bytes).TrimStart('\uFEFF');

    internal static JsonObject Json(string text)
    {
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions
        {
            AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 256
        });
        CheckNames(document.RootElement);
        return JsonNode.Parse(document.RootElement.GetRawText()) as JsonObject
            ?? throw new InvalidDataException("Save must contain a JSON object.");
    }

    private static void CheckNames(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in node.EnumerateObject())
            {
                if (!seen.Add(item.Name)) throw new InvalidDataException($"Duplicate JSON property: {item.Name}");
                CheckNames(item.Value);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var item in node.EnumerateArray()) CheckNames(item);
    }

    internal static (string Title, string PublishedId) Project(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader);
        string Read(string name)
        {
            var values = document.Root?.Elements(name).Select(x => x.Value.Trim()).ToArray() ?? [];
            if (values.Length > 1) throw new InvalidDataException($"Duplicate project field: {name}");
            return values.SingleOrDefault() ?? "";
        }
        return (Read("Title"), Read("PublishedFileId"));
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
        "DarkestDungeonModOrder", Guid.NewGuid().ToString("N"));
    internal TemporaryDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
