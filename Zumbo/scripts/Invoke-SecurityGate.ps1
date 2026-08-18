[CmdletBinding()]
param(
    [string]$ApiImage = '',
    [string]$GatewayImage = '',
    [switch]$SkipContainerScan
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$generated = Join-Path $root 'artifacts/security/generated'
$cache = Join-Path $root '.trivy-cache'
$sbom = Join-Path $root 'artifacts/security/SEC-008.sbom.cdx.json'
$trivy = 'aquasec/trivy@sha256:be1190afcb28352bfddc4ddeb71470835d16462af68d310f9f4bca710961a41e'
$semgrep = 'semgrep/semgrep@sha256:207983631beecdbe7fa29196c7f4a7a5f29033933cdb76c687ce4a672e07618d'
$mount = "${root}:/src"

New-Item -ItemType Directory -Force -Path $generated | Out-Null
New-Item -ItemType Directory -Force -Path $cache | Out-Null
$cacheMount = "${cache}:/root/.cache/trivy"

function Invoke-Native {
    param([string]$Command, [string[]]$Arguments)

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Komut başarısız ($LASTEXITCODE): $Command $($Arguments -join ' ')"
    }
}

Write-Host 'NuGet doğrudan/geçişli vulnerability taraması çalışıyor...'
$nugetJson = & dotnet list (Join-Path $root 'Backend/Zumbo.sln') package --vulnerable --include-transitive --format json | Out-String
if ($LASTEXITCODE -ne 0) { throw 'NuGet vulnerability taraması çalıştırılamadı.' }
$nuget = $nugetJson | ConvertFrom-Json
$nugetFindings = @(
    $nuget.projects.frameworks.topLevelPackages.vulnerabilities
    $nuget.projects.frameworks.transitivePackages.vulnerabilities
) | Where-Object { $_ }
if ($nugetFindings.Count -gt 0) { throw "NuGet vulnerability bulgusu: $($nugetFindings.Count)" }

Write-Host 'Frontend süreli dependency politikası çalışıyor...'
Invoke-Native 'pnpm' @('--dir', (Join-Path $root 'Frontend'), 'run', 'audit:dependencies')

Write-Host 'Depo içi Semgrep SAST kuralları çalışıyor...'
Invoke-Native 'docker' @(
    'run', '--rm', '-v', $mount, '-w', '/src', $semgrep,
    'semgrep', 'scan', '--config', '.semgrep.yml', '--error', '--metrics', 'off',
    '--exclude', 'Backend/tests', '--exclude', 'Frontend/tests', '--exclude', 'Frontend/vendor',
    '--exclude', '**/bin', '--exclude', '**/obj', '--json',
    '--output', '/src/artifacts/security/generated/semgrep.json',
    'Backend/src', 'Frontend/projects', 'Frontend/shared'
)

Write-Host 'Trivy secret ve yapılandırma taraması çalışıyor...'
Invoke-Native 'docker' @(
    'run', '--rm', '-v', $mount, '-v', $cacheMount, $trivy,
    'fs', '--scanners', 'secret,misconfig', '--severity', 'HIGH,CRITICAL', '--exit-code', '1',
    '--format', 'json', '--output', '/src/artifacts/security/generated/trivy-source.json',
    '--skip-dirs', '/src/docs', '--skip-dirs', '/src/artifacts', '--skip-dirs', '/src/Backend/tests',
    '--skip-dirs', '/src/Frontend/tests', '--skip-dirs', '/src/Frontend/node_modules',
    '--skip-dirs', '**/bin', '--skip-dirs', '**/obj',
    '--skip-files', '/src/Backend/.env.example', '/src'
)

Write-Host 'CycloneDX SBOM üretiliyor...'
Invoke-Native 'node' @((Join-Path $root 'scripts/generate-security-sbom.mjs'))

if (-not $SkipContainerScan) {
    if ([string]::IsNullOrWhiteSpace($ApiImage) -or [string]::IsNullOrWhiteSpace($GatewayImage)) {
        throw 'Container taraması için ApiImage ve GatewayImage birlikte verilmelidir.'
    }

    $imageFindings = 0
    foreach ($item in @(
        @{ Name = 'api'; Image = $ApiImage },
        @{ Name = 'gateway'; Image = $GatewayImage }
    )) {
        Write-Host "$($item.Name) image vulnerability taraması çalışıyor..."
        Invoke-Native 'docker' @(
            'run', '--rm', '-v', '/var/run/docker.sock:/var/run/docker.sock', '-v', $mount,
            '-v', $cacheMount, $trivy,
            'image', '--scanners', 'vuln', '--severity', 'HIGH,CRITICAL', '--ignore-unfixed',
            '--exit-code', '1', '--format', 'json',
            '--output', "/src/artifacts/security/generated/$($item.Name)-image.json", $item.Image
        )
        $imageReport = Get-Content (Join-Path $generated "$($item.Name)-image.json") -Raw -Encoding utf8 | ConvertFrom-Json
        $imageFindings += @($imageReport.Results | ForEach-Object { $_.Vulnerabilities } | Where-Object { $_ }).Count
    }
}
else {
    $imageFindings = $null
}

$bom = Get-Content $sbom -Raw -Encoding utf8 | ConvertFrom-Json
if ($bom.bomFormat -ne 'CycloneDX' -or [int]$bom.specVersion.Split('.')[0] -lt 1 -or @($bom.components).Count -eq 0) {
    throw 'Üretilen CycloneDX SBOM geçersiz veya boş.'
}

$semgrepResult = Get-Content (Join-Path $generated 'semgrep.json') -Raw -Encoding utf8 | ConvertFrom-Json
$trivyResult = Get-Content (Join-Path $generated 'trivy-source.json') -Raw -Encoding utf8 | ConvertFrom-Json
$summary = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    nugetVulnerabilities = $nugetFindings.Count
    semgrepFindings = @($semgrepResult.results).Count
    sourceScanFindings = @($trivyResult.Results | ForEach-Object { $_.Misconfigurations; $_.Secrets } | Where-Object { $_ }).Count
    fixableHighCriticalImageVulnerabilities = $imageFindings
    sbomComponents = @($bom.components).Count
    containerScanExecuted = -not $SkipContainerScan
    trivyImage = $trivy
    semgrepImage = $semgrep
}
$summary | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $generated 'summary.json') -Encoding utf8
Write-Host "G11 güvenlik kapısı geçti: NuGet=0, Semgrep=$($summary.semgrepFindings), source=$($summary.sourceScanFindings), SBOM=$($summary.sbomComponents)."
