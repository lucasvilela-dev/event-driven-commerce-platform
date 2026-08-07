# Status

> **Living document.** Updated at the end of every work session.
> Read this *before* `docs/roadmap.md` to know where we are right now.
> Keep entries short — full reasoning lives in the ADRs, full plan lives in
> the roadmap, tactical twists live in `docs/decisions-log.md`.

## Current phase

**Phase 3 — Product catalog (CQRS + Redis read models)** (`[next]` in the
roadmap, after Phase 2 feature-complete).

Start date: 2026-08-03.
Active step: **Write side complete** — Product Clean Architecture
scaffold (6 projects + sln) landed; 4 Avro schemas for
`product.events.*` added to `src/shared/contracts/product/`; domain
`ProductAggregate` (`Money` VO) + the four CQRS commands
(`CreateProduct` / `UpdatePrice` / `ActivateProduct` /
`DeactivateProduct`) wired as MediatR handlers that persist `products`
**AND** enqueue an `outbox` row in the SAME EF Core transaction
(ADR-004, verified against the live `product` DB); EF migration
`InitialProduct` applied locally — `products` + `outbox` tables
created; live API smoke test at `https://localhost:5002` green for
create, duplicate-SKU 409, negative-price 400. Remaining Phase 3
work: outbox publisher + Avro producer (with `traceparent` header),
projection worker (Redis read models + idempotency), read side
(Redis-first GET with PG fallback), unit + Testcontainers
integration tests, runbook.

## What's done

- ✅ **Phase 0 — Platform stack** (`2026-07-25`)
  - `docker-compose.yml` with Kafka (KRaft), Schema Registry, Postgres (5
    DBs), Redis, Seq, Kafka UI — all healthy.
  - Postgres host port remapped to `15432` to avoid local conflict.
  - Seq no-auth enabled in dev (`SEQ_FIRSTRUN_NOAUTHENTICATION=true`).
  - ADRs 001-010 written and accepted; ADR-011/012/013 placeholders in the
    index for CI, tests, K8s.
  - Root `AGENTS.md` + one `AGENTS.md` per service (api-gateway, identity,
    product, order, payment, inventory, notification, shipping, shared).
  - Stack choice: .NET 10 for .NET services (updated from 8 on
    `2026-07-25`), Go for lightweight consumers.
  - Repo initialized on `develop` branch:
    https://github.com/lucasvilela-dev/event-driven-commerce-platform
    (commit `411a39e`).

- ✅ **Phase 1 — Shared contracts & observability helpers** (`2026-07-26`)
  - 18 v1 Avro schemas written under `src/shared/contracts/`:
    12 events + 5 commands + 1 `order.events-value` union (3 Order event
    types). All validate as JSON.
  - `src/shared/contracts/README.md` with the schema → topic → subject
    → producer/consumer map, envelope conventions, money/timestamp rules,
    BACKWARD evolution cheat-sheet, and the manual registration snippet.
  - `order.events` modelled as a single Avro union subject
    (`order_events_value_v1.avsc`); other aggregates' topics stay 1:1
    file↔topic↔subject. Decision logged.
  - **Schema Registry registration deferred to first producer** — manual
    pre-registration has no value (Confluent auto-registers on first
    produce with `auto.register.schemas=true`). CI compat-check job
    planned for Phase 10. Decision logged.
  - `OpenCode.TraceContext` (.NET 10) at
    `src/shared/observability/dotnet/OpenCode.TraceContext/`:
    thin carriers over `System.Diagnostics.DistributedContextPropagator`
    + an `ActivitySource`; `ReadFrom/WriteTo HTTP|Kafka`,
    `StartChild[FromHttp|FromKafka]Headers`, `AlwaysSample()`
    (registers an `ActivityListener` for no-OTLP Seq-only setup), and a
    Serilog `WithActivityTrace()` enricher reading `Activity.Current`.
    BCL only — no extra NuGet. 12 xUnit tests passing.
  - Go `tracecontext` package at
    `src/shared/observability/go/tracecontext/` (module
    `github.com/lucasvilela-dev/edcp/shared/observability`):
    thin carriers over `go.opentelemetry.io/otel/propagation.TraceContext`
    (v1.35.0, Go-1.24-compatible). `From/Into HTTP|Kafka Headers`,
    `SpanContextFromContext`, `ContextWithSpanContext`,
    `NewRootSpanContext`. `go vet` clean, ~72% coverage.
  - `src/shared/observability/README.md` with API symmetry table and
    service-consumption guidance.

- ✅ **Phase 2 — Identity service** (`2026-07-27 → 2026-08-03`)
  - All Phase 2 acceptance criteria pass: `POST /api/identity/register`
    + `/login` return access + refresh JWTs (15m / 7d); claims `sub`,
    `email`, `role`; `/.well-known/openid-configuration` (minimal) +
    `/.well-known/jwks` (single RSA JWK with RFC 7638 `kid`); EF
    migration `InitialIdentity` creates `users`/`roles`/`user_roles`
    in the `identity` DB (seeded `customer`/`admin` roles); Serilog →
    Seq with `ServiceName=identity` + `ActivityEnricher` (ADR-015).
  - Tests: 6 unit (RegisterHandler dup-email + missing-role + happy;
    LoginHandler unknown + wrong-pw + happy) + 5 Testcontainers
    integration (Postgres `16-alpine`; register→dup→login + JWT
    signature validated against the live `/jwks`; 401 paths) —
    **11/11 green** locally with Docker Desktop running; ~8s
    integration suite.
  - Runbook: `docs/runbook/identity.md` (env vars, persistent dev PEM
    key generation, JWKS rotation procedure for a later phase,
    downstream `AddJwtBearer` validation pattern, troubleshooting).
  - ADR-014 (hand-rolled JWT issuer) accepted; service AGENTS.md
    corrected for the RFC 7638 `kid` (the static `Jwt:KeyId` config
    referenced in ADR-014's early draft is gone; `kid` is computed
    from the SPKI SHA-1 thumbprint by `SigningKeyProvider`).
  - **Open deliverable**: PR `feat/identity-scaffold → develop` still
    pending `gh auth login`. Code is feature-complete; only the PR
    opening/merge is left. (User deferred the PR — Phase 3 started
    in parallel.)

## Next 3 concrete steps

1. **Outbox publisher + Kafka Avro producer (Product, ADR-004 +
   ADR-007 + ADR-010).** Add `OutboxPublisherHostedService` in
   `Product.Infrastructure` polling `outbox` with
   `WHERE "PublishedAt" IS NULL ORDER BY "OccurredAt" FOR UPDATE SKIP
   LOCKED LIMIT <batch>` inside a transaction; for each row, build a
   Confluent `GenericRecord` from the stored JSON using the Avro
   schema fetched from Schema Registry (`Confluent.SchemaRegistry.
   Serdes.Avro.AvroSerializer`), publish to `product.events.*` with
   the `traceparent` Kafka header propagated from `Activity.Current`
   via `OpenCode.TraceContext.WriteToKafkaHeaders`, then set
   `PublishedAt = now()` (same transaction). Auto-registration on
   first produce (per `decisions-log.md` 2026-07-26).
2. **Projection worker + read side (Product, ADR-006 + ADR-009).**
   `Product.Projections`: a Kafka Avro consumer subscribing to
   `product.events.*` (one consumer group `product-projection`),
   `SET idempotency:product:{event_id} NX EX 86400` before applying,
   then write Redis hashes `product:{id}` + sorted sets
   `products:featured` (score=priority) / `products:category:{slug}`
   (score=created_at millis) / `products:search:idx` per ADR-006.
   `ProductsQueryController`: `GET /api/products/{id}`,
   `?featured=true`, `?category=...` from Redis; on miss, read from
   Postgres and backfill the read model.
3. **Unit + Testcontainers integration tests + runbook.** Unit tests
   for the four command handlers (product-create happy / dup-SKU 409
   / not-found 404 / invalid-price 400) and the outbox factory (JSON
   shape + topic/partition-key conventions). Testcontainers
   integration: Postgres + Redis + Kafka end-to-end — POST a product
   → outbox row enqueued → publisher pushes Avro to Kafka →
   projection backfills Redis → GET from Redis returns the row
   within `<5ms median` (Phase 3 acceptance criterion). Write
   `docs/runbook/product.md`.

## Blockers

- **`gh` CLI not authenticated** — branch `feat/identity-scaffold`
  was pushed; PR creation pending `gh auth login` (or manual creation
  via the URL GitHub returned on push). All Phase 2 acceptance criteria
  pass; tests are green; the runbook is written — only the deliverable
  PR remains. (User deferred the PR for now; Phase 3 is active.)

## Open questions

- Whether Order's Event Sourcing should use **Marten** or a hand-rolled
  append-only store. Defer to Phase 4; spike both.

## Decisions made this session

- **`Identity.IntegrationTests` validated locally** (`2026-08-03`):
  started Docker Desktop (`docker info` → server `28.0.4`); ran
  `dotnet test --filter IntegrationTests` — 5/5 green in ~8s
  (Testcontainers `postgres:16-alpine`); full suite
  (`dotnet test`) → 6 unit + 5 integration = 11/11 green. The JWT
  validation step (the riskiest piece — load JWKS, build
  `TokenValidationParameters`, `JwtSecurityTokenHandler.ValidateToken`)
  succeeds against the live `/jwks`, asserting `sub`/`email`/
  `role=customer`. No runtime/EOF surprises — Phase 2 implementation
  is feature-complete; only the deliverable PR remains.

- **`docs/runbook/identity.md` written** (`2026-08-03`). Covers:
  what's in/out of Phase 2 scope (incl. the deferred `/refresh`
  rotation caveat), prerequisites, ports, endpoint surface, full
  config table (`ConnectionStrings:Identity`, `ConnectionStrings:Seq`,
  `Jwt:Issuer|Audience|AccessTokenMinutes|RefreshTokenDays|
  SigningKeyPath`), persistent dev PEM key generation snippet, the
  **RFC 7638 `kid` derivation** (computed from SPKI SHA-1 thumbprint,
  not a config string — supersedes the static `Jwt:KeyId` mentioned in
  ADR-014's early draft), JWKS rotation procedure for a later phase,
  local run + smoke-test curl, migration apply/add snippets, unit +
  integration test commands with expected counts (6 / 5 = 11),
  troubleshooting (Docker hang, kid rotating per restart,
  downstream-service validation failure, Seq no logs), and the
  downstream `AddJwtBearer` validation pattern consumed by the future
  API Gateway (Phase 8).

- **Service AGENTS.md `kid` note corrected** (`2026-08-03`): the
  `src/services/identity/AGENTS.md` "Signing key" section still said
  the JWKS `kid` is `Jwt:KeyId` (default `identity-signing-key-v1`) —
  a leftover from the early ADR-014 draft. Replaced with the accurate
  RFC 7638 thumbprint description, with a pointer to the runbook's
  rotation procedure. The `Jwt:KeyId` config key is gone from
  `appsettings.json` and `JwtIssuerOptions`. ADR-014 itself was left
  as-is (it's an immutable record; the divergence is captured in
  `docs/status.md` and the runbook instead).

- **Scope of Phase 2 refresh tokens = issuance only** (option B).
  `/login` returns access + refresh JWTs (15min / 7d). The
  `/api/identity/refresh` endpoint and rotation (the
  `refresh_tokens` table with `revoked_at`/`replaced_by`/`family_id`
  and reuse-detection) are **deferred to a later phase** — Phase 2
  acceptance criteria only require issuance. Deferred work is tracked
  as a future mini-phase or stretch item; revisit after Phase 8
  (Gateway) when the auth surface is exercised end-to-end.

- **Identity approach = hand-rolled JWT issuer** (ADR-014, accepted
  2026-07-27). Duende IdentityServer rejected as feature-set mismatch
  (no `/authorize` code flow, no scopes, no introspection needed) and its
  own data model conflicts with the explicit `users`/`roles`/`user_roles`
  Phase 2 schema. Hand-rolled exercises more Clean Architecture flow
  and gives full control over JWKS rotation. RSA PEM key in dev, mounted
  secret in compose; `ITokenIssuer` abstraction in Application, a
  `SigningKeyProvider` in Infrastructure. Discovery document at
  `/.well-known/openid-configuration` is minimal and non-conformant
  (no `authorization_endpoint`, etc.) — it exists only to bootstrap
  the JWKS URI and issuer for downstream validators.

- **Phase 2 step 3 (Identity service implementation) landed** on
  `feat/identity-scaffold`:
  - `IUserRepository`/`IRoleRepository` implementations over
    `IdentityDbContext` (eager-load `User.Roles.Role`).
  - MediatR handlers: `RegisterHandler` (normalize email → reject
    duplicate → fetch `customer` role → hash pw via
    `IPasswordHasher<ApplicationUser>` → insert → save) and
    `LoginHandler` (lookup → verify → bump `LastLoginAt` → issue
    `TokenPair` via `ITokenIssuer`).
  - `IdentityController` (`POST /api/identity/register` + `/login`)
    with `DuplicateEmailException`→409, `RoleNotFoundException`→500,
    `InvalidCredentialsException`→401 mapping.
  - `DiscoveryController` (`/.well-known/openid-configuration` —
    minimal non-conformant doc: `issuer`, `jwks_uri`,
    `token_endpoint`, `id_token_signing_alg_values_supported=["RS256"]`,
    `subject_types_supported=["public"]`).
  - `JwksController` (`/.well-known/jwks` — single RSA JWK with
    `kid` = RFC 7638 SHA-1 thumbprint of the SPKI, `n`+`e`
    base64url-encoded).
  - `SigningKeyProvider` upgraded to compute RFC 7638 `kid` from the
    RSA public key (no more `Jwt:KeyId` config string). ADR-014
    captured this; `appsettings.json` + `JwtIssuerOptions` cleaned.
  - EF Core migration `InitialIdentity` (via `dotnet ef migrations
    add`): creates `users`/`roles`/`user_roles` tables with FKs +
    indexes + `HasData` seed of `customer`/`admin` roles. DbContext
    wired with `MigrationsAssembly(typeof(IdentityDbContext).Assembly)`.
  - Unit tests (6, xUnit/NSubstitute/FluentAssertions): RegisterHandler
    success + duplicate-email + missing-role; LoginHandler unknown +
    wrong-password + happy-path issues tokens and persists.
    `Identity.Api.Tests` builds and 6/6 pass.
  - Testcontainers integration test (`IdentityEndToEndTests`,
    `IdentityWebFactory: WebApplicationFactory<Program> +
    IAsyncLifetime` with a `postgres:16-alpine` container):
    discovery doc, JWKS single RSA key, register→duplicate-email→login
    round-trip with JWT signature **validated against the live
    `/.well-known/jwks`** and `sub`/`email`/`role=customer` claims
    asserted, wrong-password 401, unknown-user 401. **Skipped locally
    because Docker Desktop is not running — to run, start Docker
    Desktop and `dotnet test --filter IntegrationTests`.**
  - `Identity.Api.Tests` `SmokeTests.cs` removed (replaced by real
    handler tests).

- **Identity scaffold landed** under `src/services/identity/`: 4 Clean
  Architecture projects + `Identity.Api.Tests` (unit) +
  `tests/Identity.IntegrationTests` + `Identity.sln`. EF Core 9 +
  Npgsql 9 + MediatR 12.5 + Serilog 4 + `System.IdentityModel.Tokens.Jwt`
  8.3 + `Microsoft.Extensions.Identity.Core` 9 (IPasswordHasher only —
  no `IdentityDbContext`). Api references `OpenCode.TraceContext`
  (per ADR-015) and calls `TraceContext.AlwaysSample()` at startup;
  Serilog writes to Seq with `ServiceName=identity` +
  `.WithActivityTrace()`. Dev issuer/HTTPS port pinned at
  `https://localhost:5001` (matches `Jwt:Issuer`). Placeholders
  compile-clean: entities (`ApplicationUser`/`Role`/`UserRole`),
  EF configs (tables `users`/`roles`/`user_roles`), `IdentityDbContext`,
  `TokenIssuer`, `PasswordHasherAdapter`, `SigningKeyProvider`,
  `UnitOfWork`, `IUserRepository`/`IRoleRepository`/`IUnitOfWork`
  interfaces, `RegisterCommand`/`LoginCommand` shapes. Identity
  `AGENTS.md` rewritten to reflect ADR-014 (no more Duende conditional).
  **No handlers/controllers wired yet — that's step 3.**

- **Scope of Phase 2 refresh tokens = issuance only** (option B).
  `docs/status.md` (state) + `docs/decisions-log.md` (tactical).
- Adopted **.NET 10** as the target framework (was .NET 8 in initial draft).
- Modelled `order.events` as a single Avro union subject
  (`order.events-value`) rather than splitting into per-event topics
  (`order.events.created`, ...). Individual Order event records live
  alongside the union file for navigation; only the union is
  registered. Other aggregates' event topics stay 1:1 schema↔subject.
- Frozen envelope: events carry `event_id` (UUIDv7), `aggregate_id`,
  `occurred_at` ( millis UTC). Commands carry `command_id`,
  `aggregate_id`, `correlation_id`, `reply_to`, `issued_at`. Money =
  `bytes`/decimal(14,2). `traceparent` is a Kafka header, never in
  the payload (ADR-010).
- Deferred manual Schema-Registry registration to the first producer
  (Confluent auto-registers). CI compat-check deferred to Phase 10.
- `.NET` helper assembly called `OpenCode.TraceContext` (matches the
  name in `src/shared/AGENTS.md`). Go module = `github.com/lucasvilela-dev/
  edcp/shared/observability`, package `tracecontext`. Services will
  consume via project/module reference; Go services add a `replace`
  pointing at `../../shared/observability/go` until a version tag is cut.
- Solution `OpenCode.TraceContext.sln` added at
  `src/shared/observability/dotnet/` to enable `dotnet format`.
- **Refactored observability helpers to native propagators (ADR-015,
  amends ADR-010's *implementation*):** dropped the ~250-LOC
  hand-rolled `TraceParent` parser on both sides; replaced with thin
  carriers over `System.Diagnostics.DistributedContextPropagator` (.NET)
  and `go.opentelemetry.io/otel/propagation.TraceContext` (Go). On the
  wire format unchanged (W3C `traceparent`); `tracestate` now propagated
  automatically. BCL-only on .NET; adds OTel API v1.35 on Go.

- **Phase 3 Product scaffold + write side landed** (`2026-08-03`):
  - Scaffold: `Product.sln` + six projects
    (`Product.Domain` / `.Application` / `.Infrastructure` /
    `.Projections` / `.Api` / tests). Wire `OpenCode.TraceContext`
    project ref + `TraceContext.AlwaysSample()` at startup (ADR-015).
    Serilog → Seq with `ServiceName=product` + `WithActivityTrace()`.
    Dev HTTPS `https://localhost:5002` (Identity uses 5001; no
    collision). `dotnet build` green.
  - C# aggregate root named `ProductAggregate` (mirroring Identity's
    `ApplicationUser`) to avoid the namespace/type collision — the
    project root namespace `Product.X` makes the bare name `Product`
    resolve to the *namespace*, not the entity. The Avro records keep
    their canonical names (`ProductCreated`, etc.) — the rename is
    C#-internal.
  - Domain: `ProductAggregate` + `Money` VO (`Amount` decimal +
    `Currency` 3-char; `Cents` computed). State-based (NOT event
    sourced — that's Order per ADR-005) with `UpdatePrice` /
    `Activate` / `Deactivate` methods; `CreateNew` factory.
  - Application: four MediatR command/handler pairs
    (`CreateProduct` / `UpdatePrice` / `ActivateProduct` /
    `DeactivateProduct`). Each handler persists the `products` row
    **AND** enqueues an `outbox` row in ONE EF Core transaction
    (ADR-004 — both DbContexts share the same context, so
    `IUnitOfWork.SaveChangesAsync` issues a single commit). Outbox
    payload shape produced by `ProductOutboxFactory` (Avro-shaped
    JSON: snake_case keys, `event_id` UUIDv7, `aggregate_id`,
    `occurred_at` millis, money as numeric — Avro-bytes encoding
    happens in the publisher step).
  - Infrastructure: `ProductDbContext` (DbSet products +
    `OutboxMessage`); EF configs for `products` (snake-lowercase table
    names + PascalCase columns, matching Identity) and `outbox`
    (UUID id, jsonb payload, timestamptz occurred/published,
    `RetryCount`); real repos; `IConnectionMultiplexer` registered
    for StackExchange.Redis (ready for the projection step).
  - EF migration `InitialProduct` generated with explicit
    `--namespace Product.Infrastructure.Persistence.Migrations
    --output-dir Persistence/Migrations` (matching Identity's
    layout) — creates `products` + `outbox` tables + indexes
    (unique SKU, category, featured+priority, outbox PublishedAt +
    composite). Applied to the live `product` Postgres DB.
  - Avro schemas added under `src/shared/contracts/product/`:
    `product_created_v1.avsc`, `product_price_updated_v1.avsc`,
    `product_activated_v1.avsc`, `product_deactivated_v1.avsc`.
    Topics `product.events.{created,price-updated,activated,
    deactivated}` (one subject per event type — other aggregates'
    events stay 1:1 file↔topic↔subject; only Order uses a union).
    Contracts README map table updated. (Schema Registry
    auto-registers on first produce — `decisions-log.md` 07-26.)
  - Live smoke test at `https://localhost:5002`: `POST /api/products`
    × 2 returned 201 with the new UUIDv7 product ids
    (`019fc9c6-...`); duplicate SKU → 409; negative price → 400.
    The same-transaction outbox write was verified against the live
    DB — for each created product, a matching `outbox` row was
    committed (`EventType=ProductCreated`,
    `Topic=product.events.created`,
    `PartitionKey=<aggregate_id>`, `PublishedAt=null`,
    `OccurredAt` set, payload JSON shape Avro-correct).

## Stack versions currently in use

| Component | Version | Notes |
|---|---|---|
| .NET | 10 (installed SDK `10.0.100`) | target for `Identity`, `Product`, `Order`, `Payment`, API Gateway, and `OpenCode.TraceContext` helper |
| Go | `1.24.1` (windows/amd64) | target for `Inventory`, `Notification`, `Shipping`, and `tracecontext` helper |
| Serilog | `4.0.0` | pulled by `OpenCode.TraceContext` package |
| OpenTelemetry API (Go) | `go.opentelemetry.io/otel` v1.35.0 + `.../otel/trace` v1.35.0 | propagation only — no SDK / OTLP exporter (ADR-015) |
| Kafka | Confluent `cp-kafka 7.6.1` (KRaft) | single broker dev; 3 brokers prod |
| Schema Registry | `cp-schema-registry 7.6.1` | BACKWARD compatibility default; auto-register on first produce |
| Postgres | `16-alpine` | one DB per service |
| Redis | `7-alpine` | read models + idempotency |
| Seq | `datalust/seq:latest` | no auth in dev |

## How to update this file

At the end of a session:

1. Move the **current phase** to **What's done** with a date stamp if it
   passed all acceptance criteria; otherwise leave it in **Current phase**
   and update the **Active step**.
2. Rewrite **Next 3 concrete steps** from the roadmap.
3. Add any **Decisions made this session**.
4. Append a one-line entry to **Decisions log** (see
   `docs/decisions-log.md`).
5. Do not edit history. Strike out superseded items if you must, but do not
   delete them.