# ADR-015: Use native distributed-tracing propagators (.NET `Activity` + Go OTel `propagation`)

- **Status:** Accepted
- **Date:** 2026-07-26
- **Decisor:** Portfolio author
- **Amends:** ADR-010 (only its *Implementation* section; the architectural
  decision — Seq sink + W3C Trace Context over Kafka headers — stays unchanged)
- **Related to:** ADR-001 (polyglot), ADR-002 (Kafka), ADR-010 (observability)

## Context

ADR-010 accepted **Seq + W3C Trace Context** as the correlation mechanism.
Its *Implementation* section specified a hand-rolled `TraceParent`
parser/encoder shipped as two sibling helpers
(`src/shared/observability/dotnet/OpenCode.TraceContext/` and
`src/shared/observability/go/tracecontext/`), each ~150 LOC plus tests
re-implementing the W3C parsing rules from scratch.

After Phase 1 we noticed two issues with that approach:

1. **Both language runtimes already ship W3C Trace Context propagation
   out-of-the-box.** Re-implementing it duplicates standard logic, increases
   the maintenance surface, and risks drifting from the spec on edge cases
   (e.g. `tracestate`, baggage, future format versions).
2. **Logs lose the live `Activity`/`Span` correlation.** A hand-rolled
   `TraceParent` has to be threaded through every layer explicitly; with
   native distributed tracing, `Activity.Current` (or `trace.SpanFromContext`
   in Go) is the implicit ambient context, so inbound propagation, outbound
   HTTP, and Serilog/zerolog enrichment all read from one source.

## Decision

Replace the hand-rolled `TraceParent` helpers with thin façades over the
two runtimes' built-in W3C propagators:

| Runtime | Propagator used | Source module |
|---|---|---|
| .NET 10 | `System.Diagnostics.DistributedContextPropagator` (W3CTraceContextPropagator, default) | `System.Diagnostics` (BCL — no extra NuGet) |
| Go 1.24 | `go.opentelemetry.io/otel/propagation.TraceContext` (default propagator) | `go.opentelemetry.io/otel` v1.x |

The shared package on each side now provides only:

1. Carrier adapters that bridge the propagator to **HTTP header
   dictionaries** and **Kafka header byte-collections**.
2. **Read / Write** entry points (`ReadFromHttpHeaders`,
   `WriteToHttpHeaders`, `ReadFromKafkaHeaders`, `WriteToKafkaHeaders`)
   returning / accepting `ActivityContext?` (.NET) or `trace.SpanContext`
   (Go) instead of a custom `TraceParent`.
3. A Serilog enricher (.NET) / zerolog helper (Go) that reads the ambient
   span from `Activity.Current` / `trace.SpanFromContext` and pushes
   `TraceId` + `SpanId` (+ `ParentSpanId` where available) onto every log
   event.
4. A bootstrap helper `TraceContext.AlwaysSample()` (.NET) registering an
   `ActivityListener` that returns `AllDataAndRecorded` — required for
   `ActivitySource.StartActivity` to return non-null activities in a
   Seq-only (no OTLP exporter) deployment.

What does **not** change from ADR-010:

- Sink stays **Seq** (CLEF). We add *no* OTLP export, no Jaeger, no
  OpenTelemetry Collector. The persistent artifact remains the log
  stream correlated by `TraceId`.
- The `traceparent` still rides as a **Kafka header**, not in the Avro
  payload (`shared/AGENTS.md` §4 unchanged).
- `tracestate` is supported transparently (it’s part of the W3C spec the
  propagator implements); previously ignored.

## Why these libraries

- **`System.Diagnostics.ActivitySource`** is .NET’s canonical distributed
  tracing primitive. ASP.NET Core, `HttpClientHandler`, and EF Core already
  propagate `traceparent` through it automatically — so HTTP inbound and
  retrofitting HTTP outbound is free code.
- **`DistributedContextPropagator`** abstracts *which* propagation format
  is used; W3C is the default in .NET 8+ / 10. The propagator handles both
  the `traceparent` and `tracestate` headers, validating per spec, and is
  the documented migration path if/when we add OTLP / Collector.
- **`go.opentelemetry.io/otel/propagation.TraceContext`** is the OTel
  W3C implementation, designed for exactly this use (role: propagator
  without forcing a TracerProvider / exporter). The default
  `otel.GetTextMapPropagator()` returns it; services use it via the façade
  without ever importing an OTLP exporter.

## Consequences

- **Positive**
  - *\~250 LOC of hand-rolled parser/encoder + tests deleted* on both
    sides; replaced with carriers + a façade — total surface roughly
    halves.
  - Inbound HTTP propagation in the API Gateway and the .NET services
    becomes automatic (ASP.NET Core middleware); no per-action
    `ReadFromHeaders` plumbing except where a service wants the parsed
    `ActivityContext` explicitly.
  - Serilog enricher reads `Activity.Current`, so any code path that
    already owns an `Activity` (including EF Core query spans) gets
    correlation for free — including the saga and outbox publisher paths.
  - `tracestate` + baggage propagation are no longer dropped.
  - Path to ADR-010’s described "future: replace Seq with OTel Collector +
    Grafana" is now structural: changing the sink later is *one* DI
    registration; the propagation helpers do not change.
- **Negative**
  - Adds the `go.opentelemetry.io/otel` dependency to the Go side
    (~3 modules, well-maintained, Apache-2.0). The .NET side adds *no*
    new dependency (everything is in the BCL).
  - Services must call `TraceContext.AlwaysSample()` once at startup
    (.NET) or `otel.SetTracerProvider(...)` with a noop (Go) on programs
    that originate flows (CLI / workers). Documented in the service
    AGENTS.md files when those services are scaffolded.
  - The two helpers no longer expose identical *types* (custom `TraceParent`
    is gone); `ActivityContext` ↔ `trace.SpanContext` differ in shape.
    The on-the-wire format is identical, however, so cross-service
    correlation in Seq still works. The README symmetry table is updated
    accordingly.
- **Mitigations**
  - The shared `README.md` retains the symmetry table at the
    *behavioural* level (Extract/Inject names + W3C invariants), and
    documents the type asymmetry explicitly.
  - The bootstrap helper + service AGENTS.md call out the one-line
    wiring at process start.

## Alternatives considered

- **Keep the hand-rolled `TraceParent`** — duplicate work, fragile on
  `tracestate`, and pushes per-layer manual threading. Rejected.
- **Adopt the full OpenTelemetry SDK on both sides (TracerProvider +
  OTLP exporter + Tempo)** — out-of-scope for the portfolio since
  ADR-010 explicitly opts out of Jaeger/Tempo for the dev stack and Seq
  is the chosen sink. OTel *API* (propagation only) is what the Go
  façade pulls in; the .NET side uses the strictly-built-in
  `System.Diagnostics` instead of adding the OTel .NET SDK package.
- **Use a third-party NuGet like `Serilog.Enrichers.Destructuring`*
  style wrappers — adds deps for a problem the BCL already solves.
</content>
</invoke>