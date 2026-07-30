# Decisions log

> Tactical, day-to-day decisions during implementation. Anything that justifies
> *why* a particular library, format, version, or approach was chosen at a
> given moment. Architectural decisions go in `docs/adr/`; the long-term plan
> goes in `docs/roadmap.md`; the current state goes in `docs/status.md`.
>
> One entry per decision. Newest at the top. Format:
>
> ```
> ## YYYY-MM-DD — <short title>
> Context: <why we had to decide>
> Decision: <what we chose>
> Alternatives considered: <short list>
> Consequences: <what this enables / blocks>
> Follow-up: <optional — ADR link, ticket, etc.>
> ```

---

## 2026-07-27 — Identity approach: hand-rolled JWT issuer (ADR-014)

Context: Phase 2 needed an Identity approach before scaffolding.
Phase 2 acceptance criteria (`Register`/`Login`/`/jwks`, EF migration
with `users`/`roles`/`user_roles`, JWT with `sub`/`email`/`roles`,
15min access + 7d refresh) read like a bespoke service, not an OIDC
provider.

Decision: hand-rolled JWT issuer. RS256 signing key from PEM file in
dev (`%USERPROFILE%/.edcp/identity-signing-key.pem`), mounted secret
in compose. `ITokenIssuer` in `Identity.Application`,
`SigningKeyProvider` in `Identity.Infrastructure`. Password hashing
delegates to ASP.NET Identity's `IPasswordHasher<T>` (no
`IdentityDbContext`). Minimal `/.well-known/openid-configuration`
(non-conformant — only JWKS URI + issuer). `JwtBearer.Authority`
pointed at the Identity service for downstream validators.

Alternatives considered: Duende IdentityServer (rejected — feature-set
mismatch, heavy Duende-internal data model, no OAuth clients in this
platform), OpenIddict (same mismatch), Auth0 (out-of-scope SaaS
dependency), HMAC symmetric JWT (rejected — no JWKS, contradicts
acceptance criteria).

Consequences: schema minimal and explicit; Clean Architecture layering
exercised end-to-end; full control over key rotation. Trade-off: no
real OIDC `/authorize` flow — revisit with a superseding ADR if a SPA
with PKCE ever appears. Refresh-token rotation deferred to a later
phase.

Follow-up: ADR-014 written and accepted; ADR index updated; scaffold
the 4 Clean Architecture projects as Phase 2 step 2.

---

## 2026-07-26 — Adopt native distributed-tracing propagators (ADR-015, amends ADR-010)

Context: the initial Phase 1 implementation of ADR-010 hand-rolled a
W3C `TraceParent` parser/encoder in both `.NET` (~150 LOC + 20 tests)
and `Go` (~150 LOC + 12 tests) under `src/shared/observability/`.
After landing it we realised both language runtimes ship W3C Trace
Context propagators out-of-the-box
(`System.Diagnostics.DistributedContextPropagator` on .NET,
`go.opentelemetry.io/otel/propagation.TraceContext` on Go) and that
re-implementing it duplicates standard logic, risks drifting on
edge cases (`tracestate`, baggage, format evolution), and forces
each code layer to thread a manual `TraceParent` struct instead of
relying on the ambient `Activity.Current` / `trace.SpanFromContext`.

Decision: replace the hand-rolled `TraceParent` helpers with thin
carrier adapters over the standard propagators and a façade on each
side. The architectural decision in ADR-010 (Seq sink + W3C Trace
Context over Kafka headers) is unchanged; only its *implementation*
section is amended, recorded as **ADR-015** with `Amends: ADR-010`.
The repository ADR index mark ADR-010 as "Accepted (impl. amended by
ADR-015)". New API symmetry is behavioural, not type-symmetric:

- `.NET`: `ActivityContext?` for parsed values, `Activity` for spans,
  Serilog `WithActivityTrace()` enricher reads `Activity.Current`.
- `Go`: `trace.SpanContext` for parsed values, ambient span via
  `trace.SpanContextFromContext(ctx)`, services wire zerolog fields
  manually via `sc.TraceID().String()`.

Alternatives considered:
- Keep the hand-rolled `TraceParent` (rejected — duplicate work).
- Adopt full OpenTelemetry SDK + OTLP + Jaeger/Tempo on both sides
  (rejected — out-of-scope per ADR-010; Seq stays the only sink;
  OTel is *API*-only on the Go side, and BCL-only on .NET).
- Pin OTel `v1.44.0` on Go (rejected — requires Go 1.25, project
  targets 1.24.1 per `docs/status.md`).

Consequences:
- ~250 LOC of hand-rolled spec + tests deleted on each side; new
  surface roughly halves on the .NET side (8 helpers on top of
  BCL) and stays minimal on the Go side (carriers + façade).
- HTTP inbound propagation becomes automatic in ASP.NET Core
  (YARP) — no per-action `ReadFromHeaders` plumbing.
- `tracestate` is no longer dropped, for free, on both sides.
- One-line bootstrap added at .NET service startup
  (`TraceContext.AlwaysSample()` — keeps `ActivitySource` enabled
  without an OTLP exporter).
- The two helpers no longer expose identical *types* (`TraceParent`
  gone); README symmetry table updated to the behavioural level.
  On-the-wire format identical → cross-service correlation in Seq
  unaffected.
- The `dotnet` build emits two CS8622 nullable-warning suppressions
  around the propagator callback lambdas — localised `#pragma
  warning disable CS8622` blocks, no functional impact.

Follow-up:
- ADR-015 written and accepted (`docs/adr/ADR-015-native-distributed-
  tracing-propagators.md`); ADR index updated.
- `src/shared/AGENTS.md` and `src/shared/observability/README.md`
  reflect the new layering.
- Each .NET service scaffolded in later phases must call
  `TraceContext.AlwaysSample()` once at startup; document in the
  service AGENTS.md when that service is scaffolded.
- Go services, when scaffolded, must add a `replace` directive in
  `go.mod` for the observability module (unchanged from before).

---

## 2026-07-26 — Do not pre-register schemas in Schema Registry

Context: the Phase 1 roadmap lists "register schemas in the local
Schema Registry under `<topic>-value` with BACKWARD compatibility" as
an acceptance criterion. We tried to pre-register them manually from
the `.avsc` files before any producer exists — but the registration
calls hung intermittently from PowerShell (likely a console/encoding
issue with the curl.exe + pipe combo on Windows), wasting session time.

Decision: treat the `.avsc` files in `src/shared/contracts/` as the
authoritative schema source in git, and rely on the producers'
**auto-registration** (`AvroSerializer` with `auto.register.schemas=true`,
the Confluent default) to push each schema to the Registry the first
time a message is produced. Manual registration is reserved for prod,
where we want explicit, auditable schema versions.

Alternatives considered:
- Keep fighting the PowerShell/curl manual script — no value before
  there are producers to test with.
- Pre-validate via a CI script that POSTs each schema in CI only
  (Phase 10) — keep that as the validation gate.

Consequences:
- Phase 1 "registered" acceptance criterion is deferred to Phase 3
  (first producer = Product Service). That's fine — the criterion is
  really "schemas are valid Avro + register correctly when used".
- Schemas already validated as JSON; Avro-type validity will be
  proven the first time a producer registers them (errors surface
  then). A CI compatibility step is added in Phase 10.
- The `contracts/README.md` "Registering a new schema (dev)" section
  remains as the manual procedure for when it's actually needed.

Follow-up: add a compat-check CI job in Phase 10 that POSTs each
`.avsc` against `POST /compatibility/subjects/<subject>/versions/latest`.

---

## 2026-07-26 — Model `order.events` as a single Avro union subject

Context: the root `AGENTS.md` and ADR-005 treat `order.events` as the
Order aggregate's event stream (one topic, multiple event types:
`order_created`, `order_cancelled`, `order_saga_step_completed`, ...).
At Schema Registry registration time there is a choice: one subject for
the whole topic with a union, or one subject per event type (splitting
the topic into `order.events.created`, `order.events.cancelled`, ...).

Decision: register a single subject `order.events-value` whose schema is an
Avro `union[...]` of the FQNs of each event record. The union lives in
`src/shared/contracts/order/order_events_value_v1.avsc`; the individual
event records (`order_created_v1.avsc`, etc.) are co-located for human
navigation but only the union is registered as the subject.

Alternatives considered:
- Split into per-event topics (`order.events.created`, ...). Cleaner per
  subject, but contradicts the AGENTS root that cites `order.events` as
  the singular Order stream / Saga log, and would require revising
  ADR-005 and the root `AGENTS.md`.
- Wrapping envelope record with `event_type: enum` + `payload: union`.
  Gives an explicit discriminator in the payload but adds nesting and
  makes the message non-self-describing at the top level.

Consequences:
- Adding a new Order event type = adding a branch to the union; passes
  BACKWARD compatibility (new reader decodes old data fine).
- Consumers must pattern-match on the union branch index/named record;
  `Confluent.SchemaRegistry.Serdes.Avro` (.NET) returns `ISpecificRecord`
  and goavro returns a typed map keyed by the active branch name.
- Subject count stays low (1 per topic instead of 1 per event type),
  making the registry easier to navigate.
- Other aggregates' events (`inventory.events.reserved`,
  `payment.events.approved`, ...) stay 1:1 file↔topic↔subject — single
  event type per topic, no union needed.

Follow-up: documented in `src/shared/contracts/README.md` ("Order stream
modelling"). No ADR change needed — this realizes ADR-005/ADR-007 without
contradicting them.

---

## 2026-07-25 — Adopt roadmap-driven development

Context: the project will be built over many sessions, possibly with different
LLMs / context windows. Without a written plan and a current-state file, each
new session reinvents context.

Decision: keep three living docs — `docs/roadmap.md` (the plan, phase by
phase), `docs/status.md` (where we are right now), and this decisions log
(tactical twists). Root `AGENTS.md` instructs every agent to read them
before touching code.

Alternatives considered: keep everything in commit messages; put state in a
single `TODO.md`. Both lost context too easily across sessions.

Consequences: every new agent starts with full context in three files. Cost
is the discipline of keeping `status.md` updated at the end of each session.

Follow-up: none.

---

## 2026-07-25 — Target .NET 10 instead of .NET 8

Context: initial draft of the ADRs and AGENTS files referenced .NET 8. The
user wants .NET 10 (current preview/stable line at project start).

Decision: replace all references from `.NET 8` to `.NET 10` and `net8.0` to
`net10.0` across ADRs, README, and AGENTS files.

Alternatives considered: stay on .NET 8 (LTS, more stable); use .NET 9.
.NET 10 was chosen to keep the portfolio visually current.

Consequences: any future `dotnet new` must use `--framework net10.0`. If
.NET 10 is still preview at install time, document the preview SDK in
`docs/runbook/dev-setup.md` (to be written in Phase 1 or 2).

Follow-up: when the .NET 10 SDK is installed locally, run `dotnet --version`
and pin it in `docs/status.md`.

---

<!-- Template for next entries:

## YYYY-MM-DD — <short title>

Context:
Decision:
Alternatives considered:
Consequences:
Follow-up:

-->