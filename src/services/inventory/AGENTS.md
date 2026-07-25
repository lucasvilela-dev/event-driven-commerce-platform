# AGENTS.md — src/services/inventory/

Go service that reserves/releases stock on demand and publishes reservation
events. Participates in the Saga by responding to Order.

## Stack

- Go (latest stable), module path `github.com/<owner>/edcp/inventory`
- `segmentio/kafka-go` for Kafka
- `go-redis/v9` for idempotency keys (ADR-009)
- `rs/zerolog` → Seq CLEF (ADR-010)
- PostgreSQL via `jackc/pgx/v5` (database `inventory`, ADR-008)

## Responsibilities

- Consume `order.commands.reserve-inventory` → check stock → reserve → publish
  `inventory.events.reserved` OR `inventory.events.failed`.
- Consume `order.commands.release-reservation` — releases previously reserved
  stock (compensation). Publish `inventory.events.released`.
- Persist `products_stock` (aggregate per SKU) and `reservation_ledger`
  (idempotent on `command_id`).
- Orchestration lives in Order (ADR-003); this service only knows how to
  reserve and release its own state.

## Idempotency (ADR-009)

- Use `SET idempotency:inventory:{event_id} NX EX 86400` before processing.
- On duplicate, skip silently (fact already applied).
- `command_id` from each command is the idempotency key; a second reservation
  for the same `command_id` is a no-op.

## Project layout (idiomatic Go)

```
inventory/
  cmd/inventory/main.go      ← entrypoint, wiring
  internal/
    consumer/                ← kafka consumer loop
    domain/                  ← stock aggregate, reservation
    store/                   ← pgx repository
    publisher/              ← kafka producer + outbox (local)
  go.mod
  go.sum
```

- Internal packages are not exported across module boundaries.
- No business logic in `cmd/`; it wires only.
- Tests beside source files (`*_test.go`); integration tests use Testcontainers
  (Go version) hitting real Kafka/Postgres/Redis.

## Outbound events

Inventory is also a publisher. It uses the **same Outbox pattern** described in
ADR-004 (translated to Go: a `outbox` table polled by a goroutine). The Outbox
must be in the Inventory's own Postgres database so atomicity holds.

## Mandatory ADRs

- ADR-002 (Kafka topics)
- ADR-003 (Saga participant)
- ADR-004 (Outbox — Inventory publishes too)
- ADR-007 (Avro — decode incoming, encode outgoing via goavro)
- ADR-008 (one DB — `inventory`)
- ADR-009 (Idempotency)
- ADR-010 (observability — read `traceparent` from Kafka headers into zerolog
  context)