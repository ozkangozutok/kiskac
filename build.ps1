# Kiskac build script - uses the C# compiler that ships with Windows (.NET Framework 4.8), nothing to install.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $out | Out-Null

Get-Process Kiskac -ErrorAction SilentlyContinue | Stop-Process -Force

$src = (Get-ChildItem (Join-Path $root 'src') -Filter *.cs).FullName
$common = @(
    '/nologo', '/target:winexe', '/optimize+', '/codepage:65001',
    "/win32manifest:$(Join-Path $root 'app.manifest')",
    '/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll'
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
Write-Host "OK: $(Join-Path $out 'Kiskac.exe')"
