# Status

> **Living document.** Updated at the end of every work session.
> Read this *before* `docs/roadmap.md` to know where we are right now.
> Keep entries short — full reasoning lives in the ADRs, full plan lives in
> the roadmap, tactical twists live in `docs/decisions-log.md`.

## Current phase

**Phase 2 — Identity service** (`[next]` in the roadmap, after Phase 1 done).

Start date: 2026-07-27.
Active step: Phase 2 step 3 committed on `feat/identity-scaffold`
and pushed; PR pending `gh auth login` (Docker Desktop must be
running for the Testcontainers integration test to validate locally
before merge).

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

## Next 3 concrete steps

1. **Run the `Identity.IntegrationTests` Testcontainers suite locally**
   (start Docker Desktop on the dev machine, then
   `dotnet test --filter IntegrationTests`). Confirm register→login
   → JWT-validated-against-`/.well-known/jwks` is green; fix any
   EOF/runtime surprises from the live Postgres + JWT validation
   step before opening the PR.
2. **Authenticate `gh` and open the PR** `feat/identity-scaffold →
   develop` (run `& "$env:LOCALAPPDATA\Programs\gh\gh.exe" auth login`
   once in a terminal with browser access; I'll then call
   `gh pr create --base develop --title "feat(identity): scaffold +
   register/login/jwks (Phase 2, ADR-014)"`). Body to include the
   acceptance-criteria checklist from `docs/roadmap.md` Phase 2.
3. **Write `docs/runbook/identity.md`** — local run (HTTPS
   `https://localhost:5001`, env vars: `Jwt:Issuer`,
   `Jwt:SigningKeyPath`, `ConnectionStrings:Identity`, `Seq` URL),
   signing-key generation snippet, JWKS rotation procedure, and the
   "no /refresh in Phase 2 — refresh JWT issuance only" caveat.
   Closes Phase 2; then move to Phase 3 (Product catalog).
2. **Add `/.well-known/openid-configuration` + `/.well-known/jwks`
   controllers** in `Identity.Api` returning the minimal discovery doc
   and the RSA public key JWK (RFC 7517 + RFC 7638 `kid`).
3. **Create the EF Core migration** (`dotnet ef migrations add
   InitialIdentity`) producing `users`/`roles`/`user_roles`, plus a
   seed of the `customer`/`admin` roles; add a Testcontainers-based
   integration test that runs register → login → JWT signature
   validation against `/jwks`.

## Blockers

- **Docker Desktop is not running** on the dev machine — the
  `Identity.IntegrationTests` Testcontainers suite (register →
  login → JWT-validation-against-`/.well-known/jwks`) cannot execute
  locally until Docker Desktop is started. Unit tests pass (6/6).
- **`gh` CLI not authenticated** — branch `feat/identity-scaffold`
  was pushed; PR creation pending `gh auth login` (or manual creation
  via the URL GitHub returned on push).

## Open questions

- Whether Order's Event Sourcing should use **Marten** or a hand-rolled
  append-only store. Defer to Phase 4; spike both.

## Decisions made this session

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