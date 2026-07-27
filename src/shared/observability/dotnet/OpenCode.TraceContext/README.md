# OpenCode.TraceContext

W3C Trace Context (`traceparent`/`tracestate`) propagation helpers for .NET 10.
Implements the **amended** implementation of **ADR-010** as described in
**ADR-015**: thin carriers over `System.Diagnostics.DistributedContextPropagator`
+ an `ActivitySource` for spans — no custom `TraceParent` parser.

## API surface

- `TraceContext.ReadFromHttpHeaders(IReadOnlyDictionary<string,string?>) -> ActivityContext?`
  Extract a parent context from inbound HTTP headers.
- `TraceContext.WriteToHttpHeaders(Activity, IDictionary<string,string?>)`
  Inject the current activity onto outbound HTTP headers.
- `TraceContext.ReadFromKafkaHeaders(IReadOnlyCollection<KV<string,byte[]>>) -> ActivityContext?`
  Same for Kafka message headers.
- `TraceContext.WriteToKafkaHeaders(Activity, ICollection<KV<string,byte[]>>)`
  Same outbound.
- `TraceContext.Source` — the shared `ActivitySource` (`"OpenCode.TraceContext"`).
- `TraceContext.StartChild(name, kind)` — opens a child span of `Activity.Current`.
- `TraceContext.StartChildFromHttpHeaders(name, headers, kind = Server)`
- `TraceContext.StartChildFromKafkaHeaders(name, headers, kind = Consumer)`
- `TraceContext.AlwaysSample() -> IDisposable` — registers an `ActivityListener`
  that samples every span; required because we have no OTLP exporter, so
  `ActivitySource.StartActivity` returns null by default for unenabled sources.
  Services hold the returned `IDisposable` for the process lifetime.
- `LoggerEnricherConfigurationExtensions.WithActivityTrace()` — Serilog
  enricher that copies `Activity.Current.TraceId`/`SpanId`/`ParentSpanId`
  onto every log event.
- `TraceContextLoggerProperties` — public property-name constants
  (`TraceId`, `SpanId`, `ParentSpanId`) consumed by Seq queries.

## Behavioural notes

- The on-the-wire format is W3C Trace Context (`00-<traceId>-<spanId>-<flags>`).
- Case-insensitive header read (handled by `DistributedContextPropagator`).
- `tracestate` is propagated alongside `traceparent` automatically.
- `ReadFrom*` returns `null` on missing/invalid input — never throws.
- `Activity.Current?.Id` is the W3C traceparent string an activity emits
  when running under the default `W3CPropagator`. Use `activity.Id` directly
  when you need to log it.

## Dependencies

- BCL only — no extra NuGet packages are required.
- `Serilog` is the only direct dependency (used by the enricher); it is a
  project reference already, so callers don't have to add it.
</content>