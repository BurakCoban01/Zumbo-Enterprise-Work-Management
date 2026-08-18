# Testing and Quality Gates

Use the smallest gate that proves the changed boundary, then broaden at integration or release checkpoints. Commands below run from `Zumbo` unless stated otherwise.

## Backend

Restore and build:

```powershell
dotnet restore Backend/Zumbo.sln
dotnet build Backend/Zumbo.sln --configuration Release --no-restore --warnaserror
```

Core test projects:

```powershell
dotnet test Backend/tests/Zumbo.UnitTests/Zumbo.UnitTests.csproj --configuration Release --no-build
dotnet test Backend/tests/Zumbo.ApiTests/Zumbo.ApiTests.csproj --configuration Release --no-build
dotnet test Backend/tests/Zumbo.ArchitectureTests/Zumbo.ArchitectureTests.csproj --configuration Release --no-build
dotnet test Backend/tests/Zumbo.GatewayTests/Zumbo.GatewayTests.csproj --configuration Release --no-build
```

Provider suites require their real services:

```powershell
dotnet test Backend/tests/Zumbo.PersistenceIntegrationTests/Zumbo.PersistenceIntegrationTests.csproj --configuration Release
dotnet test Backend/tests/Zumbo.PostgreSqlIntegrationTests/Zumbo.PostgreSqlIntegrationTests.csproj --configuration Release
dotnet test Backend/tests/Zumbo.StorageIntegrationTests/Zumbo.StorageIntegrationTests.csproj --configuration Release
```

Run focused `--filter` selections during implementation when they fully cover the changed use case. Do not use a narrow filter as proof for a provider-wide change.

## Frontend

```powershell
pnpm --dir Frontend install --frozen-lockfile
pnpm --dir Frontend run lint
pnpm --dir Frontend run unit
pnpm --dir Frontend run build
pnpm --dir Frontend run audit:dependencies
pnpm --dir Frontend run audit:licenses
```

`pnpm --dir Frontend run quality` combines the standard static, unit, build and dependency checks.

## Browser acceptance

Install Chromium once for the current dependency tree:

```powershell
pnpm --dir Frontend run browser:install
```

Run the local topology and then a focused real-backend scenario or the Chromium acceptance command:

```powershell
pnpm --dir Frontend run test:e2e:chromium
```

Broader Firefox/WebKit and visual campaigns are appropriate when shared layout, browser APIs, PWA behavior or cross-browser contracts changed.

## API and migration contracts

API tests validate response envelopes, authorization and special HTTP semantics. OpenAPI scripts compare the generated contract with `contracts/openapi.v1.json`. PostgreSQL integration tests include migration idempotency and rollback/reapply behavior.

## Generated output

Coverage, OpenAPI output, browser captures, security scans and migration scripts are generated below ignored `artifacts/` paths. CI uploads relevant output with bounded retention. Do not commit run-specific output.

## Change-to-gate guide

| Change | Minimum focused evidence |
| --- | --- |
| Domain/application handler | Unit tests and module build |
| Controller/API contract | API tests and OpenAPI compatibility |
| Module/project references | Architecture tests |
| MongoDB adapter/migration | Mongo provider contract tests |
| PostgreSQL adapter/migration | PostgreSQL integration and rollback checks |
| Shared frontend behavior | Unit tests, both production builds |
| Responsive/interaction behavior | Real-backend browser check at affected breakpoints |
| Auth/security boundary | Security/authorization tests plus affected API/browser flow |
| Docker/runtime inputs | Compose config and targeted lifecycle check |
