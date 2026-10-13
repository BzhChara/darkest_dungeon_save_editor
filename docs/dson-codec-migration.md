# Native C# DSON codec migration

The native codec is available through `DsonSaveCodec.CreateNative()`. It needs
neither Java nor a JAR and uses the same decode/encode API as existing services.
Starting with `v0.1.0-beta.3`, regular desktop builds default to C# throughout
catalog loading, initial synchronization, map reads, edit previews and commits.
The JAR remains an explicit reference backend. There is no automatic fallback
to Java.

## Regular release

`tools/Publish-Release.ps1` builds a self-contained Windows x64 package. Normal
startup uses C# without Java or a separate .NET installation. The window keeps
its ordinary title; the startup log records `DSON codec: native-csharp`.
`Start-JarReference.cmd` selects Java explicitly and the title identifies the
JAR reference session. That mode requires Java 8 or newer on `PATH`.

## Desktop test build

Run `tools/Build-NativeCodecTest.ps1` from PowerShell 7 on Windows. It publishes
into a new `artifacts/native-desktop-builds/<run>/` directory without replacing an
existing package. This local package requires the installed .NET 8 Desktop Runtime.
Open `DarkestDungeonSaveEditor.App.exe`; its title includes **C# test build** and
the startup log records `DSON codec: native-csharp`. The immutable session codec
is shared by all readers and edit services in that window.

`Start-JarReference.cmd` launches the same executable with `--codec=java`. Its
title identifies the JAR reference build. The JAR and its MIT notices remain in
the package. `--codec=native` also selects C# explicitly in either build. Codec
selection applies only to that process; no persistent preference is changed.
Invalid or duplicate codec arguments stop startup instead of selecting another
backend. The test-label build property is `-p:NativeCodecTestBuild=true`; it no
longer changes the default backend.

The test build retains the normal profile selector and write guards. Test on
copies first; selecting an original profile can write that original when an edit
is explicitly applied. Automated corpus regression copies the inputs and treats
every original as read-only. Live-game tests require a separately authorized
test profile.

## Validation coverage

The supplied issue #3 save and local sample copies passed native decode/edit/
encode/readback checks, with byte-exact unchanged roundtrips and comparisons
against the retained JAR where it can parse the input. The native codec also
preserves sample fields the JAR cannot interpret; this does not make those
fields editable.

On 2026-10-13, a dedicated modded test profile passed in-game loading, town gold,
trinket and stagecoach hero generation, raid inventory edits, chest placement
and interaction, party movement, return-to-town entry and editor synchronization.
The user manually confirmed recruitment, trinket equipping and subsequent
save/reload. These results cover the tested samples and actions, not every Mod
or save layout. The issue reporter's own in-game environment and a full MO2
virtual merged environment still need separate validation.

## Attribution

Binary layout, the 53-based UTF-8 name hash and known field-type paths derive
from [robojumper/DarkestDungeonSaveEditor v0.0.70](https://github.com/robojumper/DarkestDungeonSaveEditor/tree/13fa97a28b9f405a85d93ba05fc9bad269b3c7da).
Copyright (c) 2018 robojumper. The full MIT notice is retained beside the C# source
in `src/DarkestDungeonSaveEditor.Core/Dson/LICENSE` and in
`tools/DDSaveEditor/LICENSE`, which is copied into application output and releases.
`tools/DDSaveEditor/THIRD-PARTY-NOTICES.md` describes both implementations.
The editor's GPL-3.0-or-later license does not remove the original MIT notice.

## Preservation and compatibility

- The original binary passed to `EncodeAsync` is the preservation source. An
  unchanged JSON view returns exactly its original bytes. Without an original,
  the codec builds a new document using known field paths and supported types.
- JSON names and values use `System.Text.Json` escaping. Literal `###` text stays
  a string. Native hashes are represented as integer values, not a string shortcut.
- An edited document retains native field order, every duplicate occurrence,
  untouched value bytes, embedded documents, revision and reserved header/field
  bits. Offsets and hierarchy counts are rebuilt. Alignment padding is retained
  where the required padding length is unchanged.
- The existing editor needs unique JSON keys. Its view exposes the first native
  occurrence of each name. Unrelated edits preserve all occurrences; changing or
  removing an exposed duplicate field is rejected because the target occurrence
  is ambiguous. Deleting an enclosing object removes that whole subtree.
- Unrecognized payloads (including unsupported nonfinite values or invalid UTF-8
  text) have an opaque JSON marker. Unchanged opaque bytes are preserved. Editing
  opaque data, copying its marker to a new field without native metadata, or
  relocating it to an alignment that cannot be established, fails.
- Known floats accept integer-spelled JSON numbers and reject overflow. Native
  characters require one ASCII character; names are limited to 510 UTF-8 bytes.
  Unknown float/array field types require additional verified format knowledge.
- Structural checks reject inconsistent section lengths, overlapping field
  offsets, incorrect name hashes and object relationships. Nesting is bounded.
  The supported layout has contiguous metadata sections and no unreferenced DATA
  prefix. Extended header bytes are retained when the recorded header size and
  metadata offset agree.
- Conversion occurs before output publication. A uniquely named temporary file
  is moved into place only after successful conversion and a cancellation check.
  Input/original paths cannot be used as binary conversion output paths.

JSON equality alone cannot prove that a decoder preserved native information.
Regression checks therefore include independent binary fixtures, direct payload
and metadata inspection, and byte-exact no-op comparisons with original saves.
The Java codec is only an oracle for cases where its behavior is known to be valid.

## Validation commands

From the repository root:

```powershell
dotnet build DarkestDungeonSaveEditor.sln -c Release -m:1
dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests -c Release --no-build -- . --native-codec
dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests -c Release --no-build -- . --native-all
dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests -c Release --no-build -- . --native-upload-regression '.research/github issues/#3'
dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests -c Release --no-build -- . --native-desktop
dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests -c Release --no-build -- . --native-save-regression path/to/corpus.json
```

The native unit group uses no Java. The full suite retains the existing isolated
Java-process cancellation probe; the uploaded-save comparison needs Java on PATH
to run the retained JAR. All uploaded files are hashed, copied into a new local
artifact directory, and checked again after regression. Player saves are not
committed or redistributed. The upload runner checks current and backup profiles,
compares JSON views, verifies byte-exact no-op output, and appends a synthetic
test-only scalar to verify that all original DATA bytes and field metadata remain
intact. Its edited output is a test artifact, not a save to install in the game.

The full native suite covers application edit/preview/commit guards and map,
inventory, hero and encounter workflows using synthetic workspaces. None of
these checks substitutes for loading edited saves in the actual game.

The desktop group runs the compiled WPF handlers in a hidden window, verifies
backend selection and localized titles, and tests catalog/sync/map binding,
quantity preview, the prepared commit service, verified backup and post-commit
refresh without Java. Both regular and test builds also exercise initial-sync
failure/retry and close/cancellation. Confirmation dialogs are not automated.

The corpus manifest lists `sources`, each with a unique simple `name` and an
absolute `directory`. The runner hashes all input files, copies JSON-named saves
to a unique artifact directory, and rechecks all originals after testing. It
covers binary and plain JSON saves, backups and nested expeditions. In addition
to no-op byte comparisons and an append preserving every original DATA span,
it edits an existing scalar in each supported binary file. Reports explicitly
separate a matching JAR view from a JAR that cannot parse an opaque field; other
decode errors or mismatches fail. Unknown fields remain opaque and unchanged.

Optional `workflows` entries identify a `source` and relative `profile` path.
With `gameDirectory` and optional `workshopDirectory`, these exercise the real
quantity catalog, preview, guarded commit, backup and refresh on a second profile
copy. Installed content is read only; Mod manifest generation is not invoked.
The synthetic edits and copied profiles are regression artifacts, not saves
intended for installation in the game.
