# Mic Control — architecture

## 1. Goal

A Windows tray utility that mutes and unmutes the default input device with one global hotkey and shows the state
as a heart on the desktop (whole and pulsing = on, broken = off, dim = no device). One portable exe, no installer,
no dependencies beyond what Windows 10 / 11 ships. It updates itself from GitHub Releases.

## 2. Stack and why

| Part | Choice | Why |
|---|---|---|
| Language | C# 5 | the compiler that ships with Windows (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) understands C# 5 only; no SDK to install |
| Runtime | .NET Framework 4.x (4.8 on current Windows) | preinstalled on Windows 10 / 11, so the exe is about 330 KB, most of it the two embedded typefaces |
| UI | WPF (settings window, overlay heart), WinForms (`NotifyIcon` tray menu and balloons) | WPF for the animated heart and the transparent click-through overlay; WPF has no tray icon |
| Self-update | the vendored library `selfupdate-dotnet` (`src\vendor\selfupdate-dotnet\`, namespace `SelfUpdate`) | one update engine for the C# apps; compiled into the exe, no package |
| Type | Sora and Geist Mono, embedded (`src\fonts\`) | the shared typeface of the softoblivion apps, identical on every PC |
| Audio | Core Audio COM (`IMMDeviceEnumerator`, `IAudioEndpointVolume`) | system mute of the default capture endpoint, the same switch Windows uses |
| Input | low-level keyboard hook (`WH_KEYBOARD_LL`) | any key or modifier combination, including single modifiers and media keys; injected input is ignored |
| Build | `build\build.ps1` calling `csc.exe` directly | no project file, no NuGet |

## 3. Repository layout

A plain layout without project files: no `.sln` / `.csproj` (direct `csc` build), all
application sources in one flat `src\` folder (one assembly, namespace `MicControl`) plus the vendored update
library in `src\vendor\`, and the self-test that needs the live window is compiled into the app (`src\Tests.cs`)
instead of living in `tests\`.

```
cute-mic-control\
  src\
    App.cs             entry point (apply mode, single instance, self-test switch), Preferences (settings.xml)
    Paths.cs           AppPaths: exe path and the portable data folder (the only place that builds these paths)
    UpdateSetup.cs     AppUpdate: the app's values for the update library (no WPF / WinForms references)
    vendor\selfupdate-dotnet\  the update library (check, verified download, apply mode, schedule); a synced copy,
                       never edited here; VENDORED.txt names its version
    SettingsWindow.cs  the window (title bar, main view, Settings sheet, hotkey editor), tray icon and menu, update
                       UI glue, launch-at-login registry value, UiScale (the Scale setting)
    UpdateWindow.cs    update window (new version, release notes from Markdown, progress, Install / Later)
    Typography.cs      TypeRole / TypeRoles: the type scale and the embedded faces
    Chrome.cs          TitleBar, Segmented (the segmented control), SpringEase, Icons
    fonts\             Sora-W450 / W550 / W600 / W650.ttf, GeistMono-W450.ttf and their licences (embedded)
    Theme.cs           control styles (plate buttons, title bar buttons, pill, switch, slider, list), HeartView, GlowOverlay (desktop indicator)
    Themes.cs          the four window themes, the colour derivation and the live brushes the window recolours
    Palettes.cs        five on / off colour pairs of the heart indicator
    Keyboard.cs        Native P/Invoke, KeyCatalog (248 key codes), KeyEngine (state machine), KeyboardHook
    CoreAudio.cs       Audio.Read / Audio.Toggle over Core Audio
    Tests.cs           --self-test: keyboard engine, preferences, live UI, hook, audio toggle, rendered previews
    Version.cs         the version (single source) and product constants
  tests\UpdateTests.cs console test runner for the app's use of the update library (UpdateSetup.cs, Paths.cs)
  build\build.ps1      tests, font resources, build, SHA256SUMS
  build\make-fonts.py  regenerates src\fonts\ from the published variable fonts (fontTools)
  docs\                ARCHITECTURE, SELF_UPDATE, BUILD, USER_GUIDE, THIRD_PARTY, releases\, assets\ (README images)
  .github\             issue templates
  release\             build output (ignored)
  obj\                 test runner and generated files (ignored)
```

## 4. Modules

| Module | Path | Owning doc | Notes |
|---|---|---|---|
| Startup and settings | `src\App.cs` | this file, section 5 | `Program.Main` routes `--apply-update` first, then the mutex `Local\MicControl.Glow.App` |
| Runtime paths | `src\Paths.cs` | this file, section 5 | `AppPaths.Data`, `.Settings`, `.Updates`, `.SelfTest`; `MIC_CONTROL_HOME` override |
| Self-update | `src\vendor\selfupdate-dotnet\`, `src\UpdateSetup.cs` | `docs\SELF_UPDATE.md` | `Updater`, `UpdateScheduler`, `UpdateApply`, `SemVer`, `Checksums`, `WebNet`, `Json` (namespace `SelfUpdate`); `AppUpdate.Config()` |
| Settings window and tray | `src\SettingsWindow.cs` | `docs\USER_GUIDE.md` | owns the `Updater` and the `UpdateScheduler` and passes every status to the title bar and the update window |
| Type, title bar, controls | `src\Typography.cs`, `src\Chrome.cs` | this file, section 10 | `TypeRoles`, `TitleBar`, `Segmented`, `SpringEase`, `UiScale` (in `SettingsWindow.cs`) |
| Update window | `src\UpdateWindow.cs` | `docs\SELF_UPDATE.md`, section 8 | `UpdateWindow`, `MarkdownView`; not created in the self-test |
| Indicator and styles | `src\Theme.cs`, `src\Palettes.cs` | `docs\USER_GUIDE.md` | the palettes colour the heart only |
| Window themes | `src\Themes.cs` | this file, section 8 | `ThemeSpec`, `Themes.All`, `ThemeLook` |
| Hotkey | `src\Keyboard.cs` | `docs\USER_GUIDE.md` | `KeyEngine` is pure logic, covered by the self-test |
| Microphone | `src\CoreAudio.cs` | `docs\USER_GUIDE.md` | polled every 500 ms |
| Tests | `src\Tests.cs`, `tests\UpdateTests.cs` | `docs\BUILD.md` | |

## 5. Runtime data (portable)

Everything the app writes lives in `cute-mic-control_data\` beside `MicControl.exe` (`MIC_CONTROL_HOME` replaces
that folder for tests and manual runs). Moving the exe together with this folder moves the installation.

| Path | Content |
|---|---|
| `settings.xml` | `<MicControl modifiers key overlay indicatorScale palette autoDownloadUpdates theme ui_scale />` (the older attributes are camelCase; `ui_scale` carries the name the Scale setting has in every app; `theme` is a theme name, unknown or missing -> `bloom`; `ui_scale` snaps to the nearest step, missing -> 100) |
| `updates\` | `MicControl-<version>.exe` (verified download), `.part` while downloading, `applied.json` marker, `apply.log` |
| `self-test\` | `self-test.txt` and PNG previews of `--self-test` |

Settings import (1.1.0): 1.0.0 kept `settings.xml` in `%LOCALAPPDATA%\Mic Control\` (and imported
`%LOCALAPPDATA%\MicMute\` before that). From 1.1.0 the portable folder is the only store. When it has no
`settings.xml` yet, the first start reads the profile file once and saves it into the portable folder; the profile
file is left in place and never written again. The import is skipped when `MIC_CONTROL_HOME` is set.

Outside the data folder the app writes one thing: the launch-at-login value `MicControlGlow` under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (path of the exe plus `--startup`), and it removes the legacy
`MicMuteGlow` value. A self-update keeps the exe path, so the value stays valid.

## 6. Version

`src\Version.cs` is the single source: the assembly attributes and `AppInfo.Version` / `AppInfo.DisplayVersion`.
The UI (window title, footer, tray menu) and the updater read `AppInfo`; `build.ps1` reads the file and can build
another version for tests without editing it (`-Version`, see `docs\BUILD.md`). Tags are `vX.Y.Z`.

## 7. Update flow (summary)

Launch -> background check of GitHub Releases (the latest-release API document with the notes, falling back to the
release redirect when the API is limited; repeated every 5 minutes, back-off up to 60 minutes after failures, skipped
offline) -> newer version found -> the update window opens with the release notes -> background download into
`updates\` (setting `autoDownloadUpdates`, default on, or **Install** in the window) -> verified by size and
SHA-256 -> **Restart to update** in the window (tray balloon when the window is closed) -> on click the downloaded exe runs in apply mode, waits for this process to exit, swaps the
exe and starts it in the tray -> the new instance removes `MicControl.exe.old` and shows "Updated to vX.Y.Z".
Details, messages and failure handling: `docs\SELF_UPDATE.md`.

## 8. Window themes (2.0.0)

The settings window follows the look shared by the softoblivion desktop apps (their web UI kit: themes, buttons
and motion): Mic Control re-expresses the same values in WPF and vendors nothing. `src\Themes.cs` holds the four themes (`dusk`, `nova`, `frost`, `bloom`; each a hue and an accent) and the
kit's derivation (`mix(a, b, k) = a + (b - a) * k` per channel: accent light / deep / ink, background top / mid /
bottom, line colour, glows, focus ring, tints), so the colours match the other apps to the rounding of a channel.

`ThemeLook` owns the live brushes and effects (accent, accent ink, accent gradient, frame gradient, the two
background fields, the orbit ring, the glows). The window and the controls hold them as local values or bindings;
`Apply()` recolours them in place, animated over 320 ms. They are never handed out through a style resource: WPF
freezes a brush that a style setter delivers, which would end the animation. The focus ring is the one resource
(`T.Focus`), replaced by a frozen brush on every change.

`Theme.Resources()` builds the control styles from XAML: the plate button template (fill, hairline, top bevel,
bottom shade, drop shadow, specular streak; hover over 120 ms, pressed 90 ms with a 2.5 % squish and an elastic
release over 220 ms, focus ring 2 px, disabled opacity 0.38), the `Tinted` style of the one primary button (accent
gradient, coloured glow, text shadow), the `Ghost` plate (plate on hover only), the title bar fills `TitleButton` /
`CloseButton` (30 x 28, fill on hover, red close), the version `Pill` and the `Segment` option. Plate pills bind
their corner radius to half of the control's height (`HalfRadius`), because a Border scales an oversized radius into
elliptical corners. The preference `Prefs.Theme` is saved with the other settings; the self-test switches a theme
through its chip and checks the preference round trip.

## 9. Windows

Every window of the app has a fixed size: the settings window (logical 372 x 500 plus a 12 px shadow margin, so
396 x 524 at 100 %), the indicator overlay (88 px times the indicator scale, click-through) and the update window
(logical 420 wide, its height follows the content up to 70 % of the work area). All three use `WindowStyle.None`
with `ResizeMode.NoResize`: no sizing border, no maximize, no size hook, so the page covers the whole client area.
The Scale setting (section 10) multiplies the settings and update windows; the overlay has its own size setting.

## 10. Design: type roles, title bar, density, Scale (2.2.0)

The app follows the visual specification shared by the softoblivion apps, written natively in WPF (no web code).

Type (`src\Typography.cs`): every text is one of the roles below (`TypeRole.Apply` on a TextBlock or a control,
`Theme.Label(text, role, brush)`); no element sets its own size. The roles are also a resource dictionary
(`TypeRoles.Dictionary()`, merged into the window resources): `Type.<Role>` (TextBlock style), `Type.<Role>.Size`,
`Type.<Role>.Weight`, `Type.Text`, `Type.Mono`, used by the button styles.

| Role | Weight | Size / line height (px) | Use in the app |
|---|---|---|---|
| Display | 650 | 20 / 24 | sheet titles: Settings, Hotkey, the update window title |
| Title | 650 | 16 / 20 | the Mute button, release-note headings |
| Section | 600 | 11 / 14.3, capitals | group headers (Appearance, General, Updates), the On / Off legend |
| Body | 450 | 13 / 18.85 | release notes, the key list |
| Label | 550 | 12.5 / 16.25 | the app name, setting labels, buttons, the shortcut |
| LabelS | 550 | 11.5 / 14.95 | small buttons (Quit, the update button, modifier chips), theme chips, scale options |
| Caption | 450 | 11.5 / 15.5 | status lines, hints, palette names, the footer |
| Mono | 450 | 12 / 16.8 | the key search field, code in release notes |
| MonoS | 450 | 11 / 14.3 | the version pill |

The faces are static TTF instances of the variable Sora and Geist Mono (`build\make-fonts.py`), embedded as
`MicControl.g.resources` (keys `fonts/<file>.ttf`, the layout WPF reads `pack://application:,,,/fonts/` from) and
loaded as `./fonts/#Sora` and `./fonts/#Geist Mono`. Each file declares its exact weight in `usWeightClass` and has
no `STAT` table (with it WPF reports weight 1 for 450 / 550 / 650 and picks the wrong file). WPF has no
letter-spacing, so the tracking of the roles is not reproduced; the section role writes its text in capitals.

Title bar (`TitleBar` in `src\Chrome.cs`): 44 px high, 10 px side padding; left the heart glyph (20 px, accent
gradient, glyph glow), the name (Label role) and the version pill (24 px capsule, MonoS, a 6 px dot in the accent
while a newer version is offered or downloading (pulsing), mint when ready; a click checks or opens the update
window); right one 34 px capsule (padding 3) with Settings (gear 17 px, turns 60 degrees on hover and while the sheet
is open), a separator, Minimize and Close (15 px icons, 30 x 28 fills, Close red on hover). Minimize and Close hide
the window to the tray, as before. The band between drags the window.

Density: control heights 48 (Mute, the hero action), 32 (the shortcut button, fields, update window buttons), 28
(small buttons), 26 (scale options), 24 (switches 40 x 24 with a 20 px knob, the version pill); setting rows at
least 46.

Scale (`UiScale` and `SettingsWindow.ApplyScale`): steps 100 / 125 / 150 / 175 / 200 % on top of Windows' own
scaling (the process is system-DPI aware). The window content is one `LayoutTransform` (`ScaleTransform`) on the
root grid and the window size is set to the logical size times the factor plus the shadow margin, so text and
vectors are rendered at the target size and stay sharp. The value is stored as `ui_scale`, read with the other
preferences and applied in the constructor, before the first show. A step whose window would not fit the work
area is dimmed in the control and not applied: a stored step that does not fit is shown and applied as the
largest step that fits, without changing the stored value. A change keeps the window's centre. The update window
takes the same factor (`UpdateWindow.SetScale`).

Segmented control (`Segmented`): equal options on a sunken pill track (padding 3), the selection under a raised
thumb that travels and resizes with `SpringEase` (a damped spring: response 0.42 s, damping 0.62, over 0.6 s; it
overshoots once and settles), arrow keys move the choice, dimmed options are skipped.
