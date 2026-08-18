[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$backend = Join-Path $root 'Backend'
$centralPath = Join-Path $backend 'Directory.Packages.props'
$frontendRoot = Join-Path $root 'Frontend'
$frontendPackagePath = Join-Path $frontendRoot 'package.json'
$frontendLockPath = Join-Path $frontendRoot 'pnpm-lock.yaml'
$frontendWorkspacePath = Join-Path $frontendRoot 'pnpm-workspace.yaml'
$frontendNpmrcPath = Join-Path $frontendRoot '.npmrc'
$duplicateFrontendPaths = @(
    (Join-Path $root 'pnpm-lock.yaml'),
    (Join-Path $root 'pnpm-workspace.yaml')
)

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
if ($package.engines.node -ne '>=20.9.0 <21 || >=22.22.3 <23 || >=24.15.0 <25') {
    throw 'Frontend engines.node must retain the audited Node 20, 22 and 24 LTS ranges.'
}
foreach ($duplicatePath in $duplicateFrontendPaths) {
    if (Test-Path -LiteralPath $duplicatePath) {
        throw "Duplicate frontend workspace authority is not allowed: $duplicatePath"
    }
}
if (-not (Test-Path -LiteralPath $frontendLockPath)) {
    throw 'Frontend/pnpm-lock.yaml is required for frozen installs.'
}
if (-not (Test-Path -LiteralPath $frontendWorkspacePath)) {
    throw 'Frontend/pnpm-workspace.yaml is required for the authoritative workspace.'
}
if (-not (Test-Path -LiteralPath $frontendNpmrcPath)) {
    throw 'Frontend/.npmrc is required for strict install policy.'
}
$lockHeader = (Get-Content -LiteralPath $frontendLockPath -TotalCount 8 -Encoding utf8) -join "`n"
if ($lockHeader -notmatch "lockfileVersion: '9\.0'") {
    throw 'Frontend/pnpm-lock.yaml must use lockfileVersion 9.0.'
}
$workspace = (Get-Content -LiteralPath $frontendWorkspacePath -Raw -Encoding utf8) -replace "`r`n", "`n"
if ($workspace.Trim() -ne "packages:`n  - .") {
    throw 'Frontend/pnpm-workspace.yaml must contain only the Frontend package root.'
}
$requiredNpmrc = @(
    'engine-strict=true',
    'ignore-scripts=true',
    'save-exact=true',
    'shared-workspace-lockfile=true',
    'strict-peer-dependencies=true'
)
$npmrc = @(Get-Content -LiteralPath $frontendNpmrcPath -Encoding utf8 | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
foreach ($entry in $requiredNpmrc) {
    if ($npmrc -notcontains $entry) {
        throw "Frontend/.npmrc is missing required policy '$entry'."
    }
}

Write-Host "Dependency manifests passed: $($references.Count) centrally-versioned NuGet packages and one strict Frontend pnpm 9.0 workspace."
