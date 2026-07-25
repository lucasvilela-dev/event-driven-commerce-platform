# ADR-008: One PostgreSQL database per service (DDD dogma)

- **Status:** Accepted
- **Date:** 2026-07-25
- **Decisor:** Portfolio author
- **Related to:** ADR-001 (polyglot), ADR-005 (ES), ADR-006 (CQRS)

## Context

In microservices, the DDD dogma says: **each service must have its own
persistence schema**, never sharing tables. There are two practical
interpretations:

- **A. One RDBMS per service** (separate process/instance): maximum isolation,
  possible heterogeneity (Order in Postgres, Inventory in Cassandra).
- **B. One schema (database) per service inside the same Postgres instance**,
  with access restricted by user (role) to prevent cross-service JOINs.

Spinning up 5 Postgres instances (>1GB RAM each) on a dev laptop is
prohibitively expensive with no real need.

## Decision

We adopt **option B with the discipline of A**: a single shared PostgreSQL 16
instance in dev, **one database per service**, with a **dedicated user/role
per service to prevent cross-service JOINs**.

List of databases:

- `identity`  (Identity Service)
- `product`   (Product Service)
- `order`     (Order Service — ES)
- `payment`   (Payment Service)
- `inventory` (Inventory Service)

The `init-multi-db.sh` script creates the 5 databases on the container's
first initialization.

## Discipline

- Each service connects **only to its own database**.
- **It is forbidden**: cross-DB JOINs (`dblink`, `postgres_fdw`), sharing
  tables, or accessing another service's DB through the same connection
  string.
- Each service's connection string points to its own DB and role.
- The separation evolves naturally toward one RDBMS per service in
  production: just change the `connection string` in the Helm values.

## Rationale

- **Dev efficiency:** a single instance is light (~150MB idle) instead of 5×.
- **Bounded Context respected:** the logical separation (one database per
  service) preserves the "do not share data" rule — which is what matters
  architecturally.
- **Production-ready migration:** moving each service to its own Postgres is
  a connection-string refactor, with no code changes.
- **Postgres supports `CREATE DATABASE` and roles with scoped `CONNECT`**,
  allowing strong isolation even within a single instance.

## Consequences

- **Positive:**
  - Smaller dev footprint; a single `postgres-data` Docker volume.
  - Individual backup/restore via `pg_dump <db>` — easier selective
    debugging.
  - Migrations per service run only against their own database.
- **Negative:**
  - A single instance is an SPOF in dev (in production: per-database
    replication).
  - Organizational discipline — no automatic barrier against cross-DB access.
- **Mitigations:**
  - Document the rule in the repository README.
  - CI validates that each service's migrations only create objects in its
    own schema.
  - In production/Helm, one Postgres **StatefulSet per service**.

## Alternatives considered

- **One Postgres per service:** purer but expensive in dev (150MB × 5 →
  ~750MB minimum at idle) — overkill for a portfolio.
- **A single shared schema (one database, multiple tables):** violates the
  DDD dogma and turns into a Distributed Monolith; a bad practice.
- **MongoDB per service:** valid, but adds nothing to a portfolio that
  already demonstrates Postgres + ES + CQRS.