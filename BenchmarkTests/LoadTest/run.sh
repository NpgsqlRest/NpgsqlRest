#!/bin/bash
# usage: run.sh <rounds> <seconds>   -> appends tab-separated rows to results.tsv
S="$(cd "$(dirname "$0")" && pwd)"
ROUNDS=${1:-3}; SECS=${2:-5}
PGC=npgsqlrest-benchmark-pg; DB=bench402; PORT=5190
CONN="Host=localhost;Port=55432;Database=$DB;Username=postgres;Password=postgres"
docker exec -i $PGC psql -U postgres -q -c "drop database if exists $DB with (force)" -c "create database $DB" >/dev/null
docker exec -i $PGC psql -U postgres -q -v ON_ERROR_STOP=1 -d $DB < "$S/setup.sql" >/dev/null || exit 1
VARIANTS=(jit401 jit402 aot401 aot402)
for round in $(seq 1 $ROUNDS); do
  for k in 0 1 2 3; do
    v=${VARIANTS[$(( (k + round - 1) % 4 ))]}
    LOG="$S/server-$v.log"
    if [[ $v == jit* ]]; then
      (cd "$S/builds/$v" && exec dotnet NpgsqlRestClient.dll "--ConnectionStrings:Default=$CONN" "--Urls=http://127.0.0.1:$PORT" "--NpgsqlRest:RequiresAuthorization=false" < /dev/null > "$LOG" 2>&1) &
    else
      (cd "$S/builds/$v" && exec ./NpgsqlRestClient "--ConnectionStrings:Default=$CONN" "--Urls=http://127.0.0.1:$PORT" "--NpgsqlRest:RequiresAuthorization=false" < /dev/null > "$LOG" 2>&1) &
    fi
    PID=$!
    for _ in $(seq 1 120); do grep -q 'Started in' "$LOG" && break; perl -e 'select(undef,undef,undef,0.25)'; done
    grep -q 'Started in' "$LOG" || { echo "server $v did not start"; tail -5 "$LOG"; kill $PID; exit 1; }
    dotnet "$S/loadgen/out/loadgen.dll" "http://127.0.0.1:$PORT" $PID $PGC "$v-r$round" $SECS >> "$S/results.tsv" || { echo "loadgen failed for $v"; tail -5 "$LOG"; kill $PID; exit 1; }
    kill $PID; wait $PID 2>/dev/null
    perl -e 'select(undef,undef,undef,1)'
  done
done
echo BENCH_DONE
