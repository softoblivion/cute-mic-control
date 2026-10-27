# Build, test and release

## 1. Toolchain

Nothing to install: Windows 10 / 11 with .NET Framework 4.8 (preinstalled) and Windows PowerShell 5.1.
`build\build.ps1` uses `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (falls back to `Framework\`).
Verified 2026-10-21: compiler 4.8.9221.0 (C# 5), Windows 11.

## 2. Build

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1
```

Steps: compile `tests\UpdateTests.cs` with `src\Paths.cs`, `src\UpdateSetup.cs`, `src\Version.cs` and the vendored
library `src\vendor\selfupdate-dotnet\*.cs` into `obj\tests\MicControl.Tests.exe` and run it (a failure stops the
build), write the fonts of `src\fonts\*.ttf` into `obj\MicControl.g.resources` (keys `fonts/<file name>` in lower
case, the resource set WPF reads `pack://application:,,,/fonts/` from), compile `src\*.cs` and the library into
`release\MicControl.exe` with that resource (WinExe, AnyCPU, about 330 KB), write `release\SHA256SUMS`
(`<sha256 lowercase>  MicControl.exe`, LF).

The update library is not edited here: its copy in `src\vendor\selfupdate-dotnet\` is synced from the library's own
source and `VENDORED.txt` names the version. The fonts are generated, not hand-edited: `python
build\make-fonts.py <folder with Sora-latin.woff2 and GeistMono-Variable.woff2> src\fonts` (needs `fonttools` and
`brotli`) writes static instances with their exact weights and without the `STAT` table.

| Parameter | Effect |
|---|---|
| `-Version 1.1.1` | builds that version without editing `src\Version.cs` (a patched copy goes to `obj\Version.cs`); for update tests only |
| `-OutDir <folder>` | output folder instead of `release\` |
| `-SkipTests` | skips the update tests; never for a release build |

## 3. Tests

- Update logic: run by every build (section 2). Output: one `PASS` / `FAIL` line per test and a total.
- Self-test of the app (keyboard engine, settings, live window, hook, microphone toggle, embedded faces, every text
  on a type role, title bar, Settings sheet, Scale on content and window, the spring of the scale thumb, rendered
  previews at 2x, which are also the README screenshots in `docs\assets\`):
  quit the running app, then

  ```powershell
  $env:MIC_CONTROL_HOME = "$env:TEMP\mic-control-selftest"
  Start-Process release\MicControl.exe --self-test -Wait
  Get-Content "$env:TEMP\mic-control-selftest\self-test\self-test.txt"
  ```

  The self-test toggles the microphone once and restores it. PNG previews land in the same folder.

## 4. Environment variables

| Variable | Effect |
|---|---|
| `MIC_CONTROL_HOME` | data folder instead of `cute-mic-control_data\` beside the exe; also disables the import of 1.0.0 profile settings |
| `MIC_CONTROL_RELEASE_URL` | URL of a JSON document in the GitHub "latest release" format (`tag_name`, `body` (release notes, optional), `published_at` (optional), `assets[].name`, `assets[].size`, `assets[].browser_download_url`); replaces the GitHub check (API and redirect) for update tests. 404 or unreadable -> "No release has been published yet", server down -> "No internet connection" |

## 5. End-to-end update test (before every release that touches the update path)

`powershell -ExecutionPolicy Bypass -File build\e2e-update.ps1 [-Mode auto|install|both] [-BalloonShots <folder>]`
does the steps below twice (**Download updates automatically** on, then off with **Install**), builds version N and
N+1 patch itself, presses the update window's button through UI Automation and prints one `PASS` / `FAIL` line per
assert; the exit code is 0 when all pass. The "Updated to" balloon is a Windows notification that UI Automation does
not list: with `-BalloonShots` the screen corners are saved as pictures 2.5 s after the new instance starts.
The steps by hand:

1. `build\build.ps1` (version N in `release\`), then `build\build.ps1 -Version <N+1> -OutDir <scratch>\server -SkipTests`.
2. In `<scratch>\server` write `latest.json`:
   `{"tag_name":"v<N+1>","body":"## What is new\n- A **bold** item","published_at":"2026-10-21T10:00:00Z","assets":[{"name":"MicControl.exe","size":<bytes>,"browser_download_url":"http://127.0.0.1:8765/MicControl.exe"},{"name":"SHA256SUMS","browser_download_url":"http://127.0.0.1:8765/SHA256SUMS"}]}`.
3. `python -m http.server 8765 --bind 127.0.0.1 --directory <scratch>\server`.
4. Copy `release\MicControl.exe` into an empty folder with a space in its name, set
   `MIC_CONTROL_RELEASE_URL=http://127.0.0.1:8765/latest.json` (and no `MIC_CONTROL_HOME`, so the real portable
   folder is used), start it; the update window opens with the notes, the size and the date and the download runs
   to **Restart to update** ("Version N+1 is ready to install" in Settings); note the hash of `settings.xml`, press
   **Restart to update** in the update window.
5. Assert: the old process exits, `MicControl.exe` in the same folder now has file version N+1 and runs with
   `--startup`, `MicControl.exe.old` is gone within about 15 s, `updates\` holds only `apply.log`, `settings.xml`
   is unchanged, no console window and no `cmd.exe` child appeared, `apply.log` shows wait / swapped / started.

Run of 2026-10-21 (1.1.0 -> 1.1.1): check and download in under a second, swap and restart 0.3 s after the click,
`.old` gone after 1.0 s, settings unchanged, no child processes.

## 6. Release procedure

1. Bump `src\Version.cs` (all version strings), write `docs\releases\vX.Y.Z.md`.
2. `build\build.ps1`; run the self-test; for update-path changes the end-to-end test of section 5.
3. Commit `Release vX.Y.Z` on `dev`, check that tracked files and history hold no personal data or local paths, push, tag `vX.Y.Z`, push the tag.
4. `gh release create vX.Y.Z release\MicControl.exe release\SHA256SUMS --title vX.Y.Z --notes-file docs\releases\vX.Y.Z.md`.
5. Download the published exe into a fresh folder and start it: the settings window must report
   "You have the latest version".

## 7. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Every update check says "No internet connection" although the browser works | TLS 1.2 not enabled: `WebNet`'s static constructor must run before the first request |
| "The app is already running" on start | another copy holds the mutex `Local\MicControl.Glow.App`; quit it from its tray menu |
| Self-test writes nothing | the app could not start; the exception text is in `<data>\self-test\self-test.txt` |
| A test run leaves `apply.log` in `obj\tests\` | expected: the apply-mode test logs beside the test runner |
