# AGENTS.md — src/services/order/

The most complex service in the platform. Owns the **Order aggregate** with
**Event Sourcing**, runs the **orchestrated Saga**, and publishes events via the
**Outbox**.

## Stack

- .NET 10 (`net10.0`)
- MediatR (command/query separation — both sides live here, CQRS applies
  although the durable write side is ES)
- EF Core + PostgreSQL (database `order`, ADR-008) for `order_events` and
  `order_snapshot` (ADR-005); also `outbox` (ADR-004)
- Confluent.Kafka + Avro (ADR-007) — key producer AND consumer of `*.events`
  responses
- StackExchange.Redis for read-model projections (`order_query`) and for
  idempotency keys on consumed events (ADR-009)
- Serilog → Seq (ADR-010)
- Polly for retry when publishing back to Kafka / reading snapshots

## Responsibilities

- Create an Order, validate invariants, emit `OrderCreated`.
- Drive the Saga state machine:
  `OrderCreated → InventoryPending → InventoryReserved → PaymentPending →
   PaymentApproved → ShippingPending → Shipped → OrderConfirmed`
  with compensation branches on any failure (`OrderCancelled`,
  `InventoryCompensated`, `PaymentRefunded`).
- Consume `inventory.events.reserved`, `payment.events.approved`,
  `shipping.events.shipped` and advance the Saga.
- Publish domain events via the Outbox in the same transaction as the event
  store append.
- Serve reads (`GET /orders/{id}`, `GET /orders/{id}/saga`) from the
  `order_query` projection (materialized view rebuilt from events).

## Clean Architecture layers

```
order/
  Order.Domain/              ← aggregate Order, OrderSaga, value objects
  Order.Application/         ← MediatR commands/queries + Saga orchestrator
  Order.Infrastructure/      ← ES store, outbox publisher, Kafka producer
  Order.Api/                 ← ASP.NET, DI composition root, Kafka consumer
  Order.Projections/         ← order_query projection worker
  tests/
    unit/
    integration/             ← Testcontainers (Postgres + Kafka + Redis)
```

## Saga orchestrator rules

- State transitions are functions: `(state, event) → (newState, [commands])`.
- Commands produced: `order.commands.reserve-inventory`,
  `payment.commands.charge`, `shipping.commands.ship`. Each carries
  `correlation_id = order.aggregate_id` and `reply_to` topic.
- Timeouts: scheduled check for events not arriving within N seconds emits
  `OrderSagaTimedOut` → compensations.
- Every Saga transition is an event appended to the Order stream
  (`OrderSagaWaiting`, `OrderSagaStepCompleted`, `OrderSagaCompensated`,
  `OrderSagaCompleted`, `OrderSagaFailed`) — full audit trail via ES.

## Idempotency

- Consumed events checked against Redis `idempotency:order:{event_id}`
  (ADR-009). Skip silently if present.

## Mandatory ADRs

- ADR-003 (orchestrated Saga) ← read this BEFORE touching `OrderSaga`
- ADR-004 (Outbox)
- ADR-005 (Event Sourcing on Order)
- ADR-006 (CQRS — read projection, same pattern as Product)
- ADR-007 (Avro + Schema Registry)
- ADR-008 (one DB per service — `order`)
- ADR-009 (Idempotency)
- ADR-010 (observability — Order is where most trace correlation happens)

## Anti-patterns to avoid

- Calling Inventory/Payment over HTTP. Use Kafka commands only (ADR-002/003).
- Mutating `order_snapshot` directly. Always append events; let the projection
  update the snapshot (ADR-005).
- Putting compensation logic in Inventory/Payment. Compensations are
  orchestrated from here; downstream services only know how to "undo their own
  step" on command (`inventory.commands.release-reservation`,
  `payment.commands.refund`).