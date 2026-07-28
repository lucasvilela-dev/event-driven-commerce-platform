# ADR-014: Hand-rolled JWT issuer (vs. Duende IdentityServer)

- **Status:** Accepted
- **Date:** 2026-07-27
- **Decisor:** Portfolio author
- **Implements:** Phase 2 acceptance criteria (`docs/roadmap.md`).

## Context

Phase 2 requires an Identity service that exposes:

- `POST /api/identity/register` — creates a user (email + password, hashed
  with ASP.NET Identity's `IPasswordHasher<T>`).
- `POST /api/identity/login` — returns a 15-minute access JWT and a
  7-day refresh JWT.
- `GET /.well-known/openid-configuration` and `GET /jwks` — so any other
  service (or the API Gateway) can discover the signing keys and validate
  tokens without a shared secret.
- EF Core migration creating `users`, `roles`, `user_roles` in the
  `identity` database (only — ADR-008).
- JWT claims: `sub`, `email`, `roles`; tokens signed with an asymmetric
  key (RS256) so validation is a public-key operation.

Two implementation paths were on the table:

1. **Duende IdentityServer** (`Duende.IdentityServer` 7.x) — a full OIDC
   provider: OIDC configuration endpoint, JWKS, token endpoint, scopes,
   introspection, discovery, support for authorization code + client
   credentials flows out of the box.
2. **Hand-rolled JWT issuer** — we own `Register`/`Login`, sign JWTs
   with an RSA key, expose `/.well-known/jwks` and a minimal
   `/.well-known/openid-configuration` document, store users in our own
   EF Core model. No OIDC *protocol* (no `/authorize`, no `/token` with
   grant types, no introspection endpoint).

## Decision

We adopt **option 2 — hand-rolled JWT issuer**.

Concretely:

- **Persistence:** EF Core against the `identity` Postgres database; tables
  `users`, `roles`, `user_roles` created by a single migration. Password
  hashing delegates to ASP.NET Identity's `IPasswordHasher<ApplicationUser>`
  — we *use the hasher*, not the full ASP.NET Identity membership system
  (no `IdentityDbContext`, no `UserManager` lockout/2FA tables). This keeps
  the schema lean and explicit (Phase 2 only lists `users`/`roles`/
  `user_roles`).
- **Token issuance:** an `ITokenIssuer` (in `Identity.Application`) signs
  access JWTs (15 min, claims `sub`, `email`, `roles`, `aud=edcp`,
  `iss=https://localhost:5001` in dev) with an RS256 key. Refresh JWTs
  (7 days) are signed with the same key and carry a `token_type=refresh`
  claim; rotation on `/login?grant_type=refresh` is deferred to a later
  phase.
- **Key material:** an `RsaSecurityKey` sourced from a PEM file in dev
  (`%USERPROFILE%/.edcp/identity-signing-key.pem`, generated on first run
  if absent) and from a mounted secret / env-var in compose. A rotating
  key set is out of scope for Phase 2; the JWKS endpoint advertises the
  single current key. A second `kid` can be added later without breaking
  existing tokens.
- **Discovery:** `/.well-known/openid-configuration` returns a *minimal*
  document pointing at `/jwks`, the issuer, and the supported signing alg.
  It is NOT an OIDC-compliant provider document (no `authorization_endpoint`,
  `token_endpoint`, `userinfo_endpoint`); it exists so downstream services
  can bootstrap the JWKS URI and issuer from one well-known URL.
- **`/jwks`:** returns the public RSA key in JWK format with a `kid`
  derived from the key SHA-1 thumbprint (RFC 7638).
- **Validation in other services:** API Gateway and any service that
  needs auth validates JWTs with `Microsoft.AspNetCore.Authentication.JwtBearer`
  pointing `Authority` at `http://identity:5001` (or
  `https://localhost:5001` from outside compose) and
  `TokenValidationParameters.ValidIssuer` matching the JWT `iss`. The
  JwtBearer middleware auto-fetches `/jwks`; no shared secret.
- **Observability:** Serilog → Seq with `ServiceName=identity` on every
  log line, `TraceId`/~SpanId` sourced from `Activity.Current` via the
  `OpenCode.TraceContext` enricher (ADR-015). `TraceContext.AlwaysSample()`
  is called once at startup (per ADR-015's follow-up note).

## Rationale

- **The acceptance criteria read like a bespoke service, not an OIDC
  provider.** Phase 2 lists `Register`/`Login`/`/jwks` and an EF migration
  with `users`/`roles`/`user_roles`. Duende's data model is its own
  (`Clients`, `ApiScopes`, `ApiResources`, `IdentityResources`,
  `DeviceCodes`, `PersistedGrants`, its own `Users` table) and bringing a
  custom `users` table alongside it is awkward and confusing to read.
- **Duende's strengths are unused here.** No `/authorize` code flow (this
  is a back-end platform, not a third-party-app OAuth server), no scopes
  (we use a flat `roles` claim), no introspection, no client-credentials
  grant. Paying the Duende licensing + configuration cost for a feature set
  we don't use would read as "I installed a vendor product to tick a box"
  rather than "I built the auth surface the platform actually needs."
- **Hand-rolled exercises more of the Clean Architecture flow.** It forces
  a real `ITokenIssuer` abstraction in `Identity.Application`, a real
  `SigningKeyProvider` in `Identity.Infrastructure`, and a real
  `JwtIssuerOptions` configuration wrinkle — all of which read better in a
  portfolio than "`AddIdentityServer(...)` here".
- **Full control over the JWKS rotation story.** A portfolio-grade note
  like "we can rotate keys by adding a second `kid` and serving both in
  `/jwks` for the overlap window" is more compelling when the issuer is
  ours than when it is Duende's.
- **Duende licensing** is commercially free for companies under $1M
  revenue (Duende Extensions License), but legally-licensing concerns are
  irrelevant for a portfolio project that does not need an OIDC provider in
  the first place.

## Consequences

- **Positive:**
  - Schema is minimal and explicit (`users`, `roles`, `user_roles`) per
    Phase 2 acceptance criteria; no Duende-internal tables to explain.
  - Clean Architecture layering is exercised end-to-end
    (Domain → Application → Infrastructure → Api).
  - Future key rotation, custom claims, or token formats are 100% in our
    control (no Duende upgrade / behaviour change to absorb).
  - Smaller dependency surface for the Identity service
    (`Microsoft.AspNetCore.Authentication.JwtBearer`,
    `Microsoft.IdentityModel.Tokens`,
    `System.IdentityModel.Tokens.Jwt`, `BCrypt.Net`-or-`IPasswordHasher`,
    EF Core, Serilog). No Duende package.
- **Negative:**
  - We do not get an OIDC-compliant `/authorize` flow for free. If a later
    phase needs to issue tokens to third-party clients (e.g. a SPA with
    PKCE), we would revisit this ADR and possibly supersede it.
  - Refresh-token rotation is our problem to build; deferred to a later
    phase (Phase 2 only needs to *issue* a refresh JWT, not rotate it).
  - Discovery document is non-conformant; any tooling that strictly
    validates an OIDC discovery doc will complain. Acceptable for an
    internal portfolio platform.
- **Mitigations:**
  - A future ADR may supersede this one if a real OIDC provider becomes
    necessary; the `users`/`roles`/`user_roles` schema and the JWT claim
    shape (`sub`/`email`/`roles`) would not need to change — only the
    issuance and discovery endpoints.
  - `ITokenIssuer` is the seam; swapping to a Duende-backed issuer later
    would be a drop-in replacement at the Application/Infrastructure
    boundary.

## Alternatives considered

- **Duende IdentityServer 7.x.** Rejected for the reasons in Rationale:
  feature set mismatch, heavy data model, licensing noise for a portfolio
  project that has neither OAuth clients nor a `/authorize` flow.
- **OpenIddict.** Lighter than Duende, still a full OIDC server stack —
  same mismatch. Considered to flag that the alternative was surveyed.
- **Auth0 / third-party IdP.** Out of scope: the platform is supposed to
  be self-contained (one `docker compose up`), and a SaaS dependency
  defeats the "all on the laptop" runnable demo.
- **JWT signed with a symmetric key (HMAC), no JWKS.** Rejected: it would
  require sharing a secret with every validator, contradicting the
  "other services can validate tokens via JWKS" acceptance criterion.

## Follow-up

- Implement the four Clean Architecture projects per Phase 2 deliverables
  and scaffold `Identity.sln`; reference `OpenCode.TraceContext`.
- Add `docs/runbook/identity.md` covering local run, env vars (issuer URL,
  signing-key path, Seq URL), and JWKS rotation procedure.
- When API Gateway (Phase 8) is built, point its `JwtBearer.Authority` at
  this service's discovery endpoint.