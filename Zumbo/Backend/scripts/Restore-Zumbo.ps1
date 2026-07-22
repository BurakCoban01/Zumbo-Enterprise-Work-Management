[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Mongo', 'PostgreSql', 'LocalStorage', 'Minio')]
    [string]$Provider,
    [Parameter(Mandatory)]
    [string]$BackupDirectory,
    [switch]$ConfirmIsolatedTarget,
    [string]$SourceDatabase,
    [string]$TargetDatabase,
    [string]$TargetPath,
    [string]$TargetBucket,
    [string]$ConnectionEnvironment = 'ZUMBO_RESTORE_CONNECTION_STRING',
    [string]$MinioEndpointEnvironment = 'ZUMBO_RESTORE_MINIO_ENDPOINT',
    [string]$MinioAccessKeyEnvironment = 'ZUMBO_RESTORE_MINIO_ACCESS_KEY',
    [string]$MinioSecretKeyEnvironment = 'ZUMBO_RESTORE_MINIO_SECRET_KEY'
)

$ErrorActionPreference = 'Stop'
if (-not $ConfirmIsolatedTarget) { throw 'Restore requires explicit -ConfirmIsolatedTarget.' }
$root = [System.IO.Path]::GetFullPath($BackupDirectory)
$manifestPath = Join-Path $root 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Backup manifest is missing.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.provider -ne $Provider) { throw 'Backup manifest provider/schema does not match the restore request.' }

foreach ($file in $manifest.files) {
    $path = [System.IO.Path]::GetFullPath((Join-Path $root ([string]$file.path)))
    $rootPrefix = $root.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $path.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Backup manifest path escapes the backup directory.'
    }
    $relative = $path.Substring($rootPrefix.Length)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Backup file '$relative' is missing." }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne [string]$file.sha256) { throw "Backup file '$relative' checksum mismatch." }
}

function Require-Environment([string]$Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) { throw "Required environment variable '$Name' is not set." }
    return $value
}

switch ($Provider) {
    'Mongo' {
        if ([string]::IsNullOrWhiteSpace($SourceDatabase) -or [string]::IsNullOrWhiteSpace($TargetDatabase)) { throw 'Mongo restore requires source and target database names.' }
        if ($SourceDatabase -eq $TargetDatabase) { throw 'Mongo restore target must differ from the source database.' }
        if ($SourceDatabase -notmatch '^[A-Za-z0-9_-]+$' -or $TargetDatabase -notmatch '^[A-Za-z0-9_-]+$') { throw 'Mongo database names contain unsupported characters.' }
        $connection = Require-Environment $ConnectionEnvironment
        $targetCount = & mongosh $connection --quiet --eval "db.getSiblingDB('$TargetDatabase').getCollectionNames().map(n => db.getSiblingDB('$TargetDatabase').getCollection(n).countDocuments({})).reduce((a,b) => a+b, 0)"
        if ($LASTEXITCODE -ne 0) { throw 'Mongo target preflight failed.' }
        if ([long]$targetCount -ne 0) { throw 'Mongo restore target must be absent or empty.' }
        & mongorestore "--uri=$connection" "--archive=$(Join-Path $root 'payload/mongo.archive.gz')" --gzip "--nsInclude=$SourceDatabase.*" "--nsFrom=$SourceDatabase.*" "--nsTo=$TargetDatabase.*" --stopOnError
        if ($LASTEXITCODE -ne 0) { throw "mongorestore failed with exit code $LASTEXITCODE." }
    }
    'PostgreSql' {
        $connection = Require-Environment $ConnectionEnvironment
        $tableCount = & psql $connection -Atc "SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema');"
        if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL target preflight failed.' }
        if ([long]$tableCount -ne 0) { throw 'PostgreSQL restore target must not contain user tables.' }
        & pg_restore "--dbname=$connection" --no-owner --no-privileges --exit-on-error (Join-Path $root 'payload/postgresql.dump')
        if ($LASTEXITCODE -ne 0) { throw "pg_restore failed with exit code $LASTEXITCODE." }
    }
    'LocalStorage' {
        if ([string]::IsNullOrWhiteSpace($TargetPath)) { throw 'LocalStorage restore requires -TargetPath.' }
        $target = [System.IO.Path]::GetFullPath($TargetPath)
        if ((Test-Path -LiteralPath $target) -and @(Get-ChildItem -LiteralPath $target -Force).Count -gt 0) { throw 'Local storage restore target must be new or empty.' }
        [System.IO.Directory]::CreateDirectory($target) | Out-Null
        & tar -xzf (Join-Path $root 'payload/local-storage.tar.gz') -C $target
        if ($LASTEXITCODE -ne 0) { throw "tar restore failed with exit code $LASTEXITCODE." }
    }
    'Minio' {
        if ([string]::IsNullOrWhiteSpace($TargetBucket)) { throw 'Minio restore requires -TargetBucket.' }
        $endpoint = Require-Environment $MinioEndpointEnvironment
        $accessKey = Require-Environment $MinioAccessKeyEnvironment
        $secretKey = Require-Environment $MinioSecretKeyEnvironment
        $alias = 'zumbo-restore-' + [Guid]::NewGuid().ToString('N')
        try {
            & mc alias set $alias $endpoint $accessKey $secretKey | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'mc alias set failed.' }
            $existing = @(& mc find "$alias/$TargetBucket" --maxdepth 1 2>$null)
            if ($existing.Count -gt 0) { throw 'Minio restore bucket must be absent or empty.' }
            & mc mb --ignore-existing "$alias/$TargetBucket" | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Minio target bucket creation failed.' }
            & mc mirror --overwrite (Join-Path $root 'payload/objects') "$alias/$TargetBucket"
            if ($LASTEXITCODE -ne 0) { throw 'Minio restore mirror failed.' }
        }
        finally { & mc alias remove $alias 2>$null | Out-Null }
    }
}

[ordered]@{ result = 'restored'; provider = $Provider; completedAtUtc = [DateTimeOffset]::UtcNow } | ConvertTo-Json
