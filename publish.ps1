# Thunderstore publish helper script
$ErrorActionPreference = "Stop"

$tokenFile = "$env:APPDATA\ThunderstoreCLI\token"
$localTokenFile = Join-Path $PSScriptRoot ".thunderstore_token"

$token = $null
if (Test-Path $tokenFile) {
    $token = (Get-Content -Raw $tokenFile).Trim()
} elseif (Test-Path $localTokenFile) {
    $token = (Get-Content -Raw $localTokenFile).Trim()
}

if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Error "Thunderstore token not found! Please place it in $tokenFile or $localTokenFile"
    exit 1
}

Write-Host "Publishing WorkableBoombox to Thunderstore..." -ForegroundColor Cyan
Push-Location $PSScriptRoot
try {
    tcli publish --token $token
} finally {
    Pop-Location
}
