#!/usr/bin/env bash
# 本機 E2E 的唯一入口：起 stack → 重置狀態（seed＋清 Redis）→ 跑 Playwright。
# 在 Git Bash（Windows）或 WSL 執行皆可；Windows 端沒有 docker，會自動轉進 WSL 執行。
#
# 用法：
#   scripts/e2e-local.sh                          # 全套
#   scripts/e2e-local.sh --grep "G8"              # 其餘參數原樣交給 `playwright test`
#   scripts/e2e-local.sh authorization-matrix     # 只跑某支 spec
#   scripts/e2e-local.sh --down                   # 跑完 down -v 拆掉
#
# 為什麼要有這個入口（每一條都踩過，手動拼指令時一再漏掉）：
#   1. spec 在 build 時烤進 playwright image（web/Dockerfile.e2e 是 COPY . .）
#      → 一律 `run --build`，否則跑的是舊 spec，「修了還是紅」其實是「修了沒進容器」。
#   2. seed 必須在 stack 確實起來之後才跑，且要驗筆數——db 沒起來時 seed 會靜默失敗，
#      資料相依的 spec 全部 timeout，看起來像程式壞了。
#   3. 每次都清 Session 與 Redis：session 快取以 discordId 為 key、有效期 15 分鐘，
#      殘留的舊 session 會讓固定身分的測試在重跑時偽陽性 401。
#   4. 整段在同一個行程內完成：WSL 閒置會關掉 distro（容器跟著停、/tmp 也會不見），
#      拆成多次呼叫就會中途斷掉 → log 寫在 repo 內（.e2e-local/），跨呼叫也看得到。
set -euo pipefail

# ---------------------------------------------------------------- Windows → WSL
if ! command -v docker >/dev/null 2>&1 && command -v wsl.exe >/dev/null 2>&1; then
  root="$(git rev-parse --show-toplevel)"
  if [[ "$root" =~ ^([A-Za-z]):(.*)$ ]]; then          # C:/Users/...
    root_wsl="/mnt/$(tr '[:upper:]' '[:lower:]' <<<"${BASH_REMATCH[1]}")${BASH_REMATCH[2]}"
  elif [[ "$root" =~ ^/([a-zA-Z])/(.*)$ ]]; then       # /c/Users/...
    root_wsl="/mnt/$(tr '[:upper:]' '[:lower:]' <<<"${BASH_REMATCH[1]}")/${BASH_REMATCH[2]}"
  else
    echo "無法換算 repo 路徑到 WSL：$root" >&2
    exit 1
  fi
  args=""
  for a in "$@"; do args+=" $(printf '%q' "$a")"; done
  user_opt=()
  [ -n "${E2E_WSL_USER:-}" ] && user_opt=(-u "$E2E_WSL_USER")
  # MSYS_NO_PATHCONV：避免 Git Bash 把 /mnt/c/... 改寫成 Windows 路徑
  MSYS_NO_PATHCONV=1 exec wsl.exe -d "${E2E_WSL_DISTRO:-Ubuntu}" ${user_opt[@]+"${user_opt[@]}"} \
    -e bash -lc "cd $(printf '%q' "$root_wsl") && bash scripts/e2e-local.sh$args"
fi

# ---------------------------------------------------------------- 以下在有 docker 的環境執行
cd "$(git rev-parse --show-toplevel)"

down=0
pw_args=()
for a in "$@"; do
  case "$a" in
    --down) down=1 ;;
    *) pw_args+=("$a") ;;
  esac
done

files=(-f compose.e2e.yaml)
[ -f compose.e2e.local.yaml ] && files+=(-f compose.e2e.local.yaml)   # 本機 port 避讓（未追蹤）
dc() { docker compose "${files[@]}" "$@"; }

log_dir=".e2e-local"
mkdir -p "$log_dir"
log="$log_dir/playwright-$(date +%Y%m%d-%H%M%S).log"

step() { printf '\n##### %s\n' "$*"; }

wait_http() { # $1=url $2=名稱
  local code=""
  for _ in $(seq 1 90); do
    code=$(curl -s -o /dev/null -w '%{http_code}' "$1" 2>/dev/null || true)
    [ "$code" = "200" ] && { echo "$2 ready"; return 0; }
    sleep 2
  done
  echo "$2 沒有 ready（最後狀態碼 $code），中止" >&2
  return 1
}

step "1/4 起 stack（--build --wait）"
dc up -d --build --wait e2e-frontend
wait_http http://localhost:5230/health/ready backend
wait_http http://localhost:3000 frontend

step "2/4 重置狀態：seed（TRUNCATE 含 Session）＋ 清 Redis"
dc exec -T e2e-db env PGPASSWORD=e2e psql -q -v ON_ERROR_STOP=1 -U postgres -d presentationdb < db/seed-e2e.sql
dc exec -T e2e-redis redis-cli FLUSHALL > /dev/null
read -r bosses chars < <(dc exec -T e2e-db env PGPASSWORD=e2e psql -U postgres -d presentationdb -t -A -F ' ' \
  -c 'select (select count(*) from "Boss"), (select count(*) from "Character")')
echo "Boss=$bosses Character=$chars"
if [ "${bosses:-0}" -le 0 ] || [ "${chars:-0}" -le 0 ]; then
  echo "seed 沒有進去，中止" >&2
  exit 1
fi

step "3/4 Playwright（--build：帶入最新 spec）→ $log"
set +e
if [ ${#pw_args[@]} -gt 0 ]; then
  dc --profile ci run --build --rm e2e-playwright npx playwright test "${pw_args[@]}" > "$log" 2>&1
else
  dc --profile ci run --build --rm e2e-playwright > "$log" 2>&1
fi
rc=$?
set -e

step "4/4 結果"
if grep -qE 'Running [0-9]+ test' "$log"; then
  grep -E 'Running [0-9]+ test|[0-9]+ (passed|failed|flaky|skipped|did not run)' "$log" | tail -6
  grep -E '^\s+[0-9]+\) \[' "$log" | head -20 || true
else
  echo "（沒有跑到測試，可能是 build 失敗；log 末段：）"
  tail -30 "$log"
fi
echo "playwright exit=$rc  完整 log：$log"

if [ "$down" = 1 ]; then
  step "拆除（down -v）"
  dc --profile ci down -v
fi

exit "$rc"
