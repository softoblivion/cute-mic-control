<div align="center">

<img src="docs/assets/hero.svg" alt="Mic Control" width="100%">

[![Download](https://img.shields.io/badge/Download-MicControl.exe-3ee6b8?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/softoblivion/cute-mic-control/releases/latest/download/MicControl.exe)
[![Release](https://img.shields.io/github/v/release/softoblivion/cute-mic-control?display_name=release&label=latest&style=for-the-badge)](https://github.com/softoblivion/cute-mic-control/releases/latest)

![Platform](https://img.shields.io/badge/platform-Windows_10_|_11-0078D6?logo=windows&logoColor=white)
![C#](https://img.shields.io/badge/C%23-5-512BD4?logo=dotnet&logoColor=white)
![.NET Framework](https://img.shields.io/badge/.NET_Framework-4.8-512BD4?logo=dotnet&logoColor=white)
![WPF](https://img.shields.io/badge/UI-WPF-68217A)
![PowerShell](https://img.shields.io/badge/PowerShell-5391FE?logo=powershell&logoColor=white)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)

**Tray control for the microphone and the speakers on Windows: a global hotkey mutes and unmutes the default microphone from anywhere, a held level stays where you set it however often apps change it, and one small window sets the volume of the microphone and the speakers, switches the default input and output device and plays the microphone through the speakers. A heart in the corner of the screen shows the mic state. Lives in the tray, one portable exe of about 420 KB, no installer, updates itself.**

<img src="docs/assets/settings.png" alt="Main window: microphone live, levels and meters" width="31%">
<img src="docs/assets/muted-held.png" alt="Microphone muted, speakers level held, Listen on" width="31%">
<img src="docs/assets/devices.png" alt="Device list of the microphone" width="31%">

<img src="docs/assets/settings-sheet.png" alt="Settings sheet with the desktop indicator, Appearance and Scale" width="31%">
<img src="docs/assets/palettes.png" alt="Five colour palettes, microphone on and off" width="64%">

<sub>Press the shortcut · the heart breaks when the mic is muted · set levels and devices in one window.</sub>

</div>

---

## Why Mic Control

- **One key, every app.** Mutes the Windows default input device itself, so calls, recorders and games all hear
  the same state, without looking for each app's own mute button.
- **You always know.** The heart overlay sits above ordinary windows and lets clicks pass through; the tray icon
  shows the same state.
- **Both ends of a call.** Microphone and speakers side by side: mute, level with a live meter, device choice, a
  level hold against apps that change it, and Listen to hear yourself.
- **Tiny and portable.** One exe of about 420 KB using the .NET Framework that ships with Windows. No installer, no
  admin rights, no background services. Settings stay in a folder next to the exe.
- **Free and open source.** GPL-3.0; build it yourself with the compiler that comes with Windows.
- **Nothing else.** No audio is recorded and no keystrokes are logged; the level meter reads the microphone only
  while the window is open. The only network traffic is the update check and download from GitHub.

## Quick start

1. Download [`MicControl.exe`](https://github.com/softoblivion/cute-mic-control/releases/latest/download/MicControl.exe)
   and put it into a folder of its own, for example `D:\Apps\MicControl\`.
2. Run it. The window opens with the microphone and the speakers, and the heart appears in the top-left corner.
3. Press **Ctrl + Alt + M** to mute or unmute. Close the window; the app keeps running in the tray.

**Requirements:** Windows 10 or 11 (.NET Framework 4.8 is preinstalled). No installer, no admin rights.

> [!NOTE]
> The executable is not code-signed, so Windows SmartScreen may warn on first start (**More info → Run anyway**).

## Features

| | |
|---|---|
| **Global hotkey** | Any key or combination: letters, F1–F24, NumPad and Num Enter, media keys, OEM keys, single modifiers (left or right), Ctrl / Alt / Shift / Win combinations. Record it or pick it from a searchable list. Holding the key does not toggle repeatedly. |
| **Microphone and speakers** | One card for each default device: a round mute button, the device name, a level slider with a live peak meter (0–100), and a hold button. The hotkey and Listen sit in the microphone card. |
| **Device switching** | Click a device name to pick another default microphone or output device from a list; the tray menu has the same lists. The chosen device becomes the default for every use, calls included. A link opens the Windows sound settings. |
| **Level hold** | Holds the microphone or speakers level: a change made by Windows, a driver or another app is undone at once. Holds are kept per device across restarts. |
| **Listen** | Plays the microphone through the speakers (the Windows "Listen to this device" option), to hear how you sound. |
| **Heart indicator** | Whole heart with a double pulse = on, broken heart = muted, dim = no input device. Above ordinary windows, click-through, 50–200 % size; switched on and off in Settings. |
| **Five palettes** | Nebula, Aurora, Glacier, Ember, Lunar; each a pair of on / off colours for the overlay, the preview and the tray icon. |
| **Four themes** | Dusk, Nova, Frost, Bloom: the background colour and accent of the settings window, shared with the other softoblivion desktop apps. |
| **Scale** | 100–200 % for the whole window and its text on top of the Windows display scaling; sharp at every step. |
| **Tray app** | Toggle microphone, the Microphone and Speakers device lists, Settings, update items and Quit in the tray menu; double-click opens the window. Optional launch at login. |
| **Follows Windows** | A mute, level or device change made in the Windows sound settings, by a headset button or by another app shows up at once. |
| **Self-update** | Checks GitHub Releases on start, downloads a new version in the background, verifies size and SHA-256, installs on one click. |
| **Self-test** | `--self-test` checks the hotkey engine, settings, the live window on the real devices (mute, level, hold, device list), the keyboard hook and the audio engine, and restores every value it changes, also when a check fails. |

## Documentation

| Document | What it covers |
|---|---|
| [User guide](docs/USER_GUIDE.md) | Usage, the microphone and speakers cards, hotkey, settings, data folder, updates, FAQ |
| [Build](docs/BUILD.md) | Toolchain, build, tests, environment variables, releasing |
| [Release notes](docs/releases/) | What changed in every version |
| [Third-party components](docs/THIRD_PARTY.md) | What the app uses from Windows, with licences |

## Building from source

Requirements: Windows 10 / 11 and Windows PowerShell 5.1. The build uses the C# compiler of the .NET Framework that
ships with Windows; there is nothing to install.

```powershell
git clone https://github.com/softoblivion/cute-mic-control.git
cd cute-mic-control
powershell -ExecutionPolicy Bypass -File build\build.ps1
```

The script runs the update tests and produces `release\MicControl.exe` with `release\SHA256SUMS`.

| Task | Command |
|---|---|
| Build and run the update tests | `powershell -ExecutionPolicy Bypass -File build\build.ps1` |
| Self-test of the app (quit the running app first) | `release\MicControl.exe --self-test`, report in `cute-mic-control_data\self-test\` |
| Use a scratch data folder | set `MIC_CONTROL_HOME` before starting the exe |

## License

Released under the [GNU General Public License v3.0](LICENSE). Any distributed copy or derivative work must stay
open source under the same license and keep the copyright notices.
