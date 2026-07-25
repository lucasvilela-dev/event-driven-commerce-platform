# ADR-003: Orchestrated Saga (vs. choreographed) in the Order Service

- **Status:** Accepted
- **Date:** 2026-07-25
- **Decisor:** Portfolio author
- **Related to:** ADR-002 (Kafka), ADR-004 (Outbox), ADR-005 (Event Sourcing)

## Context

The purchase flow (`Order`) involves multiple asynchronous services:

1. **Inventory** must reserve the items.
2. **Payment** must authorize the payment.
3. **Shipping** must schedule the shipment.
4. **Notification** must inform the customer.

Each step can fail and require **compensation** (release stock, refund the
payment, etc.). The decision is: who coordinates the Saga's state machine?

We evaluated:

- **A. Choreographed Saga:** each service reacts to events and publishes the
  next one, with no central orchestrator.
- **B. Orchestrated Saga:** the `Order Service` keeps the state machine and
  sends targeted commands to each service, reacting to response events.

## Decision

We adopt an **orchestrated Saga** inside the `Order Service`.

## Rationale

- **Compensation is non-trivial:** the rollback flow (release stock + refund
  payment + cancel shipment) has dependencies between steps. Choreographing
  this via events makes the code scattered and hard to audit.
- **State-machine visibility:** with orchestration, the order's current state
  is centralized in the Order Service (`OrderSagaState`) — easy to query
  (`GET /orders/{id}/saga`) and to debug.
- **Compatible with Event Sourcing:** the orchestrator emits commands and
  reacts to events; every Saga state transition is also a persisted event
  (see ADR-005), enabling replay and audit.
- **Physical decoupling via Kafka:** even while orchestrating, the Order
  Service does not call Inventory/Payment over HTTP — it publishes commands
  to `*.commands` topics and consumes `*.events`. That is, logical
  coordination, physical decoupling.
- **Conscious trade-off:** we accept centralized logical coupling in exchange
  for clarity, observability, and auditability of the purchase flow.

## Consequences

- **Positive:**
  - Compensation logic encapsulated in a single aggregate (`OrderSaga`).
  - Trivial audit: Saga events in ES show the whole path.
  - Easy to add timeout/retry policies in the orchestrator (Hangfire/Quartz).
- **Negative:**
  - The Order Service becomes the "brain" — a logical single point of failure
    (but physically stateless: state lives in Kafka/Postgres).
  - Risk of becoming a God Service without discipline (mitigated below).
- **Mitigations:**
  - The orchestrator holds **only the Saga state machine**; domain-specific
    rules (stock rules, anti-fraud) live in the responsible services.
  - The topic structure keeps `*.commands` (targeted) and `*.events`
    (broadcast) separate, preventing the orchestrator from becoming a hub
    for everything.

## Alternatives considered

- **Choreographed Saga:** simpler at first, but distributed compensation is
  fragile and hard to trace. Suitable for linear flows without complex
  rollback — not the case here.
- **External workflow engines (Temporal, Camunda):** would extract the state
  machine from the Order Service. Overkill for a portfolio and would add a
  heavy dependency to the `docker-compose`.