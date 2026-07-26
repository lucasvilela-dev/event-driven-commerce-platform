# Shared observability helpers (`src/shared/observability/`)

Cross-cutting helpers for **W3C Trace Context** propagation across HTTP and
Kafka boundaries (ADR-010, implementation amended by ADR-015). No business
logic, no logger configuration — those still belong to each service.

## Layout

```
observability/
  dotnet/
    OpenCode.TraceContext/                ← library (.NET 10, BCL only)
      TraceContext.cs                     ← façade: Read/Write, StartChild, AlwaysSample
      HttpHeaderCarrier.cs                ← carrier adapter for header dictionaries
      KafkaHeaderCarrier.cs               ← carrier adapter for Kafka KV<byte[]> headers
      ActivityEnricher.cs                 ← Serilog enricher reading Activity.Current
      README.md
    OpenCode.TraceContext.Tests/          ← xUnit
    OpenCode.TraceContext.sln
  go/
    go.mod                                ← module: github.com/lucasvilela-dev/edcp/shared/observability
    tracecontext/
      tracecontext.go                     ← façade: From/Into HTTP and Kafka, ctx helpers
      http_carrier.go                     ← net/http.Header carrier
      kafka_carrier.go                    ← []KV carrier + NewRootSpanContext
      tracecontext_test.go
```

## Design (ADR-015)

Propagation is delegated to each runtime's native W3C propagator — no
hand-rolled `TraceParent` parser. The shared package only provides thin
carrier adapters + a façade.

| Runtime | Propagator used | Dependency |
|---|---|---|
| .NET 10 | `System.Diagnostics.DistributedContextPropagator` (default = `W3CPropagator`), `ActivitySource` / `Activity` for spans | none — BCL only |
| Go 1.24 | `go.opentelemetry.io/otel/propagation.TraceContext` | `go.opentelemetry.io/otel` v1.35.0 (+ `.../otel/trace` v1.35.0) |

Symmetric behavioural API (the on-the-wire format is identical on both
sides):

| Concern                                | .NET                                                | Go                                                    |
|----------------------------------------|-----------------------------------------------------|-------------------------------------------------------|
| Parsed value                           | `ActivityContext` (struct, BCL)                   | `trace.SpanContext` (struct, OTel)                   |
| Read from HTTP headers                 | `TraceContext.ReadFromHttpHeaders(dict)`            | `FromHTTPHeaders(ctx, http.Header)`                  |
| Write to HTTP headers                  | `TraceContext.WriteToHttpHeaders(activity, dict)`   | `IntoHTTPHeaders(ctx, sc, http.Header)`              |
| Read from Kafka headers                | `TraceContext.ReadFromKafkaHeaders(KV<…,byte[]>)`   | `FromKafkaHeaders(ctx, []KV)`                         |
| Write to Kafka headers                 | `TraceContext.WriteToKafkaHeaders(activity, KV<…>)` | `IntoKafkaHeaders(ctx, sc, *[]KV)`                   |
| Start child span from inbound          | `TraceContext.StartChildFromHttpHeaders(...)`, `…FromKafkaHeaders(...)` | (use a tracer; SpanContext carries only) |
| Build root span (CLI/workers)          | `TraceContext.StartChild(name)` on a fresh `ActivitySource` | `NewRootSpanContext()` then `ContextWithSpanContext(ctx, sc)` |
| Bind to context (Go) / ambient (.NET) | `Activity.Current` (BCL ambient)                   | `trace.SpanContextFromContext(ctx)`                  |
| Logger enrichment (.NET)               | `LoggerEnrichmentConfiguration.WithActivityTrace()` (reads `Activity.Current`) | n/a (zerolog `log.With().Str("trace_id", sc.TraceID().String())...` in each service) |
| Begin-of-process sampling (.NET)       | `TraceContext.AlwaysSample()` (registers `ActivityListener`) returns `IDisposable` | n/a (OTel SDK handles sampling; default noop samples valid span contexts) |

The W3C invariants (version `00`, trace id 16 bytes hex, span id 8 bytes
hex, flags 1 byte) are enforced by the standard propagator. `ReadFrom*`/`From*`
return `null (ActivityContext?)` / `(sc, false)` on missing or malformed
input — never throws on consumer payload.

`tracestate` is propagated transparently on both sides (a freebie over
ADR-010's hand-rolled helper, which only carried `traceparent`).

## How services consume

### .NET (Identity/Product/Order/Payment + API Gateway)

- Program startup calls once:
  ```csharp
  using OpenCode.TraceContext;
  _ = TraceContext.AlwaysSample();        // keep reference for process lifetime
  ```
- HTTP inbound: in API Gateway (YARP), ASP.NET Core already creates an
  ambient `Activity` on each request and propagates the downstream
  `traceparent` automatically via HttpClient. Where manual extraction is
  needed (e.g. custom middleware), call
  `TraceContext.ReadFromHttpHeaders(headers)` and
  `TraceContext.StartChildFromHttpHeaders(...)`.
- HTTP / Kafka outbound: `TraceContext.WriteToHttpHeaders(Activity.Current, headers)` /
  `TraceContext.WriteToKafkaHeaders(Activity.Current, headers)`.
- Serilog setup:
  ```csharp
  Log.Logger = new LoggerConfiguration()
      .Enrich.WithActivityTrace()        // pushes TraceId / SpanId / ParentSpanId
      .WriteTo.Seq("http://localhost:8082")
      .CreateLogger();
  ```

### Go (Inventory/Notification/Shipping)

- `context.Context` carries the `trace.SpanContext` (via
  `trace.ContextWithRemoteSpanContext`). Use the façade:
  ```go
  sc, ok := tracecontext.FromKafkaHeaders(ctx, kHeaders)
  if ok { ctx = tracecontext.ContextWithSpanContext(ctx, sc) }

  log := zerolog.New(out).With().
      Str("trace_id", sc.TraceID().String()).
      Str("span_id",  sc.SpanID().String()).
      Logger()

  // outbound:
  var kOut []tracecontext.KV
  tracecontext.IntoKafkaHeaders(ctx, sc, &kOut)
  ```
- For a worker / scheduled job that starts a flow:
  ```go
  sc := tracecontext.NewRootSpanContext()
  ctx := tracecontext.ContextWithSpanContext(context.Background(), sc)
  ```

## Cross-module dependency in Go dev

The Go module lives at `github.com/lucasvilela-dev/edcp/shared/observability`.
Each Go service will add a `replace` directive in its `go.mod` pointing
at `../../shared/observability/go` until a proper version tag is cut
(likely never for this portfolio repo; documented here so future agents
don't break their head over it).

## Test coverage

- **.NET:** 12 xUnit tests covering HTTP/Kafka round-trip, case-insensitive
  HTTP read, missing/garbage handling, `tracestate` propagation,
  `AlwaysSample` listener wiring, `StartChildFromHttpHeaders` linking,
  and the ActivityEnricher (active/inactive).
- **Go:** 8 tests mirroring the .NET cases (HTTP/Kafka round-trip,
  case-insensitive, missing/garbage, root span context, ctx round-trip).
  `go vet` clean, ~72% statement coverage.