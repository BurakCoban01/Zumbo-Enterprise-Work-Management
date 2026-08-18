# Authentication and Authorization

Zumbo authenticates API requests with JWT bearer tokens and manages refreshable sessions through the Identity module. Account security includes password flows, session revocation, MFA setup/recovery and account lifecycle operations.

## Authorization model

- Stable permission identifiers are defined in source where endpoint policy requires them.
- Role-to-permission assignment and display metadata are persisted and evaluated at runtime.
- Organization and project scope are part of authorization context.
- Default role/catalog data is seeded deliberately rather than re-applied as an authoritative map on every request.
- Workflow status and transition policy belongs to workflow data.

Controllers require policies at the presentation boundary; application handlers also enforce invariants that cannot be trusted to routing alone. Last-administrator and system-administrator protections are domain/security rules, not user-interface constraints.

## Browser and integration security

The API configures trusted origins, rate limits, correlation and secure response behavior at the host. Realtime connections authenticate before joining scoped groups. Webhook and development integrations validate signatures/secrets and isolate provider credentials. Attachment access is authorized before storage adapters are called.

## Configuration

Never commit `Backend/.env`. Generate local secrets with:

```powershell
node scripts/operations/prepare-env.mjs --output Backend/.env
```

The generator uses random local values and refuses to overwrite an existing file. Production credentials, signing keys and certificates must be supplied through the deployment environment or a secret manager.

Security checks and commands are documented in [security validation](../quality/security-validation.md).
