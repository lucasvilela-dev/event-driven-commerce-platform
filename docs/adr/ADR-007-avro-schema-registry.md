# ADR-007: Avro + Schema Registry for versioned contracts

- **Status:** Accepted
- **Date:** 2026-07-25
- **Decisor:** Portfolio author
- **Related to:** ADR-002 (Kafka), ADR-001 (polyglot .NET + Go)

## Context

The platform is polyglot (ADR-001): the .NET 10 producers and the Go
consumers must agree on the format of Kafka events. Without a contract
system:

- Breaking a field in a producer can take down every consumer.
- There is no central schema discovery; each team/document becomes the
  source of truth — fragile.
- Schema evolution (adding, renaming, deleting a field) becomes "best-effort".

## Decision

We adopt **Apache Avro** as the serialization format and **Confluent Schema
Registry** as the central versioned schema repository.

## Implementation

- The Avro schemas (`.avsc`) live in `src/shared/contracts/` and are
  versioned in git, e.g. `order/order_created_v1.avsc`.
- Publish: the producer registers the schema in the Schema Registry; Kafka
  stores only the `schema_id` (int) + the binary payload as the value,
  saving space.
- Consumer: reads the `schema_id`, fetches the schema from the Registry,
  and deserializes.
- **Compatibility configuredured as BACKWARD:** new producers can add
  optional fields (with default) without breaking older consumers.
- CI (GitHub Actions) validates that new schemas pass the compatibility
  rule before the build.

## Topic naming convention

- `*.commands` → targeted message (exactly one logical consumer; carries
  `correlation_id` and `reply_to`).
- `*.events` → published fact (broadcast, multiple consumers, immutable).
- Examples:
  - `order.commands.reserve-inventory`
  - `inventory.events.reserved`
  - `payment.commands.charge`
  - `payment.events.approved`
  - `notification.events.sent`

The W3C trace context (`traceparent`) is propagated as a **Kafka header**
(not in the Avro payload) for span correlation.

## Rationale

- **Explicit shared contract:** `shared/contracts/` is the central point —
  both the .NET and Go teams generate code from the same `.avsc`.
- **Native versioning:** each schema has a subject + version
  (`order-created` v1, v2, ...) — auditable and comparable.
- **Controlled evolution:** BACKWARD compatibility guarantees that newly
  added fields do not break deployed consumers — essential with incremental
  deploys.
- **Binary efficiency:** Avro is more compact than JSON and stores a
  schema_id instead of an inline schema — useful at high throughput.
- **Maturity:** ready-made integration with .NET (Confluent.Kafka +
  AvroGen) and Go (schemaregistry-client + linkedin/goavro).

## Consequences

- **Positive:**
  - CI blocks breaking-change bugs before merge.
  - Living event documentation (the `.avsc` is the doc).
  - Polyglot producer/consumer support with no manual reconciliation.
- **Negative:**
  - An extra infra component (Schema Registry) — already included in the
    docker-compose, so low cost.
  - Learning curve for the evolution rules (union, default, aliases).
  - Non-trivial code generation in Go (generate bindings via goavro or use
    a generic handler).
- **Mitigations:**
  - Decision: use a binding generator in .NET
    (`Confluent.SchemaRegistry.Serdes.Avro`) and a manual map-based decoder
    in Go — more flexible.
  - Document the evolution policy in `docs/runbook/schema-evolution.md`.

## Alternatives considered

- **JSON without schema:** simple but no contract — a change silently breaks
  consumers. Inadequate for a senior portfolio.
- **Protobuf + buf registry:** richer schema (enums, oneof, tagged fields)
  but requires a separate Buf Schema Registry and does not integrate with
  the Schema Registry already deployed with Kafka.
- **MessagePack + manual schema:** faster than JSON but no registry and no
  automated evolution — loses the differentiator.