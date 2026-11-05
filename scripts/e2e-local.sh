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
# 參數以 argv 原樣傳遞，**不拼成 shell 字串**：
#   - --cd 收 Windows 絕對路徑，由 wsl.exe 自己換算成 /mnt/...（wsl --help）
#   - -e 直接執行命令、不經預設 Linux 殼層 → 參數不會被重新解析
#   曾用 printf %q 拼成 `bash -lc "…"`：Git Bash 未設 locale 時 %q 會把中文切成不合法的 UTF-8，
#   跨過 wsl.exe 後變亂碼（--grep 中文找不到測試）。直接傳 argv 就沒有這類問題。
# MSYS_NO_PATHCONV：Git Bash 呼叫 Windows 程式時，會把「看起來像 Unix 路徑」的參數改寫
#   （/ 開頭、= 後面、冒號分隔清單；例：--grep /api/Boss → C:/Program Files/Git/api/Boss）。
#   Git for Windows 只檢查這個變數「存不存在」，值是多少都會關掉改寫。
if ! command -v docker >/dev/null 2>&1 && command -v wsl.exe >/dev/null 2>&1; then
  user_opt=()
  [ -n "${E2E_WSL_USER:-}" ] && user_opt=(-u "$E2E_WSL_USER")
  MSYS_NO_PATHCONV=1 exec wsl.exe -d "${E2E_WSL_DISTRO:-Ubuntu}" ${user_opt[@]+"${user_opt[@]}"} \
    --cd "$(git rev-parse --show-toplevel)" -e bash -l scripts/e2e-local.sh "$@"
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

step "0/4 版本一致性檢查（Playwright npm↔image 等）"
# 版本對不上時（例如 dependabot 只升了 npm 的 @playwright/test、沒升 image），E2E 會整套全倒、
# 錯誤訊息看不出原因 → 起 stack 前先擋，省下 build 的幾分鐘。
python3 scripts/check-version-sync.py

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
