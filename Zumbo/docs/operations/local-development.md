# Local Development

## Prepare

```powershell
corepack enable
corepack prepare pnpm@9.0.0 --activate
pnpm --dir Frontend install --frozen-lockfile
node scripts/operations/prepare-env.mjs --output Backend/.env
```

The generator creates random local secrets, chooses an available subnet and refuses to overwrite an existing file.

## Preflight

```powershell
node scripts/operations/preflight.mjs --environment Backend/.env
```

Preflight validates Node, pnpm, .NET, Docker/Compose, memory, disk, gateway/frontend ports and Compose syntax. Resolve failures rather than weakening the checks.

## Start

```powershell
node scripts/operations/demo-start.mjs --environment Backend/.env --build
```

Use `--build` after source, dependency, Dockerfile or frontend runtime-config changes. Omit it to reuse known-good images and frontend output.

Bootstrap the first administrator once:

```powershell
node scripts/operations/bootstrap-admin.mjs --environment Backend/.env
```

## Develop clients separately

```powershell
pnpm --dir Frontend run serve:modern:desktop
pnpm --dir Frontend run serve:modern:mobile
```

The dev servers use ports 58178 and 58179. The canonical static frontend uses 58177.

## Stop

```powershell
node scripts/operations/demo-stop.mjs --environment Backend/.env
```

Routine stop preserves data volumes. Do not run broad Docker pruning as a normal reset mechanism.

## Troubleshooting

- Validate `.env` with `node scripts/operations/prepare-env.mjs --check Backend/.env`.
- Validate Compose with `docker compose --env-file Backend/.env -f Backend/docker-compose.yml config --quiet`.
- Check `/health/live` and `/health/ready` through the gateway.
- Confirm ports are loopback-bound and currently available.
- Rebuild only the affected client or container when inputs changed.
