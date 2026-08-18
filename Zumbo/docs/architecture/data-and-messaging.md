# Data, Messaging and Providers

## Document persistence

MongoDB is the default runtime provider. Module-specific database settings can override the global `MongoDb` section. Repository and transaction abstractions are implemented in `Zumbo.BuildingBlocks.Infrastructure`; modules consume document ports rather than constructing Mongo clients.

PostgreSQL support is implemented by `Zumbo.Persistence.PostgreSql`. It provides the document repository contract, expression translation, identity/work-item storage and ordered migrations. Provider contract tests run against both databases.

Select the provider through configuration and validate it before changing existing data. Provider changes are operational migrations, not simple connection-string substitutions.

## Durable messaging

Business events that must survive process restarts use outbox and inbox contracts. The MongoDB implementation requires enqueue operations to participate in an active transaction. Workers claim eligible messages, apply retry policy and record completion so consumers can remain idempotent.

The recurrence scheduler follows the same idempotency rule: a unique schedule identity prevents duplicate occurrence creation when workers retry or race.

## Distributed services

- **Redis:** cache, coordination and SignalR scale-out where configured.
- **OpenSearch:** tenant-aware work-item indexing and query transport.
- **S3/MinIO:** attachment and object-storage adapters.
- **SMTP/Mailpit:** notification delivery and local integration tests.

External adapters are health-checked and wrapped by timeout/resilience policy. Failure handling must preserve tenant boundaries and avoid acknowledging durable work before side effects are complete.

## Migrations and transfer

MongoDB migrations are versioned backfills. PostgreSQL migrations are ordered definitions with checksum and rollback behavior. The database migrator and data-transfer tools live under `Backend/tools`; operational commands are documented in [database migrations](../operations/database-migrations.md).
