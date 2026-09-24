# UI localization

Starting with `v0.1.0-beta.2`, releases include English and Simplified Chinese in the same application. The compact **Language** button beside **Support** opens a menu with **Follow system**, **Simplified Chinese**, and **English**. A check marks the saved preference. A change takes effect on the next startup; the reminder appears inside the menu. Selecting a language does not interrupt loading, synchronization, previews, or writes.

With **Follow system**, a Chinese Windows UI language selects Simplified Chinese; other system languages select English. An explicit choice takes precedence. The preference is stored separately from saves in `%LOCALAPPDATA%/DarkestDungeonSaveEditor/language.json`. Missing settings use the system language. Unreadable, malformed, or unsupported settings fall back for that session and produce a diagnostic without overwriting the file. Failed preference writes retain the previous selection.

## Resource files

Shared resources live in `src/DarkestDungeonSaveEditor.Core/Localization/`:

- `Strings.resx`: English text and the default resource fallback.
- `Strings.zh-CN.resx`: Simplified Chinese text with matching keys.
- `EditorText.cs`: resource lookup, formatting, culture selection, and game-name fallback.
- `EditorLanguageSettings.cs`: validated, atomic preference persistence.

Resources cover windows, tooltips, dialog buttons, status and preview text, catalog explanations, and editor-generated errors. WPF uses the `Text` markup extension; C# uses `EditorText.Get` or `EditorText.Format`. Resource keys are stable identifiers. Add translations to both files, preserving placeholder indices, formats, and meaningful spaces between concatenated fragments. Do not translate save fields, content IDs, paths, hashes, or protocol markers. External tool output, operating-system exceptions, and user/Mod-provided names retain their source text.

The .NET resource manager loads the Chinese satellite assembly from the published `zh-CN` directory. Keep that directory when copying or packaging the application. No additional localization dependency is required. Resources are bundled with the build; editing a source `.resx` requires rebuilding.

## Game content and diagnostics

Game and Mod translations continue to use the existing content localization catalog and its manifest/overlay rules. Primary names prefer the selected language, then the other language, then the original ID. Name columns retain both languages and put English first in English mode. Encounter search also matches English enemy names. UI language does not modify game settings or numeric/save serialization rules.

Catalog issue markers in `CatalogIssueCode` are invariant. Encounter truncation, partial localization reads, and residual raid-file notices use these markers for grouping and classification; only descriptions are translated. Translated wording must never decide write eligibility, severity, or file identity.

## Validation

Run the focused checks from the repository root:

```powershell
dotnet run --project tests/DarkestDungeonSaveEditor.ContractTests -c Release -- . --ui-localization
```

These checks verify compiled resources in both cultures, matching keys/placeholders, fallback, background task culture, unchanged numeric culture, atomic settings, diagnostic grouping/severity, actual WPF text, and next-start preference behavior. They render normal/minimum-size windows into the reported ignored workspace for visual inspection. Existing save, synchronization, and guarded-write contracts remain applicable; legacy message assertions use an explicit Chinese culture.

The published `v0.1.0-beta.1` package predates this feature. Use `v0.1.0-beta.2` or newer for bilingual UI support. Every bilingual release must include the `zh-CN` satellite resources; the packaging script checks for them before creating the archive.
