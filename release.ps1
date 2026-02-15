param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$project = Join-Path $root "src/HellCaster.App/HellCaster.App.csproj"
$releaseRoot = Join-Path $root "release"
$latestDir = Join-Path $releaseRoot "latest"
$publishTemp = Join-Path $releaseRoot "_publish_temp"
$finalExe = Join-Path $releaseRoot "HellCaster-latest.exe"

Write-Host "[1/4] Cleaning old release outputs..."
if (Test-Path $latestDir) {
    Remove-Item $latestDir -Recurse -Force
}
if (Test-Path $publishTemp) {
    Remove-Item $publishTemp -Recurse -Force
}
if (Test-Path $finalExe) {
    Remove-Item $finalExe -Force
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
New-Item -ItemType Directory -Path $publishTemp -Force | Out-Null

Write-Host "[2/4] Publishing app..."
dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -o $publishTemp

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE"
}

Write-Host "[3/4] Moving newest build to release directory..."
$exe = Get-ChildItem -Path $publishTemp -Filter *.exe | Select-Object -First 1
if (-not $exe) {
    throw "No EXE produced by publish step."
}

New-Item -ItemType Directory -Path $latestDir -Force | Out-Null
Copy-Item -Path (Join-Path $publishTemp "*") -Destination $latestDir -Recurse -Force
Copy-Item -Path $exe.FullName -Destination $finalExe -Force

Write-Host "[4/4] Final cleanup..."
Remove-Item $publishTemp -Recurse -Force

$meta = @{
    builtAtUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    configuration = $Configuration
    runtime = $Runtime
    exe = "release/HellCaster-latest.exe"
} | ConvertTo-Json -Depth 3

Set-Content -Path (Join-Path $releaseRoot "latest.json") -Value $meta -Encoding UTF8

Write-Host "Release ready: $finalExe"
Write-Host "Clean latest folder: $latestDir"
