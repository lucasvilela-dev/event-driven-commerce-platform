# Runbook — Identity Service

> Operational guide for the Identity service (`src/services/identity/`.
> Hand-rolled JWT issuer per ADR-014. Phase 2 scope: issuance only —
> refresh-token rotation is deferred (see `docs/roadmap.md` Phase 2 note).

## What this service does

- Issues RS256 JWT access (15 min) and refresh (7 d) tokens for the
  platform.
- Owns the `users` / `roles` / `user_roles` schema in its own Postgres
  database (`identity`) — ADR-008.
- Publishes `/.well-known/openid-configuration` (minimal, non-conformant)
  and `/.well-known/jwks` so any downstream service (notably the API
  Gateway in Phase 8) can validate tokens via the public RSA key without
  a shared secret.
- Logs to Seq with `ServiceName=identity` and a `TraceId` propagated from
  the inbound `traceparent` HTTP header (ADR-010 / ADR-015).

## Out of scope (Phase 2)

- `/api/identity/refresh` endpoint and refresh-token rotation
  (`refresh_tokens` table with `revoked_at` / `replaced_by` /
  `family_id` and reuse-detection) — deferred to a later mini-phase,
  revisit after Phase 8 (Gateway) exercises the auth surface end-to-end.
- OIDC `/authorize` flow, scopes, introspection, third-party client
  support — not required by the platform's back-end services.
- Kafka domain events on `UserRegistered` — not emitted; revisit with a
  new ADR if a strong contract is needed.

## Prerequisites

1. **.NET 10 SDK** (`10.0.100`). Verify with `dotnet --version`.
2. **Docker Desktop** running — needed for the platform stack and for
   the Testcontainers integration tests.
3. **Platform stack up** — from the repo root:
   ```powershell
   docker compose -f deploy/docker-compose.yml up -d
   docker compose -f deploy/docker-compose.yml ps
   ```
   Identity needs: Postgres (`localhost:15432`, DB `identity`) and Seq
   (`http://localhost:8082`). Kafka is not required by this service in
   Phase 2.

## Local ports

| Surface | URL |
|---|---|
| HTTPS API (dev issuer URL) | `https://localhost:5001` |
| HTTP API | `http://localhost:5000` |
| Health | `GET /health` (200 if the process is up; no downstream check yet) |
| Discovery | `GET /.well-known/openid-configuration` |
| JWKS | `GET /.well-known/jwks` |
| Seq | `http://localhost:8082` (filter `ServiceName=identity`) |
| Postgres | `localhost:15432` — database `identity`, user `edcp` / `edcp_dev` |

> The dev HTTPS URL `https://localhost:5001` **must** match `Jwt:Issuer`
> in `appsettings.json`. Downstream JWT validators (Gateway, other
> services) point `JwtBearer.Authority` at this URL. Do not change one
> without changing the other.

## Endpoint surface

| Method & path | Body | Returns |
|---|---|---|
| `POST /api/identity/register` | `{ "email": "...", "password": "..." }` | `201` `{ "userId": "..." }` · `409` on duplicate email |
| `POST /api/identity/login` | `{ "email": "...", "password": "..." }` | `200` `{ "accessToken", "refreshToken", "accessTokenExpiresAt", "refreshTokenExpiresAt" }` · `401` bad credentials |
| `GET /.well-known/openid-configuration` | — | `200` minimal doc: `issuer`, `jwks_uri`, `token_endpoint`, `id_token_signing_alg_values_supported=["RS256"]`, `subject_types_supported=["public"]` |
| `GET /.well-known/jwks` | — | `200` `{ "keys": [ { kty, use, alg, kid, n, e } ] }` — one RSA key |
| `GET /health` | — | `200` |

Access JWT claims: `sub` (user id), `email`, `role` (one per role),
`token_type="access"`, `jti`, `iss`, `aud`, `nbf`, `exp`. Refresh JWT
carries `token_type="refresh"` and the same `sub`/`email`/`role` claims
with a 7-day lifetime.

## Configuration

All keys live under `Identity.Api/appsettings.json`. Override via
`ASPNETCORE_*` environment variables (double-underscore for sections),
user-secrets in dev, or mounted secrets in compose.

| Key | Default | Notes |
|---|---|---|
| `ConnectionStrings:Identity` | `Host=localhost;Port=15432;Database=identity;Username=edcp;Password=edcp_dev` | Npgsql connection string. **Never reuse `edcp_dev` outside the laptop.** |
| `ConnectionStrings:Seq` | `http://localhost:8082` | Seq ingest URL. |
| `Jwt:Issuer` | `https://localhost:5001` | **Must equal the public HTTPS URL**. Baked into every issued JWT `iss` claim. |
| `Jwt:Audience` | `edcp` | `aud` claim. Downstream validators must accept this. |
| `Jwt:AccessTokenMinutes` | `15` | Access JWT lifetime. |
| `Jwt:RefreshTokenDays` | `7` | Refresh JWT lifetime (Phase 2: issuance only — no rotation endpoint yet). |
| `Jwt:SigningKeyPath` | `""` | Path to a PEM-encoded RSA private key. Empty / missing → ephemeral in-memory RSA-2048 generated per process. |
| `Serilog:MinimumLevel:Default` | `Information` (`Debug` in `Development`) | — |

Environment variable example (overriding `Jwt:SigningKeyPath`):

```powershell
$env:Jwt__SigningKeyPath = "$env:USERPROFILE\.edcp\identity-signing-key.pem"
```

## Signing key

### Generating a persistent dev key

By default `Jwt:SigningKeyPath` is empty, so `SigningKeyProvider`
generates an **ephemeral** RSA-2048 key per process. That is fine for
local play but **tokens won't survive a restart** — JWKS `kid` changes
each run, and previously issued JWTs become unvalidatable.

To get a stable `kid` and durable tokens across restarts, generate a PEM
key once and point the service at it:

```powershell
$keyDir = "$env:USERPROFILE\.edcp"
New-Item -ItemType Directory -Path $keyDir -Force | Out-Null
$rsa = [System.Security.Cryptography.RSA]::Create(2048)
[System.IO.File]::WriteAllText(
    "$keyDir\identity-signing-key.pem",
    $rsa.ExportRSAPrivateKeyPem())

# Persist the path for the current user (override the empty default).
dotnet user-secrets set "Jwt:SigningKeyPath" "$keyDir\identity-signing-key.pem" `
    --project src/services/identity/Identity.Api
```

Restart the service. `GET /.well-known/jwks` now returns the same `kid`
across restarts.

> The `kid` is the **RFC 7638 SHA-1 thumbprint** of the RSA
> SubjectPublicKeyInfo (base64url-encoded), computed by
> `SigningKeyProvider.ComputeRfc7638Kid` — not a hand-set config string.
> Two different processes with the same PEM file advertise the same
> `kid`; two processes with different keys advertise different `kid`s.
> ADR-014 mentions a static `Jwt:KeyId` config — that was superseded
> during Phase 2 step 3 (see `docs/status.md`); the config key is gone.

### JWKS rotation procedure (not implemented in Phase 2)

Phase 2 advertises exactly one key in `/jwks`. The rotation procedure a
future phase will implement:

1. Generate a new RSA key + PEM.
2. Extend `SigningKeyProvider` to expose a **list** of `(RsaSecurityKey,
   kid)` pairs — the new key first, the previous key(s) retained for the
   overlap window (at least one refresh-token lifetime, i.e. ≥ 7 days).
3. `JwksController` advertises all public keys; `TokenIssuer` signs only
   with the new key.
4. After the overlap window, drop the old key from the list, then
   garbage-collect refresh tokens signed by it.

Because downstream validators re-fetch `/jwks` on `kid` miss, no
out-of-band coordination is needed beyond serving both keys during the
overlap. The hand-rolled issuer makes this a ~20-line change in
`SigningKeyProvider` + `JwksController`; a Duende-backed issuer would
inherit its own rotation semantics.

## Running locally

### From the command line

```powershell
# 1. Bring up Postgres + Seq (only what Identity needs).
docker compose -f deploy/docker-compose.yml up -d postgres seq

# 2. Run the service (uses launchSettings.json → https://localhost:5001).
dotnet run --project src/services/identity/Identity.Api
```

### Smoke test

```powershell
# Discovery + JWKS.
curl.exe -k https://localhost:5001/.well-known/openid-configuration
curl.exe -k https://localhost:5001/.well-known/jwks

# Register + login round-trip.
$body = '{ "email": "alice@example.com", "password": "P@ssw0rd!" }'
curl.exe -k -X POST https://localhost:5001/api/identity/register `
    -H "Content-Type: application/json" -d $body
curl.exe -k -X POST https://localhost:5001/api/identity/login `
    -H "Content-Type: application/json" -d $body
```

The login response body:

```json
{
  "accessToken": "eyJhbGci...",
  "refreshToken": "eyJhbGci...",
  "accessTokenExpiresAt": "2026-08-03T14:15:00Z",
  "refreshTokenExpiresAt": "2026-08-10T14:00:00Z"
}
```

Inspect the token (header + payload) locally:

```powershell
$jwt = "<accessToken>"
$parts = $jwt -split '\.'
ForEach-Object -InputObject $parts[0,1] {
  [System.Text.Encoding]::UTF8.GetString(
    [System.Convert]::FromBase64String($_ + ('=' * (4 - $_.Length % 4))))
}
```

## Database

### Apply the migration

`Identity.Api` is wired to apply pending migrations on startup via
`MigrateDatabaseAsync()` in the test factory; **the host itself does not
auto-migrate.** Apply migrations explicitly:

```powershell
dotnet ef database update `
    --project src/services/identity/Identity.Infrastructure `
    --startup-project src/services/identity/Identity.Api `
    --connection "Host=localhost;Port=15432;Database=identity;Username=edcp;Password=edcp_dev"
```

Or, with the service running, the integration test factory applies
migrations to the Testcontainers DB only — not to your local Postgres.

### Adding a migration

```powershell
dotnet ef migrations add <Name> `
    --project src/services/identity/Identity.Infrastructure `
    --startup-project src/services/identity/Identity.Api
```

Migrations live in
`Identity.Infrastructure/Migrations/` (see
`IdentityDbContext` registration with
`MigrationsAssembly(typeof(IdentityDbContext).Assembly)`).

## Tests

### Unit tests (`Identity.Api.Tests`)

No Docker required. Covers Application layer:

- `RegisterHandler` success, duplicate-email (`DuplicateEmailException`
  → 409), missing `customer` role (`RoleNotFoundException` → 500).
- `LoginHandler` unknown user (401), wrong password (401), happy path
  (issues `TokenPair`, persists `LastLoginAt`).

```powershell
dotnet test --filter "FullyQualifiedName!~IntegrationTests" `
    --project src/services/identity/Identity.Api.Tests
```

### Integration tests (`Identity.IntegrationTests`)

**Requires Docker Desktop running** — spins a `postgres:16-alpine`
container via Testcontainers. Covers the full HTTP surface plus JWT
signature **validation against the live `/.well-known/jwks`**:

- `Well_known_discovery_returns_minimal_document`
- `Jwks_endpoint_returns_single_rsa_public_key`
- `Register_then_login_round_trip_yields_validatable_jwt_pair` — also
  asserts duplicate-email → 409 and JWT claim set (`sub`, `email`,
  `role=customer`).
- `Login_with_wrong_password_returns_401`
- `Login_unknown_user_returns_401`

```powershell
dotnet test --filter IntegrationTests `
    --project src/services/identity/tests/Identity.IntegrationTests
```

Expected: **5 passed, 0 failed, ~8 s** on a warm container (Testcontainers
adds ~5 s for Postgres boot).

### All tests at once

```powershell
dotnet test --nologo
```

Expected: **6 unit + 5 integration = 11 passed**.

## Troubleshooting

### `dotnet test --filter IntegrationTests` hangs or fails to start

- Confirm Docker Desktop is running: `docker info` returns a
  server version.
- Confirm `postgres:16-alpine` is pullable: `docker pull postgres:16-alpine`.
- If a previous run crashed mid-test, leftover containers can collide.
  Clean up: `docker ps -a --filter "ancestor=postgres:16-alpine" --format '{{.ID}}' | % { docker rm -f $_ }`.

### 401 on every login (including correct password)

- Likely cause: the persisted PEM key changed since the user registered.
  This does not affect login (passwords are hashed, not signed), so if
  you are seeing 401 on `/api/identity/login` itself, check the email
  casing — emails are normalised to lower-case on register.
- If 401 happens later when *validating* an access JWT against `/jwks`,
  the issuer's signing key rotated. Either restore the old PEM or issue
  fresh tokens via `/api/identity/login`.

### `kid` changes on every restart

`Jwt:SigningKeyPath` is unset or the path doesn't exist — the
`SigningKeyProvider` falls back to an ephemeral per-process RSA key.
Generate a persistent PEM (see *Generating a persistent dev key*) and
restart.

### Downstream service can't validate tokens

- Confirm `JwtBearer.Authority` points **exactly** at `Jwt:Issuer`
  (`https://localhost:5001` from outside compose, `http://identity:5001`
  from inside compose once Identity is containerised).
- Confirm `ValidAudience` matches `Jwt:Audience` (`edcp`).
- Hit `/.well-known/openid-configuration` from the validator's network
  (e.g. `curl https://localhost:5001/.well-known/openid-configuration`)
  and follow `jwks_uri` — the JWKS payload should list the same `kid`
  embedded in the JWT header.

### Seq shows no logs

- Confirm Seq is up: `curl http://localhost:8082` returns 200.
- Confirm `ConnectionStrings:Seq` is reachable from the service host.
- Filter by `ServiceName=identity`; the enricher is wired in `Program.cs`
  via `.Enrich.WithProperty("ServiceName", "identity")` and
  `.Enrich.WithActivityTrace()` (ADR-015).

## Validation by downstream services (how other code validates a token)

The platform's pattern (consumed by the API Gateway in Phase 8):

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.Authority = "https://localhost:5001"; // = Jwt:Issuer
        opts.Audience  = "edcp";                    // = Jwt:Audience
        // JwtBearer auto-fetches /.well-known/openid-configuration
        // and /.well-known/jwks — no static key, no shared secret.
        opts.MapInboundClaims = false; // keep "role" / "sub" as-is
    });
```

For tests outside this repo (or the integration test's manual validator),
see `AssertJwtValidatesAgainstJwksAsync` in `IdentityEndToEndTests.cs`:
fetch JWKS, build `TokenValidationParameters` with
`IssuerSigningKeys = keys.Keys`, and call
`JwtSecurityTokenHandler.ValidateToken`.

## Pointers

- ADR-014 — the hand-rolled JWT issuer decision.
- ADR-008 — one DB per service (`identity` database).
- ADR-010 as amended by ADR-015 — observability + native propagators.
- Service-local AGENTS.md — `src/services/identity/AGENTS.md`.
- Phase 2 acceptance criteria — `docs/roadmap.md` § Phase 2.
- Current state — `docs/status.md` "Phase 2" section.