# Third-party notices

`DDSaveEditor.jar` is Darkest Dungeon Save Editor v0.0.70 by robojumper, with a local JSON field-name escaping patch.

- Upstream: https://github.com/robojumper/DarkestDungeonSaveEditor
- License: MIT; see `LICENSE` in this directory.
- Purpose in this project: decode and encode Darkest Dungeon DSON save files.

The C# DSON codec in `src/DarkestDungeonSaveEditor.Core/Dson/` also derives its binary layout, field-name hash and known field-type paths from robojumper's v0.0.70 implementation (commit `13fa97a28b9f405a85d93ba05fc9bad269b3c7da`). Copyright (c) 2018 robojumper. The original MIT copyright and permission notice are retained both here in `LICENSE` and beside the C# source in `Dson/LICENSE`. The surrounding editor remains GPL-3.0-or-later; this does not remove the upstream MIT notice. The native implementation uses .NET's JSON serialization and adds original-binary preservation and input validation. It is the desktop default starting with v0.1.0-beta.3; the JAR remains an explicit reference option. See `docs/dson-codec-migration.md` for compatibility and limitations.

Local modification: `DsonFile.writeField` serializes field names with the existing bundled Gson library, preserving backslashes, quotes, and control characters in valid JSON. This also applies to embedded DSON maps. The binary writer and string-value formatting are unchanged. Patch source and rebuilding instructions are in `tools/DDSaveEditor/README.md` in the project repository. Rebuilt classes target Java 8; the upstream manifest version remains `v0.0.70`.

The wrapper preserves bytes 4-7 from the original DSON header after encoding. Version 0.0.70 writes zeroes to that revision field by default, while current local saves retain their original game revision there.
