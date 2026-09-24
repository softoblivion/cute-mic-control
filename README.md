# Mic Control

Tiny Windows mic toggle with a little heart glow indicator.

Current version: **1.0.0**

It lives in the tray, starts with Windows (optional), and shows a soft heart in the top-left when your default mic is on. , plus a 50–200% indicator size slider.

App is portable, and I swear it does not record audio or send anything anywhere :3

## Run

Open `MicControl.exe`. The default shortcut is `Ctrl + Alt + M`. Open Settings from the tray to change the shortcut, glow, size, palette or launch-at-login option.

## Build

On Windows PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The build uses the .NET Framework compiler already included with Windows.

## Tiny note

Fn keys and protected Windows shortcuts can be outside the app’s control. The app toggles the default Windows input device, so other apps need to use that same device.

More detailed behavior and maintenance notes are in [`docs/USER_GUIDE.md`](docs/USER_GUIDE.md).
