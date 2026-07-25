# ADR-010: Observability with Seq + W3C trace context correlation

- **Status:** Accepted
- **Date:** 2026-07-25
- **Decisor:** Portfolio author
- **Related to:** ADR-002 (Kafka), ADR-007 (Avro), ADR-001 (polyglot)

## Context

In a polyglot event-driven platform (.NET + Go), when an HTTP request at the
gateway triggers events that travel through Inventory→Payment→Shipping,
investigating "why is order X stuck in `PaymentPending`" requires log
correlation across 5+ processes in two runtimes.

Without standardization:
- Each service chatters logs with no context → impossible to trace.
- The trace born at the gateway dies as it enters Kafka.
- Bugs in Saga compensation become a mystery (ADR-003).

## Decision

We adopt **centralized structured logging in Seq** with correlation through
the **W3C Trace Context** (`traceparent`/`tracestate`), propagated as a
Kafka header on every event produced and consumed.

## Implementation

### Log collection
- **.NET (8):** `Serilog` → `Serilog.Sinks.Seq` → Seq (port 5341/HTTP).
- **Go:** `zerolog` (structured JSON) → an `http` sink to the Seq Ingest
  API (port 8082). CLEF-compatible formatter.

### Trace context
- **HTTP inbound** (Gateway→services): the gateway (YARP) generates a
  `traceparent` if none is present; it propagates it downstream via the
  HTTP `traceparent` header.
- **Kafka outbound** (service→another via event): the producer copies
  `traceparent` to the Kafka `traceparent` header (alongside `event_id`,
  ADR-009). `tracestate` is optional for baggage.
- **Kafka inbound** (consumer): reads the `traceparent` header and injects
  it into the current logging context
  (`LogContext.PushProperty("TraceId", ...)` in Serilog; `log.Ctx()` in
  zerolog). This way every log entry across services carries the same
  `TraceId` + `SpanId`.
- **CLI/Workers:** generate their own root `traceparent` when they start a
  flow (e.g. a scheduled Shipping job).

### Log structure
Every log entry (both runtimes) contains at minimum:
- `@t` timestamp, `@mt` template, `@l` level (Seq CLEF).
- `TraceId`, `SpanId`, `ParentSpanId`.
- `ServiceName` (constant per process).
- `EventType` (e.g. `PaymentApproved`).
- `AggregateId` (if applicable).
- `EventId` (if in a consumer context — see ADR-009).

### In Seq
- Queries by `TraceId` reconstruct the whole journey of an order.
- A "Saga stuck" dashboard filters by `EventType = 'OrderSagaWaiting'`
  lasting longer than a threshold.

## Rationale

- **Seq** was chosen for the portfolio because: a single sink for .NET and
  Go via CLEF; dev-friendly UI; lightweight; less setup than ELK/Loki;
  trivial Serilog sink. (Demonstrating a unified stack is a portfolio
  point.)
- **W3C Trace Context** is an industry standard, cross-vendor (OpenTelemetry
  uses the same format); shows adherence to standardization.
- **Propagation via a Kafka header** (not the Avro payload) separates
  transport from telemetry; if the Avro payload evolves, the trace is
  invariant.
- **Low cost:** already included in the docker-compose (ADR-002/Zero). In
  production, Seq would be replaced by the OpenTelemetry Collector +
  Tempo/Loki; the code pattern stays the same.

## Consequences

- **Positive:**
  - Cross-service investigation is a `Search TraceId:abc` in Seq.
  - No performance hit from correlation (just headers + log enrichment).
  - Works in a polyglot setup with no vendor lock-in.
- **Negative:**
  - Seq has no trace history like Jaeger — no flame graph, only correlated
    logs. Trace is carried via IDs in logs; adequate for the proposed
    level.
  - Go via zerolog→Seq requires a small adapter (a CLEF wrapper).
- **Mitigations:**
  - Documentation in `docs/runbook/tracing.md` + a link from the Seq UI
    spanID for manual reconstruction.
  - Documented evolution: replacing Seq with OpenTelemetry Collector +
    Grafana is described in a future ADR.

## Alternatives considered

- **OpenTelemetry + Jaeger + Prometheus:** more complete but 3–4 extra
  containers and more complex instrumentation — deliberately not chosen for
  this portfolio given the "Docker Compose + only structured logging" scope
  of the initial phase.
- **ELK/EFK:** heavyweight; more operational overhead than Seq for a dev
  box.
- **Application Insights/Datadog:** cloud/SaaS — does not fit the
  "spin up locally with one command" requirement.