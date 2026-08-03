# AGENTS.md — src/services/product/

Product catalog service with **CQRS** and **Redis read models**. .NET 10.

## Stack

- .NET 10 (`net10.0`), file-scoped namespaces, nullable on, `async`/`await`
  everywhere, MediatR for CQRS (commands vs. queries live in different
  folders / handler sets, but share the MediatR dispatcher).
- EF Core 9 + Npgsql (Postgres, database `product`, ADR-008). Tables
  `products` + `outbox`.
- StackExchange.Redis for read models + cache (ADR-006) AND idempotency
  key store on the projection consumer (ADR-009).
- Confluent.Kafka + `Confluent.SchemaRegistry.Serdes.Avro` for the
  outbox publisher (Infra) and the projection consumer (Projections).
- Serilog → Seq with `ServiceName=product` and the `ActivityEnricher`
  from `OpenCode.TraceContext` (ADR-010 as amended by ADR-015).
- `TraceContext.AlwaysSample()` called once at startup (per ADR-015
  follow-up).

## Responsibilities

- **Write side** (commands → `products` table + `outbox` row in ONE EF
  Core transaction, ADR-004): `CreateProduct`, `UpdatePrice`,
  `ActivateProduct`, `DeactivateProduct`. (`UpdateStock` is also
  envisioned for this service but is out of Phase 3 scope — see the
  Phase 3 acceptance-criteria list in `docs/roadmap.md`.)
- **Read side** (queries, served from Redis read models, with Postgres
  fallback + backfill on cache miss): `GetProductById`, `ListByCategory`,
  `GetFeatured`, `SearchProducts`.
- **Outbox publisher** (hosted service, ADR-004): polls `outbox` with
  `FOR UPDATE SKIP LOCKED`, publishes `product.events.*` to Kafka with
  the Avro schema + `traceparent` Kafka header (ADR-007 + ADR-010).
- **Projection consumer** (hosted service): subscribes to
  `product.events.*`, decodes Avro via Schema Registry, applies Redis
  read-model updates per ADR-006. Idempotency via
  `SET idempotency:product:{event_id} NX EX 86400` (ADR-009). Lives in
  `Product.Projections` — runs **in-process** in dev; may be split into
  its own deployment in prod for scaling.

## Non-responsibilities

- Does NOT know about orders / users / inventory. Owns only the catalog.
- Does NOT consume other aggregates' events. (Notification will fan-out
  on `product.events.*` separately; that's Notification's job.)
- Does NOT validate JWTs — that's the API Gateway's job (Phase 8).
  For Phase 3 the API surface is intentionally open at the dev port
  (no auth yet — added in Phase 8 alongside the Gateway).

## Clean Architecture layers

```
product/
  Product.sln
  Product.Domain/                 ← Aggregate root `ProductAggregate`,
  │                                  ValueObject `Money` (no deps)
  Product.Application/            ← Abstractions (IProductRepository,
  │                                  IUnitOfWork, IOutboxRepository),
  │                                  Commands/Queries (MediatR), Options,
  │                                  Exceptions
  Product.Infrastructure/        ← Persistence (ProductDbContext,
  │                                  EF Configurations, Repositories),
  │                                  Services (UnitOfWork), Outbox
  │                                  publisher (hosted service),
  │                                  Kafka producer (Avro), Redis
  │                                  multiplexer registration
  Product.Projections/           ← Kafka Avro consumer for
  │                                 product.events.*, Redis read-model
  │                                 writer (ADR-006), idempotency
  │                                 guard (ADR-009)
  Product.Api/                    ← Program.cs, Controllers, appsettings
  Product.Api.Tests/             ← unit tests (xUnit + NSubstitute +
  │                                 FluentAssertions)
  tests/Product.IntegrationTests/ ← Testcontainers Postgres + Redis +
                                    Kafka end-to-end flow
```

EF Core migrations live in `Product.Infrastructure`
(`dotnet ef migrations add ...` from the Api project; the DbContext is
registered with
`MigrationsAssembly = typeof(ProductDbContext).Assembly`).

## Aggregate root class name

The aggregate is called **`Product`** in prose, ADRs, schemas, and
topic names. Its **C# class** is `ProductAggregate`
(`src/services/product/Product.Domain/Entities/Product.cs`) — same
trick Identity used with `ApplicationUser` (the entity-class name is
suffixed to avoid a type/namespace name collision: the project root
namespace is `Product.X`, which makes the bare name `Product` resolve
to the *namespace*, not the entity, so the entity class had to be
renamed). The Avro records (`ProductCreated`, `ProductPriceUpdated`,
...) keep their canonical names on the wire.

## Read model shapes in Redis (ADR-006)

- `product:{id}` → Hash with flat fields (id, name, sku, price_cents,
  currency, category, active, featured, priority, created_at).
- `products:featured` → Sorted Set (score = priority; only products
  with `featured=true` are members).
- `products:category:{slug}` → Sorted Set (score = created_at millis).
- `products:search:idx` → token index for simple `?search=` (Phase 3
  uses a basic token-prefix match; full TF-IDF is out of scope).

## Outbox table (`outbox`)

Columns: `Id uuid`, `AggregateType`, `AggregateId uuid`, `EventType`,
`Topic`, `PartitionKey`, `PayloadJson jsonb`, `OccurredAt timestamptz`,
`PublishedAt timestamptz null`, `RetryCount int`. Polled by the
`OutboxPublisherService` with
`WHERE "PublishedAt" IS NULL ORDER BY "OccurredAt" FOR UPDATE SKIP LOCKED
LIMIT <batch>`. Same-transaction writes guarantee at-least-once
publish (ADR-004); consumer idempotency (ADR-009) dedupes.

## Mandatory ADRs

- ADR-006 (CQRS + Redis read models) — the read side pattern.
- ADR-004 (Outbox) — write-side event publication.
- ADR-007 (Avro + Schema Registry) — `product.events.*` schemas live
  in `src/shared/contracts/product/` (added in Phase 3 — Product is
  the first producer).
- ADR-008 (one DB per service — `product` database).
- ADR-009 (idempotency in the projection consumer).
- ADR-010 as amended by ADR-015 (observability + native propagators).
- ADR-001 (.NET 10 conventions).

## Local ports

- Dev HTTPS: `https://localhost:5002` (Identity uses 5001; do not
  collide).
- Dev HTTP: `http://localhost:5003`.
- Health: `GET /health`.
- Postgres: `localhost:15432`, DB `product`, user `edcp` / `edcp_dev`.
- Redis: `localhost:6379` (read models + idempotency, ADR-009).
- Kafka: `localhost:9092` (PLAINTEXT_HOST).
- Schema Registry: `http://localhost:8081` (auto-registration on first
  produce; see ADR-007 and the decision logged in
  `docs/decisions-log.md`).
- Seq: `http://localhost:8082` (filter `ServiceName=product`).