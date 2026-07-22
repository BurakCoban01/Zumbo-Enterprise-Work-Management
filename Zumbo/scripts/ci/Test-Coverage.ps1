[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsDirectory,
    [double]$MinimumLinePercent = 60,
    [double]$MinimumBranchPercent = 40
)

$ErrorActionPreference = 'Stop'
$files = @(Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter 'coverage.cobertura.xml')
if ($files.Count -eq 0) {
    throw "No coverage.cobertura.xml files found under '$ResultsDirectory'."
}

$linesValid = 0L
$linesCovered = 0L
$branchesValid = 0L
$branchesCovered = 0L
foreach ($file in $files) {
    [xml]$coverage = Get-Content -LiteralPath $file.FullName -Raw
    $linesValid += [long]$coverage.coverage.'lines-valid'
    $linesCovered += [long]$coverage.coverage.'lines-covered'
    $branchesValid += [long]$coverage.coverage.'branches-valid'
    $branchesCovered += [long]$coverage.coverage.'branches-covered'
}

if ($linesValid -eq 0 -or $branchesValid -eq 0) {
    throw 'Coverage reports contain no valid line or branch measurements.'
}

$linePercent = [Math]::Round(100 * $linesCovered / $linesValid, 2)
$branchPercent = [Math]::Round(100 * $branchesCovered / $branchesValid, 2)
if ($linePercent -lt $MinimumLinePercent) {
    throw "Line coverage $linePercent% is below the $MinimumLinePercent% threshold."
}
if ($branchPercent -lt $MinimumBranchPercent) {
    throw "Branch coverage $branchPercent% is below the $MinimumBranchPercent% threshold."
}

$summary = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    reportCount = $files.Count
    lines = [ordered]@{ covered = $linesCovered; valid = $linesValid; percent = $linePercent; threshold = $MinimumLinePercent }
    branches = [ordered]@{ covered = $branchesCovered; valid = $branchesValid; percent = $branchPercent; threshold = $MinimumBranchPercent }
}
$summaryPath = Join-Path $ResultsDirectory 'coverage-summary.json'
$summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $summaryPath -Encoding utf8
Write-Host "Coverage passed: lines=$linePercent% branches=$branchPercent% reports=$($files.Count)."
