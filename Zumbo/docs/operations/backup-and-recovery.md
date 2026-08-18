# Backup and Recovery

Backend scripts support MongoDB, PostgreSQL, local storage and MinIO backup/restore. Required provider tools must be installed on the operator host.

## Backup

```powershell
./Backend/scripts/Backup-Zumbo.ps1 -Provider Mongo -OutputDirectory <new-empty-directory> -Database zumbo
```

Set the connection in `ZUMBO_BACKUP_CONNECTION_STRING` or select another supported environment variable with `-ConnectionEnvironment`. The script writes a SHA-256 manifest and refuses a non-empty output directory.

Use the provider-specific arguments for PostgreSQL, local storage or MinIO.

## Restore

Restore is restricted to an isolated, empty target and requires explicit confirmation:

```powershell
./Backend/scripts/Restore-Zumbo.ps1 -Provider Mongo -BackupDirectory <backup-directory> -ConfirmIsolatedTarget -SourceDatabase zumbo -TargetDatabase zumbo_restore
```

The restore script validates every manifest path and checksum before invoking provider tooling. It rejects a Mongo source/target name collision and non-empty provider targets.

## Recovery verification

1. Restore to an isolated target.
2. Run provider contract and migration status checks.
3. Verify record/object counts and representative application flows.
4. Record measured restore time for the tested dataset.
5. Keep the source and backup immutable until acceptance completes.

A successful small-fixture restore is not a production-volume recovery-time commitment.
