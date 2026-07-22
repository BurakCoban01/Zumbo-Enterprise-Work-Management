[CmdletBinding()]
param(
    [switch]$StopDocker,
    [switch]$RemoveVolumes,
    [string]$ProjectName,
    [string]$ConfirmDisposableProject
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$generatedNames = @('bin', 'obj', 'TestResults', 'node_modules', 'dist', 'coverage', '.playwright')

if ($StopDocker -or $RemoveVolumes) {
    if ([string]::IsNullOrWhiteSpace($ProjectName) -or $ProjectName -notmatch '^zumbo-[a-z0-9-]+$') {
        throw 'Docker cleanup requires an explicit -ProjectName matching zumbo-<name>.'
    }
    if ($RemoveVolumes -and $ConfirmDisposableProject -cne $ProjectName) {
        throw 'Volume removal requires -ConfirmDisposableProject to exactly match -ProjectName.'
    }
}

$targets = Get-ChildItem -LiteralPath $root -Directory -Recurse -Force |
    Where-Object { $generatedNames -contains $_.Name } |
    Sort-Object { $_.FullName.Length } -Descending

foreach ($target in $targets) {
    $resolved = (Resolve-Path -LiteralPath $target.FullName).Path
    if (-not $resolved.StartsWith(
        $root + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Temizleme hedefi çalışma kökünün dışında: $resolved"
    }

    Remove-Item -LiteralPath $resolved -Recurse -Force
    Write-Host "Silindi: $resolved"
}

if ($StopDocker -or $RemoveVolumes) {
    $compose = Join-Path $root 'Backend\docker-compose.yml'
    $environment = Join-Path $root 'Backend\.env'
    $arguments = @('compose', '--project-name', $ProjectName)
    if (Test-Path -LiteralPath $environment) {
        $arguments += @('--env-file', $environment)
    }
    $arguments += @('-f', $compose, 'down', '--remove-orphans')
    if ($RemoveVolumes) {
        $arguments += '--volumes'
    }

    & docker @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Compose temizliği başarısız oldu: $LASTEXITCODE"
    }
}
