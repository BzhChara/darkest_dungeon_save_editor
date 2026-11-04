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
    private readonly Func<bool> _gameRunningProbe;
    internal Action<string>? BeforeTargetReplace { get; set; }
    internal Action<string>? AfterTargetReplace { get; set; }

    public SaveEditService(DsonSaveCodec codec, SaveEditorLocations? locations = null)
        : this(codec, locations, GameProcessGuard.IsRunning) { }

    public SaveEditService(DsonSaveCodec codec, SaveEditorLocations? locations, Func<bool> gameRunningProbe)
    {
        _codec = codec;
        _locations = locations ?? SaveEditorLocations.CreateDefault();
        _gameRunningProbe = gameRunningProbe ?? throw new ArgumentNullException(nameof(gameRunningProbe));
    }

    private void RequireGameStopped() => GameProcessGuard.RequireStopped(_gameRunningProbe);

    private GuardedSaveReplacement CreateReplacement(string target, string encoded, string originalHash, string encodedHash) =>
        new(target, encoded, originalHash, encodedHash, BeforeTargetReplace, AfterTargetReplace, RequireGameStopped);
}
