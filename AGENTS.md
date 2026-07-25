# AGENTS.md — global guidance for opencode and other coding agents

This file gives every AI coding agent (opencode, Cursor, etc.) working in this
repository a small, reliable set of rules. **Read it before touching anything.**
Each service directory also has its own `AGENTS.md` with service-specific
conventions; reconcile them with this file (local wins on conflicts).

The project lives at `C:\Users\lucas\Desktop\Portifolio\event-driven-commerce-platform`
and is a portfolio-grade event-driven commerce platform. Treat it like a real
production codebase, not a throwaway demo — code, tests, docs and CI all matter.

## 1. Language policy

- **Code, comments, commit messages, ADR titles/body, READMEs, runbooks,
  AGENTS.md, issue templates: all in English.** This is non-negotiable and
  applies to every service, no matter the language (.NET or Go).
- Do not leave Portuguese strings in files. If you find any, translate them.
- Date format: ISO `YYYY-MM-DD`. Timezone always UTC in logs.

## 2. Where decisions live

Architecture decisions are captured as **ADRs** in `docs/adr/`. Read the
`docs/adr/README.md` index first. ADRs 001–010 are already accepted and govern
the choices below. Before proposing a new pattern that contradicts an ADR, write
a new ADR with `Supersedes:` pointing at the old one — do not silently ignore
ADR decisions in code.

Mandatory reads before working on any service:
- ADR-001 Polyglot stack (.NET 10 + Go)
- ADR-002 Kafka (KRaft) as messaging backbone
- ADR-007 Avro + Schema Registry for event contracts
- ADR-010 Observability via Seq + W3C trace context

Mandatory reads for the specific services:
- Order Service: ADR-003 (orchestrated saga), ADR-004 (outbox), ADR-005 (ES)
- Product Service: ADR-006 (CQRS + Redis read models)
- All consumers (Inventory/Payment/Notification/Shipping): ADR-009 (idempotency)
- Any service writing to Postgres: ADR-008 (one DB per service)

## 3. Repository layout (do not reorganize without a new ADR)

```
docs/adr/           Architecture Decision Records
docs/architecture/  PlantUML/Mermaid diagrams
docs/runbook/       operational guides
src/api-gateway/    .NET 10 — YARP
src/services/
  identity/   product/  order/  payment/   ← .NET 10 (.csproj)
  inventory/  shipping/ notification/      ← Go (go.mod)
src/shared/
  contracts/      Avro schemas (.avsc) shared across services
  observability/  shared logging/tracing helpers
deploy/
  docker-compose.yml
  postgres/init-multi-db.sh
.github/workflows/ CI
```

Never move a service from `services/` to somewhere else. Never create shared
application libraries outside `src/shared/` (and keep `src/shared/` thin — it
must stay free of business logic).

## 4. Cross-service contracts

- Every event on Kafka has a schema in `src/shared/contracts/` (Avro,
  `.avsc`). Producers cannot invent new fields without updating the schema and
  registering it with Schema Registry under BACKWARD compatibility (ADR-007).
- Never share database tables, ORMs entities, or DTOs across services. A service
  may only reach another service through Kafka topics or through the public HTTP
  surface defined by that service's API.
- Event payload includes `event_id` (UUIDv7), `aggregate_id`, plus the versioned
  Avro fields. `traceparent` (W3C Trace Context) rides as a Kafka **header**, not
  in the payload.

## 5. Topic naming convention

- `*.commands` → directed message (exactly one logical consumer, has
  `correlation_id` and `reply_to`).
- `*.events`   → published fact (broadcast, immutable, multiple consumers).
- Concrete topics to respect: `order.commands.reserve-inventory`,
  `inventory.events.reserved`, `payment.commands.charge`,
  `payment.events.approved`, `order.events` (the Order stream / Saga log),
  `notification.events.sent`.

## 6. Coding standards

- **.NET 10 services**: file-scoped namespaces, nullable reference types on,
  `async`/`await` all I/O, MediatR for CQRS, EF Core for persistence, Serilog
  sinks to Seq, Polly for outbound resilience. Target `net10.0`. Project layout
  follows Clean Architecture layers (Domain / Application / Infrastructure /
  Api) — see each service's own AGENTS.md.
- **Go services**: `go mod` with the latest stable module path
  `github.com/<owner>/edcp/<service>`, idiomatic Go (effective Go + CodeReview
  Comments), zerolog → Seq CLEF, `segmentio/kafka-go` for Kafka, `go-redis/v9`
  for Redis. Package `internal/` for non-exported code; `cmd/<service>/main.go`
  entrypoint.
- **No comments unless asked**: the convention here is that code should be
  self-documenting. Only add comments where genuinely non-obvious. ADRs and
  runbooks are where justification goes, not inline comments.
- **Tests**: unit tests next to the code; integration tests via Testcontainers.
  No test, no merge for any non-trivial PR.

## 7. Commit policy

- Only commit when the user explicitly asks. When you do, write concise English
  commit messages in conventional-commits style: `feat(order): add outbox publisher`,
  `fix(inventory): handle duplicate reserve command`, `docs(adr): add ADR-011`.
- Stage only files you actually changed. Never commit secrets, connection strings
  or personal tokens.
- This repo is currently **not** a git repo (`git status` fails); when the user
  asks to init git, do it then.

## 8. Local platform

Most operations expect the platform stack (Kafka, Postgres, Redis, Seq) to be
up. Bring it up from the repo root:

```powershell
docker compose -f deploy/docker-compose.yml up -d
docker compose -f deploy/docker-compose.yml ps
```

Service host ports (see README for the full table): Kafka `9092`, Schema
Registry `8081`, Kafka UI `8080`, Postgres `15432` (user `edcp`, pw
`edcp_dev`, DBs `identity/product/order/payment/inventory`), Redis `6379`, Seq
`8082`.

When asked to "validate the stack":
```powershell
docker compose -f deploy/docker-compose.yml ps --format "table {{.Name}}\t{{.Status}}"
docker exec edcp-postgres psql -U edcp -d postgres -tAc "SELECT datname FROM pg_database WHERE datistemplate=false ORDER BY 1;"
docker exec edcp-kafka kafka-topics --bootstrap-server localhost:9092 --list
```
All containers should show `(healthy)`.

## 9. Secrets and configuration

- No secrets in code or in `docker-compose.yml`. Use user-secrets (dev) /
  env vars / Helm secrets (prod).
- Local dev default creds (`edcp_dev`) are fine for the compose stack only —
  never reuse them for anything that leaves the laptop.
- If you accidentally capture a secret, tell the user immediately.

## 10. When you're stuck

- Re-read the relevant ADR(s).
- Check `docs/runbook/` if it covers the scenario.
- Prefer asking the user a crisp question over inventing an architectural choice.
- New architectural question → propose an ADR draft, don't just write code.