using System.IO;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

// Selected once at startup. Every reader and edit service in a window shares
// this codec, so a native preview cannot accidentally be committed using Java.
internal sealed class DesktopCodecSession
{
#if NATIVE_DSON_TEST_BUILD
    internal static bool IsNativeTestBuild => true;
#else
    internal static bool IsNativeTestBuild => false;
#endif

    private DesktopCodecSession(bool native, string applicationDirectory)
    {
        IsNative = native;
        Codec = native ? DsonSaveCodec.CreateNative()
            : new DsonSaveCodec(Path.Combine(applicationDirectory, "tools", "DDSaveEditor", "DDSaveEditor.jar"));
    }

    internal bool IsNative { get; }
    internal DsonSaveCodec Codec { get; }
    internal string Identity => IsNative ? "native-csharp" : "java-reference";

    internal static DesktopCodecSession Create(IEnumerable<string> arguments, string applicationDirectory)
    {
        bool? selection = null;
        foreach (var argument in arguments)
        {
            if (!argument.StartsWith("--codec", StringComparison.Ordinal)) continue;
            var native = argument switch
            {
                "--codec=native" => true,
                "--codec=java" => false,
                _ => throw new ArgumentException("Use --codec=native or --codec=java.")
            };
            if (selection is not null)
                throw new ArgumentException("Specify --codec only once.");
            selection = native;
        }
        return new DesktopCodecSession(selection ?? true, applicationDirectory);
    }
}
