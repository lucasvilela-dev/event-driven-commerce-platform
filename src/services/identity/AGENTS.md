# AGENTS.md — src/services/identity/

Issues JWT/OIDC tokens and manages users/roles. .NET 10 service.

## Stack

- .NET 10 (`net10.0`)
- Duende IdentityServer (or a lightweight JWT issuer if Duende license is over
  the top for the portfolio — pick one and document it in an ADR)
- EF Core + PostgreSQL (database `identity`, ADR-008)
- Serilog → Seq (ADR-010)

## Responsibilities

- Register/authenticate users (email + password; consider social login later).
- Issue access + refresh tokens (JWT).
- Expose `.well-known/openid-configuration` and JWKS.
- Maintain `users`, `roles`, `user_roles` tables in its OWN database.

## Non-responsibilities

- Does not know about orders/products. Authoritative only for auth.
- Does not publish domain events on Kafka (this may change if `UserRegistered`
  becomes a strong contract — flag with an ADR before adding).

## Clean Architecture layers

```
identity/
  Identity.Domain/
  Identity.Application/
  Identity.Infrastructure/
  Identity.Api/
  Identity.Api.Tests/
  tests/Identity.IntegrationTests/
```

File-scoped namespaces, nullable on, `async`/`await` everywhere. MediatR for
commands/queries. EF Core migrations live in `Identity.Infrastructure`.

## Mandatory ADRs

- ADR-008 (one DB per service — `identity` database)
- ADR-010 (observability)
- ADR-001 (.NET 10 conventions)