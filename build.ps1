# Kiskac build script - uses the C# compiler that ships with Windows (.NET Framework 4.8), nothing to install.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $out | Out-Null

Get-Process Kiskac -ErrorAction SilentlyContinue | Stop-Process -Force

# Version comes from the VERSION file at the repo root (shared with the macOS build)
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
$versionCs = Join-Path $out 'Version.cs'
Set-Content -Encoding UTF8 $versionCs @"
[assembly: System.Reflection.AssemblyVersion("$version.0")]
[assembly: System.Reflection.AssemblyFileVersion("$version.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("$version")]
"@

$src = @((Get-ChildItem (Join-Path $root 'src') -Filter *.cs).FullName) + $versionCs
$common = @(
    '/nologo', '/target:winexe', '/optimize+', '/codepage:65001',
    "/win32manifest:$(Join-Path $root 'app.manifest')",
    '/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll'
)

# 1) Build without icon, export the app's own logo as .ico
$tmp = Join-Path $out 'Kiskac.tmp.exe'
$ico = Join-Path $out 'Kiskac.ico'
& $csc @common "/out:$tmp" $src
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Start-Process $tmp -ArgumentList '--export-icon', "`"$ico`"" -Wait
Remove-Item $tmp

# 2) Final build with icon
& $csc @common "/win32icon:$ico" "/out:$(Join-Path $out 'Kiskac.exe')" $src
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Remove-Item $versionCs
Write-Host "OK: $(Join-Path $out 'Kiskac.exe') ($version)"
