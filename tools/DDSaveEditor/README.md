# Bundled DSON codec

`DDSaveEditor.jar` is upstream **v0.0.70 with the local JSON field-name escaping patch**.
The upstream MIT license is retained in `LICENSE`.

## Patch scope

Upstream `DsonFile.writeField` inserts field names between quotes without JSON
escaping. A native map ID ending in a backslash therefore produces invalid JSON.
The local source uses the **Gson already bundled in the JAR** to serialize each
field name. This covers object keys, scalar keys, and embedded DSON files such as
`static_save`. It preserves the decoded name; it does not rename map areas.
HTML escaping is disabled to retain ordinary field-name spelling.

Only `DsonFile` and its nested classes are rebuilt. `DsonField`, `DsonWriter`,
string-value formatting, and the .NET wrapper are unchanged. The upstream
manifest version remains `v0.0.70`; this notice identifies the local patch.

## Source and rebuild

- Upstream tag: `v0.0.70`
- Commit: `13fa97a28b9f405a85d93ba05fc9bad269b3c7da`
- [Original DsonFile.java](https://github.com/robojumper/DarkestDungeonSaveEditor/blob/13fa97a28b9f405a85d93ba05fc9bad269b3c7da/src/main/java/de/robojumper/ddsavereader/file/DsonFile.java)
- Original source SHA256: `9b914f0bc79349894b1c930d99a9e1dc717d6bbdd89ae7fd55b144ba6e173c27`
- [Original release ZIP](https://github.com/robojumper/DarkestDungeonSaveEditor/releases/download/v0.0.70/DDSaveEditor-v0.0.70.zip); extract `DDSaveEditor.jar` before rebuilding.
- Original JAR SHA256: `fd7a052f5da21fde991893bc3d5ae9b03dbc59a9002b206388c3396199e10b90`
- Patched source: `src/de/robojumper/ddsavereader/file/DsonFile.java`

From the repository root, with a JDK that supports `javac --release 8`:

```powershell
./tools/DDSaveEditor/Build-PatchedCodec.ps1 `
    -OriginalJar 'C:/downloads/DDSaveEditor.jar' `
    -OutputJar './artifacts/DDSaveEditor.patched.jar' `
    -JdkDirectory 'C:/Program Files/Java/jdk-21'
```

The script checks the original archive hash, emits Java 8 class files, and keeps
all other archive entries intact. It refuses to overwrite an existing output.
After validation, copy the result to `tools/DDSaveEditor/DDSaveEditor.jar`.
No new runtime dependency is needed.

## Regression contracts

With Java on the process PATH:

```powershell
dotnet build DarkestDungeonSaveEditor.sln -c Release -m:1
dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests -c Release --no-build -- . --codec-json-escaping
```

This group covers backslashes, quotes, control characters, Unicode, nested DSON,
binary round trips, revision preservation, and joining static/dynamic map keys.
It also runs in the full contract suite. Fixtures are synthetic; reported player
saves must remain local and must be copied into an isolated regression workspace.
