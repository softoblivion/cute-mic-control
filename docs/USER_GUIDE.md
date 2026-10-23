# Mic Control user guide

Everything about using the app. Installation and the first steps are in the [README](../README.md).

## Usage

1. Put `MicControl.exe` into a folder of its own and open it. The window opens and the heart appears in the
   top-left corner of the primary display.
2. Press the hotkey (default **Ctrl + Alt + M**) to mute or unmute the microphone.
3. Close the window; the app keeps running in the system tray.

The app uses WPF and the .NET Framework included with Windows 10 / 11. No administrator privileges or downloaded
dependencies are needed.

| Action | How |
|---|---|
| Mute / unmute | the hotkey (default **Ctrl + Alt + M**), **Mute** in the window, or **Toggle microphone** in the tray menu |
| Change the hotkey | click the shortcut in the window → record or pick a key → **Apply** |
| Open the window | double-click the tray heart, or tray menu → **Settings** |
| Open Settings (appearance, scale, launch at login, updates) | the gear in the title bar |
| Install an update | **Install** or **Restart to update** in the update window that opens when a new version is found (the version in the title bar or tray menu → **Restart to update** reopens it), or click the "Mic Control X.Y.Z is ready" notification |
| Quit | tray menu → **Quit** (the microphone keeps its current state) |

The entire interface is in English, including the system tray, keyboard picker, tooltips and error messages.

The window has a title bar like the other softoblivion apps: the app name and the version on the left; Settings
(the gear), Minimize and Close on the right. Drag the title bar to move the window. The version shows a dot while
an update is available (mint once it is ready to install); clicking it checks for updates or opens the update
window. The main window holds the heart, the microphone state, **Mute**, the hotkey and the **Indicator** switch;
the gear opens the Settings sheet over it (the sheet scrolls; the gear, its close button or Esc close it).

Minimize and Close hide the window to the tray. Double-click the tray heart to reopen it. **Quit** exits the app.
Quitting leaves the microphone in its current state.

The tray menu has **Toggle microphone**, **Settings**, the version, **Check for updates** (**Download update** or
**Downloading update…** while a new version is known; both open the update window), **Restart to update** once a
new version is ready (opens the update window), and **Quit**.

## Keyboard

| Key | Action |
|---|---|
| **Ctrl + Alt + M** | Mute or unmute the microphone (the default hotkey; any key or combination can replace it) |
| Arrow keys | Move the theme choice while a theme chip has the focus, or the scale choice while the Scale control has it |
| `Esc` | Close the hotkey editor or the Settings sheet; in the update window: the same as **Later** |

**Changing the hotkey** — in Settings, click the current shortcut. Click the top field to record a key or
combination, or search the key list and select Ctrl, Alt, Shift and Win modifiers. Click **Apply** to save. Until
then, changes are only a draft. Escape is a valid binding; close the panel to cancel. Single modifiers are recorded
on release. Switching to another window cancels recording.

Ordinary keys, F1–F24, navigation, NumPad, a separate Num Enter, Print Screen, media keys, OEM keys and left/right
modifiers are supported. Other Windows virtual-key codes appear at the end of the list; reserved codes may not
exist on your keyboard. Holding a key does not repeatedly toggle the microphone. The current shortcut is suspended
while editing so it does not interfere with search.

Ordinary bound keys are consumed when they trigger. Single modifier bindings retain their normal system behavior
and toggle on release only when they were not used in another shortcut.

## Settings

| Setting | Where | Meaning |
|---|---|---|
| **Hotkey** | window | The shortcut that mutes and unmutes the microphone (see [Keyboard](#keyboard)). |
| **Indicator** | window | Show or hide the heart in the top-left corner of the primary display. It stays above ordinary windows and lets clicks pass through. |
| **Indicator size** | Settings → Appearance | **50% to 200%**, in steps of 5%; the default is 100%. Changes preview immediately on the desktop and save automatically. Size includes the transparent area used by the glow; the heart and glow scale together. |
| **Theme** | Settings → Appearance | The background colour and accent of the window: **Dusk**, **Nova**, **Frost** or **Bloom** (the default). |
| **Color palette** | Settings → Appearance | One of five coordinated on / off colour pairs for the heart; the default is Nebula. |
| **Scale** | Settings → Appearance | Size of the window and its text: **100 %**, **125 %**, **150 %**, **175 %** or **200 %** on top of the Windows display scaling; the default is 100 %. |
| **Launch at login** | Settings → General | Start in the system tray when the current user signs into Windows. |
| **Download updates automatically** | Settings → Updates | On by default. A new version found by the update check is downloaded in the background and verified; installing it always waits for a click on **Restart to update**. Off: the update window offers **Install** instead. |

**Scale** — the window and everything in it grow together and stay sharp; the heart indicator on the desktop keeps
its own size setting. A choice applies at once, keeps the window centred and is saved; the next start opens at that
size. Steps that would make the window larger than the screen are dimmed; when a saved step does not fit (for
example on a smaller monitor), the window opens at the largest step that fits and keeps the saved choice for a
larger screen. The update window follows the same scale.

**Theme** — **Dusk** (cobalt with a coral accent), **Nova** (violet with an ember accent), **Frost** (ice blue with
a lilac accent) and **Bloom** (magenta with a mint accent). The accent colours the Mute button, the slider, the
switches, the shortcut and the selection in the key list; the theme does not change the heart indicator. A choice
applies at once with a short colour fade and is saved. The same four themes exist in the other softoblivion
desktop apps.

**Color palette** — the left swatch is microphone on; the right is microphone off. Gradients, glow, the large heart
preview and the tray icon all use the selected palette. Palette changes fade smoothly and save automatically.

| Palette | Microphone on | Microphone off |
| --- | --- | --- |
| Nebula | Pink | Violet |
| Aurora | Mint | Rose |
| Glacier | Ice blue | Lavender |
| Ember | Amber | Coral |
| Lunar | Pearl | Muted lilac |

A whole heart with a double pulse and expanding glow means on. A broken heart means off. An unavailable device
displays a dim broken heart and a status message in Settings. The indicator has no text or background panel.

**Launch at login** — the value stays valid across updates, because an update keeps the exe path.

**Update status** — the line below the **Download updates automatically** switch shows the update status ("Mic
Control is up to date", "Version X.Y.Z is ready to install", or the reason a check failed) and a button: **Check
for updates** starts a check; while a new version is known (**Download**, **Show progress**, **Show update**,
**Restart to update**) it opens the update window.

### Settings file

Settings are stored in `cute-mic-control_data\settings.xml` next to the exe and saved as soon as they change.

| Key | Default | Meaning |
|---|---|---|
| `modifiers`, `key` | Ctrl + Alt, M | The hotkey (modifier bits Alt 1, Ctrl 2, Shift 4, Win 8; Windows virtual-key code) |
| `overlay` | `True` | Show the heart indicator |
| `indicatorScale` | `100` | Indicator size in percent, 50–200 |
| `theme` | `bloom` | Window theme: `dusk`, `nova`, `frost` or `bloom` (an unknown name falls back to `bloom`) |
| `palette` | `0` | 0 Nebula, 1 Aurora, 2 Glacier, 3 Ember, 4 Lunar |
| `autoDownloadUpdates` | `True` | Download a new version in the background; installing is always a click on **Restart to update** |
| `ui_scale` | `100` | Scale of the window in percent: 100, 125, 150, 175 or 200 (other numbers snap to the nearest step) |

**Launch at login** is not in the file: it is the value `MicControlGlow` under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; changing the setting also removes the legacy `MicMuteGlow`
value.

## Microphone behavior

The app controls the system mute state of the **default Windows input device** through Core Audio. State is
checked every 500 ms, including external changes (a mute from the Windows sound settings or a headset button) and
default-device changes.

## Data and privacy

Everything the app stores is in `cute-mic-control_data\` next to `MicControl.exe`; nothing else is written to the
user profile. The environment variable `MIC_CONTROL_HOME` replaces that folder (for a scratch data folder).

| Path | Content |
|---|---|
| `settings.xml` | Shortcut, indicator, theme, palette, scale, update switch (see [Settings file](#settings-file)) |
| `updates\` | Downloaded versions and the update log `apply.log` |
| `self-test\` | Report and previews of `MicControl.exe --self-test` ([BUILD.md](BUILD.md)) |

Outside that folder the app writes only the launch-at-login value in the registry (see
[Settings file](#settings-file)). Moving the exe together with its data folder moves the whole installation.

The app does not record audio or log keystrokes. Keyboard state is held only in memory to detect the shortcut;
synthetic input is ignored. Its only network access is the update check and download from GitHub.

> [!TIP]
> Upgrading from 1.0.0: that version kept its settings in `%LOCALAPPDATA%\Mic Control\settings.xml` (or the older
> `%LOCALAPPDATA%\MicMute\settings.xml`). The first start of 1.1.0 or later copies them into
> `cute-mic-control_data\`, so the shortcut and appearance stay as they were; the old file is not changed.

## Updates

- The app checks GitHub Releases of `softoblivion/cute-mic-control` on every start, then every 5 minutes while it
  runs (also while the window is hidden in the tray), and with **Check for updates** in the tray menu, the version in the title bar or the
  Settings sheet. After a failed check the wait grows (10, 20, 40, up to 60 minutes) and returns to 5 minutes once a
  check succeeds; nothing is checked while Windows reports no network connection.
- With **Download updates automatically** on (the default) a new version is downloaded at once in the background,
  but it is installed only when you choose **Restart to update**. A new version is saved to
  `cute-mic-control_data\updates\` and verified by byte size and SHA-256 against the `SHA256SUMS` asset of the
  release.
- When a new version is found, the update window opens: "Mic Control X.Y.Z", the installed version, the download
  size and the release date, and the release notes (links open in the browser). **Install** downloads the version
  and restarts into it as soon as it is verified; while it downloads, a bar shows the progress. When the version
  was already downloaded automatically, the button reads **Restart to update**.
- **Later** (or Esc) closes the window; it does not open again by itself for that version until the app restarts,
  and the tray menu item or the update button in Settings brings it back.
- When a background download finishes while the window is closed, a notification "Mic Control X.Y.Z is ready"
  appears; clicking it restarts into the update.
- **Restart to update** closes the app; the downloaded exe waits for it to exit, replaces `MicControl.exe` in the
  same folder (the previous file stays as `MicControl.exe.old` until the new one starts), and starts the new
  version in the tray; a notification confirms "Updated to vX.Y.Z". Settings are kept. If anything fails, the
  current exe stays as it was.
- Version 1.0.0 has no updater: replace its exe with the current `MicControl.exe` once by hand.
- An exe in a folder you cannot write to (for example `Program Files`) cannot update itself; download the release
  instead.
- Every release ships two assets: `MicControl.exe` and `SHA256SUMS`. Release notes: [`releases/`](releases/).

## FAQ

**A key does not work as the hotkey.** Fn, hardware-specific buttons and protected system shortcuts such as
Ctrl+Alt+Delete may not reach the app. Some media-key drivers use a different input path.

**Another app still hears me, or does not hear me.** If another app uses a different microphone, select the same
device in Windows or that app. An app's internal mute is separate from Windows system mute.

**The heart is missing over a game.** The overlay may not appear above exclusive fullscreen games.

**How do I move the app?** Disable **Launch at login**, quit, move `MicControl.exe` together with
`cute-mic-control_data\`, start it and enable the setting again.

**How do I uninstall it?** Disable **Launch at login**, quit the app, then delete its folder.

**How do I build it myself?** See [Building from source](../README.md#building-from-source) and
[BUILD.md](BUILD.md) for the build, the tests and the self-test.
