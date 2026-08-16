#!/usr/bin/env bash
# 多輪負載測試自動化：每輪重啟 backend（清連線池）+ reseed + 跑 k6 + 驗證 DB。
# period-less 重指版：Phase 1（register→auto-assign）已退場（端點與 classId 1001 鎖都沒了）；
# 只剩 Phase 2 —— 對同一 teamSlotId 併發接受邀請，量 ConfirmMemberAsync 的 classId 1002 鎖排隊 vs lock_timeout 5s。
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

seed_confirm() { $DC exec -T e2e-db env PGPASSWORD=e2e psql -U postgres -d presentationdb < db/seed-load-confirm.sql >/dev/null 2>&1; }

# 正確性硬條件：confirmed 應 = VUS（全接受）、無隊超編、無同玩家同時段跨隊重複 Confirmed。
verify_confirm() {
  restart_backend
  local out
  out=$($DC exec -T e2e-db env PGPASSWORD=e2e psql -U postgres -d presentationdb -t -c "
    select 'confirmed='||(select count(*) from \"TeamSlotCharacter\" where \"Status\"='Confirmed')
       ||' overcap='||(select count(*) from (select ts.\"Id\" from \"TeamSlot\" ts join \"Boss\" b on b.\"Id\"=ts.\"BossId\" join \"TeamSlotCharacter\" tsc on tsc.\"TeamSlotId\"=ts.\"Id\" and tsc.\"Status\"='Confirmed' group by ts.\"Id\", b.\"RequireMembers\" having count(tsc.\"Id\")>b.\"RequireMembers\") x)
       ||' overlap_dup='||(select count(*) from (select \"DiscordId\",\"SlotDateTime\" from \"TeamSlotCharacter\" where \"Status\"='Confirmed' and \"DiscordId\"<>0 group by \"DiscordId\",\"SlotDateTime\" having count(*)>1) y);
  " 2>&1)
  log "  [verify] $out"
}

run_confirm() {
  local vus=$1
  log "  >> confirm-accept VUS=$vus"
  MSYS_NO_PATHCONV=1 $DOCKER_RUN run --rm $K6_NET -v "$(pwd)/k6:/scripts" -e BASE_URL="$BASE_URL" -e VUS=$vus grafana/k6 run /scripts/confirm-accept-load.js >> "$LOGFILE" 2>&1
}

log "===================== 入隊定案鎖（classId 1002）— $ROUNDS 輪 ====================="
for round in $(seq 1 "$ROUNDS"); do
  log ""
  log "----- Round $round -----"
  for vus in 60 250 500; do
    restart_backend
    seed_confirm
    run_confirm "$vus"
    verify_confirm
  done
done

log ""
log "===================== DONE ====================="
