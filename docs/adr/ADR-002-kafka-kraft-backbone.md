# ADR-002: Kafka (KRaft) as the messaging backbone

- **Status:** Accepted
- **Date:** 2026-07-25
- **Decisor:** Portfolio author
- **Supersedes:** —
- **Related to:** ADR-007 (Avro + Schema Registry), ADR-003 (orchestrated Saga)

## Context

The platform needs a message broker that acts as the **event-driven
backbone**, connecting .NET services (domain producers) to Go consumers
(Inventory/Notification/Shipping), ensuring durability, replay, and temporal
decoupling.

We evaluated three market options:

1. **Apache Kafka** (KRaft mode, no ZooKeeper).
2. **RabbitMQ** (exchange/queue, AMQP 0.9.1).
3. **NATS JetStream** (lighter, cloud-native).

## Decision

We adopt **Apache Kafka in KRaft mode** (single broker for the dev
environment via Docker Compose; multi-broker would be the natural step in
production/Helm).

## Rationale

- **Durable log + replay:** each event is an immutable record in the log;
  consumers can reprocess history (essential for Event Sourcing in `Order`
  and for rebuilding read models in `Product`). RabbitMQ discards the
  message after ack.
- **Consumer groups:** horizontal scaling of Go consumers (Inventory,
  Notification) with no extra code — partition + group do the sharding.
- **Pub/Sub and Command (request/reply via headers)** on the same broker;
  we segregate by naming (`*.events` vs `*.commands`) — see ADR-007.
- **KRaft mode** removes ZooKeeper, reducing footprint and setup/dev
  complexity, and is the official path for Kafka since 3.3+.
- **Ecosystem maturity:** Schema Registry, kafka-ui, and robust, documented
  .NET (Confluent.Kafka) and Go (segmentio/kafka-go) connectors.
- For a senior portfolio, Kafka demonstrates knowledge of partitioning,
  retention, exactly-once via transactions, and integration with Schema
  Registry.

## Consequences

- **Positive:**
  - Event replay enables audit and projection rebuilds.
  - Go consumers scale by consumer group with no code change.
  - A single broker in KRaft simplifies the `docker-compose.yml`.
- **Negative:**
  - A single broker is an SPOF in dev; production would require 3 brokers +
    RF=3.
  - Kafka's operational overhead is higher than RabbitMQ (JVM, tuning of
    memory/partitions/retention).
  - Higher publish latency than NATS for synchronous paths — mitigated by
    using Kafka only for the asynchronous flow.
- **Mitigations:** set `KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1` for dev;
  in production, Helm with 3 brokers via StatefulSet.

## Alternatives considered

- **RabbitMQ:** simpler, great for routing/exchanges/DLQ and request/reply,
  but it does not natively support event replay — strong impact on Event
  Sourcing (ADR-005).
- **NATS JetStream:** light and fast, but a smaller ecosystem and no native
  Schema Registry — weakens versioned contracts (ADR-007).
- **Kafka + ZooKeeper (classic):** stable but adds a process to manage
  with no real benefit for a portfolio — KRaft is the future of Kafka.