# Scripts Reference

Scripts are grouped by responsibility. Run them from the `Zumbo` application root so relative paths resolve consistently.

## Operations

| Script | Purpose |
| --- | --- |
| `operations/prepare-env.mjs` | Generate or validate a local `.env` without overwriting existing values |
| `operations/preflight.mjs` | Validate tool versions, Docker, resources, ports and Compose configuration |
| `operations/demo-start.mjs` | Build/reuse clients and containers, start services and verify readiness |
| `operations/demo-stop.mjs` | Stop the local topology while preserving persistent volumes |
| `operations/bootstrap-admin.mjs` | Perform one-time local administrator bootstrap |
| `operations/demo-prepare.mjs` | Prepare or verify the supported populated local dataset |
| `operations/qa002-*` | Validate a disposable clean-Linux lifecycle and sanitized workflow output |

Scripts fail closed on placeholder secrets, non-loopback local endpoints and ambiguous destructive operations.

## Product data

`seed-*.mjs` utilities populate defined local scenarios through product APIs. Review each script's inputs before execution. Do not use local seed/reset utilities against shared environments.

## CI and quality

`ci/` contains executable repository, API, migration, security and runtime contracts used by GitHub Actions. `product/` contains product capability extraction and validation utilities. CI-generated output belongs under ignored `artifacts/` directories.

## Security and SBOM

```powershell
./scripts/Invoke-SecurityGate.ps1
node scripts/generate-security-sbom.mjs
```

The security gate expects its required scanners and images when container scanning is enabled. CI installs or provides those dependencies explicitly.

## Safety

- Keep credentials in environment variables or `.env`, never command history or source.
- Prefer scripts' built-in confirmation/validation options over direct database modification.
- Preserve volumes for routine stop/resume.
- Use targeted cleanup only for resources owned by the selected Compose project.
- Store generated logs, screenshots and reports outside Git history.
