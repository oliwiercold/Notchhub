# Builds the NotchHub release artifacts:
#   artifacts/NotchHub-Setup-<version>.exe       notch-style installer (embeds the single-file NotchHub.exe)
#   artifacts/NotchHub-<version>-portable.zip    NotchHub.exe + Assets + README + LICENSE
#   artifacts/SHA256SUMS.txt
# Both executables are framework-dependent (win-x64) and need the .NET 10 Desktop Runtime.
# The version comes from <Version> in Directory.Build.props.
#
# Usage: pwsh tools/build-release.ps1 [-Configuration Release] [-NoClean]
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$NoClean
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$props = [xml](Get-Content -Raw (Join-Path $root "Directory.Build.props"))
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw "No <Version> in Directory.Build.props" }
$version = "$version".Trim()

$artifacts = Join-Path $root "artifacts"
$pubApp = Join-Path $artifacts "publish/app"
$pubSetup = Join-Path $artifacts "publish/setup"
$appProj = Join-Path $root "src/NotchHub.App/NotchHub.App.csproj"
$setupProj = Join-Path $root "src/NotchHub.Installer/NotchHub.Installer.csproj"

Write-Host "NotchHub $version ($Configuration)" -ForegroundColor Cyan

if (-not $NoClean -and (Test-Path $artifacts)) { Remove-Item -Recurse -Force $artifacts }
New-Item -ItemType Directory -Force $artifacts | Out-Null

function Invoke-Dotnet {
    param([string[]]$Arguments)
    Write-Host "> dotnet $($Arguments -join ' ')" -ForegroundColor DarkGray
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE" }
}

$common = @(
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained", "false",
    "-p:PublishSingleFile=true",
    "-p:DebugType=none",
    "-p:DebugSymbols=false",
    "-p:Version=$version",
    "-nologo"
)

# 1) the app, single file
Write-Host "`n[1/4] Publishing NotchHub.exe" -ForegroundColor Cyan
Invoke-Dotnet (@("publish", $appProj, "-o", $pubApp) + $common)
$appExe = Join-Path $pubApp "NotchHub.exe"
if (-not (Test-Path $appExe)) { throw "Publish did not produce $appExe" }

# 2) the installer, embedding the app
Write-Host "`n[2/4] Publishing the installer" -ForegroundColor Cyan
Invoke-Dotnet (@("publish", $setupProj, "-o", $pubSetup, "-p:NotchHubPayload=$appExe") + $common)
$setupOut = Join-Path $pubSetup "NotchHub-Setup.exe"
if (-not (Test-Path $setupOut)) { throw "Publish did not produce $setupOut" }
$setupExe = Join-Path $artifacts "NotchHub-Setup-$version.exe"
Copy-Item $setupOut $setupExe -Force

# 3) portable zip
Write-Host "`n[3/4] Portable zip" -ForegroundColor Cyan
$stage = Join-Path $artifacts "publish/portable/NotchHub"
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item $appExe $stage
$assets = Join-Path $stage "Assets"
New-Item -ItemType Directory -Force $assets | Out-Null
# the tray icon is loaded from Assets\app.ico next to the exe
Copy-Item (Join-Path $root "src/NotchHub.App/Assets/app.ico") $assets -Force
if (Test-Path (Join-Path $pubApp "Assets")) {
    Get-ChildItem (Join-Path $pubApp "Assets") -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring((Join-Path $pubApp "Assets").Length).TrimStart('\', '/')
        $dest = Join-Path $assets $rel
        New-Item -ItemType Directory -Force (Split-Path -Parent $dest) | Out-Null
        Copy-Item $_.FullName $dest -Force
    }
}
foreach ($doc in "README.md", "LICENSE", "NOTICE", "THIRD-PARTY-NOTICES.md") {
    $p = Join-Path $root $doc
    if (Test-Path $p) { Copy-Item $p $stage }
}
$zip = Join-Path $artifacts "NotchHub-$version-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal

# 4) checksums
Write-Host "`n[4/4] SHA-256" -ForegroundColor Cyan
$sums = foreach ($f in @($setupExe, $zip)) {
    $h = Get-FileHash -Algorithm SHA256 $f
    "{0}  {1}" -f $h.Hash.ToLowerInvariant(), (Split-Path -Leaf $f)
}
$sums | Set-Content -Encoding ascii (Join-Path $artifacts "SHA256SUMS.txt")
$sums | ForEach-Object { Write-Host $_ }

Write-Host "`nDone:" -ForegroundColor Green
Get-Item $setupExe, $zip | ForEach-Object { "{0,-40} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) } | Write-Host
