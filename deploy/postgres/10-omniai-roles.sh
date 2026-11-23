#!/bin/sh
# OmniAI database and roles, created once on an empty PostgreSQL data directory
# (the official image runs /docker-entrypoint-initdb.d/* only then).
#
#   omniai_owner  owns the database and schema; used ONLY by the migration job.
#   omniai_app    the API's runtime role: connect + DML, nothing else. It cannot
#                 create, alter, drop or truncate anything, and cannot create
#                 roles or databases.
#
# Every table a future migration creates is granted to omniai_app automatically
# by the default privileges below, so a migration never needs a hand-written
# GRANT (the schema stays model-generated).
#
# Passwords come from the environment only (see deploy/.env.example); nothing is
# printed. The superuser (POSTGRES_USER) is for operators and this script only.
set -eu

: "${OMNIAI_DB:?OMNIAI_DB is required}"
: "${OMNIAI_OWNER_PASSWORD:?OMNIAI_OWNER_PASSWORD is required}"
: "${OMNIAI_APP_PASSWORD:?OMNIAI_APP_PASSWORD is required}"
: "${OMNIAI_APP_CONNECTION_LIMIT:=90}"

psql -v ON_ERROR_STOP=1 --no-psqlrc --quiet \
  --username "$POSTGRES_USER" --dbname postgres \
  -v db="$OMNIAI_DB" \
  -v owner_pw="$OMNIAI_OWNER_PASSWORD" \
  -v app_pw="$OMNIAI_APP_PASSWORD" \
  -v app_limit="$OMNIAI_APP_CONNECTION_LIMIT" <<'SQL'
CREATE ROLE omniai_owner LOGIN PASSWORD :'owner_pw'
  NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;

-- The runtime role's own connection cap is the database's last line of defence
-- for the budget the API validates at startup (Database:Budget:*).
CREATE ROLE omniai_app LOGIN PASSWORD :'app_pw'
  NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT
  CONNECTION LIMIT :app_limit;

CREATE DATABASE :"db" OWNER omniai_owner;
REVOKE ALL ON DATABASE :"db" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"db" TO omniai_app;

\connect :"db"

-- Install the vector and text-search extensions here, so the migration's
-- CREATE EXTENSION IF NOT EXISTS is a no-op and the owner needs no extra rights.
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

ALTER SCHEMA public OWNER TO omniai_owner;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO omniai_app;

ALTER DEFAULT PRIVILEGES FOR ROLE omniai_owner IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO omniai_app;
ALTER DEFAULT PRIVILEGES FOR ROLE omniai_owner IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO omniai_app;
ALTER DEFAULT PRIVILEGES FOR ROLE omniai_owner IN SCHEMA public
  GRANT EXECUTE ON FUNCTIONS TO omniai_app;
SQL
