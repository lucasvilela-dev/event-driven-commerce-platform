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

## 2026-08-07 — Use `.slnx` for new service solutions; ignore `.DotSettings.user`

Context: two repo-hygiene items surfaced while reviewing the Product
service. (1) `Product.sln.DotSettings.user` was present on disk — it's
a JetBrains Rider **per-user** settings file (machine/AppData-cache
paths inside) and should never be tracked; only the team-shared
`Product.sln.DotSettings` belongs in source control. The existing root
`.gitignore` already had a broad `*.user` glob which technically
covered it, but we wanted the more explicit Rider/ReSharper patterns
(`*.sln.DotSettings.user`, `*.DotSettings.user`, `**/obj/rider.*.info`)
so the intent is self-documenting. (2) The repo currently uses the
classic `.sln` format for every service solution; .NET 10 shipped a
new XML-based `.slnx` solution format that is diff-friendly,
mergeable, and free of GUID/configuration-platform boilerplate —
better suited for a polyrepo-style monorepo where each service has
its own solution.

Decision:
- Extend the existing root `.gitignore` with explicit JetBrains
  Rider / ReSharper patterns: `*.DotSettings.user`,
  `*.sln.DotSettings.user`, `**/obj/rider.*.info`. The existing broad
  `*.user` stays as a backstop. `Product.sln.DotSettings.user` is
  untracked (it was never committed) — confirm it stays so.
- Adopt `.slnx` as the convention for **any new service solution**
  scaffolded from now on (Order, Payment, Inventory, Shipping,
  Notification — none exist yet). Use `dotnet new slnx` to create it.
- Leave the existing service solutions (`Identity.sln`, `Product.sln`)
  on the legacy `.sln` format to avoid churn; migrate them later with
  `dotnet sln migrate` only if there's a compelling reason (e.g. a
  tricky merge conflict on the binary-ish `.sln`).
- Both formats coexist fine in the IDE/tooling — no forced migration.

Alternatives considered:
- Migrate `Identity.sln` and `Product.sln` to `.slnx` right now for
  consistency — rejected: pure churn, no functional gain this session,
  and touches working solutions we just validated.
- Leave the `.gitignore` as the single broad `*.user` glob — rejected:
  the explicit Rider patterns make the intent legible to future agents
  and reviewers.
- Make `.slnx` mandatory for ALL solutions including existing ones —
  rejected: the legacy `.sln` works and migration is optional per
  Microsoft's own guidance.

Consequences:
- `Product.sln.DotSettings.user` remains untracked (it never was);
  the explicit ignore makes the rule future-proof against broad-glob
  edits.
- Future agents scaffolding a new service must use `dotnet new slnx`
  (not `dotnet new sln`), and add projects with the same
  `dotnet sln add <proj.csproj>` command — the CLI detects the extension.
- The monorepo now has a documented convention: new solutions = `.slnx`,
  existing solutions = `.sln` until explicitly migrated.
- IDE support is verified for VS 2022 17.14+, Rider 2024.3+,
  VS Code C# Dev Kit — all current at project-start time.

Follow-up: document the `.slnx` choice in each new service's AGENTS.md
when it's scaffolded.

---

## 2026-08-03 — Phase 2 Identity feature-complete; runbook + AGENTS kid-note fix

Context: Phase 2 step 3 had landed on `feat/identity-scaffold` and was
pushed, but the Testcontainers integration suite had never run locally
(Docker Desktop was down at the previous session's end) and the
`docs/runbook/identity.md` deliverable for Phase 2 was still missing.
A stale `kid` description in `src/services/identity/AGENTS.md` was
also noticed (it referenced a static `Jwt:KeyId` config that no longer
exists — the `kid` is the RFC 7638 SPKI SHA-1 thumbprint computed by
`SigningKeyProvider`).

Decision: ran the full Testcontainers suite locally (Docker Desktop up,
`postgres:16-alpine`) — 5/5 integration + 6/6 unit green; wrote
`docs/runbook/identity.md` (env vars, signing-key generation, JWKS
rotation procedure, downstream `AddJwtBearer` validation pattern, the
deferred `/refresh` caveat); corrected the AGENTS.md `kid` note to
match the code; left ADR-014 unchanged as an immutable record.

Alternatives considered: defer the runbook until the PR merge (rejected
— Phase 2 acceptance criteria list it as a deliverable); edit ADR-014
in place (rejected — ADRs are immutable; the divergence is captured
in `status.md` and runbook).

Consequences: Phase 2 is feature-complete; only the deliverable PR
(`gh auth login` then `gh pr create`) remains before moving to Phase 3
(Product CQRS scaffold). Future agents reading `AGENTS.md` get the
correct `kid` derivation.

Follow-up: open the PR `feat/identity-scaffold → develop`; after merge
move Phase 2 to "What's done" in `status.md` and begin Phase 3
Product scaffold.

---

## 2026-08-03 — Product C# aggregate root renamed to `ProductAggregate`

Context: Phase 3 Product scaffold hit a C# name collision — the
project root namespace `Product.X` (per `<RootNamespace>Product.X</RootNamespace>`)
makes the bare name `Product` resolve to the *namespace* `Product`,
not the entity class `Product.Domain.Entities.Product`. References to
the unqualified entity type failed with CS0118 ("Product is a
namespace, but is used as a type"). Tried a project-level
`<Using Include="..." Alias="Product" />` — generated CS0576 (alias
conflicts with the global namespace `Product`).

Decision: rename the C# aggregate-root class `Product` →
`ProductAggregate` (`src/services/product/Product.Domain/Entities/
Product.cs`). The Avro records keep their canonical names
(`ProductCreated`, `ProductPriceUpdated`, `ProductActivated`,
`ProductDeactivated`), and prose / ADRs keep "the Product aggregate"
— the rename is C#-internal. Same trick Identity used with
`ApplicationUser` vs the `Identity.X` namespace root.

Alternatives considered:
- Project-level `<Using Alias="Product">` — rejected (CS0576 alias
  vs namespace conflict at global scope).
- Rename the root namespace to `Edcp.Product.X` — rejected: the
  sub-namespace segment `Product` still collides with the entity
  bare name in any `using Edcp.Product.Domain.Entities;` consumer.
- Use a fully-qualified `Product.Domain.Entities.Product` everywhere
  — rejected: noisy, easy to forget, breaks with `using`.

Consequences:
- Code reads `ProductAggregate.CreateNew` / `ProductAggregate.UpdatePrice`.
- DbSet stays `Products` and table stays `products`; only the C#
  class name changes. Avro / Kafka topics unaffected.
- The next aggregate root that collides with a namespace segment
  (e.g. Order, Payment, Inventory) will follow the same rename
  pattern; document the precedent in the service AGENTS.md.

Follow-up: documented in `src/services/product/AGENTS.md` "Aggregate
root class name" section.

---

## 2026-08-03 — EF migrations placed at `Persistence/Migrations/` via CLI flags

Context: `dotnet ef migrations add InitialProduct` (without flags)
placed the migration at `<ProjectRoot>/Migrations/` with namespace
`Product.Infrastructure.Migrations`. Identity's migrations live at
`Identity.Infrastructure/Persistence/Migrations/` with namespace
`Identity.Infrastructure.Persistence.Migrations` (matching the
DbContext namespace), giving a coherent folder layout.

Decision: regenerate with explicit
`--namespace Product.Infrastructure.Persistence.Migrations
--output-dir Persistence/Migrations`, matching Identity exactly.
Apply the same flags when scaffolding EF migrations for any service
whose DbContext does NOT live at the project root folder.

Alternatives considered:
- Accept the default `Migrations/` at project root for Product and
  accept the folder drift between services — rejected (one-folder
  drift is annoying in code review and Docker/CLI commands).
- Change `<RootNamespace>` to include `Persistence` — rejected:
  changes every namespace in the project for EF migration cosmetic
  convenience; net negative.

Follow-up: documented in the service `AGENTS.md` EF Core paragraphs.

---

## 2026-08-03 — Product events: 1:1 file↔topic↔subject (NOT a union)

Context: Phase 3 produces the first Product Kafka events. ADR-005
establishes `order.events` as a single-topic multi-event stream
registered under Avro `union[...]` subject `order.events-value`. The
`decisions-log.md` 2026-07-26 entry "Model `order.events` as a single
Avro union subject" codifies that call but does not pin the policy
for the OTHER aggregates. Need a rule for Product events.

Decision: Product stays **1:1 file↔topic↔subject**, matching the
existing inventory / payment / shipping / notification pattern (see
the contracts/README map table): one event type per topic, one
subject per topic. Concretely:
- `product_created_v1.avsc`     → topic `product.events.created`
                                  → subject `product.events.created-value`
- `product_price_updated_v1.avsc` → `product.events.price-updated` → ...`-value`
- `product_activated_v1.avsc`   → `product.events.activated` → ...
- `product_deactivated_v1.avsc` → `product.events.deactivated` → ...

The `com.edcp.product.events` Avro namespace is shared across all
four records; only Order uses the single-union-subject form because
its event stream is consumed as a single append-only log (ADR-005).
Product / Inventory / Payment / Shipping / Notification each have
only **a handful of event types per aggregate** and project consumers
pick topics deliberately rather than as a single stream, so per-event
topics read better and wire to cleaner consumer group topologies.

Alternatives considered:
- A single `product.events` union subject mirroring Order — rejected:
  Product events are independent projection inputs, not an event-
  sourced aggregate log; the union model brings no consumer benefit.
- One topic per aggregate plus a generic `ProductEvent` payload with
  `event_type` enum — rejected: conflates wire contracts with internal
  domain polymorphism, harder to reason about in Schema Registry /
  backwards-compat review.

Consequences:
- Adding a new Product event type = adding a new `.avsc`, a new row
  to the contracts/README map, and a new topic — independent
  subject, independent compatibility check.
- The Product projection consumer subscribes to all four topics
  (one consumer group `product-projection`).
- The Order pattern stays the ONLY union subject; document this so
  nobody later tries to unify Product the same way just because
  Order did it.

Follow-up: contracts/README.md map table updated; this entry codifies
the rule so future aggregates get the same per-event-topic treatment
unless an ADR-005-style event-sourcing requirement forces a union.

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