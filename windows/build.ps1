# Kıskaç Windows derleme betiği. Windows'la gelen .NET Framework 4.8 C# derleyicisini kullanır; kurulum gerektirmez.
# Çıktı: bin\Kiskac.exe (depo kökünde)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path $here -Parent
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $out | Out-Null

Get-Process Kiskac -ErrorAction SilentlyContinue | Stop-Process -Force

# Sürüm kökteki VERSION dosyasından gelir (macOS ve Linux ile ortak)
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
$versionCs = Join-Path $out 'Version.cs'
Set-Content -Encoding UTF8 $versionCs @"
[assembly: System.Reflection.AssemblyVersion("$version.0")]
[assembly: System.Reflection.AssemblyFileVersion("$version.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("$version")]
"@

$src = @((Get-ChildItem (Join-Path $here 'src') -Filter *.cs).FullName) + $versionCs
$common = @(
    '/nologo', '/target:winexe', '/optimize+', '/codepage:65001',
    "/win32manifest:$(Join-Path $here 'app.manifest')",
    '/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll'
)

# Metin tanıma (Windows.Media.Ocr) için Windows Runtime tanımları: Windows 10+ ile gelir.
$fw = Split-Path $csc -Parent
$winmd = Join-Path $env:WINDIR 'System32\WinMetadata'
$common += @(
    "/r:$(Join-Path $fw 'System.Runtime.dll')",
    "/r:$(Join-Path $winmd 'Windows.Foundation.winmd')",
    "/r:$(Join-Path $winmd 'Windows.Globalization.winmd')",
    "/r:$(Join-Path $winmd 'Windows.Graphics.winmd')",
    "/r:$(Join-Path $winmd 'Windows.Media.winmd')",
    "/r:$(Join-Path $winmd 'Windows.Security.winmd')",
    "/r:$(Join-Path $winmd 'Windows.Storage.winmd')"
)

# 1) Simgesiz derle, uygulamanın kendi logosunu .ico olarak dışa aktar
$tmp = Join-Path $out 'Kiskac.tmp.exe'
$ico = Join-Path $out 'Kiskac.ico'
& $csc @common "/out:$tmp" $src
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Start-Process $tmp -ArgumentList '--export-icon', "`"$ico`"" -Wait
Remove-Item $tmp

# 2) Simgeyle son derleme
& $csc @common "/win32icon:$ico" "/out:$(Join-Path $out 'Kiskac.exe')" $src
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Remove-Item $versionCs
Write-Host "OK: $(Join-Path $out 'Kiskac.exe') ($version)"
