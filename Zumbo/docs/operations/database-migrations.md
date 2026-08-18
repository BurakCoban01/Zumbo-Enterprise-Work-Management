# Database Migrations and Transfer

Database changes must be reviewed with the application version that consumes them. Back up the source, inspect status and test rollback against an isolated target before a real change window.

## PostgreSQL migrator

Run from `Zumbo`:

```powershell
dotnet run --project Backend/tools/Zumbo.DatabaseMigrator/Zumbo.DatabaseMigrator.csproj -- status
dotnet run --project Backend/tools/Zumbo.DatabaseMigrator/Zumbo.DatabaseMigrator.csproj -- script --idempotent --output artifacts/migrations/postgresql.sql
dotnet run --project Backend/tools/Zumbo.DatabaseMigrator/Zumbo.DatabaseMigrator.csproj -- apply
```

Rollback requires an explicit target:

```powershell
dotnet run --project Backend/tools/Zumbo.DatabaseMigrator/Zumbo.DatabaseMigrator.csproj -- rollback --target-version <version>
```

Supply the connection through the supported environment/configuration path. Do not place credentials in committed command examples.

## Provider transfer

`Zumbo.DataTransfer` supports `export`, `import` and `verify` for MongoDB and PostgreSQL bundles. Connection values are read from a named environment variable.

```powershell
dotnet run --project Backend/tools/Zumbo.DataTransfer/Zumbo.DataTransfer.csproj -- export --provider mongo --connection-env ZUMBO_SOURCE_CONNECTION --database zumbo --bundle <bundle-path>
dotnet run --project Backend/tools/Zumbo.DataTransfer/Zumbo.DataTransfer.csproj -- import --provider postgresql --connection-env ZUMBO_TARGET_CONNECTION --bundle <bundle-path> --dry-run
dotnet run --project Backend/tools/Zumbo.DataTransfer/Zumbo.DataTransfer.csproj -- verify --provider postgresql --connection-env ZUMBO_TARGET_CONNECTION --bundle <bundle-path>
```

Validate counts and application behavior after import. Keep the original provider unchanged until verification succeeds and rollback remains possible.

## MongoDB backfills

Mongo migrations are versioned in the API infrastructure. Apply them through the supported host/migration path and retain migration ledger/checksum integrity. Never edit applied migration definitions in place.
