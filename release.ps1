param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publish = Join-Path $root "publish"
$release = Join-Path $root "release"

dotnet publish (Join-Path $root "JumpzysVortex.App\JumpzysVortex.App.csproj") -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false -p:DebugType=none -p:DebugSymbols=false -o $publish

if (Test-Path $release) { Remove-Item -LiteralPath $release -Recurse -Force }
New-Item -ItemType Directory -Path $release | Out-Null

$version = "2.2.0"
$zip = Join-Path $release "JumpzysVortex_$version.zip"
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip -Force

$hash = Get-FileHash -LiteralPath $zip -Algorithm SHA256
$hash.Hash | Set-Content -LiteralPath (Join-Path $release "JumpzysVortex_$version.sha256")

@{
    version = $version
    file = [IO.Path]::GetFileName($zip)
    sha256 = $hash.Hash
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $release "update-manifest.json")

Write-Host "Release created in $release"
