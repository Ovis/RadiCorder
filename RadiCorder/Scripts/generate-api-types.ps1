$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $projectRoot "Scripts/generated"
$outFile = Join-Path $outDir "openapi-types.d.ts"
$openApiPath = Join-Path $projectRoot "openapi/openapi.json"
$baseUrl = if ($env:RADIKEEP_OPENAPI_BASEURL) { $env:RADIKEEP_OPENAPI_BASEURL } else { "https://127.0.0.1:7262" }

& (Join-Path $PSScriptRoot "generate-openapi.ps1") -BaseUrl $baseUrl

if (!(Test-Path $openApiPath)) {
    throw "OpenAPI document not found: $openApiPath"
}

if (!(Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

npx openapi-typescript "$openApiPath" -o "$outFile"
if ($LASTEXITCODE -ne 0) {
    throw "Type generation failed. Install openapi-typescript or allow npx to fetch it."
}

Write-Host "Type definitions generated: $outFile"

