# Roadmap

> **Read this first.** Ordered phases for building the platform end-to-end.
> Each phase lists its **goal**, **acceptance criteria**, **deliverables**,
> the **ADRs it implements**, the **files it touches**, its **dependencies**
> on previous phases, and a rough **effort estimate** (S/M/L).
>
> Update `docs/status.md` at the end of every session to reflect actual progress.
> Do **not** renumber phases or rewrite history here — if the plan changes,
> add a note at the bottom (e.g. *"Phase 4 split into 4a/4b on 2026-08-12"*).
> Resist the urge to skip ahead: later phases depend on contracts, schemas
> and helpers laid down in earlier ones.

## Legend

- Effort: **S** ≈ 1 session, **M** ≈ 2-4 sessions, **L** ≈ 5+ sessions.
- Status badge: `[done]`, `[next]`, `[planned]`, `[blocked]`.
- A phase is done only when **all** acceptance criteria pass.

---

## Phase 0 — Platform stack `[done]`

**Goal:** local infrastructure (Kafka, Postgres, Redis, Seq, Schema Registry,
Kafka UI) up with one command and validated.

**Acceptance criteria**
- [x] `docker compose -f deploy/docker-compose.yml up -d` succeeds.
- [x] All containers report `(healthy)` in `docker compose ps`.
- [x] Postgres has 5 databases: `identity`, `product`, `order`, `payment`,
      `inventory` (scripted via `init-multi-db.sh`).
- [x] `kafka-topics --list` returns `__consumer_offsets` and `_schemas`.
- [x] `curl http://localhost:8081/subjects` returns `[]` (Schema Registry up).
- [x] `curl http://localhost:8082` returns 200 (Seq up).

**Deliverables:** `deploy/docker-compose.yml`, `deploy/postgres/init-multi-db.sh`,
`.gitignore`, root `README.md`, `AGENTS.md` (global), per-service `AGENTS.md`
skeletons, ADRs 001-010.

**ADRs implemented:** none yet — only the docs that *justify* future choices.

**Files:** `deploy/`, `docs/adr/`, `src/**/AGENTS.md`, `README.md`.

**Dependencies:** none.

**Effort:** done (~1 session).

---

## Phase 1 — Shared contracts & observability helpers `[next]`

**Goal:** fix the cross-service interface once and for all. By the end of this
phase, any .NET or Go service can produce/consume Avro events and propagate a
W3C trace context through Kafka.

**Acceptance criteria**
- [ ] All event schemas in `src/shared/contracts/` are valid Avro and pass
      `avro-tools compile` (CLI or schema registry `POST /subjects/...`).
- [ ] Schemas are registered in the local Schema Registry under the
      `<topic>-value` subject with BACKWARD compatibility.
- [ ] `docs/decisions-log.md` notes the version convention for events
      (`<aggregate>_<event>_v1.avsc`, subject `<topic>-value`).
- [ ] .NET helper `OpenCode.TraceContext` reads/writes `traceparent` to/from
      Kafka headers and HTTP headers, with unit tests.
- [ ] Go helper package `tracecontext` does the same with unit tests.
- [ ] A README in `src/shared/` lists the canonical schemas with their topic
      and producer.

**Deliverables**
- Avro schemas (v1) for at least: `order_created`, `order_cancelled`,
  `order_saga_step_completed`, `inventory_reserved`, `inventory_failed`,
  `inventory_released`, `payment_approved`, `payment_declined`,
  `payment_refunded`, `shipping_shipped`, `shipping_failed`,
  `notification_sent`.
- Command schemas: `reserve_inventory`, `release_reservation`, `charge`,
  `refund`, `ship`.
- Producer/consumer quick-reference table in `src/shared/contracts/README.md`.
- `src/shared/observability/` helpers (.NET + Go) with tests.

**ADRs implemented:** ADR-007 (Avro + Schema Registry), ADR-010 (trace context).

**Files:** `src/shared/contracts/**`, `src/shared/observability/**`.

**Dependencies:** Phase 0 (Schema Registry up).

**Effort:** M.

---

## Phase 2 — Identity service `[planned]`

**Goal:** clients can obtain a JWT that authorizes calls to the rest of the
platform. Other services can validate tokens via JWKS.

**Acceptance criteria**
- [ ] `POST /api/identity/register` creates a user (email + password, hashed
      with ASP.NET Identity).
- [ ] `POST /api/identity/login` returns access + refresh JWT.
- [ ] `GET /.well-known/openid-configuration` and `/jwks` exposed.
- [ ] EF Core migration creates `users`, `roles`, `user_roles` in the
      `identity` database (not any other database).
- [ ] JWT includes `sub`, `email`, `roles` claims; expiry 15min access,
      7d refresh.
- [ ] Unit tests for `PasswordHasher` adapter and login handler; an
      integration test uses Testcontainers to spin Postgres and run the
      register→login→token-validation flow.
- [ ] Serilog logs to Seq with `ServiceName=identity` and `TraceId` propagated
      from inbound HTTP header.

**Deliverables**
- `src/services/identity/Identity.Domain`, `Identity.Application`,
  `Identity.Infrastructure`, `Identity.Api` projects + test projects.
- `docs/runbook/identity.md` — local run, env vars, troubleshooting.

**ADRs implemented:** ADR-001 (.NET 10 conventions), ADR-008 (one DB per
service), ADR-010 (observability).

**Files:** `src/services/identity/**`.

**Dependencies:** Phase 1 (trace context helper).

**Effort:** M.

> **Deferred from Phase 2 (tracked here so it is not lost):**
> `/api/identity/refresh` endpoint + refresh-token rotation. Phase 2
> only *issues* a refresh JWT at `/login`; the rotation endpoint and
> the `refresh_tokens` table (`revoked_at`/`replaced_by`/`family_id`
> with reuse-detection) are deferred to a later mini-phase — revisit
> after Phase 8 (Gateway) once the auth surface is exercised
> end-to-end.

---

## Phase 3 — Product catalog (CQRS + Redis read models) `[planned]`

**Goals**
- Establish the CQRS read-model pattern that other services will copy.
- Have a real "write side → outbox → Kafka event → projection → Redis read
  model → read side" loop working end-to-end.

**Acceptance criteria**
- [ ] Commands `CreateProduct`, `UpdatePrice`, `ActivateProduct`,
      `DeactivateProduct` work via HTTP and MediatR.
- [ ] Each command writes the row in `products` AND an `outbox` row in the
      same transaction (EF Core transaction).
- [ ] A hosted Outbox publisher polls `outbox` with
      `FOR UPDATE SKIP LOCKED` and publishes `product.events.*` to Kafka with
      the Avro schema and `traceparent` header.
- [ ] A projection worker (inside the same process for dev) consumes the
      events and writes Redis hashes/sorted-sets per ADR-006.
- [ ] `GET /api/products/{id}`, `GET /api/products?featured=true`,
      `GET /api/products?category=...` return data from Redis in <5ms median.
- [ ] Cache miss falls back to Postgres and backfills the read model.
- [ ] Idempotency key set on consumer (`idempotency:product:{event_id}`).
- [ ] Unit + integration tests (Testcontainers for Postgres, Kafka, Redis).

**Deliverables**
- `Product.Domain`, `Product.Application`, `Product.Infrastructure`,
  `Product.Api`, `Product.Projections` projects.
- `docs/runbook/product.md`.

**ADRs implemented:** ADR-004 (Outbox), ADR-006 (CQRS + Redis read models),
ADR-007 (Avro), ADR-008 (one DB per service), ADR-009 (idempotency),
ADR-010 (observability).

**Files:** `src/services/product/**`.

**Dependencies:** Phase 1 (contracts + trace helpers).

**Effort:** L. (This is the blueprint for Order's own projections.)

---

## Phase 4 — Order domain (Event Sourcing, no Saga yet) `[planned]`

**Goal:** the `Order` aggregate is real, durable as an event stream, and
readable through a projection. No Inventory/Payment calls yet — we only
implement the aggregate plus the basic CRUD-over-events surface.

**Acceptance criteria**
- [ ] `order_events` table (append-only) + `order_snapshot` projection in
      the `order` database.
- [ ] Commands `CreateOrder`, `CancelOrder` (user-initiated).
- [ ] Aggregate rehydration from events since last snapshot.
- [ ] Snapshot every 50 events (configurable).
- [ ] `GET /api/orders/{id}` reads from the `order_query` projection
      (rebuilt by a projection worker).
- [ ] Events streamed to `order.events` via Outbox with Avro + `traceparent`
      header.
- [ ] Unit tests cover aggregate invariants (cannot cancel an already
      cancelled order, cannot create order with empty items, etc.).
- [ ] Integration tests replay events and assert projected state.

**Deliverables**
- `Order.Domain` (aggregate, value objects), `Order.Application` (commands,
  queries, handlers), `Order.Infrastructure` (ES store, outbox publisher,
  Kafka producer), `Order.Api`, `Order.Projections`.

**ADRs implemented:** ADR-004 (Outbox), ADR-005 (Event Sourcing),
ADR-007 (Avro), ADR-008 (one DB), ADR-010.

**Files:** `src/services/order/**`.

**Dependencies:** Phase 1 (contracts), Phase 3 (projection pattern reference).

**Effort:** L.

---

## Phase 5 — Saga participants (Inventory + Payment) `[planned]`

**Goal:** two Saga participants ready to receive commands and reply with
events. Order is *not* yet sending commands — we only validate each
participant in isolation.

**Acceptance criteria — Inventory (Go)**
- [ ] `cmd/inventory/main.go` connects to Kafka, Postgres (`inventory` DB),
      Redis (idempotency).
- [ ] Consumes `order.commands.reserve-inventory`; on success publishes
      `inventory.events.reserved`; on stock failure publishes
      `inventory.events.failed`.
- [ ] Consumes `order.commands.release-reservation`; publishes
      `inventory.events.released`.
- [ ] Local Outbox in Go (goroutine poller) for publish durability.
- [ ] Idempotency on `command_id`.
- [ ] Unit tests for stock aggregate; integration tests via Testcontainers
      (Go) hit Kafka + Postgres + Redis.

**Acceptance criteria — Payment (.NET)**
- [ ] Consumes `payment.commands.charge`; calls a mocked `IPaymentGateway`
      behind Polly (CB + retry); publishes `payment.events.approved` or
      `payment.events.declined`.
- [ ] Consumes `payment.commands.refund`; publishes `payment.events.refunded`.
- [ ] `payments` and `refunds` tables in `payment` DB.
- [ ] Idempotency on `charge_id` (= Order `aggregate_id`).
- [ ] Unit + integration tests.

**Deliverables**
- `src/services/inventory/**` (full Go module).
- `src/services/payment/**` (.NET projects).
- `docs/runbook/inventory.md`, `docs/runbook/payment.md`.

**ADRs implemented:** ADR-002 (Kafka), ADR-003 (Saga participants),
ADR-004 (Outbox), ADR-007, ADR-008, ADR-009, ADR-010.

**Files:** `src/services/inventory/**`, `src/services/payment/**`.

**Dependencies:** Phase 1 (contracts).

**Effort:** L.

---

## Phase 6 — Saga orchestration in Order `[planned]`

**Goal:** Order drives the full purchase flow. An end-to-end happy path
`CreateOrder → InventoryReserved → PaymentApproved → OrderConfirmed` completes,
and a failure path triggers compensations.

**Acceptance criteria**
- [ ] `OrderSaga` state machine lives in `Order.Application`, with explicit
      transitions on each consumed event (`inventory.events.reserved`,
      `payment.events.approved`, `shipping.events.shipped`, …).
- [ ] Each transition produces the next command (`order.commands.reserve-
      inventory`, `payment.commands.charge`, `shipping.commands.ship`) with
      `correlation_id` + `reply_to`.
- [ ] Failure paths emit compensations: `order.commands.release-reservation`,
      `payment.commands.refund`, then append `OrderCancelled`.
- [ ] Saga timeouts: a hosted scheduler emits `OrderSagaTimedOut` after N
      seconds without a reply.
- [ ] Every Saga transition appends an event to the Order stream
      (`OrderSagaWaiting`, `OrderSagaStepCompleted`, `OrderSagaCompensated`,
      `OrderSagaCompleted`, `OrderSagaFailed`).
- [ ] `GET /api/orders/{id}/saga` returns the current state and history.
- [ ] Integration test runs the full happy path and a payment-failure path
      with compensations asserted.

**Deliverables**
- Saga orchestrator, scheduler, additional Kafka consumers in `Order.Api`.

**ADRs implemented:** ADR-003 (orchestrated Saga), ADR-004, ADR-005.

**Files:** `src/services/order/**` (only order changes — participants stay put).

**Dependencies:** Phase 4 (Order aggregate), Phase 5 (participants).

**Effort:** L.

---

## Phase 7 — Notification + Shipping (last consumers) `[planned]`

**Goal:** close the fan-out. Notification is fire-and-forget; Shipping is the
last Saga step.

**Acceptance criteria — Notification (Go)**
- [ ] Subscribes to `order.events.*` (OrderConfirmed/Cancelled/Shipped),
      `payment.events.approved`, `notification.commands.send`.
- [ ] Fan-out via goroutines to mocked email/SMS/push behind `Channel`
      interface; per-channel status persisted in `notifications` table.
- [ ] `notification.events.sent` published for observability.
- [ ] Idempotency on `event_id`.

**Acceptance criteria — Shipping (Go)**
- [ ] Consumes `shipping.commands.ship`; calls mocked `IShippingCarrier`;
      publishes `shipping.events.shipped` with tracking code or
      `shipping.events.failed`.
- [ ] `shipments` table in a `shipping` database — **add `shipping` to
      `deploy/postgres/init-multi-db.sh`**.
- [ ] Idempotency on `event_id`.

**Deliverables**
- `src/services/notification/**`, `src/services/shipping/**` Go modules.
- `docs/runbook/notification.md`, `docs/runbook/shipping.md`.
- Update to `deploy/postgres/init-multi-db.sh` (add `shipping`).

**ADRs implemented:** ADR-002, ADR-003 (final step), ADR-004, ADR-007,
ADR-008, ADR-009, ADR-010.

**Files:** `src/services/notification/**`, `src/services/shipping/**`,
`deploy/postgres/init-multi-db.sh`.

**Dependencies:** Phase 1 (contracts), Phase 6 (Order publishes events).

**Effort:** M.

---

## Phase 8 — API Gateway (YARP) `[planned]`

**Goal:** a single public HTTP entry point that protects routes with JWT and
propagates `traceparent`.

**Acceptance criteria**
- [ ] Routes `/api/identity/*`, `/api/products/*`, `/api/orders/*`,
      `/api/payments/*` map to the right backends.
- [ ] Protected routes require a valid JWT signed by the Identity service;
      `X-User-Id` and `roles` are propagated as downstream headers.
- [ ] Inbound `traceparent` is propagated; missing one is generated.
- [ ] Rate limiting per IP and per user (`AspNetCore.RateLimiting`).
- [ ] Health endpoint aggregates downstream healthchecks.
- [ ] Integration test: proxy a request, assert downstream received headers.

**Deliverables**
- `src/api-gateway/**` (.NET project, YARP config in `appsettings.json`).
- `docs/runbook/api-gateway.md`.

**ADRs implemented:** ADR-001 (.NET 10), ADR-010 (observability starts at the
edge).

**Files:** `src/api-gateway/**`.

**Dependencies:** Phase 2 (Identity for JWT validation).

**Effort:** M.

---

## Phase 9 — Observability polish & runbooks `[planned]`

**Goal:** one click in Seq reconstructs a Saga. Runbooks cover the common
operations.

**Acceptance criteria**
- [ ] Every service publishes a `ServiceName`, `TraceId`, `SpanId`,
      `ParentSpanId`, `AggregateId` and `EventId` (when applicable) on every
      log line.
- [ ] Seq dashboard "Saga stuck" surfaces Orders stuck in
      `OrderSagaWaiting` beyond threshold.
- [ ] Seq dashboard "Outbox lag" surfaces publish latency.
- [ ] `docs/runbook/` covers: bring up/down the stack, schema evolution
      procedure, restart a consumer, replay events, deal with a poisoned
      message, failover a Postgres DB.
- [ ] Architecture diagrams in `docs/architecture/diagrams/` (at least one
      PlantUML for the Saga flow, one for the Outbox flow).

**Deliverables**
- Seq dashboards as JSON in `docs/architecture/seq/`.
- Diagrams in `docs/architecture/diagrams/`.
- Runbook entries.

**ADRs implemented:** ADR-010 (full realization).

**Files:** `docs/runbook/**`, `docs/architecture/**`, small touch-ups in
services to enrich logs.

**Dependencies:** all service phases (2-7).

**Effort:** M.

---

## Phase 10 — CI/CD & tests consolidation `[planned]`

**Goal:** GitHub Actions build, test, lint, schema-validate — every push.

**Acceptance criteria**
- [ ] Matrix workflow builds every .NET service and every Go module.
- [ ] `dotnet test` and `go test ./...` run on every push and PR.
- [ ] Avro schemas validated against Schema Registry compatibility in CI
      (script calls `POST /compatibility/subjects/...`).
- [ ] Lint: `dotnet format --verify-no-changes`, `golangci-lint`.
- [ ] Test coverage reported (Coverlet + `go test -coverprofile`); fails
      below a documented threshold (start at 60%, raise over time).
- [ ] Draft ADR-011 (CI/CD) capturing the choices.

**Deliverables**
- `.github/workflows/ci.yml`.
- ADR-011.

**ADRs implemented:** ADR-011 (new — planned).

**Files:** `.github/workflows/**`, `docs/adr/ADR-011-ci-cd.md`.

**Dependencies:** at least Phases 1-3 worth of code to test.

**Effort:** M.

---

## Phase 11 — Portfolio presentation `[planned]`

**Goal:** a recruiter or senior engineer lands in the repo and "gets it" in
under 5 minutes.

**Acceptance criteria**
- [ ] Top-level README has: one-paragraph pitch, architecture diagram, stack
      table, patterns applied, "how to run" block, links to ADRs and runbooks.
- [ ] A `docs/scenarios.md` walks through 3 end-to-end scenarios (happy path
      purchase, payment failure with compensation, replay of events after a
      consumer restart) with curl/Kafka-UI screenshots or transcripts.
- [ ] Each service README has a short "what it does, why it's interesting"
      section.
- [ ] LICENSE added (MIT).
- [ ] Optional: a 2-min narrated Loom or GIF of the happy path in the README.

**Deliverables**
- Polished READMEs, `docs/scenarios.md`, LICENSE, diagrams referenced from
  README.

**ADRs implemented:** none (presentation layer).

**Files:** `README.md`, `docs/**`, `LICENSE`.

**Dependencies:** Phases 9 + 10.

**Effort:** S-M.

---

## Phase dependency graph

```
Phase 0 (platform) ──┬──► Phase 1 (shared) ──┬──► Phase 2 (identity) ───► Phase 8 (gateway)
                     │                       ├──► Phase 3 (product)  ─┐
                     │                       ├──► Phase 4 (order)    │
                     │                       │                          ├─► Phase 6 (saga) ──► Phase 7 (notif+ship)
                     │                       │                          │
                     │                       └──► Phase 5 (inv+pay)  ──┘
                     │
                     └─► (every later phase) ──► Phase 9 (observability) ──► Phase 10 (CI/CD) ──► Phase 11 (presentation)
```

## Notes on sequencing

- **Phase 1 before any service code.** Skipping it means rewriting contracts
  once services exist — expensive.
- **Phase 3 (Product) before Phase 4 (Order)** is recommended because Product
  establishes the Outbox+projection pattern that Order copies. Tackling Order
  first re-invents the wheel.
- **Phase 8 (Gateway) only after Phase 2 (Identity)** — there is nothing to
  protect before tokens exist.
- **Phase 9 (observability polish) intentionally last-ish** — every service
  already logs from Phase 2 onward; Phase 9 just consolidates dashboards and
  runbooks once all signal sources exist.
- **Phases 10 and 11 (CI, presentation)** overlap with the tail end of
  implementation — start drafting CI as soon as Phase 1 has tests.