param(
    [string]$Version = "2.2.4"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetPath = if ($dotnetCommand) { $dotnetCommand.Source } else { $null }
$localDotnet = Join-Path (Split-Path $root -Parent) ".tools\dotnet\dotnet.exe"
$hasSdk = $false
$usingLocalSdk = $false
if ($dotnetPath) {
    $sdks = & $dotnetPath --list-sdks 2>$null
    $hasSdk = -not [string]::IsNullOrWhiteSpace(($sdks -join ""))
}
if (-not $hasSdk -and (Test-Path -LiteralPath $localDotnet)) {
    $dotnetPath = $localDotnet
    $hasSdk = $true
    $usingLocalSdk = $true
}
if (-not $hasSdk) {
    throw ".NET 8 SDK not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0"
}
$releaseRoot = Join-Path $root "artifacts\release-v$Version"
$publishDir = Join-Path $releaseRoot "publish"
$packageDir = Join-Path $releaseRoot "CodexUsageWidget-v$Version-win-x64"
$zipPath = Join-Path $releaseRoot "CodexUsageWidget-v$Version-win-x64.zip"
$checksumPath = Join-Path $releaseRoot "SHA256SUMS.txt"

if (-not $usingLocalSdk) {
    & $dotnetPath restore (Join-Path $root "CodexUsageWidget.sln")
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }
}
& $dotnetPath test (Join-Path $root "CodexUsageWidget.sln") -c Release --no-restore `
    --disable-build-servers -m:1 -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw "dotnet test failed" }
& $dotnetPath publish (Join-Path $root "src\CodexUsageWidget\CodexUsageWidget.csproj") `
    -c Release -r win-x64 --self-contained true -o $publishDir --no-restore `
    --disable-build-servers -m:1 -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
Copy-Item (Join-Path $publishDir "CodexUsageWidget.exe") $packageDir -Force
Copy-Item (Join-Path $root "README.md") $packageDir -Force
Copy-Item (Join-Path $root "LICENSE") $packageDir -Force
Copy-Item (Join-Path $root "PRIVACY.md") $packageDir -Force

if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

$assets = @(
    (Join-Path $publishDir "CodexUsageWidget.exe"),
    $zipPath
)
$lines = foreach ($asset in $assets) {
    $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($asset))"
}
$lines | Set-Content -LiteralPath $checksumPath -Encoding ascii

Write-Host ""
Write-Host "Release assets:"
@($assets + $checksumPath) | ForEach-Object { Get-Item -LiteralPath $_ } |
    Select-Object Name, Length, FullName | Format-Table -AutoSize
