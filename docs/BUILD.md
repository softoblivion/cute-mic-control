# Build, test and release

## 1. Toolchain

Nothing to install: Windows 10 / 11 with .NET Framework 4.8 (preinstalled) and Windows PowerShell 5.1.
`build\build.ps1` uses `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (falls back to `Framework\`).
Verified 2026-10-21: compiler 4.8.9221.0 (C# 5), Windows 11.

## 2. Build

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1
```

Steps: compile `tests\UpdateTests.cs` with `src\Updater.cs`, `src\Paths.cs`, `src\Version.cs` into
`obj\tests\MicControl.Tests.exe` and run it (a failure stops the build), compile `src\*.cs` into
`release\MicControl.exe` (WinExe, AnyCPU, about 100 KB), write `release\SHA256SUMS`
(`<sha256 lowercase>  MicControl.exe`, LF).

| Parameter | Effect |
|---|---|
| `-Version 1.1.1` | builds that version without editing `src\Version.cs` (a patched copy goes to `obj\Version.cs`); for update tests only |
| `-OutDir <folder>` | output folder instead of `release\` |
| `-SkipTests` | skips the update tests; never for a release build |

## 3. Tests

- Update logic: run by every build (section 2). Output: one `PASS` / `FAIL` line per test and a total.
- Self-test of the app (keyboard engine, settings, live window, hook, microphone toggle, rendered previews):
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
| `MIC_CONTROL_RELEASE_URL` | URL of a JSON document in the GitHub "latest release" format (`tag_name`, `assets[].name`, `assets[].size`, `assets[].browser_download_url`); replaces the GitHub check for update tests. 404 or unreadable -> "No release has been published yet", server down -> "No internet connection" |

## 5. End-to-end update test (before every release that touches the update path)

1. `build\build.ps1` (version N in `release\`), then `build\build.ps1 -Version <N+1> -OutDir <scratch>\server -SkipTests`.
2. In `<scratch>\server` write `latest.json`:
   `{"tag_name":"v<N+1>","assets":[{"name":"MicControl.exe","size":<bytes>,"browser_download_url":"http://127.0.0.1:8765/MicControl.exe"},{"name":"SHA256SUMS","browser_download_url":"http://127.0.0.1:8765/SHA256SUMS"}]}`.
3. `python -m http.server 8765 --bind 127.0.0.1 --directory <scratch>\server`.
4. Copy `release\MicControl.exe` into an empty folder with a space in its name, set
   `MIC_CONTROL_RELEASE_URL=http://127.0.0.1:8765/latest.json` (and no `MIC_CONTROL_HOME`, so the real portable
   folder is used), start it, wait for "Version N+1 is ready to install", note the hash of `settings.xml`, press
   **Restart to update**.
5. Assert: the old process exits, `MicControl.exe` in the same folder now has file version N+1 and runs with
   `--startup`, `MicControl.exe.old` is gone within about 15 s, `updates\` holds only `apply.log`, `settings.xml`
   is unchanged, no console window and no `cmd.exe` child appeared, `apply.log` shows wait / swapped / started.

Run of 2026-10-21 (1.1.0 -> 1.1.1): check and download in under a second, swap and restart 0.3 s after the click,
`.old` gone after 1.0 s, settings unchanged, no child processes.

## 6. Release procedure

1. Bump `src\Version.cs` (all four version strings), write `docs\releases\vX.Y.Z.md`.
2. `build\build.ps1`; run the self-test; for update-path changes the end-to-end test of section 5.
3. Commit `Release vX.Y.Z` on `dev`, run the workspace pre-publish check, push, tag `vX.Y.Z`, push the tag.
4. `gh release create vX.Y.Z release\MicControl.exe release\SHA256SUMS --title vX.Y.Z --notes-file docs\releases\vX.Y.Z.md`.
5. Download the published exe into a fresh folder and start it: the settings window must report
   "Mic Control is up to date".

## 7. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Every update check says "No internet connection" although the browser works | TLS 1.2 not enabled: `WebNet`'s static constructor must run before the first request (see `docs\SELF_UPDATE.md`, section 3) |
| "The app is already running" on start | another copy holds the mutex `Local\MicControl.Glow.App`; quit it from its tray menu |
| Self-test writes nothing | the app could not start; the exception text is in `<data>\self-test\self-test.txt` |
| A test run leaves `apply.log` in `obj\tests\` | expected: the apply-mode test logs beside the test runner |
