# Visual Design and QA Record

This document records the Darkest Dungeon Save Editor's accepted visual direction, historical failures, technical fixes, and regression checks. Chat screenshots and temporary render workspaces remain historical analysis material; durable conclusions must be reproducible from tracked assets, code, or tests.

- Last updated: 2026-09-05
- Target window: 1440 × 900
- Minimum window: 1120 × 720
- Current status: the user confirmed the title animation, readability, and stable wordmark as normal. The current Release native frame and texture were captured in the 2026-09-05 review; cross-DPI and user acceptance are separate from that bounded check.
- Functional and save rules: [Content Catalog and Save-Write Rules](content-save-rules.md)

## 1. Visual baseline

The UI borrows the material language of the original Darkest Dungeon panels while preserving the readability and safety needs of a save tool.

- Use black, charcoal, and low-saturation materials instead of a bright system-white shell.
- Keep ordinary text readable. The title wordmark retains black interiors and narrow red outlines.
- Frame important actions; do not wrap every region in nested closed boxes.
- Gold is a hover or limited hierarchy signal, not a focus border that remains after clicking.
- Use a full-row dark-red state for the current selection instead of a gold cell border.
- A press must not scale controls or move adjacent layout.
- Panel title bands can provide separation without redundant grey lines or side borders.
- Window chrome, scrollbars, TextBoxes, ComboBoxes, tabs, and dialogs belong to the same dark theme.
- The Windows-native caption stays near-black with muted light text and a dark-red border. Do not use a bright yellow/gold DWM border.
- The charcoal texture covers the full window through `UniformToFill`; translucent shell and panel surfaces must reveal it across the working area instead of only at the outer margin.

## 2. Title wordmark and torch animation

### 2.1 Tracked assets

- Animated sheet: [dd-title-logo-loop.png](../src/DarkestDungeonSaveEditor.App/Assets/dd-title-logo-loop.png)
- Static atlas: [darkest-dungeon-title-atlas.png](../src/DarkestDungeonSaveEditor.App/Assets/darkest-dungeon-title-atlas.png)
- Application icon: [save-editor-icon.png](../src/DarkestDungeonSaveEditor.App/Assets/save-editor-icon.png)

The title animation was rendered from the game's `titles.sprite.skel` `torch_loop`, not reconstructed from a few atlas screenshots.

- 120 frames;
- native presentation size 320 × 100;
- 10 FPS;
- complete 12-second loop;
- flame, smoke, sparks, torch, and geometry timelines remain animated;
- the wordmark outline uses its accepted readable setup opacity instead of pulsing with the flame.

### 2.2 Resolved historical failures

| Symptom | Root cause | Permanent rule |
| --- | --- | --- |
| Lower portions of `kest`, `Dung`, and the torch handle were absent | Spine quad vertices were split as `(0,1,2)+(1,3,2)`, creating an overlap and an unpainted wedge | Use `(0,1,2)+(0,2,3)` |
| Outer letters merged into the black header | Premultiplied-alpha source RGB was treated as straight RGB and multiplied by alpha again | Carry premultiplied RGB through compositing once and convert to straight BGRA once at PNG export |
| The repaired title became too bright and black fills turned red-brown | A hidden final `LiftTone`/gamma gain remained | Do not add brightness, recoloring, drop shadow, duplicate glow, or a second tone pass |
| The wordmark brightness pulsed abruptly with the flame | The original `torch_loop` also animated the wordmark `outline` slot opacity | Restore that slot's setup opacity per frame without freezing any fire/smoke/geometry timeline |
| 320 × 100 was suspected to be too small | Same-size comparisons showed that the source remained legible at that size | Keep native dimensions and only use high-quality scaling for OS DPI |

### 2.3 Automated regression anchors

Contract tests should continue to protect these properties:

- both formerly missing wordmark regions and the torch handle are populated in every frame;
- outline anchors remain red-dominant;
- the outer antialiasing anchor remains temporally stable instead of following flame opacity;
- frame zero matches the accepted readable asset;
- the complete packaged resource SHA-256 is pinned;
- no tone lift or second alpha multiplication returns.

The relevant assertions live in [UiContractTests.cs](../tests/DarkestDungeonSaveEditor.ContractTests/UiContractTests.cs). `Program.cs` is only the test executable entry point. Pixel coordinates remain test implementation details rather than a duplicate list of constants in this document.

## 3. Layout and interaction rules

### 3.1 Main catalogs

- The first tab is `小镇物品 / ESTATE ITEMS` or `副本背包 / RAID ITEMS` after catalog load (and the neutral `物品 / ITEMS` before load); it remains left of `饰品 / TRINKETS`, `人物 / HEROES`, and `战斗 / BATTLE`. All four tab headers are centered, equally sized, and fully visible. Battle uses a map workspace rather than the other tabs' catalog tables.
- Catalog content is top-left aligned. A filter yielding fewer results must not recenter the result grid.
- Row text is vertically centered. ID, bilingual names, source, and operation-relevant state take priority.
- Selecting an item, trinket, hero, or quirk uses only the full-row red fill. Cell, row, and keyboard-focus outlines must not add a white or gold frame; automatic gridlines remain visible.
- All remaining catalog columns use the same one-pixel separator. Body cells use the automatic DataGrid gridline; every column header draws the shared fixed separator on its leading inside edge so star-column remeasurement, loading rows, hovering, sorting, and resizing cannot clip a header-only gap. No individual column boundary is emphasized as a section divider.
- Catalog totals do not occupy a summary line above the controls. After loading, the runtime log records a concise load summary and a separate content-count summary; raid occupied-slot counts include every saved inventory entry, including carried trinkets that the quantity catalog intentionally does not expose. The persistent log additionally records the selected profile path and hashes for the game and active town/raid quantity saves.
- Low-frequency diagnostics such as state shape and definition conflict should not crowd out primary fields; use tooltips or the risk panel.
- Item storage/type/stack, trinket rarity/definition limit, and hero generation mode/natural quirk ranges are retained in selected-row details rather than permanent columns. Names and sources receive the freed width; long cell values retain their full-text tooltips. All three grids use pixel scrolling so a selected row taller than a short viewport can still be read. Trinket limit warnings remain in the existing preview warning area.
- Item details show separate labeled location, inventory type, and stack fields in a wrapping row. Town counters show stack as not applicable; raid entries show the positive per-slot limit or unknown. Estate provisioning information remains a separate field and is omitted in other locations. Preview text and save behavior are unchanged.
- The load action sits outside the path ScrollViewer in a fixed footer. Path content fits the default window without a scrollbar; only genuine overflow at smaller viewports scrolls.
- Catalog tab labels may scale down at narrow widths, but must not grow above their normal size or clip their right edge.
- Preview/write buttons stay near the relevant catalog action area rather than inside an otherwise empty titled panel.
- The runtime-log header stays concise and does not repeat the log-directory explanation.

### 3.2 Inputs and buttons

- TextBox and ComboBox borders remain one pixel. Hover, press, or focus must not change control dimensions.
- On first focus, the target/add quantity input selects its current value so typing replaces the default directly; a later click while it is already focused may place the caret normally.
- The hero-level ComboBox does not retain a gold focus frame after selection.
- Hover may turn a button border gold. Pressed state does not scale, shift, or retain gold.
- DataGrid uses a full-row red selection without a focused-cell gold border.
- Custom scrollbars use the same dark visual system as the rest of the application.

### 3.3 Hero and quirk windows

- The hero-level area has enough width for `XP`, weapon rank, and armor rank to remain inside its layout.
- The quirk window opens at 1220 px. At the 900 px minimum viewport it may scroll horizontally rather than collapsing source or unavailable-reason columns to zero.
- Source and unavailable-reason columns retain 135/190 px minimum widths and expose truncated text through tooltips.
- The quirk selector has no static rules section or rules disclosure. Keep the quota summary, actual incompatibility/unavailability reasons, and live validation messages; the directory title remains the main separator.
- Empty quirk validation messages take no layout space. Non-empty messages must remain visible above the dialog actions.
- Item-filter and quirk checkboxes share fixed-size dark chrome, a clear checked mark, and visible disabled/keyboard-focus states without click scaling.
- `自然怪癖范围` is rendered as `正面 n–m / 负面 n–m`, not the ambiguous `+1-2 -1-2` shorthand.
- Hero generation status uses `游戏自然 / 编辑器手动`, `仅编辑器手动`, or `自然状态未知 / 编辑器手动`; an absent flag is not treated as proof of natural generation.
- If no level passes the candidate-factory preflight, the hero instead shows `暂不可生成` and `不可生成`. Partial support lists only available levels; selecting a failed level disables preview and shows its actual failure in the existing warning area, without adding a new panel or weakening generation checks.

### 3.4 Application dialogs

- Confirmation, success, and failure messages use the application's square dark double frame instead of the default Windows `MessageBox` surface.
- The message body uses an original, subdued parchment texture with dark text and no additional nested frame; the header marker is also unboxed.
- The footer contains only the required actions. Do not add a lower-left Enter/Escape helper or another framed footer region.
- Destructive save confirmation defaults to `返回`: pressing Enter without moving focus must not approve a write. Escape also returns without writing.
- Informational dialogs expose one `知道了` action and support both Enter and Escape.
- Long messages scroll inside the parchment surface instead of growing beyond the display.
- The native folder picker remains an operating-system dialog because it provides filesystem navigation rather than an in-application message.

## 4. Historical evidence and reproducibility

The original investigation used user-provided base-game screenshots, editor screenshots, and comparison boards under `workspaces/title_logo_renderer`. Those paths were temporary or ignored by Git and are intentionally not permanent links here.

When a title regression must be investigated again, prefer:

1. the tracked packaged assets;
2. the game's Spine skeleton, atlas, and textures;
3. contract-test pixel and hash guards;
4. a newly generated same-size comparison board;
5. a user screenshot from the actual WPF/DPI environment.

Never adjust brightness, hue, or geometry solely from a screenshot that a chat client may have rescaled. First distinguish source art, alpha compositing, render resolution, WPF scaling, container clipping, and animation timelines.

## 5. Acceptance state

### Confirmed

- The user confirmed that the missing title geometry and torch handle were fixed.
- The user confirmed that the title/background readability problem was fixed.
- The user confirmed that the wordmark no longer flickers abruptly with the flame and that the final effect is normal.
- The asset preserves the original black faces, red outlines, dark iron torch, and original flame/smoke palette without global brightening.
- Filtering no longer moves a short result set toward the center.
- Focus no longer changes layout through thicker borders or control scaling.
- Buttons and tables follow the hover-gold/full-row-red interaction rule.

### Required regression pass after future UI changes

1. 1440 × 900 and 1120 × 720 windows;
2. 100%, 125%, and 150% Windows DPI;
3. at least one complete title-animation loop;
4. item, trinket, and hero searches producing zero, one, few, and many rows;
5. hero-level selection, quirk selection, long source text, and long unavailable reasons;
6. keyboard Tab focus, mouse hover, pressed state, row selection, and close/reopen behavior;
7. XAML parsing, Release build, and the complete contract suite.

There is no known title-animation blocker in this record. The native dark-red border and full-window charcoal texture have automated XAML coverage. The 2026-09-05 review also captured the current Release window through Windows Computer Use without changing desktop permissions. This is bounded visual evidence, not user acceptance at every DPI or a replacement for game-level tests. Since 2026-09-06, the user prefers the assistant to operate authorized live tests through Computer Use; preserve each test's approved save target and mutation boundaries, and record observed evidence separately from user acceptance.
