# Third-party notices

`DDSaveEditor.jar` is Darkest Dungeon Save Editor v0.0.70 by robojumper.

- Upstream: https://github.com/robojumper/DarkestDungeonSaveEditor
- License: MIT; see `LICENSE` in this directory.
- Purpose in this project: decode and encode Darkest Dungeon DSON save files.

The wrapper preserves bytes 4-7 from the original DSON header after encoding. Version 0.0.70 writes zeroes to that revision field by default, while current local saves retain their original game revision there.
