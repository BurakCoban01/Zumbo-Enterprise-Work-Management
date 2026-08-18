# Zumbo Frontend

The frontend is an Angular CLI multi-project workspace with separate desktop and mobile applications plus shared product infrastructure.

## Workspace

```text
Frontend/
|-- angular.json
|-- package.json
|-- projects/
|   |-- modern-desktop/   Angular + Bulma desktop application
|   |-- modern-mobile/    Ionic Angular mobile application
|   `-- modern-shared/    Shared API, auth, state and domain UI
|-- shared/               Design-system assets and product marks
`-- tests/                Unit, contract, accessibility and browser tests
```

The workspace uses Angular 22.0.8, Angular CLI/build 22.0.9, Ionic Angular 8.8.15, TypeScript 6.0.2 and pnpm 9.0.0. Exact versions are pinned in `package.json` and `pnpm-lock.yaml`.

## Applications

### Desktop

`projects/modern-desktop` is optimized for dense professional workflows. Its feature routes cover home, personal work, inbox, projects, boards, list/backlog/sprint planning, work-item detail, reporting, intake, automation, strategy, administration and settings.

Bulma provides baseline layout primitives. `shared/design-system.css` and application SCSS define Zumbo's responsive visual system.

### Mobile

`projects/modern-mobile` uses Ionic Angular components and navigation patterns. It shares business and API behavior with desktop while adapting layout, primary actions, menus and detail flows for touch and narrow viewports.

### Shared library

`projects/modern-shared/src/lib` owns code needed by both clients, including API transport, session/authentication behavior, route contracts, product models and shared interaction logic. Platform-specific shell and navigation code remains in each application.

## Runtime configuration

Production builds are emitted under:

- `dist-modern/modern-desktop`
- `dist-modern/modern-mobile`

The local operations script writes `runtime-config.js` for both applications and checks that the configured API URL targets the local gateway. Do not hardcode environment-specific hostnames in application source.

Default local endpoints:

| Surface | URL |
| --- | --- |
| Canonical frontend server | `http://127.0.0.1:58177` |
| Angular desktop dev server | `http://127.0.0.1:58178` |
| Angular mobile dev server | `http://127.0.0.1:58179` |
| Gateway/API origin | `http://127.0.0.1:58089` |

## Install

```powershell
corepack enable
corepack prepare pnpm@9.0.0 --activate
pnpm install --frozen-lockfile
```

Use a Node version allowed by the `engines` field. The canonical local preflight currently validates Node 20.9 or later in the Node 20 line.

## Development

Run from `Zumbo/Frontend`:

```powershell
pnpm run serve:modern:desktop
pnpm run serve:modern:mobile
```

Each command uses the Angular CLI from local dependencies. No global Angular CLI installation is required.

Production builds:

```powershell
pnpm run build:modern:desktop
pnpm run build:modern:mobile
pnpm run build
```

The combined build also creates the canonical static output expected by local operations and runtime browser checks.

## Frontend architecture

- Feature routes own page composition and route-level data loading.
- Shared services own API/session behavior and reusable state transitions.
- Components expose loading, empty, error, denied, offline and conflict states where the workflow requires them.
- Desktop and mobile reuse domain contracts without forcing identical layouts.
- Permission and workflow metadata comes from the API rather than a second frontend catalog.
- Realtime updates reconcile server changes without replacing optimistic local interactions unnecessarily.

When adding a feature, keep route wiring, API contracts and visible permission behavior aligned across the clients that expose it.

## PWA and offline behavior

Both applications configure Angular service workers for production builds. Update prompts remain user-controlled. Cached application assets improve startup resilience; server-owned work is not presented as synchronized until the API confirms it.

## Accessibility and responsive behavior

The shared visual system defines focus visibility, contrast, motion preferences and adaptive spacing. Browser tests exercise keyboard navigation, responsive layouts and cross-browser behavior. New controls must retain semantic names, predictable focus order and touch-sized interactions.

Desktop density should not be implemented by unreadably small text. Mobile adaptation should prioritize frequent work rather than render the desktop navigation at a smaller width.

## Tests

Focused checks:

```powershell
pnpm run lint
pnpm run unit
pnpm run build
pnpm run audit:dependencies
pnpm run audit:licenses
```

Combined frontend quality:

```powershell
pnpm run quality
```

Browser setup and Chromium acceptance:

```powershell
pnpm run browser:install
pnpm run test:e2e:chromium
```

Real-backend scenarios require the local Compose topology and generated runtime configuration. Test output belongs under ignored `Zumbo/artifacts/` paths and is uploaded by CI when useful.

## Dependency changes

Update `package.json` and `pnpm-lock.yaml` together. Run the production build, dependency audit and license audit before merging. Keep Angular framework packages on compatible versions and review Ionic peer requirements when changing Angular major versions.
