# API and Gateway

## API host

`Zumbo.Api` is the composition and presentation host. `AddControllers()` configures MVC behavior, and `MapControllers()` exposes business `/api/**` routes. Controllers are grouped by module and operation under `Presentation/Controllers` and delegate to module use cases.

The API preserves a consistent response envelope, correlation metadata, authorization policies and problem handling. File transfers, ETags, webhooks and concurrency-sensitive operations retain their dedicated HTTP semantics at the presentation boundary.

## Non-controller endpoints

- `/health/live` verifies that the process is running.
- `/health/ready` evaluates checks tagged for readiness.
- `/hubs/work-items` provides realtime work-item collaboration through SignalR.

These endpoints remain framework-specific because they are not business-resource controllers.

## Gateway

`Zumbo.Gateway` uses YARP to provide the client-facing API origin. It applies gateway concerns without duplicating module authorization. Local defaults expose the gateway at `127.0.0.1:58089` and the direct API at `127.0.0.1:58088`.

## Contract compatibility

`contracts/openapi.v1.json` is the versioned compatibility baseline. API tests and OpenAPI checks detect accidental route, method and schema changes. Update the baseline only after reviewing the change as an intentional public-contract modification.
