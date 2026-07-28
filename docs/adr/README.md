# Architecture Decision Records

Index of project ADRs. Every relevant architectural decision has its own
ADR, following a *lite* [MADR](https://adr.github.io/madr/) format
(Context, Decision, Rationale, Consequences, Alternatives).

Discipline: a new important decision → a new ADR before the code; a decision
that is revoked → a new ADR with a **Supersedes** field pointing at the
previous one (do not edit the revoked ADR; it is history).

## Accepted ADRs

| ID | Title | Status | Date |
|---|---|---|---|
| [ADR-001](ADR-001-polyglot-stack-dotnet-go.md) | Polyglot stack — .NET 10 + Go | Accepted | 2026-07-25 |
| [ADR-002](ADR-002-kafka-kraft-backbone.md) | Kafka (KRaft) as the messaging backbone | Accepted | 2026-07-25 |
| [ADR-003](ADR-003-orchestrated-saga.md) | Orchestrated Saga (vs. choreographed) in the Order Service | Accepted | 2026-07-25 |
| [ADR-004](ADR-004-outbox-pattern.md) | Outbox Pattern for commit/event atomicity | Accepted | 2026-07-25 |
| [ADR-005](ADR-005-event-sourcing-order.md) | Event Sourcing applied to the Order aggregate | Accepted | 2026-07-25 |
| [ADR-006](ADR-006-cqrs-product-read-models.md) | CQRS in the Product Catalog with read models + Redis | Accepted | 2026-07-25 |
| [ADR-007](ADR-007-avro-schema-registry.md) | Avro + Schema Registry for versioned contracts | Accepted | 2026-07-25 |
| [ADR-008](ADR-008-one-database-per-service.md) | One PostgreSQL database per service (DDD dogma) | Accepted | 2026-07-25 |
| [ADR-009](ADR-009-idempotency-redis.md) | Idempotency via Redis in .NET and Go consumers | Accepted | 2026-07-25 |
| [ADR-010](ADR-010-observability-seq-tracecontext.md) | Observability: Seq + W3C trace context | Accepted (impl. amended by ADR-015) | 2026-07-25 |
| [ADR-014](ADR-014-identity-hand-rolled-jwt-issuer.md) | Hand-rolled JWT issuer (vs. Duende IdentityServer) | Accepted | 2026-07-27 |
| [ADR-015](ADR-015-native-distributed-tracing-propagators.md) | Native distributed-tracing propagators (.NET `Activity` + Go OTel `propagation`) | Accepted | 2026-07-26 |

## Planned ADRs (future)

- **ADR-011:** CI/CD with GitHub Actions (matrix per service/language).
- **ADR-012:** Testing strategy (unit + integration with Testcontainers +
  contract tests via Avro).
- **ADR-013:** Kubernetes deploy (Helm) — when evolving from Docker Compose.

## Quick dependency map across ADRs

```
ADR-001 (polyglot stack) ─┬─► ADR-007 (Avro: shared contracts)
                          └─► ADR-010 (unified observability via Seq)

ADR-002 (Kafka backbone)  ─┬─► ADR-004 (Outbox: publish durability)
                          ├─► ADR-009 (Idempotency: at-least-once)
                          └─► ADR-007 (Avro: serialization + registry)

ADR-003 (orchestrated Saga) ─► ADR-005 (ES: the state machine is the stream)

ADR-005 (Event Sourcing)  ─► ADR-006 (CQRS: read projections)
ADR-006 (CQRS)            ─► ADR-008 (1 DB per service)
```

## How to propose a new ADR

1. Create `docs/adr/ADR-XXX-title-kebab-case.md` using ADR-001 as a template.
2. Fill in: Status, Date, Decisor, Context, Decision, Rationale,
   Consequences (positive/negative/mitigations), Alternatives.
3. Add a row to the table above.
4. An ADR with `Supersedes: ADR-XXX` revokes the previous one; do not edit
   the revoked ADR — it is history.