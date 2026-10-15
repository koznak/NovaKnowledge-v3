# Scrub secrets and personal data from the entire git history (local, safe, non-destructive to the
# remote). Produces a rewritten clone you then force-push yourself.
#
# This automates steps 2-4 of docs/secrets-rotation.md:
#   1. clones the repo to a throwaway folder (never touches your working tree)
#   2. removes the personal brief files from all history (--invert-paths)
#   3. runs `git filter-repo --replace-text secrets.txt`
#   4. verifies that no secret/PII literal survives in ANY commit
#
# It does NOT push. Force-pushing is a destructive, irreversible remote operation you must run
# yourself after rotating the exposed keys in Azure.
#
# Usage:
#   .\scripts\scrub-history.ps1                 # uses secrets.txt + origin URL from ./git
#   .\scripts\scrub-history.ps1 -Source <path>  # scrub a different local repo
#   .\scripts\scrub-history.ps1 -OutDir <path>  # choose the output location

param(
    [string]$Source,
    [string]$OutDir,
    [string]$MapFile,
    [string[]]$RemovePath = @(
        "docs/redacted-company-interview-brief.md",
        "docs/redacted-company-interview-brief.pdf"
    )
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Source) { $Source = $repoRoot }
if (-not $MapFile) { $MapFile = Join-Path $Source "secrets.txt" }
if (-not $OutDir) { $OutDir = Join-Path $env:TEMP ("nk-scrub-" + (Get-Date -Format "yyyyMMdd-HHmmss")) }

Write-Host "NovaKnowledge history scrub" -ForegroundColor Cyan
Write-Host "  source : $Source"
Write-Host "  map    : $MapFile"
Write-Host "  output : $OutDir"
Write-Host ""

# --- Preflight -----------------------------------------------------------------------------------
if (-not (Test-Path (Join-Path $Source ".git"))) {
    throw "'$Source' is not a git repository."
}
if (-not (Test-Path $MapFile)) {
    throw @"
Replacement map '$MapFile' not found.

Create it first (it is git-ignored). One rule per line, e.g.:
  literal:<actual-chat-key>==>REDACTED_AZURE_OPENAI_CHAT_KEY
  literal:<actual-email>==>REDACTED_EMAIL

See docs/secrets-rotation.md section 2.
"@
}

# Extract the literals we will assert are gone after the rewrite.
$rules = Get-Content $MapFile | Where-Object { $_ -match "^\s*literal:" }
$literals = @()
foreach ($rule in $rules) {
    $match = [regex]::Match($rule, "^\s*literal:(.*?)==>")
    if ($match.Success) { $literals += $match.Groups[1].Value }
}
if ($literals.Count -eq 0) { throw "No 'literal:' rules found in $MapFile." }
Write-Host "Loaded $($literals.Count) literal(s) to scrub." -ForegroundColor DarkGray

if (-not (Get-Command git-filter-repo -ErrorAction SilentlyContinue)) {
    Write-Host "git-filter-repo not found. Installing via pip..."
    pip install git-filter-repo | Out-Null
    if (-not (Get-Command git-filter-repo -ErrorAction SilentlyContinue)) {
        throw "git-filter-repo could not be installed. Run: pip install git-filter-repo"
    }
}

# --- Clone (throwaway; your working tree is never modified) --------------------------------------
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
Write-Host "`nCloning to throwaway copy..." -ForegroundColor Cyan
git clone --no-local $Source $OutDir | Out-Null
if ($LASTEXITCODE -ne 0) { throw "git clone failed." }

Push-Location $OutDir
try {
    $before = git rev-list --count --all

    Copy-Item $MapFile (Join-Path $OutDir "secrets.txt") -Force

    # 1. Purge whole files from every commit. Binary artifacts (e.g. the personal PDF) cannot be
    #    scrubbed with --replace-text, so they must be removed entirely.
    if ($RemovePath.Count -gt 0) {
        Write-Host "Removing files from history: $($RemovePath -join ', ')" -ForegroundColor Cyan
        $pathArgs = @()
        foreach ($p in $RemovePath) { $pathArgs += @("--path", $p) }
        git filter-repo @pathArgs --invert-paths --force | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "git filter-repo (path removal) failed." }
    }

    # 2. Replace secret/PII literals in remaining text files across all commits.
    Write-Host "Rewriting history with git-filter-repo..." -ForegroundColor Cyan
    git filter-repo --replace-text secrets.txt --force | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "git filter-repo failed." }

    # --- Verify ----------------------------------------------------------------------------------
    Write-Host "`nVerifying no scrubbed literal survives in any commit..." -ForegroundColor Cyan
    $revs = git rev-list --all
    $failures = @()
    foreach ($literal in $literals) {
        $hits = git grep -l --fixed-strings $literal $revs 2>$null
        if ($hits) { $failures += $literal }
    }

    # File-name check for the removed personal brief.
    $nameLeaks = git log --all --name-only --pretty=format: | Select-String -Pattern "interview-brief"
    if ($nameLeaks) { $failures += "<interview-brief file path>" }

    $after = git rev-list --count --all
    Write-Host "`nResult:" -ForegroundColor Cyan
    Write-Host "  commits before : $before"
    Write-Host "  commits after  : $after"

    if ($failures.Count -gt 0) {
        Write-Host "`nFAILED: $($failures.Count) literal(s) still present:" -ForegroundColor Red
        $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
        throw "Scrub verification failed. Do NOT push this output."
    }

    Write-Host "`nPASS: all $($literals.Count) literal(s) removed from every commit." -ForegroundColor Green
    Write-Host @"

Next steps (run these yourself):
  1. Rotate the exposed Azure keys (docs/secrets-rotation.md section 1).
  2. Push the cleaned history:
       cd "$OutDir"
       git remote add origin <your-repo-url>
       git push --force --all origin
       git push --force --tags origin
  3. Flip visibility:
       gh repo edit koznak/NovaKnowledge --visibility public --accept-visibility-change-consequences
"@ -ForegroundColor Yellow
}
finally {
    Pop-Location
}
