# Mic Control for Windows

Put `MicControl.exe` into a folder of its own and open it. The default shortcut is **Ctrl + Alt + M**. The app uses WPF and .NET Framework included with Windows 10/11. No administrator privileges or downloaded dependencies are needed.

## Settings

The entire interface is in English, including the system tray, keyboard picker, tooltips and error messages. Drag the top of the window to move it. On smaller screens, the settings area can be scrolled.

**Hotkey** — click the current shortcut. Click the top field to record a key or combination, or search the key list and select Ctrl, Alt, Shift and Win modifiers. Click **Apply** to save. Until then, changes are only a draft. Escape is a valid binding; close the panel to cancel. Single modifiers are recorded on release. Switching to another window cancels recording.

Ordinary keys, F1–F24, navigation, NumPad, a separate Num Enter, Print Screen, media keys, OEM keys and left/right modifiers are supported. Other Windows virtual-key codes appear at the end of the list; reserved codes may not exist on your keyboard. Holding a key does not repeatedly toggle the microphone. The current shortcut is suspended while editing so it does not interfere with search.

Fn, hardware-specific buttons and protected system shortcuts such as Ctrl+Alt+Delete may not reach the app. Some media-key drivers use a different input path. Ordinary bound keys are consumed when they trigger. Single modifier bindings retain their normal system behavior and toggle on release only when they were not used in another shortcut.

**Indicator** — show or hide the heart in the top-left corner of the primary display. It stays above ordinary windows and lets clicks pass through.

**Indicator size** — adjust from **50% to 200%**, in steps of 5%. Changes preview immediately on the desktop and save automatically. The default is 100%. Size includes the transparent area used by the glow; the heart and glow scale together.

**Color palette** — choose one of five coordinated pairs. The left swatch is microphone on; the right is microphone off. Gradients, glow, the large heart preview and the tray icon all use the selected palette. Palette changes fade smoothly and save automatically.

| Palette | Microphone on | Microphone off |
| --- | --- | --- |
| Nebula | Pink | Violet |
| Aurora | Mint | Rose |
| Glacier | Ice blue | Lavender |
| Ember | Amber | Coral |
| Lunar | Pearl | Muted lilac |

The default palette is Nebula. A whole heart with a double pulse and expanding glow means on. A broken heart means off. An unavailable device displays a dim broken heart and a status message in Settings. The indicator has no text or background panel.

**Launch at login** — start in the system tray when the current user signs into Windows. The value stays valid across updates, because an update keeps the exe path.

**Download updates automatically** — on by default. A new version found by the update check is downloaded in the background and verified; installing it always waits for a click on **Restart to update**. Off: the settings window and the tray menu offer **Download** instead.

The line below the switch shows the update status ("Mic Control is up to date", "Version X.Y.Z is ready to install", or the reason a check failed) and the button for the next step: **Check for updates**, **Download** or **Restart to update**.

Closing or minimizing Settings hides the window to the tray. Double-click the tray heart to reopen it. **Quit** exits the app. Quitting leaves the microphone in its current state.

The tray menu has **Toggle microphone**, **Settings**, the version, **Check for updates** (**Download update** when a new version waits for a download), **Restart to update** once a new version is ready, and **Quit**.

## Updates

The app checks GitHub Releases of `softoblivion/cute-mic-control` when it starts and then every 5 minutes while it runs. After a failed check the wait grows (10, 20, 40, up to 60 minutes) and returns to 5 minutes once a check succeeds; nothing is checked while Windows reports no network connection. With **Download updates automatically** on (the default) a new version is downloaded at once, but it is installed only when you choose **Restart to update**. A new version is saved to `cute-mic-control_data\updates\` and checked against the published size and SHA-256. When it is ready, a notification "Mic Control X.Y.Z is ready" appears; click it, or choose **Restart to update** in the tray menu or the settings window. The app closes, the new version replaces `MicControl.exe` in the same folder and starts in the tray, and a notification confirms "Updated to vX.Y.Z". Settings are kept.

Version 1.0.0 has no updater: replace its exe with the current release once by hand. An exe in a folder you cannot write to (for example `Program Files`) cannot update itself; download the release instead.

## Microphone behavior

The app controls the system mute state of the **default Windows input device** through Core Audio. State is checked every 500 ms, including external changes and default-device changes. If another app uses a different microphone, select the same device in Windows or that app. An app's internal mute is separate from Windows system mute.

The app does not record audio or log keystrokes. Its only network access is the update check and download from GitHub. Keyboard state is held only in memory to detect the shortcut; synthetic input is ignored. The overlay may not appear above exclusive fullscreen games.

## Files

Everything the app stores is in `cute-mic-control_data\` next to `MicControl.exe`: `settings.xml` (shortcut, indicator, palette, update switch) and `updates\` (downloaded versions and the update log). On the first start of 1.1.0 or later, settings of 1.0.0 from `%LOCALAPPDATA%\Mic Control\settings.xml` (or the older `%LOCALAPPDATA%\MicMute\settings.xml`) are copied into that folder, so the shortcut and appearance carry over; the old file is not changed. Launch at login uses the value `MicControlGlow` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; changing the setting also removes the legacy `MicMuteGlow` value.

To move the app, disable **Launch at login**, quit, move `MicControl.exe` together with `cute-mic-control_data\`, start it and enable the setting again. To uninstall, disable **Launch at login**, quit the app, then delete its folder.

Building from source, tests and the self-test: [BUILD.md](BUILD.md).
