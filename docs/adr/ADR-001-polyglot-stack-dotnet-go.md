# ADR-001: Polyglot stack — .NET 10 + Go

- **Status:** Accepted
- **Date:** 2026-07-25
- **Decisor:** Portfolio author

## Context

The platform needs to demonstrate architectural maturity in a senior backend
portfolio. Some services require complex patterns (CQRS, Event Sourcing,
orchestrated Saga, Outbox), while others are simple high-throughput consumers
(Inventory, Notification, Shipping).

We considered three paths:

1. .NET 10 across all services.
2. Go across all services.
3. Polyglot stack: .NET 10 for services with complex domain logic + Go for
   lightweight consumers.

## Decision

We adopt **option 3 — polyglot stack**.

- **.NET 10** for: API Gateway (YARP), Identity, Product (CQRS), Order
  (Saga + Outbox + Event Sourcing), and Payment (Polly).
- **Go** for: Inventory, Notification, and Shipping.

## Rationale

- .NET 10 offers a mature ecosystem for DDD/CQRS/ES (MediatR, EF Core,
  Marten for ES) and YARP makes the API Gateway idiomatic and simple.
- Go shines in I/O-bound consumers with fan-out (goroutines), with a low
  footprint and no heavy runtime — ideal for Inventory/Notification/Shipping.
- The split reflects a conscious decision based on the complexity level of
  each service, rather than arbitrary preference.

## Consequences

- **Positive:** the portfolio demonstrates justified polyglotism; each service
  uses the most appropriate tool; smaller footprint for the Go consumers.
- **Negative:** two toolchains for CI (_dotnet_ and _go_), two styles of
  structured logging (Seq ingests both via JSON), and shared Avro contracts
  will have to be compiled in both C# and Go.
- **Mitigations:** contracts centralized in `src/shared/contracts/` (Avro
  + Schema Registry); a single CI pipeline with a per-service job matrix.

## Alternatives considered

- **.NET everywhere:** simplifies CI but does not demonstrate polyglotism, nor
  does it pick the right tool for lightweight consumers.
- **Go everywhere:** valid for consumers, but it would make CQRS/ES/Saga very
  verbose and less idiomatic.