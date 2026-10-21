# Mic Control — architecture

## 1. Goal

A Windows tray utility that mutes and unmutes the default input device with one global hotkey and shows the state
as a heart on the desktop (whole and pulsing = on, broken = off, dim = no device). One portable exe, no installer,
no dependencies beyond what Windows 10 / 11 ships. It updates itself from GitHub Releases.

## 2. Stack and why

| Part | Choice | Why |
|---|---|---|
| Language | C# 5 | the compiler that ships with Windows (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) understands C# 5 only; no SDK to install |
| Runtime | .NET Framework 4.x (4.8 on current Windows) | preinstalled on Windows 10 / 11, so the exe is about 100 KB |
| UI | WPF (settings window, overlay heart), WinForms (`NotifyIcon` tray menu and balloons) | WPF for the animated heart and the transparent click-through overlay; WPF has no tray icon |
| Audio | Core Audio COM (`IMMDeviceEnumerator`, `IAudioEndpointVolume`) | system mute of the default capture endpoint, the same switch Windows uses |
| Input | low-level keyboard hook (`WH_KEYBOARD_LL`) | any key or modifier combination, including single modifiers and media keys; injected input is ignored |
| Build | `build\build.ps1` calling `csc.exe` directly | no project file, no NuGet |

## 3. Repository layout

The default layout of the workspace, with these deviations: no `.sln` / `.csproj` (direct `csc` build), all
application sources in one flat `src\` folder (one assembly, namespace `MicControl`), and the self-test that
needs the live window is compiled into the app (`src\Tests.cs`) instead of living in `tests\`.

```
cute-mic-control\
  src\
    App.cs             entry point (apply mode, single instance, self-test switch), Preferences (settings.xml)
    Paths.cs           AppPaths: exe path and the portable data folder (the only place that builds these paths)
    Updater.cs         self-update: check, verified download, apply mode (no WPF / WinForms references)
    SettingsWindow.cs  settings window, tray icon and menu, update UI glue, launch-at-login registry value
    Theme.cs           styles, HeartView, GlowOverlay (desktop indicator)
    Palettes.cs        five on / off colour pairs
    Keyboard.cs        Native P/Invoke, KeyCatalog (248 key codes), KeyEngine (state machine), KeyboardHook
    CoreAudio.cs       Audio.Read / Audio.Toggle over Core Audio
    Tests.cs           --self-test: keyboard engine, preferences, live UI, hook, audio toggle, rendered previews
    Version.cs         the version (single source) and product constants
  tests\UpdateTests.cs console test runner for Updater.cs / Paths.cs, built and run by build.ps1
  build\build.ps1      tests, build, SHA256SUMS
  docs\                ARCHITECTURE, SELF_UPDATE, BUILD, USER_GUIDE, releases\, assets\ (README images)
  release\             build output (ignored)
  obj\                 test runner and generated files (ignored)
```

## 4. Modules

| Module | Path | Owning doc | Notes |
|---|---|---|---|
| Startup and settings | `src\App.cs` | this file, section 5 | `Program.Main` routes `--apply-update` first, then the mutex `Local\MicControl.Glow.App` |
| Runtime paths | `src\Paths.cs` | this file, section 5 | `AppPaths.Data`, `.Settings`, `.Updates`, `.SelfTest`; `MIC_CONTROL_HOME` override |
| Self-update | `src\Updater.cs` | `docs\SELF_UPDATE.md` | `Updater`, `UpdateApply`, `SemVer`, `Checksums`, `WebNet` |
| Settings window and tray | `src\SettingsWindow.cs` | `docs\USER_GUIDE.md` | the only UI consumer of `Updater` |
| Indicator and theme | `src\Theme.cs`, `src\Palettes.cs` | `docs\USER_GUIDE.md` | |
| Hotkey | `src\Keyboard.cs` | `docs\USER_GUIDE.md` | `KeyEngine` is pure logic, covered by the self-test |
| Microphone | `src\CoreAudio.cs` | `docs\USER_GUIDE.md` | polled every 500 ms |
| Tests | `src\Tests.cs`, `tests\UpdateTests.cs` | `docs\BUILD.md` | |

## 5. Runtime data (portable)

Everything the app writes lives in `cute-mic-control_data\` beside `MicControl.exe` (`MIC_CONTROL_HOME` replaces
that folder for tests and manual runs). Moving the exe together with this folder moves the installation.

| Path | Content |
|---|---|
| `settings.xml` | `<MicControl modifiers key overlay indicatorScale palette autoDownloadUpdates />` (attribute names follow the file's existing camelCase) |
| `updates\` | `MicControl-<version>.exe` (verified download), `.part` while downloading, `applied.json` marker, `apply.log` |
| `self-test\` | `self-test.txt` and PNG previews of `--self-test` |

Decision (2026-10-21, 1.1.0): 1.0.0 kept `settings.xml` in `%LOCALAPPDATA%\Mic Control\` (and imported
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

Launch -> background check of GitHub Releases -> newer version found -> background download into
`updates\` (setting `autoDownloadUpdates`, default on) -> verified by size and SHA-256 -> tray balloon and menu item
**Restart to update** -> on click the downloaded exe runs in apply mode, waits for this process to exit, swaps the
exe and starts it in the tray -> the new instance removes `MicControl.exe.old` and shows "Updated to vX.Y.Z".
Details, messages and failure handling: `docs\SELF_UPDATE.md`.
