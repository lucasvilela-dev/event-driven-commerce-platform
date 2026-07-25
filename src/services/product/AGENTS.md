# AGENTS.md — src/services/product/

Product catalog service with **CQRS** and **Redis read models**. .NET 10.

## Stack

- .NET 10 (`net10.0`)
- MediatR (CQRS), EF Core + PostgreSQL (database `product`, ADR-008)
- StackExchange.Redis for read models and cache (ADR-006)
- Confluent.Kafka + Avro (publishes `product.events.*`)
- Serilog → Seq (ADR-010)

## Responsibilities

- Write side: `CreateProduct`, `UpdatePrice`, `ActivateProduct`,
  `DeactivateProduct`, `UpdateStock`. Persists to `products` table and writes
  events to the **outbox** (ADR-004).
- Read side: `GetProductById`, `SearchProducts`, `GetFeatured`,
  `ListByCategory`. Served from Redis read models; falls back to Postgres on
  cache miss (then backfills).
- Projection consumer: subscribes to `product.events.*` and updates read models
  in Redis. Same service, different hosted worker.

## Clean Architecture layers

```
product/
  Product.Domain/            ← aggregate `Product`, value objects
  Product.Application/       ← MediatR commands/queries, handlers, validators
  Product.Infrastructure/    ← EF Core, outbox publisher, Kafka producer
  Product.Api/               ← ASP.NET controllers, DI composition root
  Product.Projections/       ← Redis projection worker (separate process
                                option in prod)
  tests/
```

The projection worker is part of the same solution; in dev it can run in the
same process, in prod it may be split for scaling.

## Read model shapes in Redis (ADR-006)

- `product:{id}` → Hash with flat fields (id, name, price_cents, currency,
  active, …).
- `products:featured` → Sorted Set (score = priority).
- `products:category:{slug}` → Sorted Set (score = created_at).
- Search: simple token index; details in ADR-006.

## Mandatory ADRs

- ADR-006 (CQRS + Redis read models)
- ADR-004 (Outbox — write-side event publication)
- ADR-007 (Avro + Schema Registry)
- ADR-008 (one DB per service — `product`)
- ADR-009 (Idempotency in the projection consumer)
- ADR-010 (observability)