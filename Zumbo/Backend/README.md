# Zumbo Backend

The backend is a .NET 8 modular monolith. `Zumbo.sln` contains the API and gateway hosts, module projects, shared building blocks, provider implementations, operational tools and automated tests.

## Solution organization

```text
Backend/
|-- src/
|   |-- Zumbo.Api/                         Composition and HTTP presentation
|   |-- Zumbo.Gateway/                     YARP client-facing gateway
|   |-- Zumbo.SharedKernel/                Stable shared primitives
|   |-- Zumbo.BuildingBlocks.Application/  Application contracts and behaviors
|   |-- Zumbo.BuildingBlocks.Infrastructure/ Infrastructure adapters
|   |-- Zumbo.Modules.*/                   Business modules and boundary contracts
|   `-- Zumbo.Persistence.PostgreSql/       PostgreSQL provider
|-- tests/                                  Unit, API, architecture and provider tests
|-- tools/                                  Capacity, migration and transfer utilities
|-- observability/                          Prometheus, Grafana and collector config
`-- docker-compose*.yml                     Runtime and validation topologies
```

Central package versions are defined in `Directory.Packages.props`. All projects target `net8.0`.

## Hosts

### API

`src/Zumbo.Api/Program.cs` is intentionally small. It registers module entry points and delegates host configuration to `Composition/Hosting`. The main boundaries are:

- `Composition/Modules`: module registration and host adapters;
- `Presentation/Controllers`: controller-based business HTTP APIs;
- `Infrastructure`: concrete persistence, messaging, storage, search and integration adapters;
- `Application`: host-level operations that do not belong to a business module.

The middleware pipeline maps liveness/readiness checks, the work-item SignalR hub and MVC controllers.

### Gateway

`src/Zumbo.Gateway` is the client-facing reverse proxy. It centralizes gateway concerns and forwards application traffic to the API without reimplementing business policy.

## Modules

| Module | Responsibility | Key entry points | Persistence/contracts |
| --- | --- | --- | --- |
| Identity | Credentials, login, sessions, MFA, account security and permission evaluation | `Zumbo.Modules.Identity`, `Composition/Modules/Identity` | Identity documents and `Zumbo.Modules.Identity.Contracts` |
| Organizations | Organization lifecycle and tenant context | `Zumbo.Modules.Organizations`, `Composition/Modules/Organizations` | Module documents and organization repositories |
| Teams | Teams, membership and invitation workflows | `Zumbo.Modules.Teams`, `Composition/Modules/Teams` | Team documents and durable invitation events |
| Projects | Projects, membership, resources, portfolio, goals and knowledge composition | `Zumbo.Modules.Projects`, `Composition/Modules/Projects` | Project documents and `Zumbo.Modules.Projects.Contracts` |
| Boards | Boards, columns, views, swimlanes and ordering | `Zumbo.Modules.Boards`, `Composition/Modules/Boards` | Board documents and rank/concurrency contracts |
| Workflows | Workflow definitions, statuses and transitions | `Zumbo.Modules.Workflows`, `Composition/Modules/Workflows` | Workflow documents and transition policy |
| WorkItems | Work lifecycle, collaboration, planning, recurrence, reports, automation and integrations | `Zumbo.Modules.WorkItems`, `Composition/Modules/WorkItems` | Work-item documents and `Zumbo.Modules.WorkItems.Contracts` |
| Notifications | Preferences, inbox and delivery operations | `Zumbo.Modules.Notifications`, `Composition/Modules/Notifications` | Notification documents and delivery ports |
| Audit | Audit records, history and integrity queries | `Zumbo.Modules.Audit`, `Composition/Modules/Audit` | Append/query audit storage |

Dashboard, capacity, sprint, portfolio, goal and knowledge registration is composed through the owning project/work-item module boundaries rather than separate deployment units.

## Application and domain boundaries

Feature handlers coordinate one use case. Domain types own invariants. Presentation controllers handle HTTP binding, response status, headers and authorization policy. Infrastructure adapters implement application ports.

Avoid placing business logic in:

- controllers;
- dependency-injection registrars;
- persistence documents;
- gateway routes;
- background worker loops.

Compatibility facades are allowed when callers require a stable service boundary, but they should delegate to focused handlers.

## Persistence providers

### MongoDB

MongoDB is the default provider and uses a replica set for transactional behavior. Module-specific settings can override the global configuration. Versioned backfills live with the Mongo migration runner.

### PostgreSQL

`Zumbo.Persistence.PostgreSql` implements document repository and specialized storage contracts using Npgsql. Ordered migration definitions, expression translation and rollback checks are covered by PostgreSQL integration tests.

### Supporting services

- Redis: distributed cache, coordination and SignalR backplane
- OpenSearch: work-item search indexes and aliases
- S3/MinIO: attachment and object storage
- SMTP/Mailpit: notification delivery testing

## Durable processing

Outbox and inbox abstractions preserve messages across retries. Mongo transactions ensure aggregate changes and emitted durable events are committed together. Background workers use claims, retry timing and idempotency records. Recurrence scheduling uses a unique schedule identity to prevent duplicate occurrences.

## Migrations and tools

| Tool | Purpose |
| --- | --- |
| `tools/Zumbo.DatabaseMigrator` | Inspect, apply, script and roll back database migrations |
| `tools/Zumbo.DataTransfer` | Transfer and verify data between supported providers |
| `tools/Zumbo.Capacity` | Capacity/load-related validation utilities |

Run database operations only with an explicit environment and backup plan. See [`docs/operations/database-migrations.md`](../docs/operations/database-migrations.md).

## Docker Compose variants

| File | Purpose |
| --- | --- |
| `docker-compose.yml` | Canonical local topology |
| `docker-compose.host-access.yml` | Host-accessible dependency ports |
| `docker-compose.baseline.yml` | Additional baseline integration services |
| `docker-compose.ci.yml` | CI-specific overrides |
| `docker-compose.postgresql.test.yml` | PostgreSQL provider tests |
| `docker-compose.observability.yml` | Prometheus, Grafana and telemetry collector |
| `docker-compose.hardened.yml` | Hardened runtime overrides |
| `docker-compose.production-like.yml` | Production-like validation topology |
| `docker-compose.scale.yml` / `capacity.yml` | Scale and capacity campaigns |

Validate a topology before starting it:

```powershell
docker compose --env-file .env -f docker-compose.yml config --quiet
```

## Test projects

| Project | Coverage |
| --- | --- |
| `Zumbo.UnitTests` | Domain, application and infrastructure units |
| `Zumbo.ApiTests` | HTTP/API contracts and selected integrations |
| `Zumbo.ArchitectureTests` | Module, dependency and source-layout boundaries |
| `Zumbo.GatewayTests` | Gateway routing and policy |
| `Zumbo.PersistenceIntegrationTests` | MongoDB and durable storage contracts |
| `Zumbo.PostgreSqlIntegrationTests` | PostgreSQL provider and migration contracts |
| `Zumbo.StorageIntegrationTests` | Object-storage behavior |

`tests/Shared` contains reusable provider contract fixtures and is not a standalone test project.

## Build and test

Run from `Zumbo/Backend`:

```powershell
dotnet restore Zumbo.sln
dotnet build Zumbo.sln --configuration Release --no-restore --warnaserror
dotnet test tests/Zumbo.UnitTests/Zumbo.UnitTests.csproj --configuration Release --no-build
dotnet test tests/Zumbo.ApiTests/Zumbo.ApiTests.csproj --configuration Release --no-build
dotnet test tests/Zumbo.ArchitectureTests/Zumbo.ArchitectureTests.csproj --configuration Release --no-build
dotnet test tests/Zumbo.GatewayTests/Zumbo.GatewayTests.csproj --configuration Release --no-build
```

Provider tests require their real dependency:

```powershell
dotnet test tests/Zumbo.PersistenceIntegrationTests/Zumbo.PersistenceIntegrationTests.csproj --configuration Release
dotnet test tests/Zumbo.PostgreSqlIntegrationTests/Zumbo.PostgreSqlIntegrationTests.csproj --configuration Release
dotnet test tests/Zumbo.StorageIntegrationTests/Zumbo.StorageIntegrationTests.csproj --configuration Release
```

## Configuration

`src/Zumbo.Api/appsettings.json` contains non-secret defaults. Module-specific appsettings files define module storage sections. Local secrets and published ports belong in `.env`, generated from `.env.example` by the repository operation script.

Do not commit signing keys, database passwords, bootstrap tokens or provider credentials.
