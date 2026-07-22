[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$backend = Join-Path $root 'Backend'
$centralPath = Join-Path $backend 'Directory.Packages.props'
$frontendPackagePath = Join-Path $root 'Frontend/package.json'
$frontendLockPath = Join-Path $root 'Frontend/pnpm-lock.yaml'

[xml]$central = Get-Content -LiteralPath $centralPath -Raw
if ($central.Project.PropertyGroup.ManagePackageVersionsCentrally -ne 'true') {
    throw 'Directory.Packages.props must enable ManagePackageVersionsCentrally.'
}

$centralVersions = @{}
foreach ($entry in @($central.Project.ItemGroup.PackageVersion)) {
    $name = [string]$entry.Include
    $version = [string]$entry.Version
    if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($version)) {
        throw 'Every central PackageVersion requires Include and Version.'
    }
    if ($version.Contains('*') -or $version.Contains('$(')) {
        throw "Package '$name' must use an exact central version, found '$version'."
    }
    if ($centralVersions.ContainsKey($name)) {
        throw "Package '$name' is declared more than once in Directory.Packages.props."
    }
    $centralVersions[$name] = $version
}

$references = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($project in Get-ChildItem -LiteralPath $backend -Recurse -Filter '*.csproj') {
    [xml]$xml = Get-Content -LiteralPath $project.FullName -Raw
    foreach ($reference in @($xml.Project.ItemGroup.PackageReference)) {
        $name = [string]$reference.Include
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        if ($reference.HasAttribute('Version') -or $null -ne $reference.Version) {
            throw "$($project.FullName) pins '$name' outside Directory.Packages.props."
        }
        if (-not $centralVersions.ContainsKey($name)) {
            throw "$($project.FullName) references '$name' without a central version."
        }
        [void]$references.Add($name)
    }
}

$unused = @($centralVersions.Keys | Where-Object { -not $references.Contains($_) })
if ($unused.Count -gt 0) {
    throw "Unused central package versions: $($unused -join ', ')."
}

$package = Get-Content -LiteralPath $frontendPackagePath -Raw -Encoding utf8 | ConvertFrom-Json
if ($package.packageManager -ne 'pnpm@9.0.0' -or $package.engines.pnpm -ne '9.0.0') {
    throw 'Frontend packageManager and engines.pnpm must both pin pnpm 9.0.0.'
}
if (-not (Test-Path -LiteralPath $frontendLockPath)) {
    throw 'Frontend/pnpm-lock.yaml is required for frozen installs.'
}
$lockHeader = (Get-Content -LiteralPath $frontendLockPath -TotalCount 8 -Encoding utf8) -join "`n"
if ($lockHeader -notmatch "lockfileVersion: '9\.0'") {
    throw 'Frontend/pnpm-lock.yaml must use lockfileVersion 9.0.'
}

Write-Host "Dependency manifests passed: $($references.Count) centrally-versioned NuGet packages and pnpm 9.0 frozen lock."
