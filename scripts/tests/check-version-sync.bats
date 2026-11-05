#!/usr/bin/env bats
# scripts/check-version-sync.py 的測試（case V1–V8，2026/11/04 核可）。
#
# 每個測試在暫存目錄建一個假 repo（設定檔 ＋ 被檢查的檔案），用 --root 指過去，
# 所以不依賴真實 repo 目前的版本狀態。
#
# 執行（WSL / Linux）：bats scripts/tests

SCRIPT="$BATS_TEST_DIRNAME/../check-version-sync.py"

setup() {
  FIX="$BATS_TEST_TMPDIR/repo"
  mkdir -p "$FIX/scripts" "$FIX/web"
}

write() { # $1=相對路徑 $2=內容
  mkdir -p "$(dirname "$FIX/$1")"
  printf '%s\n' "$2" > "$FIX/$1"
}

not_in_output() { # 否定斷言：bats 中不在最後一行的 `! [[ … ]]` 不會讓測試失敗（同 SC2314）
  if [[ "$output" == *"$1"* ]]; then
    echo "輸出不應包含：$1" >&2
    return 1
  fi
}

# 一組「完整版本」＋ 一組「大版本」的設定，涵蓋 JSON 與正規式兩種來源
write_config() {
  write scripts/version-sync.json '{
  "groups": [
    {
      "name": "Playwright",
      "compare": "exact",
      "sources": [
        { "path": "web/package-lock.json", "json": ["packages", "node_modules/@playwright/test", "version"] },
        { "path": "web/Dockerfile.e2e", "regex": "playwright:v([0-9][0-9.]*)" }
      ]
    },
    {
      "name": "Node",
      "compare": "major",
      "sources": [
        { "path": "ci.yml", "regex": "node-version:\\s*([0-9][0-9.]*)" },
        { "path": "web/Dockerfile", "regex": "^FROM node:([0-9][0-9.]*)" }
      ]
    }
  ]
}'
}

write_consistent_files() {
  write web/package-lock.json '{ "packages": { "node_modules/@playwright/test": { "version": "1.64.1" } } }'
  write web/Dockerfile.e2e 'FROM mcr.microsoft.com/playwright:v1.64.1-noble'
  write ci.yml '          node-version: 24'
  write web/Dockerfile $'FROM node:24-alpine AS deps\nFROM node:24-alpine AS builder\nFROM node:24-alpine AS runner'
}

run_check() {
  run python3 "$SCRIPT" --root "$FIX"
}

# ---------------------------------------------------------------- V1 一致

@test "V1 所有來源一致 → exit 0，列出每組版本" {
  write_config
  write_consistent_files

  run_check
  [ "$status" -eq 0 ]
  [[ "$output" == *"Playwright"*"1.64.1"* ]]
  [[ "$output" == *"Node"*"24"* ]]
}

# ---------------------------------------------------------------- V2 不一致

@test "V2 有一個來源不一致 → exit 1，列出每個位置與版本，標出不同的那個" {
  write_config
  write_consistent_files
  write web/Dockerfile.e2e 'FROM mcr.microsoft.com/playwright:v1.61.1-noble'

  run_check
  [ "$status" -eq 1 ]
  [[ "$output" == *"Playwright"* ]]
  [[ "$output" == *"web/package-lock.json"*"1.64.1"* ]]
  [[ "$output" == *"web/Dockerfile.e2e:1"*"1.61.1"* ]]
  # 標出不同的位置：兩個來源各一個值時無法以多數決判斷，兩邊都要標
  [[ "$output" == *"不一致"* ]]
  [[ "$output" == *"web/package-lock.json"*"1.64.1  ← 不同"* ]]
  [[ "$output" == *"web/Dockerfile.e2e:1"*"1.61.1  ← 不同"* ]]
}

@test "V2 多數決：三處中只有一處不同 → 只有那一處被標為不同" {
  write_config
  write_consistent_files
  write web/Dockerfile $'FROM node:24-alpine AS deps\nFROM node:22-alpine AS builder\nFROM node:24-alpine AS runner'

  run_check
  [ "$status" -eq 1 ]
  [[ "$output" == *"web/Dockerfile:2"*"22"*"← 不同"* ]]
  not_in_output "web/Dockerfile:1  24  ← 不同"
  not_in_output "ci.yml:1  24  ← 不同"
}

@test "V2 某一組失敗時，其他組仍會被檢查並列出（不提早結束）" {
  write_config
  write_consistent_files
  write web/Dockerfile.e2e 'FROM mcr.microsoft.com/playwright:v1.61.1-noble'

  run_check
  [ "$status" -eq 1 ]
  [[ "$output" == *"Node"*"24"* ]]
}

# ---------------------------------------------------------------- V3 同檔多處

@test "V3 同一檔案出現多次，每一處都要一致；第 3 處不同 → exit 1 並指出行號" {
  write_config
  write_consistent_files
  write web/Dockerfile $'FROM node:24-alpine AS deps\nFROM node:24-alpine AS builder\nFROM node:20-alpine AS runner'

  run_check
  [ "$status" -eq 1 ]
  [[ "$output" == *"web/Dockerfile:3"*"20"* ]]
}

# ---------------------------------------------------------------- V4 / V5 比對方式

@test "V4 只比大版本：24.19.2 與 24 視為一致" {
  write_config
  write_consistent_files
  write ci.yml '          node-version: 24.19.2'

  run_check
  [ "$status" -eq 0 ]
  # 只檢查 exit 0 的話，一律回 0 的空殼也會過 → 要確認真的有比對並列出該組
  [[ "$output" == *"Node"*"24"* ]]
}

@test "V5 比完整版本：1.64.1 與 1.64.0 視為不一致" {
  write_config
  write_consistent_files
  write web/Dockerfile.e2e 'FROM mcr.microsoft.com/playwright:v1.64.0-noble'

  run_check
  [ "$status" -eq 1 ]
}

# ---------------------------------------------------------------- V6 / V7 抽不到就失敗

@test "V6 正規式對不到（檔案寫法被改掉）→ exit 1，指出檔案與規則，不得靜默跳過" {
  write_config
  write_consistent_files
  write web/Dockerfile.e2e 'FROM some-other-image:latest'

  run_check
  [ "$status" -eq 1 ]
  [[ "$output" == *"web/Dockerfile.e2e"* ]]
  [[ "$output" == *"抽不到版本"* ]]
}

@test "V6 JSON 路徑不存在 → exit 1，指出檔案與路徑" {
  write_config
  write_consistent_files
  write web/package-lock.json '{ "packages": {} }'

  run_check
  [ "$status" -eq 1 ]
  [[ "$output" == *"web/package-lock.json"* ]]
  [[ "$output" == *"抽不到版本"* ]]
}

@test "V7 設定檔指定的檔案不存在 → exit 1" {
  write_config
  write_consistent_files
  rm "$FIX/ci.yml"

  run_check
  [ "$status" -eq 1 ]
  [[ "$output" == *"ci.yml"* ]]
  [[ "$output" == *"找不到檔案"* ]]
}

@test "V7 path 用萬用字元：符合的每個檔案都會被檢查；一個都沒對到 → exit 1" {
  write scripts/version-sync.json '{
  "groups": [
    { "name": "PostgreSQL", "compare": "major", "sources": [
      { "path": "compose*.yaml", "regex": "image:\\s*postgres:([0-9][0-9.]*)" },
      { "path": "Fixture.cs", "regex": "\"postgres:([0-9][0-9.]*)\"" }
    ] }
  ]
}'
  write Fixture.cs '.WithImage("postgres:18")'
  write compose.yaml '    image: postgres:18'
  write compose.e2e.yaml '    image: postgres:17'

  run_check
  [ "$status" -eq 1 ]
  [[ "$output" == *"compose.e2e.yaml:1"*"17"* ]]

  rm "$FIX/compose.yaml" "$FIX/compose.e2e.yaml"
  run_check
  [ "$status" -eq 1 ]
  [[ "$output" == *"compose*.yaml"* ]]
  [[ "$output" == *"找不到檔案"* ]]
}

# ---------------------------------------------------------------- V9 輸出端編碼

@test "V9 輸出端編碼不是 UTF-8（Windows 導向管線時是 cp950）→ 仍正常輸出，不得 UnicodeEncodeError" {
  write_config
  write_consistent_files

  PYTHONIOENCODING=cp950 run_check
  [ "$status" -eq 0 ]
  [[ "$output" == *"Playwright"*"1.64.1"* ]]
  not_in_output "UnicodeEncodeError"
}

# ---------------------------------------------------------------- V8 從其他目錄執行

@test "V8 不帶 --root、從 repo 以外的目錄執行 → 以腳本所在 repo 為根目錄" {
  write_config
  write_consistent_files
  cp "$SCRIPT" "$FIX/scripts/check-version-sync.py"

  cd "$BATS_TEST_TMPDIR"
  run python3 "$FIX/scripts/check-version-sync.py"
  [ "$status" -eq 0 ]
  [[ "$output" == *"Playwright"*"1.64.1"* ]]
}
