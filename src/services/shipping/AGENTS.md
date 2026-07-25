# AGENTS.md — src/services/shipping/

Go service that schedules shipments after payment is approved. Participates in
the Saga by emitting `shipping.events.shipped`.

## Stack

- Go, module path `github.com/<owner>/edcp/shipping`
- `segmentio/kafka-go`
- `rs/zerolog` → Seq CLEF (ADR-010)
- `rs/redis/v9` for idempotency (ADR-009)
- PostgreSQL via `pgx/v5` (database `shipping` — to be added to the compose
  init script; current `init-multi-db.sh` covers identity/product/order/payment
  /inventory, so add `shipping` if this service starts persisting)

## Responsibilities

- Consume `shipping.commands.ship` (correlated to an order).
  Event payload includes shipping address + items summary (read from the
  event, not from the Order DB — ADR-008 forbids cross-DB reads).
- Call a mocked carrier API (`IShippingCarrier`) to get a tracking code.
- Publish `shipping.events.shipped` with tracking code.
- Persist `shipments` row with status + tracking code + order_id.

## Saga participation (ADR-003)

- Shipping is the **last step** of the Saga. On failure, publish
  `shipping.events.failed` — Order orchestrates the compensation cascade
  (refund payment, release inventory), Shipping only emits the failure fact.
- No `shipping.commands.cancel` for the MVP; if a shipment can be cancelled
  pre-dispatch, document in a runbook and add a new contract.

## Idempotency

- `SET idempotency:shipping:{event_id} NX EX 86400` before processing.
- Duplicate `ship` command → skip and re-publish the original `shipped` event
  (idempotency-by-replay) so Order never blocks.

## Project layout

```
shipping/
  cmd/shipping/main.go
  internal/
    consumer/
    domain/
    carrier/         ← mocked IShippingCarrier implementation
    store/
    publisher/
  go.mod
```

## Mandatory ADRs

- ADR-002 (Kafka topics: `shipping.commands.ship`, `shipping.events.shipped`)
- ADR-003 (Saga participant — final step)
- ADR-004 (Outbox — publishes results transactionally)
- ADR-007 (Avro)
- ADR-008 (one DB per service — note: add `shipping` to `init-multi-db.sh`
  before this service connects)
- ADR-009 (Idempotency)
- ADR-010 (observability)