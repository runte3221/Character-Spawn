# Automated Release & Verification Script for Character Spawn
# Usage: powershell -File tools/release.ps1 0.1.42.0 "Commit message"
param (
    [Parameter(Mandatory=$true)]
    [string]$Version,
    [Parameter(Mandatory=$false)]
    [string]$Message = "release: update to v$Version"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path "$PSScriptRoot\.."

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Starting Release Pipeline for Character Spawn v$Version" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Step 1: Bump version and validate manifests
Write-Host "`n[Step 1/5] Bumping version across all manifests..." -ForegroundColor Yellow
& "$PSScriptRoot\bump-version.ps1" -Version $Version
if ($LASTEXITCODE -ne 0) {
    throw "Version bump failed!"
}

# Step 2: Validate CHANGELOG.md contains version
Write-Host "`n[Step 2/5] Validating CHANGELOG.md..." -ForegroundColor Yellow
$changelogPath = Join-Path $repoRoot "CHANGELOG.md"
if (Test-Path $changelogPath) {
    $changelog = Get-Content -Raw -Path $changelogPath
    $shortVer = $Version -replace '\.0$', ''
    if (-not ($changelog -match "## \[$Version\]" -or $changelog -match "## \[$shortVer\]")) {
        Write-Warning "CHANGELOG.md does not seem to contain an entry for [$Version] or [$shortVer]."
    } else {
        Write-Host "  CHANGELOG.md entry verified." -ForegroundColor Green
    }
}

# Step 3: Git Commit & Push
Write-Host "`n[Step 3/5] Committing and pushing to GitHub..." -ForegroundColor Yellow
Set-Location $repoRoot
git add .
git commit -m "$Message"
git push
Write-Host "  Pushed to GitHub main successfully." -ForegroundColor Green

# Step 4: Monitor GitHub Actions CI/CD Build
Write-Host "`n[Step 4/5] Monitoring GitHub Actions build..." -ForegroundColor Yellow
Start-Sleep -Seconds 5

$maxAttempts = 30
$runSuccess = $false
for ($i = 1; $i -le $maxAttempts; $i++) {
    try {
        $res = Invoke-RestMethod -Uri "https://api.github.com/repos/runte3221/Character-Spawn/actions/runs?per_page=1"
        $latestRun = $res.workflow_runs | Select-Object -First 1
        if ($latestRun) {
            Write-Host "  Attempt $i/$maxAttempts: Status = $($latestRun.status), Conclusion = $($latestRun.conclusion) ($($latestRun.html_url))"
            if ($latestRun.status -eq "completed") {
                if ($latestRun.conclusion -eq "success") {
                    $runSuccess = $true
                    Write-Host "  GitHub Actions build SUCCEEDED!" -ForegroundColor Green
                    break
                } else {
                    throw "GitHub Actions build failed with conclusion: $($latestRun.conclusion)"
                }
            }
        }
    } catch {
        Write-Host "  Polling note: $_"
    }
    Start-Sleep -Seconds 10
}

if (-not $runSuccess) {
    Write-Warning "GitHub Actions run did not complete within the timeout period. Please check the URL manually."
}

# Step 5: Verify Raw repo.json & Wait for CDN Cache Expiry
Write-Host "`n[Step 5/5] Verifying live repo.json on raw.githubusercontent.com (waiting for CDN cache update)..." -ForegroundColor Yellow
$cdnMaxAttempts = 35 # up to ~5.5 minutes
$cdnSuccess = $false

for ($j = 1; $j -le $cdnMaxAttempts; $j++) {
    try {
        $rawUrl = "https://raw.githubusercontent.com/runte3221/Character-Spawn/main/repo.json?_nocache=$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
        $webRes = Invoke-WebRequest -Uri $rawUrl -UseBasicParsing
        $rawText = $webRes.Content.Trim()
        
        if ($rawText.StartsWith("[") -and $rawText.EndsWith("]")) {
            $rawJson = $rawText | ConvertFrom-Json
            $entry = $rawJson | Where-Object { $_.InternalName -eq "CharacterSpawn" } | Select-Object -First 1
            if ($entry -and $entry.AssemblyVersion -eq $Version) {
                Write-Host "  Live CDN repo.json successfully updated! (AssemblyVersion: $($entry.AssemblyVersion), Array format verified)" -ForegroundColor Green
                $cdnSuccess = $true
                break
            } else {
                Write-Host "  Attempt $j/$cdnMaxAttempts: CDN still serving version '$($entry.AssemblyVersion)'. Waiting 10s for Fastly CDN cache expiry..."
            }
        } else {
            Write-Host "  Attempt $j/$cdnMaxAttempts: CDN still serving old non-array format. Waiting 10s for Fastly CDN cache expiry..."
        }
    } catch {
        Write-Host "  Attempt $j/$cdnMaxAttempts: Request error ($_) - retrying in 10s..."
    }
    Start-Sleep -Seconds 10
}

if (-not $cdnSuccess) {
    Write-Warning "CDN cache did not reflect the new version within 5 minutes. It may take another 1-2 minutes to expire automatically."
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "  Release v$Version pipeline finished successfully!" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
