# Griff Design System

A dark theme built for long sessions in a dense tool UI. Chrome stays quiet and content stands out.
Orange is the brand color, used sparingly. To browse every token and control live, open
**Help → Design System Gallery** in the editor.

## Files

| File | Contents |
| --- | --- |
| `Foundation.xaml` | Palette, semantic brushes, typography, metrics and shared primitives |
| `Controls/*.xaml` | Implicit styles for standard controls, plus named variants |
| `Controls/ChromeWindow.cs` | Base class for every editor window (custom title bar, dark frame) |
| `Icons.cs` | Icon glyph constants |
| `Theme.xaml` | Entry point. `App.xaml` merges only this file |

Each control dictionary merges `Foundation.xaml` itself, so `StaticResource` lookups work when it loads.

## Rules

1. **Use semantic brushes and never palette colors.** UI code references `Brush.*`. `Color.*` exists only so
   `Foundation.xaml` can define the brushes.
2. **Reference brushes with `DynamicResource`**, so a light or high-contrast theme can be swapped in later.
   Metrics, fonts and styles use `StaticResource`.
3. **Accent is not selection.** Orange (`Brush.Accent`) marks the primary action, focus and the active tab.
   Selected items use blue (`Brush.Selection`), or gray (`Brush.Selection.Inactive`) when their list doesn't have focus.
4. **Every window derives from `ChromeWindow`.** Don't use a plain `Window`.
5. **Icons come from `Icons.cs`.** Add a named constant; never use a raw code point in XAML.

## Tokens

**Surfaces**, from back to front:
- `Surface.Chrome`: title bar, toolbar, status bar and the gutters between panels
- `Surface.PanelHeader`: tab strips
- `Surface.Panel`: tool panels
- `Surface.Document`: the central editing area
- `Surface.Flyout`: menus, dropdowns and tooltips

**Text**:
- `Primary`
- `Secondary`: labels
- `Tertiary`: metadata and timestamps
- `Disabled`
- `OnAccent`: text on orange

**Type**:
- Font families: Segoe UI for the UI, Cascadia Mono for code and logs, Segoe Fluent Icons for icons
- Body text is 12px
- Sizes: `Caption` 11, `Body` 12, `Code` 13, `Subtitle` 13, `Title` 15, `Display` 20

**Metrics**:
- Controls are 24px tall
- Toolbar buttons are 28px
- Tab headers are 28px
- The title bar is 32px
- Spacing scale: `Space.XXS` through `Space.XL` (2, 4, 8, 12, 16, 24)

## Named styles

| Key | Use |
| --- | --- |
| `Style.Button.Primary` | The one main action in a dialog or form |
| `Style.Button.Toolbar` | Icon-only button for toolbars and panel headers |
| `Style.ToggleButton.Toolbar` | Icon toggle for filters and modes (dimmed when off) |
| `Style.TextBox.Code` | Full-bleed monospace editing surface |
| `Style.TabControl.Document` | Tabs for the central document area |
| `Style.Separator.Vertical` | Divider between toolbar button groups |
| `Style.Icon` | `TextBlock` that renders an `Icons` glyph |
| `Style.Text.*` | `Display`, `Title`, `Subtitle`, `Body`, `Secondary`, `Caption`, `Overline`, `Mono` |

## Not yet styled

These still use the default WPF look and need styles before use: `Slider`, `Expander`, `ProgressBar`,
`RadioButton`, `DataGrid`, `ListView`/`GridView`, and editable `ComboBox`.
