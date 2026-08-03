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

## Production scaling notes

> The implementation in Phase 3 keeps the **whole catalog** in Redis as
> flat hashes, with Postgres fallback on a miss. That is the right call
> for a portfolio demo and holds in production for catalogs up to a few
> million SKUs. Beyond that, "all in memory" stops being the right
> default. This section records the scaling path so the ADR does not
> read as if we forgot.

### Sizing rule of thumb

A product hash (`id`, `name`, `sku`, `description`, `price_cents`,
`currency`, `category`, `active`, `featured`, `priority`,
`created_at`, `updated_at`) is ~300–500 bytes on the wire plus Redis
hash overhead of ~80 bytes — figure **~500 bytes per product** in
resident memory.

| Catalog size | Redis RSS (approx) | Verdict |
|---|---|---|
| ≤ 100 k SKUs | < 50 MB | trivial — Phase 3 demo range |
| 100 k – 1 M | 50 MB – 500 MB | comfortable on a single Redis node |
| 1 M – 10 M | 500 MB – 5 GB | still fine on one box; revisit instance size |
| 10 M – 100 M | 5 GB – 50 GB | standalone Redis no longer fits; **Redis Cluster** (sharded by key), or shift strategy |
| > 100 M | > 50 GB | Redis-as-full-mirror stops being the right tool — keep hot set only, or move to Postgres read replica / Elasticsearch |

### Strategy 1 — hot/cold split (the common production fix)

Most e-commerce catalogs are heavily skewed: a small "hot" set
(active, in stock, featured, recently viewed) absorbs the vast
majority of read traffic, while a long tail of discontinued / out-of-
stock / inactive SKUs is rarely read.

- The projection **writes to Redis only when** `product.Active &&
  product.InStock` (or a similar predicate). Cold products are never
  materialized, or are evicted via `ZREMRANGEBYSCORE` on a TTL.
- Redis `maxmemory-policy allkeys-lru` (already set in
  `deploy/docker-compose.yml`) provides a safety net: if the hot set
  still overflows memory, least-recently-used entries are evicted
  automatically. A cache miss falls back to Postgres (Phase 3 already
  implements this path) — correctness is preserved, only latency on
  the cold read regresses.
- Typical compression: cover 80–95% of read traffic with 10–20% of
  the catalog footprint.

### Strategy 2 — Redis Cluster (mid-size catalogs that still fit in distributed RAM)

Above ~5 GB on a single node, or to remove Redis as a SPOF, switch
from standalone Redis to **Redis Cluster**. `StackExchange.Redis`
connects to a cluster transparently — no application code changes.
Keys are sharded by hash slot; `product:{id}` keeps co-located reads
on the same slot. Redis replicas per shard give HA. This is the right
move for ~10 M–100 M SKUs before falling back to a non-RAM strategy.

### Strategy 3 — non-Redis read models (very large catalogs or full-text search)

CQRS does NOT mandate Redis — it mandates a read model **separate**
from the write side. When the catalog no longer fits in RAM at a
sensible cost:

- **Postgres read replica** dedicated to the catalog, tuned and
  indexed for read queries, isolated from the write master. Postgres
  MVCC means reads do not block writes; the replica can be scaled
  independently. This is the natural fallback when "140 GB catalog,
  sub-ms reads" is no longer realistic on RAM, and 10–50 ms reads are
  acceptable.
- **Elasticsearch / OpenSearch** for full-text search (`?search=...`).
  Phase 3 implements a token-prefix index in a Redis sorted set —
  fine for short catalogs, but a real catalog search at scale wants
  inverted-index relevance, analyzers, and typo tolerance, which
  Elasticsearch does natively. In that setup, Redis stays for
  `GET /api/products/{id}` and `?featured=true` (single-key / sorted-
  set reads), while `?search=` is routed to Elasticsearch.
- **Both behind the same `IProductReadModel` abstraction** in
  `Product.Application` so the read side can route per query shape.
  The command handler / projection stays unchanged — only the read
  model *implementations* differ by surface.

### What this means for Phase 3

For the portfolio we intentionally keep "all in memory": the dataset
is tiny in dev, the pattern is what we want to demonstrate, and the
`maxmemory-policy: allkeys-lru` already protects against overflow.
The fallback path to Postgres (which Phase 3 wires in next) **is**
the cold path of Strategy 1 — so the production scaling fix is
mostly "let LRU evict + rely on the existing fallback", not a
rebuild. The point of recording the path here is so a senior
reviewer reading the ADR sees the limits of the pattern were
understood, not glossed over.

### Cross-references

- Phase 3 acceptance criteria (`docs/roadmap.md`): cache miss falls
  back to Postgres and backfills — the very mechanism Strategy 1
  leans on.
- ADR-009: idempotency key lives in the same Redis instance — a
  production hot/cold split must not evict idempotency keys (use a
  separate logical DB or a non-evicted keyspace).
- ADR-008: each service owns its DB. A Postgres read replica for the
  catalog is still the Product service's own replica — not a shared
  instance.