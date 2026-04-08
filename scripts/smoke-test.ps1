#Requires -Version 5.1
<#
  Smoke test against a running Functions host (e.g. func start).
  Usage:
    $env:SMOKE_BASE_URL = "http://localhost:7071"   # optional, default shown
    $env:SMOKE_API_KEY  = "<same as RENDER_API_KEY>" # required for auth checks
    .\scripts\smoke-test.ps1
#>
$ErrorActionPreference = "Stop"

$baseUrl = if ($env:SMOKE_BASE_URL) { $env:SMOKE_BASE_URL.TrimEnd("/") } else { "http://localhost:7071" }
$apiKey = $env:SMOKE_API_KEY
if (-not $apiKey) {
    Write-Error "Set SMOKE_API_KEY to match RENDER_API_KEY (e.g. from local.settings.json)."
}

$validateBody = '{"schema_version":"1.0","workbook":{"worksheets":[{"name":"Sheet1","blocks":[{"type":"table","start_cell":"A1","columns":[{"key":"a","header":"A","type":"string"}],"rows":[{"a":"ok"}]}]}]}}'

Write-Host "GET $baseUrl/api/health"
$health = Invoke-RestMethod -Uri "$baseUrl/api/health" -Method Get
if ($health.status -ne "ok") { throw "Expected health.status ok, got $($health | ConvertTo-Json -Compress)" }
Write-Host "  auth_configured=$($health.auth_configured)"

Write-Host "POST /api/validate without X-Api-Key (expect 403)"
try {
    Invoke-WebRequest -Uri "$baseUrl/api/validate" -Method Post -ContentType "application/json" -Body $validateBody -UseBasicParsing -ErrorAction Stop | Out-Null
    throw "Expected 403 Forbidden without API key"
}
catch {
    $resp = $_.Exception.Response
    if ($null -eq $resp -or [int]$resp.StatusCode -ne 403) { throw }
}

Write-Host "POST /api/validate with X-Api-Key (expect 200)"
$ok = Invoke-RestMethod -Uri "$baseUrl/api/validate" -Method Post -ContentType "application/json" `
    -Headers @{ "X-Api-Key" = $apiKey } -Body $validateBody
if (-not $ok.valid) { throw "Expected valid contract, got $($ok | ConvertTo-Json -Compress)" }

Write-Host "Smoke test passed."
