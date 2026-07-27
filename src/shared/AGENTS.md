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

Implementation governed by ADR-010 as **amended by ADR-015** (native
distributed-tracing propagators replace the previously hand-rolled
`TraceParent` parser).

- `OpenCode.TraceContext` (.NET 10, BCL only): thin carriers over
  `System.Diagnostics.DistributedContextPropagator` + an
  `ActivitySource` named `"OpenCode.TraceContext"`. Exposes
  `ReadFrom/WriteTo HTTP|Kafka`, `StartChild[FromHttp|FromKafka]Headers`,
  `AlwaysSample()` (registers an `ActivityListener` so
  `ActivitySource.StartActivity` returns non-null in a no-OTLP setup),
  and a Serilog enricher reading `Activity.Current` (`.WithActivityTrace()`).
- `tracecontext` (Go): thin carriers over
  `go.opentelemetry.io/otel/propagation.TraceContext`. Exposes
  `From/Into HTTP|Kafka Headers`, `SpanContextFromContext`,
  `ContextWithSpanContext`, `NewRootSpanContext`. No TracerProvider /
  OTLP export here — Seq stays the only sink.

No Serilog/zerolog configuration lives here — each service owns its own
logger setup; this folder only exposes propagation helpers + the
enricher(s) tied to `Activity.Current` / `trace.SpanContext`.