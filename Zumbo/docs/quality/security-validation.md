# Security Validation

Security validation combines source policy, tests, dependency review and runtime boundaries.

## Repository gates

```powershell
./scripts/Invoke-SecurityGate.ps1
node scripts/generate-security-sbom.mjs
pnpm --dir Frontend run audit:dependencies
pnpm --dir Frontend run audit:licenses
```

The PowerShell gate coordinates configured source and container scanners and writes sanitized output below ignored `artifacts/security/generated`. CI builds the API and gateway images before image scanning.

## Required review areas

- authentication, refresh sessions, MFA and account recovery;
- organization/project authorization and permission metadata;
- CSRF/CORS and browser credential handling;
- webhook signatures and provider secret rotation;
- attachment authorization and object keys;
- tenant isolation in persistence, cache, search and realtime groups;
- log/telemetry redaction;
- migration and backup credential handling;
- dependency and container vulnerabilities.

## Secrets

`Backend/.env.example` contains placeholders only. Local secrets are generated into ignored `Backend/.env`. CI uses ephemeral values. Never commit tokens, real connection strings, certificates, private keys or exported environment files.

## CodeQL

The workflow evaluates whether GitHub Code Security is available for the private repository. CodeQL jobs run only when capability is enabled. An unavailable external capability is not reported as a passed scan; the remaining repository security gates still run.

## Vulnerability decisions

Do not suppress a finding without a scoped, time-bounded policy and compensating evidence. Re-run the affected gate after dependency or base-image changes.
