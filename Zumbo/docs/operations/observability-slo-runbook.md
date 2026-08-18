# Observability SLO Runbook

The optional observability profile supports private local and production-like validation. The API does not require a collector when `Observability:OtlpEnabled` is false.

Metric labels never include tenant, user or project identifiers, resource IDs, email addresses, URL queries, payloads or secrets. Correlation identifiers belong in bounded logs and traces rather than metric dimensions.

## Start and stop

```powershell
docker compose --env-file Backend/.env -f Backend/docker-compose.yml -f Backend/docker-compose.observability.yml --profile observability up --detach --build
docker compose --env-file Backend/.env -f Backend/docker-compose.yml -f Backend/docker-compose.observability.yml --profile observability down
```

Prometheus binds to `127.0.0.1:59090` and Grafana to `127.0.0.1:53000`. The collector is not published to the host. Stopping the profile does not delete product data or observability volumes.

## Service objectives

| Signal | Initial objective | Window |
| --- | ---: | ---: |
| API availability | At least 99.5% non-5xx responses | Rolling 30 days |
| API latency | p95 at or below 500 ms | Rolling 1 hour |
| Durable delivery | At least 99.9% without dead letter | Rolling 30 days |
| Outbox freshness | Oldest pending age at or below 60 seconds | Rolling 15 minutes |
| Realtime publish | At least 99.9% successful | Rolling 1 hour |

Treat these values as initial operating thresholds. Establish production commitments only after observing representative traffic and dependency behavior.

## High error budget burn

1. Compare readiness, route-template error rates and the latest configuration change.
2. Correlate traces and redacted logs by trace ID; never export raw queries or user payloads.
3. Separate process failure from an optional dependency in degraded state.
4. Record the affected route group, safe correlation examples and recovery window.

## Dependency circuit open

1. Inspect the authorized external-dependency snapshot and rejection rate.
2. Validate provider-native health without logging credentials or endpoints containing secrets.
3. Wait for the bounded half-open probe; do not force-reset the circuit.
4. Review attempts, retries, in-flight work and queue depth together before changing limits.

## Outbox lag

1. Compare pending age with claimed, completed, retried and dead-letter counts.
2. Validate lease ownership, fencing and the selected persistence provider.
3. Repair the handler before using bounded replay; never bulk-replay unreviewed dead letters.
4. Monitor throughput and oldest pending age until the queue returns to its objective.

## Realtime failure

1. Compare active connections and publish failures by API instance.
2. Check Redis backplane readiness, gateway WebSocket upgrades and client reconnect/resync behavior.
3. Keep connection, user and project identifiers out of metrics and incident notes.

## Exporter failure

The OTLP batch queue is bounded. Collector failure must not stop HTTP requests, outbox processing or realtime delivery. Repair the collector, confirm new telemetry flow and accept the bounded telemetry gap; do not replay business traffic solely to reconstruct observability data.

## Dashboard validation

The `Zumbo Service Overview` dashboard should show HTTP rate, p95 latency, 5xx rate, dependency latency, outbox age and realtime signals. Confirm the `zumbo-slo` rules through Prometheus before treating an empty panel as an application failure.
