# Status

> **Living document.** Updated at the end of every work session.
> Read this *before* `docs/roadmap.md` to know where we are right now.
> Keep entries short — full reasoning lives in the ADRs, full plan lives in
> the roadmap, tactical twists live in `docs/decisions-log.md`.

## Current phase

**Phase 1 — Shared contracts & observability helpers** (`[next]` in the roadmap)

Start date: not started yet.
Active step: — (pick the first Avro schema to write).

## What's done

- ✅ **Phase 0 — Platform stack** (`2026-07-25`)
  - `docker-compose.yml` with Kafka (KRaft), Schema Registry, Postgres (5
    DBs), Redis, Seq, Kafka UI — all healthy.
  - Postgres host port remapped to `15432` to avoid local conflict.
  - Seq no-auth enabled in dev (`SEQ_FIRSTRUN_NOAUTHENTICATION=true`).
  - ADRs 001-010 written and accepted; ADR-011/012/013 placeholders in the
    index for CI, tests, K8s.
  - Root `AGENTS.md` + one `AGENTS.md` per service (api-gateway, identity,
    product, order, payment, inventory, notification, shipping, shared).
  - Stack choice: .NET 10 for .NET services (updated from 8 on
    `2026-07-25`), Go for lightweight consumers.
  - Repo initialized on `develop` branch:
    https://github.com/lucasvilela-dev/event-driven-commerce-platform
    (commit `411a39e`).

## Next 3 concrete steps

1. **Draft the Avro schemas** for the core order flow:
   `order_created_v1.avsc`, `inventory_reserved_v1.avsc`,
   `payment_approved_v1.avsc` — enough to register the first three
   `<topic>-value` subjects in Schema Registry with BACKWARD compatibility.
2. **Write `src/shared/contracts/README.md`** listing the schema → topic →
   producer/consumer mapping, so future agents can navigate it.
3. **Implement `OpenCode.TraceContext` (.NET) + `tracecontext` (Go) helpers**
   in `src/shared/observability/` with unit tests; trace propagation is
   cheap now, expensive to retrofit later.

## Blockers

None.

## Open questions

- Whether Identity should use **Duende IdentityServer** (full OIDC) or a
  lightweight hand-rolled JWT issuer. Duende is more impressive for
  portfolio but heavier; decide in Phase 2 with a mini-ADR.
- Whether Order's Event Sourcing should use **Marten** or a hand-rolled
  append-only store. Defer to Phase 4; spike both.

## Decisions made this session

- Adopted **roadmap-driven development** with `docs/roadmap.md` (plan) +
  `docs/status.md` (state) + `docs/decisions-log.md` (tactical).
- Adopted **.NET 10** as the target framework (was .NET 8 in initial draft).

## Stack versions currently in use

| Component | Version | Notes |
|---|---|---|
| .NET | 10 | target for `Identity`, `Product`, `Order`, `Payment`, API Gateway |
| Go | latest stable | target for `Inventory`, `Notification`, `Shipping` |
| Kafka | Confluent `cp-kafka 7.6.1` (KRaft) | single broker dev; 3 brokers prod |
| Schema Registry | `cp-schema-registry 7.6.1` | BACKWARD compatibility default |
| Postgres | `16-alpine` | one DB per service |
| Redis | `7-alpine` | read models + idempotency |
| Seq | `datalust/seq:latest` | no auth in dev |

## How to update this file

At the end of a session:

1. Move the **current phase** to **What's done** with a date stamp if it
   passed all acceptance criteria; otherwise leave it in **Current phase**
   and update the **Active step**.
2. Rewrite **Next 3 concrete steps** from the roadmap.
3. Add any **Decisions made this session**.
4. Append a one-line entry to **Decisions log** (see
   `docs/decisions-log.md`).
5. Do not edit history. Strike out superseded items if you must, but do not
   delete them.