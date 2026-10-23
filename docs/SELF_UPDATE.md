# Self-update

Owning document of `src\Updater.cs`. The design is the one the other softoblivion desktop apps use (check on launch,
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

Besides the stage and message, a status carries the release it is about (copied in `Set()` from the found release,
empty while `Checking` and after an up-to-date or failed check): `Latest` (X.Y.Z), `Size` (bytes, -1 unknown),
`Notes` (the Markdown body of the release, null when the source has none or it is blank) and `PublishedAt` (epoch
seconds, -1 unknown). The update window (section 8) shows them.

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

- When: on every launch (in the background, the window and hotkey work at once), then every 5 minutes while the
  app runs (a `DispatcherTimer` on the app dispatcher, so it keeps firing while the window is hidden in the tray),
  and on **Check for updates** (tray menu or settings window). `UpdateSchedule.Wait(failures)` gives the wait:
  5 minutes, doubled after every failed check (10, 20, 40) up to 60 minutes; the first check that ends in another
  stage returns it to 5 minutes. A periodic tick is skipped, without counting as a failure, while Windows reports
  no network connection (`InternetGetConnectedState`) and while an update is already downloaded and waits for the
  restart. With **Download updates automatically** on (the default) a found version is downloaded at once; it is
  installed only on **Restart to update**.
- Source, first: `GET https://api.github.com/repos/softoblivion/cute-mic-control/releases/latest` with
  `Accept: application/vnd.github+json`, `X-GitHub-Api-Version: 2022-11-28` and the usual User-Agent
  (`MicControl/<version>`), 20 s timeout, the same retry rule as every request (one retry, none for 403 / 404 /
  429). From the document: `tag_name`, `body` (notes), `published_at` (ISO 8601 -> epoch seconds) and the
  `MicControl.exe` / `SHA256SUMS` entries of `assets[]` (`name`, `size`, `browser_download_url`). The
  unauthenticated API allows 60 requests per hour per IP; the check uses at most 12 (one per 5 minutes).
- Source, fallback: when the API answers 403 / 429 (quota), 404, does not answer, or returns a text that is not a
  release document with a `tag_name`, the check continues with
  `HEAD https://github.com/softoblivion/cute-mic-control/releases/latest` without following redirects; the tag is
  read from `Location: .../releases/tag/<tag>` (no quota, no notes, no date). No redirect or a 404 there means "No
  release has been published yet". The check is therefore never worse than the redirect alone; offline both fail
  and the message is "No internet connection".
- Newer only: `SemVer.IsNewer(latest, AppInfo.Version)`; equal or older is "up to date", never a downgrade.
- The asset size comes from the API document; after the fallback, from a `HEAD` of
  `.../releases/download/<tag>/MicControl.exe` (redirects followed), where a 404 means the release has no exe. A
  document without a `MicControl.exe` asset gives the same message.
- Test override: `MIC_CONTROL_RELEASE_URL` replaces both sources with a document in the same format (`docs\BUILD.md`,
  section 4); a missing or unreadable document there is "No release has been published yet", no fallback.
- A verified `updates\MicControl-<latest>.exe` that is already on disk goes straight to `Ready`.
- With `autoDownloadUpdates` on (default) the same background operation continues with the download.
- TLS: the exe has no target-framework attribute, so .NET runs it with the 4.0 defaults (TLS 1.0); `WebNet`
  enables TLS 1.2 / 1.3 before the first request, otherwise every GitHub request fails.

## 4. Download and verification

1. Fetch `SHA256SUMS` of the tag and take the `MicControl.exe` entry; no file or no entry stops here.
2. Stream the exe to `updates\MicControl-<version>.exe.part`, hashing while writing; progress once per percent.
3. Size (when known) and SHA-256 must match, otherwise the `.part` is deleted ("damaged").
4. Rename to `MicControl-<version>.exe`, stage `Ready`. The update window (section 8) shows **Restart to update**,
   or restarts at once when the user pressed **Install**; when the window is closed, the tray balloon "Mic Control
   X.Y.Z is ready" appears (a click on it applies). The tray item and the settings button **Restart to update** open
   the update window. Nothing installs without a click.

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
| Offline, GitHub limit, no release | status `Error`, nothing on disk changes, retried with a growing wait (10, 20, 40, then 60 minutes), back to 5 minutes after the next successful check |
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
  tag, missing asset, release document override, API document with notes / date / size and its headers, API 403 /
  429 / garbage / no tag / offline falling back to the redirect), download (ready, progress, checksum / size mismatch, missing
  checksum), verified earlier download, automatic download, apply (marker, helper arguments, refusals), startup
  cleanup and the one-time toast, and the swap against a running stand-in exe in a folder with spaces and
  parentheses (rename while running, restore on a failed copy, the full apply mode waiting for the stand-in).
- End-to-end before every release that touches the update path: `docs\BUILD.md`, section 5.
- The update window is not part of `--self-test` (like the updater, it is created only outside the self-test); it is
  checked in the end-to-end run against a local release document with notes.

## 8. Update window

`src\UpdateWindow.cs` (`UpdateWindow`, `MarkdownView`), wired in `SettingsWindow`: the update notice shared by the
softoblivion desktop apps (the "update sheet": a modal notice with the release notes, Install and Later) as a WPF
window. It is
a top-level window of its own, so it works while the settings window is hidden in the tray; every status reaches it
through the dispatcher-marshalled `OnStatus` of the settings window.

- Look: the settings window's frame (theme `Frame` / `FrameEdge` / `FrameShadow`, the notes on a `Sheet` panel),
  Bahnschrift, the shared control styles (`Tinted` for the main button, the plain plate for **Later**, `Ghost` for
  the close button), entrance fade and rise. 460 wide, height to content up to 70 % of the work area (the notes
  scroll), fixed size, centred over the settings window when it is visible, otherwise on the work area; dragged by
  its header. A theme change recolours it with the settings window (shared `ThemeLook`).
- Opens by itself the first time in a run that a status names a newer version (`Available`, `Downloading` or
  `Ready` of a version not announced yet, including a verified earlier download found at launch); never for
  `UpToDate`, `Error` or `Checking`. It opens again only from the tray item (**Download update**,
  **Downloading update…**, **Restart to update**) or the settings button (**Download**, **Show progress**, **Show
  update**, **Restart to update**), which open the window instead of acting while a newer version is known.
- Content: "Mic Control X.Y.Z"; "Installed vA.B.C · 12.1 MB · Oct 21, 2026" (size in KB under 1 MB; date in local
  time, en-US; unknown parts left out); the notes rendered from Markdown (headings as bold lines, `-` / `*` / `+`
  bullets and numbered items with their indent, quotes, fenced code, rules, paragraphs; inline bold, italic, code,
  `[text](url)` and bare links; only http / https targets become links, opened in the browser; HTML stays text), or
  "No release notes for this version."; while downloading a bar and "42% · 5.1 MB of 12.1 MB".
- Buttons by stage: `Available` **Install** (arms the install and starts the download; while the updater still
  finishes a check it retries every 400 ms); `Downloading` "Downloading… 42%" (disabled); `Ready` **Restart to
  update**, or the restart at once when the install is armed (`Updater.Apply()`, then the app quits); `Ready` with
  `CanApply` false: disabled, the tooltip says why; `Error` after a failed download: the message and **Try again**
  (download again, armed); `Error` after a failed check: **Try again** starts a check; `Checking` "Checking…"
  (disabled). `UpToDate` hides the window and forgets the version.
- **Later**, the close button, Esc and closing the window hide it and disarm a pending install; it does not open
  again by itself for the same version in this run. The ready balloon still appears for a background download that
  finishes while the window is hidden.
