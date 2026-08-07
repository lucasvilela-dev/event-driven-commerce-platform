# Shared contracts (`src/shared/contracts/`)

Versioned Avro schemas (`.avsc`) for every message that crosses a service
boundary via Kafka. Implements **ADR-007** (Avro + Schema Registry with
BACKWARD compatibility). Producers in `.NET` and Go services code-gen or
hand-decode from these files; nothing else is the source of truth for the
on-the-wire shape of a message.

> **Nothing here is business logic.** This folder is the wire contract only.
> Domain rules, Saga orchestration and projections live in the services.

## Naming rules

| Item              | Convention                                                          | Example                                   |
|-------------------|---------------------------------------------------------------------|-------------------------------------------|
| File name         | `<aggregate>_<event>_v<N>.avsc`                                     | `order_created_v1.avsc`                   |
| Folder            | one per topic-prefix aggregate (lowercase)                          | `order/`, `inventory/`, `payment/`, ...   |
| Avro record name  | PascalCase, single event                                            | `OrderCreated`                            |
| Avro namespace    | `com.edcp.<domain>.<events\|commands>`                              | `com.edcp.order.events`                   |
| Schema Registry   | **Subject = `<kafka-topic>-value`**                                 | `order.events-value`                      |
| Compatibility     | `BACKWARD` (default on the registry)                                | adds optional fields, adds union branches |

Multiple events for the **same** aggregate use a single subject that is an
Avro **union** of all event records (`order.events` is the canonical example
— see "Order stream modelling" below).

## Common envelope

All **event** records carry these fields (the Avro payload; `traceparent`
rides as a **Kafka header**, never in the payload, per ADR-010):

| Field           | Avro type                       | Meaning                                        |
|-----------------|--------------------------------|------------------------------------------------|
| `event_id`      | `string` (UUIDv7)              | Globally unique; idempotency key (ADR-009).    |
| `aggregate_id`  | `string`                       | Id of the aggregate that owns the event.       |
| `occurred_at`   | `long` w/ `timestamp-millis`   | Epoch millis UTC when appended to the stream.   |

All **command** records carry:

| Field             | Avro type                     | Meaning                                                          |
|-------------------|-------------------------------|------------------------------------------------------------------|
| `command_id`      | `string` (UUIDv7)            | Idempotency key (also the reply aggregate_id when relevant).     |
| `aggregate_id`    | `string`                     | Id of the originating aggregate (usually the Order saga).        |
| `correlation_id`  | `string`                     | Saga correlation id; copied verbatim onto every reply event.     |
| `reply_to`        | `string` (with default)      | Kafka topic to which the reply should be published.              |
| `issued_at`       | `long` w/ `timestamp-millis` | Epoch millis UTC.                                                |

Money is always `bytes` w/ `logicalType=decimal`, `precision=14`, `scale=2`.
Timestamps are always `long` w/ `logicalType=timestamp-millis`, UTC.

## Order stream modelling

Topic `order.events` is the **Order aggregate's event stream** (ADR-005).
Multiple event types live on it under a single schema-registry subject:

- File `order/order_events_value_v1.avsc` is a `union[...]` of FQNs of the
  individual event records.
- Subject in the registry: `order.events-value`.
- Adding a new Order event type = adding a new branch to the union. Schema
  Registry accepts this as **BACKWARD-compatible** (a new reader can still
  decode older messages — they fall outside the new branch).
- Per-event schemas (`order_created_v1.avsc`, etc.) live alongside the union
  file for human navigation; only the union is registered as the
  `order.events-value` subject.

## Schema → topic → producer/consumer map (v1)

### Events

| File                                              | Topic                              | Subject                                | Producer           | Consumer(s)                                  |
|---------------------------------------------------|------------------------------------|----------------------------------------|--------------------|----------------------------------------------|
| `order/order_events_value_v1.avsc` (union)        | `order.events`                     | `order.events-value`                   | Order Service      | Order Saga (self), Notification, observability |
| `order/order_created_v1.avsc` (member)            | `order.events`                     | (via union)                            | Order Service      | as above                                     |
| `order/order_cancelled_v1.avsc` (member)          | `order.events`                     | (via union)                            | Order Service      | as above                                     |
| `order/order_saga_step_completed_v1.avsc` (member)| `order.events`                     | (via union)                            | Order Saga         | observability                                |
| `inventory/inventory_reserved_v1.avsc`            | `inventory.events.reserved`        | `inventory.events.reserved-value`      | Inventory Service  | Order Saga                                   |
| `inventory/inventory_failed_v1.avsc`              | `inventory.events.failed`          | `inventory.events.failed-value`        | Inventory Service  | Order Saga                                   |
| `inventory/inventory_released_v1.avsc`            | `inventory.events.released`        | `inventory.events.released-value`      | Inventory Service  | Order Saga                                   |
| `payment/payment_approved_v1.avsc`                | `payment.events.approved`          | `payment.events.approved-value`        | Payment Service    | Order Saga, Notification                     |
| `payment/payment_declined_v1.avsc`                | `payment.events.declined`          | `payment.events.declined-value`        | Payment Service    | Order Saga                                   |
| `payment/payment_refunded_v1.avsc`                | `payment.events.refunded`          | `payment.events.refunded-value`        | Payment Service    | Order Saga                                   |
| `shipping/shipping_shipped_v1.avsc`               | `shipping.events.shipped`          | `shipping.events.shipped-value`        | Shipping Service   | Order Saga, Notification                     |
| `shipping/shipping_failed_v1.avsc`                | `shipping.events.failed`           | `shipping.events.failed-value`         | Shipping Service   | Order Saga                                   |
| `notification/notification_sent_v1.avsc`          | `notification.events.sent`         | `notification.events.sent-value`       | Notification Service | observability                               |
| `product/product_created_v1.avsc`                | `product.events.created`           | `product.events.created-value`        | Product Service    | Product projection (self), Notification (later) |
| `product/product_price_updated_v1.avsc`          | `product.events.price-updated`     | `product.events.price-updated-value`  | Product Service    | Product projection (self)                  |
| `product/product_activated_v1.avsc`              | `product.events.activated`         | `product.events.activated-value`      | Product Service    | Product projection (self)                  |
| `product/product_deactivated_v1.avsc`           | `product.events.deactivated`        | `product.events.deactivated-value`    | Product Service    | Product projection (self)                  |

### Commands

| File                                          | Topic                                | Subject                                          | Producer      | Consumer           |
|-----------------------------------------------|--------------------------------------|--------------------------------------------------|---------------|--------------------|
| `order/reserve_inventory_v1.avsc`             | `order.commands.reserve-inventory`   | `order.commands.reserve-inventory-value`         | Order Saga    | Inventory Service  |
| `order/release_reservation_v1.avsc`           | `order.commands.release-reservation` | `order.commands.release-reservation-value`       | Order Saga    | Inventory Service  |
| `payment/charge_v1.avsc`                      | `payment.commands.charge`           | `payment.commands.charge-value`                  | Order Saga    | Payment Service    |
| `payment/refund_v1.avsc`                      | `payment.commands.refund`            | `payment.commands.refund-value`                   | Order Saga    | Payment Service    |
| `shipping/ship_v1.avsc`                       | `shipping.commands.ship`             | `shipping.commands.ship-value`                    | Order Saga    | Shipping Service   |

Kafka headers always include `traceparent` (W3C Trace Context, ADR-010) and
`content-type: application/avro`. Commands additionally carry a `reply_to`
header mirroring the payload field (consumers prefer the header if present).

## Registering a new schema (dev)

The Schema Registry runs at `http://localhost:8081` (Phase 0 stack).
Register a schema with:

```powershell
$body = Get-Content -Raw -LiteralPath "src/shared/contracts/inventory/inventory_reserved_v1.avsc"
$schema = @{ schema = $body } | ConvertTo-Json -Compress
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:8081/subjects/inventory.events.reserved-value/versions" `
  -ContentType "application/vnd.schemaregistry.v1+json" `
  -Body $schema
```

For the union subject (`order.events-value`), the `schema` body must be the
JSON array form (see `order/order_events_value_v1.avsc`). The registry will
resolve references to the named records when those are sent alongside, or you
register the individual records first under their own subjects and reference
them by FQN.

## Evolution cheat-sheet (BACKWARD)

| Change                                                        | Allowed?             |
|---------------------------------------------------------------|----------------------|
| Add a new optional field with a `default`                     | ✅ Yes               |
| Add a new branch to a union (new event type)                  | ✅ Yes               |
| Add a new enum symbol                                         | ✅ Yes (with default) |
| Remove a field                                                | ❌ No (use `default`) |
| Rename a field (use `aliases`)                                | ⚠ Use Avro aliases   |
| Change a field type (e.g. `int` → `long`, widening)           | ⚠ Document each case |
| Remove a union branch / enum symbol                           | ❌ No                |

When in doubt, ask the user before bumping a version. Bumps go into a new
file (`<event>_v2.avsc`) registered as the next version of the same subject.

## Pending subjects (to register when the Phase 0 stack is up)

The first three subjects to validate end-to-end (per `docs/status.md`
"Next 3 concrete steps"):

1. `order.events-value` (union)
2. `inventory.events.reserved-value`
3. `payment.events.approved-value`

The remaining schemas can be registered in the same session once the
registry is reachable. Run `docker compose -f deploy/docker-compose.yml up -d`
to bring it up.

## Next steps in `src/shared/`

- `src/shared/observability/` — `Opencode.TraceContext` (.NET) and
  `tracecontext` (Go) helpers, with unit tests (ADR-010). Pending.