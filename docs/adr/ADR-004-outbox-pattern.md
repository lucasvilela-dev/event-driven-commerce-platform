# ADR-004: Outbox Pattern for commit/event atomicity

- **Status:** Accepted
- **Date:** 2026-07-25
- **Decisor:** Portfolio author
- **Related to:** ADR-002 (Kafka), ADR-003 (Saga), ADR-005 (Event Sourcing)

## Context

When a domain service publishes an event to Kafka, there is a failure window
between the **database transaction commit** and the **producer ack in Kafka**:

- If the DB commits but the process dies before publish → the event is lost.
- If the publish happens but the DB rolls back → a ghost event is published.

In a Saga (ADR-003) this is catastrophic: losing `PaymentApproved` leaves
the order pending forever; publishing a ghost triggers a rollback with no
compensation.

## Decision

We adopt the **Outbox Pattern** in every .NET service that publishes domain
events (Order, Product, Payment, Inventory if it ever publishes).

## Implementation

1. In the same transaction as the aggregate, the repository writes:
   - The aggregate mutation (`orders`, `products`, etc.)
   - A row in the `outbox` table with the event payload (Avro/JSON).

2. An **_OUTBOX PUBLISHER_** (background hosted service via `IHostedService`)
   does periodic **polling** of the `outbox` table with
   `WHERE published_at IS NULL ORDER BY id FOR UPDATE SKIP LOCKED` and
   publishes to Kafka.
   - `SKIP LOCKED` allows multiple concurrent publishers without a full lock.
   - On success, it sets `published_at = now()` and advances the `offset`.

3. For single-instance dev, one publisher is enough. In multi-instance
   deployments, `SKIP LOCKED` distributes rows among workers.

## Rationale

- **Transactional "at-least-once" guarantee:** the event exists only if the
  business commit exists; and vice-versa.
- **No 2PC / XA:** simple to maintain, with no distributed transaction
  coordinator (which is expensive and fragile across Postgres + Kafka).
- **Crash-tolerant recovery:** if the producer crashes mid-way, the row stays
  at `published_at IS NULL` and will be re-published in the next window.
- **Consumer idempotency** (see ADR-009) handles duplicates in the case of
  a resend between publish and the marking step.

## Consequences

- **Positive:**
  - The Order→Inventory→Payment chain never loses events mid-flight.
  - Order is preserved by the outbox's sequential id (per-aggregate order is
    kept if we use partition key = aggregateId).
  - The outbox table also doubles as a local audit log.
- **Negative:**
  - Added latency (1–5s polling in dev; larger production setups use CDC via
    Debezium for lower latency).
  - The `outbox` table grows — needs periodic purge/archive.
  - Consumers must be idempotent (already planned in ADR-009).
- **Mitigations:**
  - Partition Kafka by `aggregateId` to keep order within an aggregate.
  - A cleanup job that archives rows with `published_at` older than 7 days.

## Alternatives considered

- **Direct publish (no Outbox):** simple but loses events on failure —
  unacceptable in a compensatable Saga.
- **CDC via Debezium:** better latency and decouples the app from the
  Outbox, but adds Kafka Connect + a Debezium container to the stack —
  overkill for a portfolio at this stage. Left as a future evolution in
  the README.
- **Transaction Outbox with Eventual Consistency + 2PC:** XA transactions
  are notoriously slow and fragile with Postgres + Kafka; not recommended.