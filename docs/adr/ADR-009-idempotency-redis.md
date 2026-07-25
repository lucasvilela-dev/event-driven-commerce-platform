# ADR-009: Idempotency via Redis in .NET and Go consumers

- **Status:** Accepted
- **Date:** 2026-07-25
- **Decisor:** Portfolio author
- **Related to:** ADR-004 (Outbox), ADR-002 (Kafka)

## Context

The Outbox Pattern (ADR-004) guarantees **at-least-once** delivery through
polling-window publishing. This means consumers can receive the same event
more than once when:

- The producer publishes but does not confirm the `published_at` marker
  (crash) → the event is re-published.
- The consumer processes, commits the offset, but dies before acking the
  DB — on restart it consumes the same event again.
- A consumer group rebalance can redeliver messages already processed by
  another consumer.

Without explicit idempotency, a duplicate `OrderCreated` would generate two
stock reservations (Inventory) or two charges (Payment) — catastrophic data.

## Decision

Every .NET and Go consumer uses **Redis-based idempotency** with a key
derived from the event.

## Implementation

Every domain event (Avro, ADR-007) mandatorily carries:

- `event_id` (UUID v7 — preserves order) — the natural idempotency key.
- `aggregate_id` (UUID of the aggregate that originated the event).

Before processing, the consumer runs (pseudocode):

```text
SET idempotency:{consumer_name}:{event_id} NX EX 86400
  -> if it failed, it means we already processed it → skip (idempotent)
process event
commit offset (auto.commit=false on the Kafka client)
```

- `NX` fails if the key already exists → indicates prior processing.
- `EX 86400` (24h) covers retries / the rebalance window.
- A silent skip is safe: the event's semantics are "a fact already
  published"; a duplicate is a transport artifact, not new information.

## Rationale

- **Shared Redis** between Order/Payment (.NET, uses StackExchange.Redis)
  and Inventory/Notification/Shipping (Go, uses go-redis) — same source of
  idempotency truth, same semantics.
- **Sub-ms performance** for SET NX — no per-event Postgres lookup overhead.
- **Short TTL** controls key growth; a 24h window covers rebalances and
  extreme retries.
- **Scoped by `consumer_name`:** an Inventory reservation processed does not
  block Payment from reading the same `event_id` — the key is scoped by
  who idempots.

## Consequences

- **Positive:**
  - .NET and Go consumers promote at-least-once semantics to
    effectively-once without 2PC / cross-talk.
  - Redis also serves Product read models (ADR-006) — no extra component.
  - Replaying an already-processed event is safe and silent.
- **Negative:**
  - Redis becomes a dependency for idempotency — if it goes down during
    SET NX there are two cases:
    1) it returns an error → the consumer NACKs and resets the offset
       (correctness preserved).
    2) "network partition": duplicate processing may occur; mitigated by
       the unique `event_id` on all domain mutation operations.
  - Memory cost of keys (small: 24h × event rate).
- **Mitigations:**
  - Connection retry + backoff on the Redis client (Polly for .NET; manual
    circuit breaker in Go).
  - At the domain layer, **operators** must also be idempotent when
    possible (e.g. `UPDATE inventory SET reserved = reserved + :delta
    WHERE product_id = :p AND order_id != :current`).

## Alternatives considered

- **UNIQUE CONSTRAINT in Postgres:** store `processed_events` in the DB.
  Cost of INSERT + egress per event; DB contention at high throughput.
- **Per-consumer unique transaction ID in Kafka:** integration with the DB
  via the transactional outbox for marking too.
- **Redis Streams:** a Kafka variant native to Redis — would replace the
  whole ADR-002. Out of scope.
- **At-most-once via auto-commit:** simpler but loses events — not aligned
  with a senior platform standard.