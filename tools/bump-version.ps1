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
# NOTE: repo.json MUST ALWAYS BE AN ARRAY ([ { ... } ]). Do NOT use ConvertTo-Json as PowerShell unrolls single-element arrays into objects!
$repoJsonPath = Join-Path $repoRoot "repo.json"
if (Test-Path $repoJsonPath) {
    $rawRepo = Get-Content -Raw -Path $repoJsonPath
    $updatedRepo = $rawRepo -replace '("AssemblyVersion"\s*:\s*)"[^"]+"', "`$1`"$Version`""
    Set-Content -Path $repoJsonPath -Value $updatedRepo -NoNewline
    Write-Host "  Updated repo.json"
}

# 4. Update package.json
$pkgJsonPath = Join-Path $repoRoot "package.json"
if (Test-Path $pkgJsonPath) {
    $rawPkg = Get-Content -Raw -Path $pkgJsonPath
    $shortVer = $Version -replace '\.0$', ''
    $updatedPkg = $rawPkg -replace '("version"\s*:\s*)"[^"]+"', "`$1`"$shortVer`""
    Set-Content -Path $pkgJsonPath -Value $updatedPkg -NoNewline
    Write-Host "  Updated package.json"
}

# 5. Strict Validation
Write-Host "Validating all manifests..."

# Validate CharacterSpawn.json
$csJson = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json
if ($csJson.AssemblyVersion -ne $Version) {
    throw "Validation failed: CharacterSpawn.json AssemblyVersion is '$($csJson.AssemblyVersion)', expected '$Version'"
}

# Validate repo.json format and version
$rawRepoCheck = (Get-Content -Raw -Path $repoJsonPath).Trim()
if (-not $rawRepoCheck.StartsWith("[") -or -not $rawRepoCheck.EndsWith("]")) {
    throw "Validation failed: repo.json MUST be an array starting with '[' and ending with ']'! Current start: $($rawRepoCheck.Substring(0, 10))"
}
$repoJsonArray = $rawRepoCheck | ConvertFrom-Json
$repoEntry = $repoJsonArray | Where-Object { $_.InternalName -eq "CharacterSpawn" } | Select-Object -First 1
if (-not $repoEntry) {
    throw "Validation failed: CharacterSpawn entry not found in repo.json"
}
if ($repoEntry.AssemblyVersion -ne $Version) {
    throw "Validation failed: repo.json AssemblyVersion is '$($repoEntry.AssemblyVersion)', expected '$Version'"
}

Write-Host "  Manifest consistency validation PASSED!"

Write-Host "Version bump to $Version completed successfully across all manifests."
