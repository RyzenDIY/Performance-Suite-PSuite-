# RustVision

A standalone WPF (.NET 8) desktop UI prototype — a dark, red-accent "configuration menu" look
in the style of modern gaming settings panels. It is **UI only**:

- No memory reading, DLL injection, hooking, or process interaction of any kind.
- No aimbot, ESP, or anti-cheat bypass logic.
- The "Live Preview" panel is a decorative local mock-up driven only by checkboxes on this
  window; it is not connected to any game or external process.
- Not tied to Rust or any other specific game.

The overall composition (sidebar width, header layout, card density) was adapted from a
reference screenshot the project owner provided, but functionality specific to that reference
(a live "Injected / Working" status, an "Enable Aimbot" toggle, head/bone targeting, no-recoil
sliders, NPC/player target filtering, real gameplay footage in the preview) was intentionally
**not** carried over — it falls under the exact things this project is scoped to avoid.

## Requirements

- Visual Studio 2022 (17.8+) with the ".NET desktop development" workload
- .NET 8 SDK

## Running

1. Open `RustVision.slnx` (or open the folder directly) in Visual Studio.
2. Press **F5**. This builds and launches a single `RustVision.exe` process.
3. Use the left navigation to switch between AIM / VISUALS / MISC / CONFIG / SETTINGS / ABOUT.

## Single instance

`App.xaml.cs` acquires a named Mutex (`RustVision.SingleInstance`) on startup. If it's already
held by a running instance, the new process shuts down immediately without creating a second
`MainWindow` — pressing F5 again while RustVision is already running will not open a duplicate.

## Config system

- `CONFIG` tab: choose a profile (Default / Legit / Custom) — switching profiles instantly loads
  that profile's saved settings. **SAVE**, **LOAD**, and **RESET** are also available explicitly.
- **Auto-save**: any change to a control (checkbox, slider, combo, accent color, compact mode,
  menu hotkey) is written to the currently selected profile ~600ms after you stop interacting
  with it, so you don't have to remember to hit SAVE.
- Files are written to a `configs/` folder created next to the executable
  (`bin/Debug/net8.0-windows/configs/...json`), independent from the `Config/*.json` seed files
  shipped with the source.
- Missing files or malformed JSON fall back to defaults instead of crashing.

## Accent color

`SETTINGS` → `ACCENT COLOR` offers six preset swatches (red/blue/purple/green/orange/cyan).
Picking one mutates the shared `BrushAccent` / `BrushAccentDark` resources at runtime (no
DynamicResource needed — every control that already references `BrushAccent` updates live),
and the chosen color is persisted per profile.

## Menu hotkey (window-only)

`SETTINGS` → `MENU HOTKEY` lets you pick Insert / F9 / Home / Delete. This registers a standard
Win32 global hotkey (`RegisterHotKey`) that minimizes/restores **this window only** — it does not
read, write, or attach to any other process, the same way a screenshot tool or volume overlay
registers its own shortcut.

## Compact mode

The icon button next to minimize/close in the header toggles a smaller window (~420×600), hides
the right-hand preview/status/actions sidebar, and collapses the left navigation to icons only.
Toggling back restores the previous window size. The state is remembered per profile.

## Logo

`Resources/logo.png` is the logo file you provided, cropped only to trim its transparent margins
— the artwork itself was not edited. It's wired in via WPF's `Resource` build action and shown at
three sizes through plain XAML `<Image Stretch="Uniform">` elements (header, sidebar branding
card, About tab) — same source file, no distortion, aspect ratio preserved at every size.

## Project layout

```
RustVision/
├── RustVision.csproj
├── RustVision.slnx
├── App.xaml / App.xaml.cs         (single-instance Mutex, manual window startup)
├── MainWindow.xaml / MainWindow.xaml.cs
├── UI/
│   ├── Theme.xaml       (colors, brushes, typography)
│   ├── Controls.xaml    (button/checkbox/slider/combobox/nav/accent-swatch templates)
│   └── Icons.xaml       (original vector glyphs, no third-party assets)
├── Resources/
│   └── logo.png         (provided by project owner)
├── Config/
│   ├── ConfigManager.cs
│   ├── default.json
│   └── legit.json
└── Properties/
    └── launchSettings.json
```

## Notes

- The window is borderless (`WindowStyle="None"`, `AllowsTransparency="True"`) with its own
  title bar; dragging the top bar moves the window, and resizing works from any edge via
  `WindowChrome`.
- No `LetterSpacing` property is used anywhere (that property doesn't exist on WPF `TextBlock`).
