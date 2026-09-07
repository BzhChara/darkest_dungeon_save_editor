using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkestDungeonSaveEditor.Core;

public sealed partial class SaveEditService
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly DsonSaveCodec _codec;
    private readonly SaveEditorLocations _locations;
    internal Action<string>? BeforeTargetReplace { get; set; }
    internal Action<string>? AfterTargetReplace { get; set; }

    public SaveEditService(DsonSaveCodec codec, SaveEditorLocations? locations = null)
    {
        _codec = codec;
        _locations = locations ?? SaveEditorLocations.CreateDefault();
    }
}
