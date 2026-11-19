#!/usr/bin/env bash
# AOT smoke test: starts a published npgsqlrest binary against PostgreSQL and serves real requests.
# A version check or config validation is not enough - in .NET 11 a native AOT build once passed both and then
# crashed on its first request.
#
# Usage: aot-smoke-test.sh <path-to-NpgsqlRestClient-binary>
# Env:   PSQL     command that runs psql as a superuser (default: psql -h localhost -U postgres)
#        PGCONN   connection string base for the server (default: Host=localhost;Port=5432;Username=postgres;Password=postgres)
set -euo pipefail

BIN="$1"
PSQL="${PSQL:-psql -h localhost -U postgres}"
PGCONN="${PGCONN:-Host=localhost;Port=5432;Username=postgres;Password=postgres}"
DB=npgsqlrest_aot_smoke
PORT=5899
LOG="$(mktemp)"
export PGPASSWORD="${PGPASSWORD:-postgres}"

$PSQL -v ON_ERROR_STOP=1 -q -d postgres -c "drop database if exists $DB" -c "create database $DB"
$PSQL -v ON_ERROR_STOP=1 -q -d $DB <<'SQL'
create function smoke_text() returns text language sql stable as $$ select 'Hello, World!' $$;
create function smoke_rows(_count int) returns table (id int, name text, tags text[], data jsonb) language sql stable as $$
  select i, 'name ' || i, array['a' || i, 'b\'], jsonb_build_object('i', i) from generate_series(1, _count) i $$;
create function smoke_echo(_value text) returns text language sql as $$ select 'got ' || _value $$;
create function smoke_void() returns void language sql as $$ select 1 $$;
create function smoke_secret() returns text language sql stable as $$ select 'secret' $$;
create function smoke_big() returns text language sql stable as $$ select repeat('x', 200000) $$;
comment on function smoke_text() is 'HTTP GET';
comment on function smoke_rows(int) is 'HTTP GET';
comment on function smoke_echo(text) is 'HTTP POST';
comment on function smoke_void() is 'HTTP POST';
comment on function smoke_secret() is 'HTTP GET
authorize';
comment on function smoke_big() is 'HTTP GET';
SQL

# The default configuration requires authorization on every endpoint; smoke_secret keeps its own `authorize`.
"$BIN" "--ConnectionStrings:Default=$PGCONN;Database=$DB" "--Urls=http://127.0.0.1:$PORT" "--NpgsqlRest:RequiresAuthorization=false" \
  < /dev/null > "$LOG" 2>&1 &
PID=$!
cleanup() {
  kill "$PID" 2>/dev/null || true
  $PSQL -q -d postgres -c "drop database if exists $DB with (force)" > /dev/null 2>&1 || true
}
trap cleanup EXIT

for _ in $(seq 1 120); do
  grep -q 'Started in' "$LOG" && break
  kill -0 "$PID" 2>/dev/null || { echo "FAIL: server exited during startup"; cat "$LOG"; exit 1; }
  sleep 0.5
done
grep -q 'Started in' "$LOG" || { echo "FAIL: server did not start"; cat "$LOG"; exit 1; }

B="http://127.0.0.1:$PORT"
check() { # <name> <expected> <actual>
  if [ "$2" != "$3" ]; then
    echo "FAIL $1"; echo "  expected: $2"; echo "  actual:   $3"; echo "--- server log ---"; cat "$LOG"; exit 1
  fi
  echo "ok   $1"
}

check "GET scalar"          'Hello, World! [200]' "$(curl -s -w ' [%{http_code}]' "$B/api/smoke-text")"
check "GET rows"            '[{"id":1,"name":"name 1","tags":["a1","b\\"],"data":{"i": 1}},{"id":2,"name":"name 2","tags":["a2","b\\"],"data":{"i": 2}}] [200]' \
                            "$(curl -s -w ' [%{http_code}]' "$B/api/smoke-rows?count=2")"
check "POST JSON body"      'got smoke [200]' "$(curl -s -w ' [%{http_code}]' -H 'Content-Type: application/json' -d '{"value":"smoke"}' "$B/api/smoke-echo")"
check "POST void"           '[204]' "$(curl -s -o /dev/null -w '[%{http_code}]' -X POST "$B/api/smoke-void")"
check "GET authorize, anonymous" '[401]' "$(curl -s -o /dev/null -w '[%{http_code}]' "$B/api/smoke-secret")"
check "GET gzip"            '[200]' "$(curl -s --compressed -H 'Accept-Encoding: gzip' -o /dev/null -w '[%{http_code}]' "$B/api/smoke-rows?count=200")"
# Responses that are sent in several pieces: a value larger than 64 KB, and more rows than fit in 16 KB.
check "GET large scalar"    '200000 [200]' "$(curl -s -o /dev/null -w '%{size_download} [%{http_code}]' "$B/api/smoke-big")"
check "GET 2000 rows"       '2000' "$(curl -s "$B/api/smoke-rows?count=2000" | grep -o '"id":' | wc -l | tr -d ' ')"
check "200 parallel requests" '200' \
  "$(seq 1 200 | xargs -P 20 -I{} curl -s -o /dev/null -w '%{http_code}\n' "$B/api/smoke-rows?count=10" | grep -c '^200$')"
kill -0 "$PID" 2>/dev/null || { echo "FAIL: server died"; cat "$LOG"; exit 1; }
echo "ok   server alive after all requests"
