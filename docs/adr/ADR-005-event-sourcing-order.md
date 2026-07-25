# ADR-005: Event Sourcing applied to the Order aggregate

- **Status:** Accepted
- **Date:** 2026-07-25
- **Decisor:** Portfolio author
- **Related to:** ADR-003 (Saga), ADR-004 (Outbox), ADR-006 (CQRS)

## Context

The `Order` aggregate is the richest state in the platform and also the
state of the purchase Saga (confirm stock, payment, shipment, etc.). Modeling
Order as a mutable table in Postgres has limitations:

- **Lost history:** UPDATEs overwrite state; you cannot audit "why was this
  order cancelled" without an ad-hoc log table.
- **Obscured historical invariants:** it is impossible to know every state
  the order went through for backtesting or analysis.
- **Opaque Saga compensation:** the Saga state machine becomes implicit,
  scattered across columns (`status`, `payment_status`,
  `inventory_status`).

## Decision

We apply **Event Sourcing** only to the `Order` aggregate (a deliberately
limited scope, tied to the orchestrated Saga).

## Implementation

- `order_events` table (append-only): `(position BIGSERIAL,
  aggregate_id UUID, event_type TEXT, payload JSONB, metadata JSONB,
  occurred_at TIMESTAMPTZ)`.
- `order_snapshot` table (projection): the aggregate's current materialized
  state, used by queries (`GET /orders/{id}`) and by the orchestrator.
- The `Order` aggregate exposes commands (`CreateOrder`, `ConfirmInventory`,
  `ConfirmPayment`, `CancelWithReason`, etc.) that produce events; the
  instance is reconstituted by replaying events since the last snapshot.
- Snapshot every N events (configurable, e.g. 50) for bounded rehydration.

## Rationale

- **Total auditability:** each transition is an immutable event;
  `order_events` is the source of truth, and `order_snapshot` is just an
  optimized read projection.
- **Explicit compensation:** `OrderInventoryCompensated`,
  `OrderPaymentRefunded`, `OrderCancelled` show up in the stream — auditing
  a failed Saga is just reading the events.
- **Aligns with the Outbox:** the same events the aggregate produces are the
  ones that go to Kafka via the Outbox (ADR-004) — a single concept, two
  roles (local persistence + messaging).
- **Conscious scoping:** applying ES to every aggregate would increase
  boilerplate with no real gain; Product, Inventory, and Payment stay in
  classic CRUD modeling (the pragmatic choice).

## Consequences

- **Positive:**
  - Replay lets us reconstruct state at any point in time.
  - The event schema acts as an evolutive contract (versioned via Avro,
    ADR-007).
  - Trivial Saga bug debugging: read the stream and replay it.
- **Negative:**
  - Reconstitution by replay can be costly; mitigated with snapshots.
  - Schema changes require upcasters / event versioning.
  - CQRS is mandatory for reads (projections) — already planned for
    Product (ADR-006); we reuse the pattern.
- **Mitigations:**
  - Snapshot every 50 events (configurable).
  - Events carry `version` in the payload + Avro schema evolution
    (BACKWARD).
  - Libraries: evaluate Marten (ES + Postgres) or a lightweight manual
    implementation.

## Alternatives considered

- **Classic CRUD with an audit-log table:** simpler, but the log becomes a
  second-class table with fragile synchronization.
- **Marten (document/ES store):** a concrete candidate to replace our
  manual implementation; reduces boilerplate. Left as a spike.
- **ES on every aggregate:** pure dogma — we accept ES where it adds value;
  CRUD where it does not.