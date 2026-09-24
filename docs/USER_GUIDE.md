# Mic Control for Windows

Open `Mic Control.exe`. The default shortcut is **Ctrl + Alt + M**. The app uses WPF and .NET Framework included with Windows 10/11. No administrator privileges or downloaded dependencies are needed.

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

**Launch at login** — start in the system tray when the current user signs into Windows. Existing startup settings remain intact when updating.

Closing or minimizing Settings hides the window to the tray. Double-click the tray heart to reopen it. **Quit** exits the app. Quitting leaves the microphone in its current state.

## Microphone behavior

The app controls the system mute state of the **default Windows input device** through Core Audio. State is checked every 500 ms, including external changes and default-device changes. If another app uses a different microphone, select the same device in Windows or that app. An app's internal mute is separate from Windows system mute.

The app does not record audio, log keystrokes or access the network. Keyboard state is held only in memory to detect the shortcut; synthetic input is ignored. The overlay may not appear above exclusive fullscreen games.

## Files and maintenance

- `App.cs`: startup and preference persistence.
- `CoreAudio.cs`: Windows microphone control.
- `Keyboard.cs`: global input and key catalog.
- `Palettes.cs`: five coordinated gradient and glow pairs.
- `Theme.cs`: controls, hearts, animation and overlay.
- `SettingsWindow.cs`: settings UI and tray.
- `Tests.cs`: input, preference, appearance and rendering checks.
- `build.ps1`: build with the Windows .NET Framework compiler.

Build from PowerShell: `powershell -ExecutionPolicy Bypass -File .\build.ps1`.

Run `Mic Control.exe --self-test` with the main app closed. It checks shortcuts, preference migration and persistence, live size/palette controls, the keyboard hook and rendering. It also briefly toggles the microphone and restores its initial state. Reports and PNG previews are written next to the executable. Preference tests use temporary files; they do not overwrite your settings.

Preferences are stored in `%LOCALAPPDATA%\Mic Control\settings.xml`. Existing settings from `%LOCALAPPDATA%\MicMute\settings.xml` are imported automatically, so the rename does not reset your shortcut or appearance. Startup uses `MicControlGlow` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; changing the setting also removes the legacy `MicMuteGlow` value.

To uninstall, disable **Launch at login**, quit the app, then remove its folder. To move the app, disable startup first, move it, then enable startup from the new location.
