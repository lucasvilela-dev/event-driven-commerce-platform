# AGENTS.md — src/services/payment/

Charges/refunds orchestration service. .NET 10.

## Stack

- .NET 10 (`net10.0`)
- MediatR, EF Core + PostgreSQL (database `payment`, ADR-008)
- Polly (circuit breaker + retry) for the outbound payment gateway mock
- Confluent.Kafka + Avro (consumes `payment.commands.charge`,
  `payment.commands.refund`; publishes `payment.events.approved`,
  `payment.events.declined`, `payment.events.refunded`)
- Serilog → Seq (ADR-010)
- StackExchange.Redis for idempotency keys (ADR-009)

## Responsibilities

- Consume `payment.commands.charge` (correlated to an order). Perform a
  (mocked) charge against an external gateway; publish the result.
- Consume `payment.commands.refund` and publish `payment.events.refunded`.
- Persist `payments`, `refunds` rows.
- Idempotency on `charge_id` (= an Order's `aggregate_id`) — never double-charge.

## Outbound gateway

The gateway is mocked for the portfolio. Build behind an interface
(`IPaymentGateway`) and provide an in-memory implementation. Polly wraps the
call with circuit breaker (5 failures / 30s break) and exponential backoff
retry. This lets the portfolio show resilience patterns without a real gateway.

## Mandatory ADRs

- ADR-002 (Kafka topics: `payment.commands.*`, `payment.events.*`)
- ADR-003 (Saga participant — replies to Order)
- ADR-004 (Outbox — publish result events transactionally)
- ADR-007 (Avro)
- ADR-008 (one DB — `payment`)
- ADR-009 (Idempotency — never double-charge)
- ADR-010 (observability)