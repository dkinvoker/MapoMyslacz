# Buduje MapoMyślacza (Native AOT):
#   publish\win-<arch>\          - folder z aplikacją (exe + biblioteki Windows App SDK)
#   dist\MapoMyslacz.exe         - JEDEN plik do rozdania (aplikacja spakowana w środku)
#
#   powershell -ExecutionPolicy Bypass -File publish.ps1
#   powershell -ExecutionPolicy Bypass -File publish.ps1 -Arch arm64
#   powershell -ExecutionPolicy Bypass -File publish.ps1 -NoSingleFile
#   powershell -ExecutionPolicy Bypass -File publish.ps1 -Version 1.2.0     (nadpisuje numer z Directory.Build.props)
param(
    [ValidateSet('x64', 'arm64')][string]$Arch = 'x64',
    [switch]$NoSingleFile,
    [string]$Version
)
$ErrorActionPreference = 'Stop'

$app = Join-Path $PSScriptRoot 'src\MapoMyslacz\MapoMyslacz.csproj'
$launcher = Join-Path $PSScriptRoot 'src\MapoMyslacz.Launcher\MapoMyslacz.Launcher.csproj'
$out = Join-Path $PSScriptRoot "publish\win-$Arch"
$extra = @()
if ($Version) { $extra += "-p:Version=$Version" }

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish $app -c Release -r "win-$Arch" -o $out @extra
if ($LASTEXITCODE -ne 0) { throw "Publikacja aplikacji nie powiodła się (kod $LASTEXITCODE)." }

$size = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Folder:      {0}  ({1:N0} MB)" -f $out, $size) -ForegroundColor Green

if (-not $NoSingleFile) {
    $dist = Join-Path $PSScriptRoot 'dist'
    $name = if ($Arch -eq 'x64') { 'MapoMyslacz.exe' } else { "MapoMyslacz-$Arch.exe" }
    $tmp = Join-Path $PSScriptRoot "artifacts\launcher-$Arch"
    if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }

    # AppPublishDir bez końcowego "\" (inaczej \" psuje cudzysłów w linii poleceń)
    dotnet publish $launcher -c Release -r "win-$Arch" -o $tmp "-p:AppPublishDir=$out" @extra
    if ($LASTEXITCODE -ne 0) { throw "Budowanie pliku jednoplikowego nie powiodło się (kod $LASTEXITCODE)." }

    New-Item -ItemType Directory -Force $dist | Out-Null
    Copy-Item (Join-Path $tmp 'MapoMyslacz.exe') (Join-Path $dist $name) -Force
    $one = Get-Item (Join-Path $dist $name)
    $hash = (Get-FileHash $one.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ("Jeden plik:  {0}  ({1:N0} MB)" -f $one.FullName, ($one.Length / 1MB)) -ForegroundColor Green
    Write-Host ("SHA-256:     {0}" -f $hash)
}
