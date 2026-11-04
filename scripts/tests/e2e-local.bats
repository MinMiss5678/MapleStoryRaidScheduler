#!/usr/bin/env bats
# scripts/e2e-local.sh 的判斷邏輯測試。
#
# 外部程式（docker / curl / wsl.exe / git / sleep）一律換成 PATH 最前面的假指令，由測試控制回應。
# 測的是：該中止時有沒有中止、exit code 有沒有傳出去、路徑換算對不對、關鍵旗標（--build）有沒有帶。
#
# ⚠️ 假指令是對真指令行為的「假設」（例如 psql 計數查詢輸出 `2 7`）。真的 docker / psql / WSL
#    是否照這些假設運作，這裡測不到——那要靠本機實跑 `scripts/e2e-local.sh` 完整套件確認。
#
# 刻意**不測**「Git Bash 轉進 WSL」那段（2026/11/04 決議）：
#   - 它壞掉時是大聲失敗（立刻報錯或 No tests found），實際使用就會發現
#   - 假指令重現不了 Git Bash 的行為：曾有測試在這段是綠的，但中文參數的亂碼 bug 確實存在
#   → 這裡只測「壞了不會出聲」的防護（等待、seed 驗證、exit code、--build）；轉進 WSL 那段以
#     「參數直接傳、不拼字串」在結構上避開問題，改動時從 Git Bash 實際執行一次確認。
#
# 執行（WSL / Linux）：bats scripts/tests
#
# ⚠️ 否定斷言一律用 not_in_file，**不要寫 `! grep …`**：bats 靠 set -e 判定失敗，
#    而 bash 的 set -e 會忽略被 `!` 反轉的指令 → 不是最後一行時完全沒作用（shellcheck SC2314）。

# 假指令內容刻意用單引號：$變數要在假指令「執行時」才展開，不是寫入時
# shellcheck disable=SC2016

SCRIPT="$BATS_TEST_DIRNAME/../e2e-local.sh"

make_stub() { # $1=指令名 $2=內容
  printf '#!/usr/bin/env bash\n%s\n' "$2" > "$STUB/$1"
  chmod +x "$STUB/$1"
}

not_in_file() { # $1=pattern $2=檔案（grep -E 規則）；出現就失敗
  if grep -qE -- "$1" "$2"; then
    echo "不應出現：$1（於 $2）" >&2
    grep -nE -- "$1" "$2" >&2
    return 1
  fi
}

setup() {
  export T="$BATS_TEST_TMPDIR"
  export WORK="$T/repo" CALLS="$T/calls"
  STUB="$T/stub"
  mkdir -p "$WORK/db" "$STUB"
  : > "$CALLS"
  printf -- '-- fake seed\nSELECT 1;\n' > "$WORK/db/seed-e2e.sql"
  touch "$WORK/compose.e2e.yaml"

  # 假指令的預設行為：一切正常
  export FAKE_HTTP_BACKEND=200 FAKE_HTTP_FRONTEND=200
  export FAKE_COUNTS="2 7" FAKE_SEED_RC=0 FAKE_PW_RC=0
  export FAKE_PW_OUT=$'Running 3 tests using 1 worker\n  3 passed (1.0s)'

  make_stub git 'echo "$WORK"'
  make_stub sleep ':'
  make_stub curl 'case "$*" in *5230*) printf %s "$FAKE_HTTP_BACKEND" ;; *) printf %s "$FAKE_HTTP_FRONTEND" ;; esac'
  make_stub docker '
echo "docker $*" >> "$CALLS"
case "$*" in
  *" up "*)                      exit 0 ;;
  *"ON_ERROR_STOP"*)             cat > "$T/seed_stdin"; exit "$FAKE_SEED_RC" ;;
  *"exec -T e2e-db"*)            echo "$FAKE_COUNTS"; exit 0 ;;
  *"exec -T e2e-redis"*)         exit 0 ;;
  *" run "*)                     printf "%s\n" "$FAKE_PW_OUT"; exit "$FAKE_PW_RC" ;;
  *" down "*)                    exit 0 ;;
esac'
  export PATH="$STUB:$PATH"
}

# ---------------------------------------------------------------- 守門（該中止就中止）

@test "S4 backend 一直沒 ready → 中止，不執行 seed 與 playwright" {
  FAKE_HTTP_BACKEND=503 run bash "$SCRIPT"

  [ "$status" -ne 0 ]
  [[ "$output" == *"backend 沒有 ready"* ]]
  not_in_file "ON_ERROR_STOP" "$CALLS"
  not_in_file " run " "$CALLS"
}

@test "S5 seed 後 Boss 筆數為 0 → 中止，不執行 playwright" {
  FAKE_COUNTS="0 7" run bash "$SCRIPT"

  [ "$status" -ne 0 ]
  [[ "$output" == *"seed 沒有進去"* ]]
  not_in_file " run " "$CALLS"
}

@test "S5 seed 後 Character 筆數為 0 → 中止，不執行 playwright" {
  FAKE_COUNTS="2 0" run bash "$SCRIPT"

  [ "$status" -ne 0 ]
  not_in_file " run " "$CALLS"
}

@test "S6 seed 的 psql 失敗 → 中止，不執行 playwright" {
  FAKE_SEED_RC=3 run bash "$SCRIPT"

  [ "$status" -ne 0 ]
  not_in_file " run " "$CALLS"
}

@test "S7 playwright 回傳 1 → 腳本也回傳 1（不得被吞成 0）" {
  FAKE_PW_RC=1 FAKE_PW_OUT=$'Running 3 tests using 1 worker\n  1 failed\n  2 passed (1.0s)' run bash "$SCRIPT"

  [ "$status" -eq 1 ]
  [[ "$output" == *"playwright exit=1"* ]]
  [[ "$output" == *"1 failed"* ]]
}

@test "S8 log 沒有 Running N test（build 失敗）→ 印出 log 末段，並傳回原 exit code" {
  FAKE_PW_RC=17 FAKE_PW_OUT="ERROR: failed to solve: build broke here" run bash "$SCRIPT"

  [ "$status" -eq 17 ]
  [[ "$output" == *"沒有跑到測試"* ]]
  [[ "$output" == *"build broke here"* ]]
}

# ---------------------------------------------------------------- 參數

@test "S9 帶 --down 才在最後 down -v" {
  run bash "$SCRIPT" --down
  [ "$status" -eq 0 ]
  grep -q " down -v" "$CALLS"

  : > "$CALLS"
  run bash "$SCRIPT"
  [ "$status" -eq 0 ]
  not_in_file " down " "$CALLS"
}

@test "S10 帶 --grep 時 playwright 收到 test --grep X；沒帶參數就用預設 command" {
  run bash "$SCRIPT" --grep G8
  [ "$status" -eq 0 ]
  grep -q "run --build --rm e2e-playwright npx playwright test --grep G8$" "$CALLS"

  : > "$CALLS"
  run bash "$SCRIPT"
  grep -q "run --build --rm e2e-playwright$" "$CALLS"
}

# ---------------------------------------------------------------- 正常流程

@test "S11 正常流程：--build 起 stack、seed 檔送進 psql、清 Redis、playwright 帶 --build，順序正確" {
  run bash "$SCRIPT"

  [ "$status" -eq 0 ]
  [[ "$output" == *"3 passed"* ]]
  grep -q " up -d --build --wait e2e-frontend" "$CALLS"
  cmp -s "$WORK/db/seed-e2e.sql" "$T/seed_stdin"
  grep -q "exec -T e2e-redis redis-cli FLUSHALL" "$CALLS"
  grep -q " run --build " "$CALLS"

  # 順序：up → seed → flush → 計數 → playwright
  local up seed flush run_line
  up=$(grep -n " up -d " "$CALLS" | cut -d: -f1)
  seed=$(grep -n "ON_ERROR_STOP" "$CALLS" | cut -d: -f1)
  flush=$(grep -n "FLUSHALL" "$CALLS" | cut -d: -f1)
  run_line=$(grep -n " run " "$CALLS" | cut -d: -f1)
  [ "$up" -lt "$seed" ] && [ "$seed" -lt "$flush" ] && [ "$flush" -lt "$run_line" ]
}
