# ADR-006: CQRS in the Product Catalog with read models + Redis

- **Status:** Accepted
- **Date:** 2026-07-25
- **Decisor:** Portfolio author
- **Related to:** ADR-005 (ES), ADR-002 (Kafka)

## Context

The `Product Service` has a typically **read-heavy** access profile: every
catalog load, product search, highlights list, or widget triggers hundreds
of SELECTs for every catalog INSERT/UPDATE. Modeling it as plain CRUD
brings two problems:

- Read access needs distinct formats (paginated list, full-text search, SKU
  lookup, landing-page projection) that fit poorly with a single relational
  schema.
- Every GET hits Postgres directly, overloading the critical write path and
  the Saga.

## Decision

We apply **CQRS** in the Product Service: a physical (and logical)
separation between commands (write side) and queries (read side), with read
models materialized in Redis as projections.

## Implementation

- **Write side** (`ProductCommandHandler`, MediatR):
  - Commands: `CreateProduct`, `UpdatePrice`, `ActivateProduct`,
    `DeactivateProduct`.
  - Persists to Postgres (`products`) and publishes `ProductEvent` via the
    Outbox.
- **Read side** (`ProductQueryHandler`, MediatR):
  - Queries: `GetProductById`, `SearchProducts`, `GetFeatured`,
    `ListByCategory`.
  - Optimized via denormalized **read models** in Redis.
- **Projection** (the `product.events` Kafka consumer): a hosted service
  that consumes product change events and updates the Redis read models:
  - Hash by id: `product:{id}` (flattened fields for O(1) reads).
  - Featured sorted set: `products:featured` (score = priority).
  - Search sorted set: `products:search:idx` with simple TF-IDF tokens.
- **Query:** reads straight from Redis; falls back to Postgres on a cache
  miss (with a Doctrine-like backfill).

## Rationale

- **Read-heavy** justifies CQRS — real catalogs see 100+:1 read/write ratios.
- **Redis** is the right choice for read models: sub-ms latency and rich
  structures (Hashes, Sorted Sets).
- **Decouples the read schema from the write schema:** the Product aggregate
  focuses on invariants; the projection focuses on consumption formats.
  Changing the UI does not require migrating relational columns.
- **Reusable:** the same projection pattern can be reused for `order_query`
  (ADR-005 also needs CQRS for reading Order).

## Consequences

- **Positive:**
  - Catalog listing latency ~ms (Redis).
  - Write side unblocked from reads: no Postgres lock on cache miss.
  - Demonstrates senior-level CQRS + cache usage to the portfolio.
- **Negative:**
  - **Eventual consistency** between write and read (ms–sec lag). It can
    confuse a user who edits and sees an "outdated" result. Mitigation:
    a local cache-aside in the command handler when published by the same
    instance.
  - Projection complexity (a Kafka consumer that maintains Redis).
  - Redis as a read SPOF — mitigated with Redis replicas in production.
- **Mitigations:**
  - TTLs on read models (fallback to Postgres) for staleness tolerance.
  - Consumer idempotency via the event key (see ADR-009).

## Alternatives considered

- **Plain CRUD:** overloads Postgres in a read-heavy scenario; does not
  showcase CQRS.
- **CQRS with Postgres materialized views as read models:** no Redis, but MV
  refresh is slow and adds log overhead; less of a differentiator.
- **CDN edge cache:** suitable but does not replace the read-models
  pattern — a complement in production.