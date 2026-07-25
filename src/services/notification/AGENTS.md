# AGENTS.md — src/services/notification/

Go service — pure **fire-and-forget** fan-out: email, SMS, push. Never
participates in the Saga, never replies.

## Stack

- Go, module path `github.com/<owner>/edcp/notification`
- `segmentio/kafka-go`
- `rs/zerolog` → Seq CLEF (ADR-010)
- `rs/redis/v9` for idempotency keys (ADR-009)
- Outbound channels are mocked: providers live behind interfaces and run as
  goroutines (fan-out is idiomatic Go).

## Responsibilities

- Consume `order.events.*` (OrderConfirmed, OrderCancelled, OrderShipped),
  `payment.events.approved`, `notification.commands.send`.
- Build the notification payload and fan-out across enabled channels
  (email/SMS/push) concurrently using goroutines + `errgroup`.
- Persist `notifications` row with per-channel status.
- Publish `notification.events.sent` for observability (not consumed by Saga).

## Non-responsibilities

- **Never replies into the Saga.** If a notification fails, the order flow is
  not affected — only a retry/redelivery schedule for the notification.
- Does not own user identity; reads contact info from the event envelope or by
  calling Identity over HTTP (allowed only because it's read-only metadata; the
  Saga path never touches this).

## Concurrency model

- A worker pool consumes events from Kafka (one consumer per partition).
- Per event, spawn a goroutine per channel. Use `errgroup.Group` bound to a
  context with timeout (default 10s). Failures do not NACK the parent event —
  we ACK and log to Seq, then enqueue a retry row in `notification_outbox`.

## Project layout

```
notification/
  cmd/notification/main.go
  internal/
    consumer/
    channels/        ← email.go, sms.go, push.go (each implements Channel)
    domain/
    store/
  go.mod
```

## Mandatory ADRs

- ADR-002 (Kafka topics — broadcasts only)
- ADR-007 (Avro decode)
- ADR-009 (Idempotency — never send the same notification twice per event_id)
- ADR-010 (observability)