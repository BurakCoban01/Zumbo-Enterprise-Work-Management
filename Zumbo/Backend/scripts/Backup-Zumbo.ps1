[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Mongo', 'PostgreSql', 'LocalStorage', 'Minio')]
    [string]$Provider,
    [Parameter(Mandatory)]
    [string]$OutputDirectory,
    [string]$Database,
    [string]$SourcePath,
    [string]$Bucket,
    [string]$ConnectionEnvironment = 'ZUMBO_BACKUP_CONNECTION_STRING',
    [string]$MinioEndpointEnvironment = 'ZUMBO_BACKUP_MINIO_ENDPOINT',
    [string]$MinioAccessKeyEnvironment = 'ZUMBO_BACKUP_MINIO_ACCESS_KEY',
    [string]$MinioSecretKeyEnvironment = 'ZUMBO_BACKUP_MINIO_SECRET_KEY'
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $root) -and @(Get-ChildItem -LiteralPath $root -Force).Count -gt 0) {
    throw 'Backup output directory must be new or empty.'
}
[System.IO.Directory]::CreateDirectory($root) | Out-Null
$payload = Join-Path $root 'payload'
[System.IO.Directory]::CreateDirectory($payload) | Out-Null
$started = [DateTimeOffset]::UtcNow

function Require-Environment([string]$Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) { throw "Required environment variable '$Name' is not set." }
    return $value
}

switch ($Provider) {
    'Mongo' {
        if ([string]::IsNullOrWhiteSpace($Database)) { throw 'Mongo backup requires -Database.' }
        $connection = Require-Environment $ConnectionEnvironment
        & mongodump "--uri=$connection" "--db=$Database" "--archive=$(Join-Path $payload 'mongo.archive.gz')" --gzip
        if ($LASTEXITCODE -ne 0) { throw "mongodump failed with exit code $LASTEXITCODE." }
    }
    'PostgreSql' {
        $connection = Require-Environment $ConnectionEnvironment
        & pg_dump "--dbname=$connection" --format=custom --no-owner --no-privileges "--file=$(Join-Path $payload 'postgresql.dump')"
        if ($LASTEXITCODE -ne 0) { throw "pg_dump failed with exit code $LASTEXITCODE." }
    }
    'LocalStorage' {
        if ([string]::IsNullOrWhiteSpace($SourcePath)) { throw 'LocalStorage backup requires -SourcePath.' }
        $source = [System.IO.Path]::GetFullPath($SourcePath)
        if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw 'Local storage source directory does not exist.' }
        & tar -czf (Join-Path $payload 'local-storage.tar.gz') -C $source .
        if ($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE." }
    }
    'Minio' {
        if ([string]::IsNullOrWhiteSpace($Bucket)) { throw 'Minio backup requires -Bucket.' }
        $endpoint = Require-Environment $MinioEndpointEnvironment
        $accessKey = Require-Environment $MinioAccessKeyEnvironment
        $secretKey = Require-Environment $MinioSecretKeyEnvironment
        $alias = 'zumbo-backup-' + [Guid]::NewGuid().ToString('N')
        try {
            & mc alias set $alias $endpoint $accessKey $secretKey | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'mc alias set failed.' }
            & mc mirror --overwrite "$alias/$Bucket" (Join-Path $payload 'objects')
            if ($LASTEXITCODE -ne 0) { throw 'mc mirror failed.' }
        }
        finally { & mc alias remove $alias 2>$null | Out-Null }
    }
}

$files = @(Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relativePath = ($_.FullName.Substring($root.TrimEnd([System.IO.Path]::DirectorySeparatorChar).Length)).TrimStart([System.IO.Path]::DirectorySeparatorChar).Replace([System.IO.Path]::DirectorySeparatorChar, '/')
    [ordered]@{
        path = $relativePath
        sizeBytes = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
$manifest = [ordered]@{
    schemaVersion = 1
    provider = $Provider
    database = $Database
    bucket = $Bucket
    startedAtUtc = $started
    completedAtUtc = [DateTimeOffset]::UtcNow
    files = $files
}
$manifestJson = $manifest | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText((Join-Path $root 'manifest.json'), $manifestJson, [System.Text.UTF8Encoding]::new($false))
$manifestJson
