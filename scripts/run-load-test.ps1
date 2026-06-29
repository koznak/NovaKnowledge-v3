# Run NovaKnowledge k6 load tests (installs k6 locally if needed).
# Requires Gateway running on http://localhost:5000 (and Search.Api behind it).

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$k6Dir = Join-Path $repoRoot "tools\k6"
$testScript = Join-Path $repoRoot "tests\NovaKnowledge.LoadTests\load-test.js"

function Get-K6Path {
    $cmd = Get-Command k6 -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $local = Get-ChildItem -Path $k6Dir -Recurse -Filter k6.exe -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($local) { return $local.FullName }

    Write-Host "k6 not found. Downloading latest release to tools/k6 ..."
    New-Item -ItemType Directory -Force -Path $k6Dir | Out-Null

    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/grafana/k6/releases/latest"
    $asset = $release.assets | Where-Object { $_.name -match "windows-amd64\.zip$" } | Select-Object -First 1
    if (-not $asset) { throw "Could not find a Windows k6 release asset." }

    $zip = Join-Path $env:TEMP $asset.name
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip
    Expand-Archive -Path $zip -DestinationPath $k6Dir -Force

    $downloaded = Get-ChildItem -Path $k6Dir -Recurse -Filter k6.exe | Select-Object -First 1
    if (-not $downloaded) { throw "k6 download failed." }
    return $downloaded.FullName
}

$k6 = Get-K6Path
$baseUrl = if ($env:GATEWAY_URL) { $env:GATEWAY_URL } else { "http://localhost:5000" }
Write-Host "Using k6: $k6"
Write-Host "Target: $baseUrl"

Write-Host "Preflight checks..."
try {
    $health = Invoke-WebRequest -Uri "$baseUrl/health" -UseBasicParsing -TimeoutSec 5
    if ($health.StatusCode -ne 200) { throw "Gateway health returned $($health.StatusCode)" }
    Write-Host "  [ok] GET /health -> 200"
} catch {
    throw "Gateway not reachable at $baseUrl/health. Start NovaKnowledge.Gateway first.`n$($_.Exception.Message)"
}

$searchBody = '{"query":"PTO policy","tenantId":"contoso","topK":5,"useHybrid":true}'
try {
    $search = Invoke-WebRequest -Uri "$baseUrl/api/search/query" -Method POST -Body $searchBody -ContentType "application/json" -UseBasicParsing -TimeoutSec 15
    if ($search.StatusCode -ne 200) { throw "Search returned $($search.StatusCode)" }
    Write-Host "  [ok] POST /api/search/query -> 200"
} catch {
    $status = $_.Exception.Response.StatusCode.value__
    if ($status -eq 500 -and $_.ErrorDetails.Message -match "empty string.*key") {
        throw @"
Search.Api returned 500: AzureSearch:ApiKey is empty.
Add your Azure AI Search key in src/Services/Search/NovaKnowledge.Search.Api/appsettings.json, restart Search.Api, then re-run this script.
"@
    }
    throw "Search preflight failed (HTTP $status). Ensure Search.Api is running on :5003 and Azure Search is configured.`n$($_.Exception.Message)"
}

& $k6 run $testScript @args
