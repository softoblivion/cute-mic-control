# Builds release\MicControl.exe and release\SHA256SUMS with the .NET Framework 4 compiler that ships with Windows.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'src'
$out = Join-Path $root 'release'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$wpf = Join-Path (Split-Path $compiler) 'WPF'
$sources = @(Get-ChildItem -LiteralPath $src -Filter '*.cs' | ForEach-Object { $_.FullName })
$exe = Join-Path $out 'MicControl.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/out:$exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll /reference:System.Core.dll /reference:System.Xaml.dll "/reference:$wpf\WindowsBase.dll" "/reference:$wpf\PresentationCore.dll" "/reference:$wpf\PresentationFramework.dll" $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $out 'SHA256SUMS'), "$hash  MicControl.exe`n", (New-Object Text.UTF8Encoding $false))
Write-Host "MicControl.exe is ready: $exe"
