# Zumbo Application Guide

This directory is the application root. Run backend, frontend, Docker and operations commands from here unless a guide states otherwise.

## Requirements

- Docker Desktop with Docker Compose
- .NET SDK capable of targeting .NET 8
- Node 20.9 or later in the Node 20 line for the canonical local preflight
- pnpm 9.0.0 through Corepack
- At least 2 GiB free memory and 4 GiB free disk for the default preflight

## First local start

```powershell
corepack enable
corepack prepare pnpm@9.0.0 --activate
pnpm --dir Frontend install --frozen-lockfile
node scripts/operations/prepare-env.mjs --output Backend/.env
node scripts/operations/preflight.mjs --environment Backend/.env
node scripts/operations/demo-start.mjs --environment Backend/.env --build
```

`prepare-env.mjs` generates random local credentials, selects an available Docker subnet and refuses to overwrite an existing environment file. `demo-start.mjs` builds when requested, starts the Compose topology, waits for health and serves both modern frontend applications.

Open `http://127.0.0.1:58177`.

## Runtime topology

| Component | Default local endpoint |
| --- | --- |
| Frontend | `http://127.0.0.1:58177` |
| Gateway | `http://127.0.0.1:58089` |
| Direct API | `http://127.0.0.1:58088` |
| MongoDB | `127.0.0.1:58217` |
| Redis | `127.0.0.1:58379` |
| MinIO API / console | `127.0.0.1:58400` / `58401` |
| OpenSearch | `http://127.0.0.1:59200` |
| PostgreSQL test provider | `127.0.0.1:58432` |
| Prometheus / Grafana overlay | `59090` / `53000` |

The canonical Compose file runs MongoDB with replica-set initialization, OpenSearch, Redis, MinIO, API, worker and gateway. All published application endpoints are loopback-bound by the local environment contract.

## Administrator bootstrap

The generated environment configures `admin@zumbo.local` as the initial administrator identity but does not store an administrator password. After the API is ready, bootstrap once:

```powershell
node scripts/operations/bootstrap-admin.mjs --environment Backend/.env
```

The script reads the password interactively and requires at least 12 characters with lower-case, upper-case, digit and symbol classes. Registration uses the one-time bootstrap token from the local environment.

## Populated local data

Product seed scripts under `scripts/` create organizations, projects, teams and work items through application APIs. Use only the seed script appropriate to the intended local dataset. Scripts with reset behavior require their documented confirmation value and should not be pointed at shared or production data.

## Stop and resume

Stop the application while preserving volumes:

```powershell
node scripts/operations/demo-stop.mjs --environment Backend/.env
```

Start it again without rebuilding:

```powershell
node scripts/operations/demo-start.mjs --environment Backend/.env
```

Generated validation output is written below ignored `artifacts/` paths.

## Persistence

MongoDB is the default local provider. PostgreSQL implements the same core persistence contracts and is exercised through its test Compose profile and integration suite. Redis, OpenSearch and object storage are infrastructure adapters, not authoritative replacements for module data.

Before switching providers or applying migrations, read [database migrations and transfer](docs/operations/database-migrations.md).

## Durable messaging

Outbox and inbox workers deliver transactionally recorded business events with retries and idempotency. Do not bypass them for behavior that must survive process restart. Health checks report durable-processing readiness.

## Configuration

- `Backend/.env.example`: versioned environment shape and safe placeholders
- `Backend/.env`: generated local values; ignored and never committed
- `Backend/src/Zumbo.Api/appsettings*.json`: non-secret host and module defaults
- `Frontend/runtime-config.js`: generated frontend runtime configuration

Keep environment-specific URLs and secrets out of source.

## Validation

```powershell
dotnet restore Backend/Zumbo.sln
dotnet build Backend/Zumbo.sln --configuration Release --no-restore
dotnet test Backend/tests/Zumbo.UnitTests/Zumbo.UnitTests.csproj --configuration Release --no-build
dotnet test Backend/tests/Zumbo.ArchitectureTests/Zumbo.ArchitectureTests.csproj --configuration Release --no-build
pnpm --dir Frontend run quality
docker compose --env-file Backend/.env -f Backend/docker-compose.yml config --quiet
```

Provider and browser campaigns have additional prerequisites. See [testing](docs/quality/testing.md).

## Further reading

- [Backend](Backend/README.md)
- [Frontend](Frontend/README.md)
- [Architecture](docs/architecture/overview.md)
- [Local operations](docs/operations/local-development.md)
- [Scripts](scripts/README.md)
- [OpenAPI contract](contracts/README.md)
