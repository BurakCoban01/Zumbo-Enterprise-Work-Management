[CmdletBinding()]
param(
    [switch]$StopDocker,
    [switch]$RemoveVolumes
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$generatedNames = @('bin', 'obj', 'TestResults', 'node_modules', 'dist', 'coverage', '.playwright')

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
    $arguments = @('compose', '-f', $compose, 'down', '--remove-orphans')
    if ($RemoveVolumes) {
        $arguments += '--volumes'
    }

    & docker @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Compose temizliği başarısız oldu: $LASTEXITCODE"
    }
}
