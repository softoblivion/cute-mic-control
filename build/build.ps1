# Builds release\MicControl.exe and release\SHA256SUMS with the .NET Framework 4 compiler that ships with Windows,
# after compiling and running the update tests (tests\UpdateTests.cs). Windows PowerShell 5.1 compatible.
#   -Version <x.y.z>  build-time version override for update tests; src\Version.cs stays unchanged
#   -OutDir <folder>  output folder instead of release\
#   -SkipTests        skip the update tests (never for a release build)
[CmdletBinding()]
param([string]$Version, [string]$OutDir, [switch]$SkipTests)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'src'
$obj = Join-Path $root 'obj'
if ($OutDir) { $out = [IO.Path]::GetFullPath($OutDir) } else { $out = Join-Path $root 'release' }
New-Item -ItemType Directory -Force -Path $out, (Join-Path $obj 'tests') | Out-Null

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$framework = Split-Path $compiler
$wpf = Join-Path $framework 'WPF'

$versionFile = Join-Path $src 'Version.cs'
$current = [regex]::Match((Get-Content -LiteralPath $versionFile -Raw), 'Version = "(\d+\.\d+\.\d+)"').Groups[1].Value
if (!$current) { throw 'Version not found in src\Version.cs' }
if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must be X.Y.Z, got '$Version'" }
    $text = (Get-Content -LiteralPath $versionFile -Raw).Replace($current, $Version)
    $versionFile = Join-Path $obj 'Version.cs'
    [IO.File]::WriteAllText($versionFile, $text, (New-Object Text.UTF8Encoding $false))
    $current = $Version
}

$common = @('/nologo', '/platform:anycpu', '/optimize+', '/codepage:65001', '/reference:System.Core.dll', "/reference:$framework\System.Web.Extensions.dll")

if (!$SkipTests) {
    $testExe = Join-Path $obj 'tests\MicControl.Tests.exe'
    $testSources = @((Join-Path $root 'tests\UpdateTests.cs'), (Join-Path $src 'Updater.cs'), (Join-Path $src 'Paths.cs'), $versionFile)
    & $compiler @common /target:exe "/out:$testExe" $testSources
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Update tests failed' }
}

$sources = @(Get-ChildItem -LiteralPath $src -Filter '*.cs' | Where-Object { $_.Name -ne 'Version.cs' } | ForEach-Object { $_.FullName }) + $versionFile
$exe = Join-Path $out 'MicControl.exe'
& $compiler @common /target:winexe "/out:$exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll /reference:System.Xaml.dll "/reference:$wpf\WindowsBase.dll" "/reference:$wpf\PresentationCore.dll" "/reference:$wpf\PresentationFramework.dll" $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $out 'SHA256SUMS'), "$hash  MicControl.exe`n", (New-Object Text.UTF8Encoding $false))
Write-Host "MicControl.exe $current is ready: $exe"
