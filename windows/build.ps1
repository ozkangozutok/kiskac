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
