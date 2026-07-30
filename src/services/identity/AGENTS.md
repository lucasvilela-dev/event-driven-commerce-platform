# AGENTS.md — src/services/identity/

Issues JWT tokens and manages users/roles for the platform. .NET 10 service.

## Stack

- .NET 10 (`net10.0`), file-scoped namespaces, nullable on, `async`/`await`
  everywhere, MediatR for commands/queries.
- **Hand-rolled JWT issuer** (decision landed in ADR-014; not Duende).
  RS256 signing key from a PEM file in dev, mounted secret in compose.
  `ITokenIssuer` lives in `Identity.Application`; `SigningKeyProvider`
  in `Identity.Infrastructure`.
- `IPasswordHasher<ApplicationUser>` from
  `Microsoft.Extensions.Identity.Core` for password hashing — we *use the
  hasher*, not the full ASP.NET Identity membership stack. No
  `IdentityDbContext`, no `UserManager`, no lockout/2FA tables.
- EF Core 9 + Npgsql (Postgres, database `identity`, ADR-008).
- Serilog → Seq with `ServiceName=identity` plus the `ActivityEnricher`
  from `OpenCode.TraceContext` (ADR-010 as amended by ADR-015).
- `TraceContext.AlwaysSample()` is called once at startup (per ADR-015
  follow-up).

## Responsibilities

- Register/authenticate users (email + password; social login deferred).
- Issue access (15m) + refresh (7d) JWTs at `/login`. Claims: `sub`,
  `email`, `roles`, plus a `token_type` claim distinguishing access vs
  refresh. Refresh-token rotation is *deferred* to a later phase (see
  `docs/roadmap.md` deferred-from-Phase-2 note); Phase 2 only *issues*
  refresh JWTs.
- Expose `/.well-known/openid-configuration` (minimal, non-conformant —
  only `issuer` + `jwks_uri` + signing alg) and `/.well-known/jwks`
  (RFC 7517 + RFC 7638 `kid`).
- Maintain `users`, `roles`, `user_roles` tables in its OWN database.

## Non-responsibilities

- Does not know about orders/products. Authoritative only for auth.
- Does not publish domain events on Kafka (this may change if
  `UserRegistered` becomes a strong contract — flag with an ADR before
  adding).
- Does not validate downstream JWTs — that's the API Gateway's job
  (Phase 8) using `AddJwtBearer().Authority = https://localhost:5001`.

## Clean Architecture layers

```
identity/
  Identity.sln
  Identity.Domain/                ← Entities, ValueObjects (no deps)
  Identity.Application/           ← Abstractions, Commands, Options, MediatR
  Identity.Infrastructure/        ← Persistence (DbContext, Configurations,
  │                                  Migrations), Services (TokenIssuer,
  │                                  PasswordHasherAdapter, SigningKeyProvider)
  Identity.Api/                    ← Program.cs, Controllers, appsettings
  Identity.Api.Tests/             ← unit tests (xUnit + NSubstitute +
  │                                  FluentAssertions) — covers Application +
  │                                  Domain
  tests/Identity.IntegrationTests/ ← Testcontainers Postgres + WebFactory
                                     — register→login→token-validation flow
```

EF Core migrations live in `Identity.Infrastructure`
(`dotnet ef migrations add ...` from the Api project; the DbContext is
registered with `MigrationsAssembly = typeof(IdentityDbContext).Assembly`).

## Mandatory ADRs

- ADR-008 (one DB per service — `identity` database)
- ADR-010 as amended by ADR-015 (observability + native propagators)
- ADR-014 (hand-rolled JWT issuer — *this* service's signature choice)
- ADR-001 (.NET 10 conventions)

## Local ports

- Dev HTTPS: `https://localhost:5001` (must match `Jwt:Issuer` in
  `appsettings.json`; downstream validators point `Authority` here).
- Dev HTTP: `http://localhost:5000`.
- Postgres: `localhost:15432`, DB `identity`, user `edcp` / `edcp_dev`.
- Seq: `http://localhost:8082`.

## Signing key

- Dev: PEM path comes from `Jwt:SigningKeyPath`. If unset / file missing,
  `SigningKeyProvider` generates an ephemeral in-memory RSA-2048 key per
  process — fine for local play, **tokens won't survive a restart**.
  To persist a dev key, generate one and set the path:
  ```powershell
  $rsa = [System.Security.Cryptography.RSA]::Create(2048)
  [System.IO.File]::WriteAllText("$env:USERPROFILE\.edcp\identity-signing-key.pem", $rsa.ExportRSAPrivateKeyPem())
  ```
  then point `Jwt:SigningKeyPath` at it (user-secrets or env var).
- The `kid` advertised in `/jwks` is `Jwt:KeyId` (default
  `identity-signing-key-v1`). Rotating keys = bump `KeyId` and serve
  both old and new in `/jwks` for the overlap window (out of Phase 2
  scope; documented in ADR-014 as a future enhancement).