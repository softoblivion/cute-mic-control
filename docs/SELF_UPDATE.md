# Self-update

Owning document of `src\Updater.cs`. The design is the one the workspace's Python apps use (check on launch,
verified download, the downloaded exe swaps itself in), implemented in C# 5 inside this project. Tests:
`tests\UpdateTests.cs`.

## 1. Release layout

| Item | Rule |
|---|---|
| Repository | `softoblivion/cute-mic-control` (`AppInfo.Repository`) |
| Tag | `vX.Y.Z`; the numeric prefix is compared, a suffix such as `-beta.1` is ignored |
| Asset `MicControl.exe` | the build, exactly this name (`AppInfo.AssetName`) |
| Asset `SHA256SUMS` | one line `<sha256 lowercase>  MicControl.exe` (two spaces), written by `build\build.ps1` |

Only the release that `/releases/latest` points to is considered, so drafts and prereleases are never offered.
Renaming either asset strands every installed copy ("The release has no MicControl.exe" / "The release has no
checksum file").

## 2. States and messages

`Idle` -> `Checking` -> `UpToDate` | `Available` | `Error`; `Available` -> `Downloading` -> `Ready` | `Error`;
`Ready` -> apply. One operation at a time; a request while one runs is ignored. Every change is passed to
`OnStatus` as a copy (worker thread; the window marshals it to its dispatcher).

| Stage | Message |
|---|---|
| Idle | Updates are not checked yet |
| Checking | Checking for updates… |
| UpToDate | Mic Control is up to date |
| Available | Version X.Y.Z is available |
| Downloading | Downloading Mic Control X.Y.Z… 42% |
| Ready | Version X.Y.Z is ready to install |
| Error | No internet connection / GitHub limited the request, try again later / No release has been published yet / Could not read the release version / The release has no MicControl.exe / The release has no checksum file / The downloaded file is damaged (checksum mismatch) / Could not save the update file / The update is not ready to install / The downloaded update file is missing / Could not start the update helper / The update check failed |

## 3. Check

- When: on every launch (in the background, the window and hotkey work at once), every 6 hours while running,
  15 minutes after a failed check, and on **Check for updates** (tray menu or settings window).
- Source: `HEAD https://github.com/softoblivion/cute-mic-control/releases/latest` without following redirects;
  the tag is read from `Location: .../releases/tag/<tag>`. No API call, no API quota. No redirect or a 404 means
  "No release has been published yet".
- Newer only: `SemVer.IsNewer(latest, AppInfo.Version)`; equal or older is "up to date", never a downgrade.
- The asset size comes from a `HEAD` of `.../releases/download/<tag>/MicControl.exe` (redirects followed); a 404
  there means the release has no exe.
- A verified `updates\MicControl-<latest>.exe` that is already on disk goes straight to `Ready`.
- With `autoDownloadUpdates` on (default) the same background operation continues with the download.
- TLS: the exe has no target-framework attribute, so .NET runs it with the 4.0 defaults (TLS 1.0); `WebNet`
  enables TLS 1.2 / 1.3 before the first request, otherwise every GitHub request fails.

## 4. Download and verification

1. Fetch `SHA256SUMS` of the tag and take the `MicControl.exe` entry; no file or no entry stops here.
2. Stream the exe to `updates\MicControl-<version>.exe.part`, hashing while writing; progress once per percent.
3. Size (when known) and SHA-256 must match, otherwise the `.part` is deleted ("damaged").
4. Rename to `MicControl-<version>.exe`, stage `Ready`, tray balloon "Mic Control X.Y.Z is ready" and the tray
   item **Restart to update**; the settings window shows the same button. Nothing installs without that click.

## 5. Apply

`Updater.Apply()` (on the click) re-hashes the file, writes `updates\applied.json`
(`{"version": "X.Y.Z", "at": <epoch>}`) and starts
`updates\MicControl-X.Y.Z.exe --apply-update <pid> "<path of MicControl.exe>"` (`UseShellExecute=false`, a GUI exe
starting a GUI exe: no console, no cmd or bat). The app then quits the way **Quit** does.

Apply mode (`UpdateApply.Run`, routed in `Program.Main` before the mutex and any window), each step appended to
`updates\apply.log`:

1. Wait up to 120 s for `<pid>` to exit (a pid that no longer exists counts as exited).
2. Remove a stale `MicControl.exe.old` (or move it aside when locked); rename `MicControl.exe` to `.old`
   (retried 40 x 0.5 s: antivirus scans or the closing process may hold it); copy itself over `MicControl.exe` and
   compare sizes. Any failure after the rename deletes the partial copy and renames `.old` back.
3. Start `MicControl.exe --startup` (tray only) in its own folder and exit (0 = swapped, 1 = failed).

The new instance (`Updater.Startup`) shows the balloon "Updated to vX.Y.Z" once when `applied.json` names the
running version, deletes the marker, and in a background thread (20 x 0.5 s retries) deletes `MicControl.exe.old`
and every `MicControl-<v>.exe(.part)` in `updates\` that is not newer than the running version.

## 6. Failure handling

| Failure | Result |
|---|---|
| Offline, GitHub limit, no release | status `Error`, nothing on disk changes, retried after 15 minutes |
| Missing asset or checksum | status `Error`, nothing downloaded |
| Interrupted download, size or hash mismatch | `.part` deleted, status `Error`; the next attempt starts over |
| File changed or vanished before apply | file deleted, status `Error`, the app keeps running |
| Helper cannot start | marker deleted, "Could not start the update helper", the app keeps running |
| Exe locked for 20 s or copy fails | previous exe stays or is restored; `apply.log` says why; the update is offered again |
| Folder not writable (e.g. Program Files) | "Could not save the update file"; download the release by hand |

The updater shipped in a release is the one that installs the next one; a broken updater strands that version, and
the fix's release notes must say so. Version 1.0.0 has no updater: its copies need one manual download of 1.1.0.

## 7. Testing

- Unit tests (`tests\UpdateTests.cs`, run by `build\build.ps1`): version parsing and compare, `SHA256SUMS`
  parsing, file hash, check outcomes with a fake network (available, up to date, offline, limit, no release, bad
  tag, missing asset, release document override), download (ready, progress, checksum / size mismatch, missing
  checksum), verified earlier download, automatic download, apply (marker, helper arguments, refusals), startup
  cleanup and the one-time toast, and the swap against a running stand-in exe in a folder with spaces and
  parentheses (rename while running, restore on a failed copy, the full apply mode waiting for the stand-in).
- End-to-end before every release that touches the update path: `docs\BUILD.md`, section 5.
