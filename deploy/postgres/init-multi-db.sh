#!/bin/bash
# =============================================================================
# Initializes multiple databases on the PostgreSQL instance.
# This script runs from docker-entrypoint-initdb.d only on the first creation
# of the volume (postgres-data). Later changes must be done via migrations.
# =============================================================================
set -euo pipefail

echo "[init-multi-db] Creating databases: $POSTGRES_MULTIPLE_DATABASES"

for db in $(echo "$POSTGRES_MULTIPLE_DATABASES" | tr ',' ' '); do
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" <<-EOSQL
    CREATE DATABASE "$db";
    GRANT ALL PRIVILEGES ON DATABASE "$db" TO "$POSTGRES_USER";
EOSQL
  echo "[init-multi-db] Database '$db' created."
done

echo "[init-multi-db] Done."