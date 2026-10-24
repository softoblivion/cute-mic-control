# End-to-end test of the self-update path against a local HTTP server, run before every release that touches it
# (docs\BUILD.md, section 5). Builds version N (src\Version.cs) and N+1 (patch + 1) with build\build.ps1, serves N+1, its
# SHA256SUMS and a GitHub "latest release" document (latest.json) from 127.0.0.1, points MIC_CONTROL_RELEASE_URL at it,
# runs N from a scratch folder (with a space and parentheses in its name) and presses the update window's button through UI
# Automation. Two runs:
#   auto     automatic download on (the default): the update window opens, the download finishes, "Restart to update"
#   install  automatic download off: the window opens at "Install", the click downloads and restarts by itself
# Asserts per run: N+1 runs from the same path; no console window (no conhost / cmd child of the app's processes);
# "MicControl.exe.old" is gone within 15 s; the data folder is untouched (settings.xml and user files are kept, the marker
# and the used download are cleaned up). The new instance starts in the tray (--startup), so its windows are hidden and
# its "Updated to v<N+1>" notice is a Windows balloon, which UI Automation cannot read: with -BalloonShots <folder> the
# bottom-right corner of every screen is saved 2.5 s after the new instance starts, to be looked at (a balloon that
# Focus Assist holds back does not appear).
# Exit code 0: every assert passed. Windows PowerShell 5.1 compatible; the script text is ASCII on purpose.
#   -Mode auto|install|both (default both)   -Port <n> (default 8765)   -Keep: leave the scratch folder
#   -BalloonShots <folder>: save the screen corners of each run there
[CmdletBinding()]
param([ValidateSet('auto', 'install', 'both')][string]$Mode = 'both', [int]$Port = 8765, [switch]$Keep, [string]$BalloonShots)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public static class Dpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }'
[void][Dpi]::SetProcessDPIAware()
$root = Split-Path -Parent $PSScriptRoot
$asset = 'MicControl.exe'; $urlVariable = 'MIC_CONTROL_RELEASE_URL'; $homeVariable = 'MIC_CONTROL_HOME'; $dataName = 'cute-mic-control_data'
$versionN = [regex]::Match((Get-Content -LiteralPath (Join-Path $root 'src\Version.cs') -Raw), 'Version = "(\d+)\.(\d+)\.(\d+)"')
$n = "$($versionN.Groups[1].Value).$($versionN.Groups[2].Value).$($versionN.Groups[3].Value)"
$n1 = "$($versionN.Groups[1].Value).$($versionN.Groups[2].Value).$([int]$versionN.Groups[3].Value + 1)"

$scratch = Join-Path $env:TEMP ('update-e2e-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$serve = Join-Path $scratch 'serve'
New-Item -ItemType Directory -Force -Path $serve | Out-Null
$failures = 0
function Check($ok, $what) { if ($ok) { Write-Host "PASS  $what" } else { Write-Host "FAIL  $what"; $script:failures++ } }
function Wait-Until([scriptblock]$condition, [double]$seconds) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $seconds) { $r = & $condition; if ($r) { return $r }; Start-Sleep -Milliseconds 200 }
    return (& $condition)
}

# ---- builds ----
$build = Join-Path $root 'build\build.ps1'
& powershell -NoProfile -ExecutionPolicy Bypass -File $build -Version $n -OutDir (Join-Path $scratch "v$n") -SkipTests
if ($LASTEXITCODE -ne 0) { throw "build of $n failed" }
& powershell -NoProfile -ExecutionPolicy Bypass -File $build -Version $n1 -OutDir $serve -SkipTests
if ($LASTEXITCODE -ne 0) { throw "build of $n1 failed" }

# ---- release document and server ----
$base = "http://127.0.0.1:$Port/"
$notes = "## What is new`n- The update path is tested **end to end**`n- Release notes render ``code`` and [links](https://github.com/)`n`nMore text."
$document = [ordered]@{
    tag_name = "v$n1"; body = $notes; published_at = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
    assets = @(
        [ordered]@{ name = $asset; size = (Get-Item -LiteralPath (Join-Path $serve $asset)).Length; browser_download_url = $base + $asset },
        [ordered]@{ name = 'SHA256SUMS'; size = (Get-Item -LiteralPath (Join-Path $serve 'SHA256SUMS')).Length; browser_download_url = $base + 'SHA256SUMS' }
    )
}
[IO.File]::WriteAllText((Join-Path $serve 'latest.json'), ($document | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
$stopFile = Join-Path $scratch 'stop-server'
$server = Start-Job -ArgumentList $serve, $Port, $stopFile -ScriptBlock {
    param($dir, $port, $stopFile)
    $listener = New-Object Net.Sockets.TcpListener ([Net.IPAddress]::Loopback, $port); $listener.Start()
    try {
        while (-not (Test-Path -LiteralPath $stopFile)) {
            if (-not $listener.Pending()) { Start-Sleep -Milliseconds 30; continue }
            $client = $listener.AcceptTcpClient()
            try {
                $stream = $client.GetStream(); $reader = New-Object IO.StreamReader ($stream, [Text.Encoding]::ASCII, $false, 4096, $true)
                $request = $reader.ReadLine(); while ($true) { $h = $reader.ReadLine(); if ($h -eq $null -or $h -eq '') { break } }
                $parts = $request.Split(' '); $name = [Uri]::UnescapeDataString($parts[1].Split('?')[0].TrimStart('/'))
                $file = Join-Path $dir $name
                if ($name -and (Test-Path -LiteralPath $file -PathType Leaf)) { $body = [IO.File]::ReadAllBytes($file); $status = '200 OK' }
                else { $body = [Text.Encoding]::ASCII.GetBytes('not found'); $status = '404 Not Found' }
                $type = 'application/octet-stream'; if ($name -like '*.json') { $type = 'application/json' } elseif ($name -eq 'SHA256SUMS') { $type = 'text/plain' }
                $head = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 $status`r`nContent-Type: $type`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n")
                $stream.Write($head, 0, $head.Length); if ($parts[0] -ne 'HEAD') { $stream.Write($body, 0, $body.Length) }; $stream.Flush()
            } catch { } finally { $client.Close() }
        }
    } finally { $listener.Stop() }
}

# ---- UI Automation ----
function Windows-Of($processId) {
    $cond = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$processId)
    return [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children, $cond)
}
function Find-Element($processId, $controlType, $name, [switch]$Enabled) {
    $cond = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::ControlTypeProperty, $controlType)
    foreach ($w in Windows-Of $processId) {
        try { foreach ($e in $w.FindAll([Windows.Automation.TreeScope]::Descendants, $cond)) { if ($e.Current.Name -eq $name -and (!$Enabled -or $e.Current.IsEnabled)) { return $e } } } catch { }
    }
    return $null
}
# Windows draws the balloon in the shell, and UI Automation does not list it: the bottom-right corner of every screen is
# saved as a picture for the reader of the run.
function Save-Corners($folder, $prefix) {
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    foreach ($s in [Windows.Forms.Screen]::AllScreens) {
        $b = $s.Bounds; $w = [Math]::Min(700, $b.Width); $h = [Math]::Min(420, $b.Height)
        $bmp = New-Object Drawing.Bitmap $w, $h; $g = [Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($b.Right - $w, $b.Bottom - $h, 0, 0, $bmp.Size); $g.Dispose()
        $file = Join-Path $folder ($prefix + '-' + ($s.DeviceName -replace '[^\w]', '') + '.png'); $bmp.Save($file); $bmp.Dispose(); Write-Host "saved $file"
    }
}
function App-Processes { @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($scratch, [StringComparison]::OrdinalIgnoreCase) }) }

$savedUrl = [Environment]::GetEnvironmentVariable($urlVariable); $savedHome = [Environment]::GetEnvironmentVariable($homeVariable)
[Environment]::SetEnvironmentVariable($urlVariable, $base + 'latest.json'); [Environment]::SetEnvironmentVariable($homeVariable, $null)
try {
    $modes = @('auto', 'install'); if ($Mode -ne 'both') { $modes = @($Mode) }
    foreach ($run in $modes) {
        Write-Host "== $run ($n -> $n1)"
        $folder = Join-Path $scratch "run $run (portable)"; $data = Join-Path $folder $dataName
        New-Item -ItemType Directory -Force -Path $data | Out-Null
        $exe = Join-Path $folder $asset; Copy-Item -LiteralPath (Join-Path $scratch "v$n\$asset") -Destination $exe
        [IO.File]::WriteAllText((Join-Path $data 'keep.txt'), 'user data', (New-Object Text.UTF8Encoding $false))
        $auto = if ($run -eq 'install') { 'False' } else { 'True' }
        $settings = "<MicControl modifiers=`"3`" key=`"77`" overlay=`"False`" indicatorScale=`"100`" palette=`"0`" autoDownloadUpdates=`"$auto`" theme=`"bloom`" ui_scale=`"100`" />"
        [IO.File]::WriteAllText((Join-Path $data 'settings.xml'), $settings, (New-Object Text.UTF8Encoding $false))
        $old = Start-Process -FilePath $exe -WorkingDirectory $folder -PassThru
        $button = if ($run -eq 'install') { 'Install' } else { 'Restart to update' }
        $target = Wait-Until { Find-Element $old.Id ([Windows.Automation.ControlType]::Button) $button -Enabled } 60
        Check ($target -ne $null) "the update window offers '$button'"
        if ($target -eq $null) { Stop-Process -Id $old.Id -Force -ErrorAction SilentlyContinue; continue }
        $title = Wait-Until { Find-Element $old.Id ([Windows.Automation.ControlType]::Text) "Mic Control $n1" } 5
        Check ($title -ne $null) "the update window names version $n1"
        $target.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        # Watch for console processes under the app's processes until the new instance runs.
        $consoles = @(); $fresh = $null; $clock = [Diagnostics.Stopwatch]::StartNew()
        while ($clock.Elapsed.TotalSeconds -lt 90 -and $fresh -eq $null) {
            $all = @(Get-CimInstance Win32_Process)
            $ours = @($all | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($scratch, [StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { $_.ProcessId })
            $consoles += @($all | Where-Object { ($_.Name -eq 'conhost.exe' -or $_.Name -eq 'cmd.exe') -and $ours -contains $_.ParentProcessId } | ForEach-Object { $_.Name })
            $fresh = $all | Where-Object { $_.ProcessId -ne $old.Id -and $_.ExecutablePath -and [string]::Equals($_.ExecutablePath, $exe, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
            if ($fresh -eq $null) { Start-Sleep -Milliseconds 250 }
        }
        Check ($old.WaitForExit(5000)) "version $n exited"
        Check ($fresh -ne $null) "a new instance runs from the same path"
        if ($fresh -eq $null) { continue }
        if ($BalloonShots) { Start-Sleep -Milliseconds 2500; Save-Corners $BalloonShots "updated-balloon-$run" }
        Check ((Get-Item -LiteralPath $exe).VersionInfo.FileVersion -eq "$n1.0") "the exe at the path is $n1"
        Check ((Wait-Until { -not (Test-Path -LiteralPath "$exe.old") } 15) -eq $true) "$asset.old is gone within 15 s"
        Check ($consoles.Count -eq 0) "no console window ($($consoles -join ', '))"
        $updates = Join-Path $data 'updates'
        $clean = Wait-Until { -not (Test-Path -LiteralPath (Join-Path $updates 'applied.json')) -and @(Get-ChildItem -LiteralPath $updates -Filter '*.exe' -ErrorAction SilentlyContinue).Count -eq 0 } 15
        Check ($clean -eq $true) "the marker and the used download are cleaned up"
        Check (((Get-Content -LiteralPath (Join-Path $data 'keep.txt') -Raw) -eq 'user data') -and ((Get-Content -LiteralPath (Join-Path $data 'settings.xml') -Raw) -eq $settings)) "the data folder is untouched"
        $process = Get-Process -Id $fresh.ProcessId -ErrorAction SilentlyContinue
        if ($process) { Stop-Process -Id $process.Id -Force }
    }
} finally {
    [Environment]::SetEnvironmentVariable($urlVariable, $savedUrl); [Environment]::SetEnvironmentVariable($homeVariable, $savedHome)
    foreach ($p in App-Processes) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
    New-Item -ItemType File -Force -Path $stopFile | Out-Null
    Wait-Job $server -Timeout 10 | Out-Null; Remove-Job $server -Force
    if (!$Keep) { Start-Sleep -Milliseconds 500; Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue } else { Write-Host "scratch: $scratch" }
}
if ($failures -gt 0) { Write-Host "$failures assert(s) failed"; exit 1 }
Write-Host 'The update path works end to end.'
