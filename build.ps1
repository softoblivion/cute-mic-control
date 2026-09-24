$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$wpf = Join-Path (Split-Path $compiler) 'WPF'
$sources = @('App.cs','CoreAudio.cs','Keyboard.cs','Palettes.cs','Theme.cs','SettingsWindow.cs','Tests.cs','Version.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/out:$PSScriptRoot\Mic Control.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll /reference:System.Core.dll /reference:System.Xaml.dll "/reference:$wpf\WindowsBase.dll" "/reference:$wpf\PresentationCore.dll" "/reference:$wpf\PresentationFramework.dll" $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host 'Mic Control.exe is ready.'
