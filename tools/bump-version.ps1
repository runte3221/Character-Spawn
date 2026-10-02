# Bump-Version Script for Character Spawn
# Usage: powershell -File tools/bump-version.ps1 0.1.41.0
param (
    [Parameter(Mandatory=$true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path "$PSScriptRoot\.."

Write-Host "Updating Character Spawn to version $Version..."

# 1. Update CharacterSpawn.csproj
$csprojPath = Join-Path $repoRoot "CharacterSpawn.csproj"
if (Test-Path $csprojPath) {
    $content = Get-Content -Raw -Path $csprojPath
    $content = $content -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
    $content = $content -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$Version</AssemblyVersion>"
    $content = $content -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$Version</FileVersion>"
    Set-Content -Path $csprojPath -Value $content -NoNewline
    Write-Host "  Updated CharacterSpawn.csproj"
}

# 2. Update CharacterSpawn.json
$manifestPath = Join-Path $repoRoot "CharacterSpawn.json"
if (Test-Path $manifestPath) {
    $json = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json
    $json.AssemblyVersion = $Version
    $newJson = $json | ConvertTo-Json -Depth 10
    Set-Content -Path $manifestPath -Value $newJson
    Write-Host "  Updated CharacterSpawn.json"
}

# 3. Update repo.json (CRITICAL: Dalamud Plugin Installer compares this with latest.zip!)
$repoJsonPath = Join-Path $repoRoot "repo.json"
if (Test-Path $repoJsonPath) {
    $repoList = Get-Content -Raw -Path $repoJsonPath | ConvertFrom-Json
    foreach ($entry in $repoList) {
        if ($entry.InternalName -eq "CharacterSpawn") {
            $entry.AssemblyVersion = $Version
        }
    }
    $newRepoJson = $repoList | ConvertTo-Json -Depth 10
    Set-Content -Path $repoJsonPath -Value $newRepoJson
    Write-Host "  Updated repo.json"
}

Write-Host "Version bump to $Version completed successfully across all manifests."
