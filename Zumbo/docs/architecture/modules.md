# Module Boundaries

Zumbo modules are separate .NET projects under `Backend/src`. Each module owns its domain behavior and application slices. The API project composes modules but is not the home of business rules.

| Module | Primary responsibility | Main source area |
| --- | --- | --- |
| Identity | Authentication, credentials, sessions, MFA and permission evaluation | `Zumbo.Modules.Identity` |
| Organizations | Organization lifecycle and tenant-scoped policy | `Zumbo.Modules.Organizations` |
| Teams | Team membership, invitations and collaboration boundaries | `Zumbo.Modules.Teams` |
| Projects | Projects, members, resources, portfolios, goals and knowledge composition | `Zumbo.Modules.Projects` |
| Boards | Board definitions, columns, saved views, swimlanes and ranking | `Zumbo.Modules.Boards` |
| Workflows | Status definitions, transitions and workflow policy | `Zumbo.Modules.Workflows` |
| WorkItems | Work lifecycle, planning, collaboration, reporting, automation and integrations | `Zumbo.Modules.WorkItems` |
| Notifications | Notification preferences, creation and delivery operations | `Zumbo.Modules.Notifications` |
| Audit | Audit records, history and integrity queries | `Zumbo.Modules.Audit` |

Contract projects exist where a stable boundary is consumed outside a module:

- `Zumbo.Modules.Identity.Contracts`
- `Zumbo.Modules.Projects.Contracts`
- `Zumbo.Modules.WorkItems.Contracts`

## Feature organization

Use cases are organized by feature and operation. A typical slice contains request/response contracts, validation and a handler close to the domain behavior it coordinates. Compatibility facades may remain where callers require a stable interface, but they delegate to feature handlers rather than collecting unrelated implementation.

## Rules

1. Keep domain decisions inside the owning module.
2. Keep HTTP status, headers and binding behavior in presentation controllers.
3. Access infrastructure through application ports.
4. Do not read another module's persistence collection directly.
5. Add cross-module contracts deliberately and keep them narrow.
6. Update architecture tests when introducing a legitimate new dependency direction.

The executable checks are in `Backend/tests/Zumbo.ArchitectureTests`.
