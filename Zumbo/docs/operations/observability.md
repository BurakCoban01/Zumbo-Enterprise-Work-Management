# Observability

Zumbo emits application telemetry through OpenTelemetry. The observability Compose overlay contains collector, Prometheus and Grafana configuration.

Local overlay endpoints default to:

- Prometheus: `http://127.0.0.1:59090`
- Grafana: `http://127.0.0.1:53000`

Configuration lives under `Backend/observability` and `Backend/docker-compose.observability.yml`.

## Operational signals

Monitor at minimum:

- API and gateway readiness/latency;
- dependency health and timeout rates;
- outbox/inbox backlog, claims, retries and dead letters;
- recurrence scheduler lag and duplicate prevention;
- realtime connection/dispatch failures;
- database pool and migration state;
- storage/search delivery failures;
- authentication and authorization failures without sensitive payloads.

Correlation identifiers should flow through gateway, API, background work and outbound adapters. Logs must not contain tokens, credentials, attachment content or unnecessary personal data.
