#!/usr/bin/env bash
# 多輪負載測試自動化：每輪重啟 backend（清連線池）+ reseed + 跑 k6 + 驗證 DB。
# 用法：loadtest-multiround.sh <APP_DIR> <DC_CMD> <BASE_URL> <K6_NET> <DOCKER_RUN> <ROUNDS> <LOGFILE>
set -uo pipefail

APP_DIR="$1"; DC="$2"; BASE_URL="$3"; K6_NET="$4"; DOCKER_RUN="$5"; ROUNDS="$6"; LOGFILE="$7"

cd "$APP_DIR" || exit 1
: > "$LOGFILE"
HEALTH_URL="http://localhost:5230/health/ready"

log() { echo "$@" >> "$LOGFILE"; }

restart_backend() {
  $DC restart e2e-backend >/dev/null 2>&1
  sleep 2
  for i in $(seq 1 15); do
    code=$(curl -sS -o /dev/null -w '%{http_code}' "$HEALTH_URL" 2>/dev/null || echo "000")
    [ "$code" = "200" ] && break
    sleep 1
  done
}

seed_register() { $DC exec -T e2e-db env PGPASSWORD=e2e psql -U postgres -d presentationdb < db/seed-load.sql >/dev/null 2>&1; }
seed_teamslot() { $DC exec -T e2e-db env PGPASSWORD=e2e psql -U postgres -d presentationdb < db/seed-load-teamslot.sql >/dev/null 2>&1; }

verify_register() {
  restart_backend
  local out
  out=$($DC exec -T e2e-db env PGPASSWORD=e2e psql -U postgres -d presentationdb -t -c "
    select 'registered='||(select count(*) from \"PlayerRegister\")
       ||' overcap='||(select count(*) from (select ts.\"Id\" from \"TeamSlot\" ts join \"Boss\" b on b.\"Id\"=ts.\"BossId\" join \"TeamSlotCharacter\" tsc on tsc.\"TeamSlotId\"=ts.\"Id\" and tsc.\"CharacterId\" is not null group by ts.\"Id\", b.\"RequireMembers\" having count(tsc.\"Id\")>b.\"RequireMembers\") x)
       ||' dup='||(select count(*) from (select \"CharacterId\" from \"TeamSlotCharacter\" where \"CharacterId\" is not null group by \"CharacterId\" having count(distinct \"TeamSlotId\")>1) y);
  " 2>&1)
  log "  [verify] $out"
}

verify_teamslot() {
  restart_backend
  local out
  out=$($DC exec -T e2e-db env PGPASSWORD=e2e psql -U postgres -d presentationdb -t -c "
    select 'filled='||(select count(*) from \"TeamSlotCharacter\" where \"CharacterId\" is not null)
       ||' dup='||(select count(*) from (select \"CharacterId\" from \"TeamSlotCharacter\" where \"CharacterId\" is not null group by \"CharacterId\" having count(distinct \"TeamSlotId\")>1) y);
  " 2>&1)
  log "  [verify] $out"
}

run_register() {
  local vus=$1 offset=$2
  log "  >> register VUS=$vus OFFSET=$offset"
  MSYS_NO_PATHCONV=1 $DOCKER_RUN run --rm $K6_NET -v "$(pwd)/k6:/scripts" -e BASE_URL="$BASE_URL" -e VUS=$vus -e OFFSET=$offset grafana/k6 run /scripts/register-load.js >> "$LOGFILE" 2>&1
}

run_teamslot() {
  local vus=$1
  log "  >> teamslot VUS=$vus"
  MSYS_NO_PATHCONV=1 $DOCKER_RUN run --rm $K6_NET -v "$(pwd)/k6:/scripts" -e BASE_URL="$BASE_URL" -e VUS=$vus grafana/k6 run /scripts/teamslot-edit-load.js >> "$LOGFILE" 2>&1
}

log "===================== PHASE 1 ($ROUNDS 輪) ====================="
for round in $(seq 1 "$ROUNDS"); do
  log ""
  log "----- Phase1 Round $round -----"
  restart_backend
  seed_register
  run_register 60 0
  run_register 150 60
  restart_backend
  seed_register
  run_register 200 0
  run_register 200 200
  verify_register
done

log ""
log "===================== PHASE 2 ($ROUNDS 輪) ====================="
for round in $(seq 1 "$ROUNDS"); do
  log ""
  log "----- Phase2 Round $round -----"
  for vus in 60 250 500; do
    restart_backend
    seed_teamslot
    run_teamslot "$vus"
    verify_teamslot
  done
done

log ""
log "===================== DONE ====================="
