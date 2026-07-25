# AGENTS.md — src/shared/

Shared assets **with no business logic**. Two concerns live here:

| Folder | Purpose |
|---|---|
| `contracts/` | Avro schemas (`.avsc`) consumed by services and the Schema Registry (ADR-007). |
| `observability/` | Cross-cutting helpers for logging and trace-context propagation (ADR-010). |

## Rules

- This folder must stay **free of business logic**. Put helpers, serializers,
  tracing utilities here — never domain entities, saga logic, or repository
  code.
- Nothing here may depend on any service under `src/services/`. The arrow is
  one-way: services depend on `shared/`, never the reverse.
- Any new `*.avsc` schema must be registered with the Schema Registry under
  BACKWARD compatibility (ADR-007) and added to `contracts/README.md`.
- Cross-protocol helpers (.NET helpers and Go helpers) live side-by-side; keep
  their public APIs symmetric where feasible.

## contracts/ structure

```
contracts/
  order/
    order_created_v1.avsc
    inventory_reserved_v1.avsc
    ...
  product/
  payment/
  inventory/
  notification/
  README.md         ← schema index + naming rules
```

Filename convention: `<aggregate>_<event>_v<N>.avsc`. Subject name in the
Registry: `<topic>-value` (matches the Kafka topic name, ADR-007).

## observability/

- `Opencode.TraceContext` (.NET): helpers to read `traceparent` from Kafka
  headers / HTTP headers and push into `LogContext`.
- `tracecontext` (Go): small package exposing `FromKafkaHeaders(h) context.Context`
  and `FromHttpHeaders(h) context.Context`.

No Serilog/zerolog configuration lives here — each service owns its own
logger setup; this folder only exposes propagation helpers.