#!/usr/bin/env bash
# 建立/設定 Microsoft Graph OAuth2 寄信用的 Azure App Registration，並跑一次性登入拿 refresh token。
# 背景/決策見 plans/2026-07-31-error-alerting.md、plans/2026-08-02-azure-app-registration-automation.md，
# 踩過的坑見 docs/microsoft-mail-oauth2-setup.md。
#
# 唯一的人工步驟：腳本會印出授權網址，貼到瀏覽器登入同意後，把導回的網址貼回這裡
# （OAuth2 設計上就需要真人同意，不自動化、也不應該自動化）。
#
# 用法：./k8s/setup-microsoft-mail-oauth.sh
# 重跑安全（idempotent）：App 已存在時會沿用既有 appId，只更新設定，不會建立重複的 App。

set -euo pipefail

DISPLAY_NAME="seq-mail-alert"
REDIRECT_URI="http://localhost:8400"
# Microsoft Graph 的 Mail.Send / offline_access（Delegated）權限 id，查證方式：
#   az ad sp show --id 00000003-0000-0000-c000-000000000000 \
#     --query "oauth2PermissionScopes[?value=='Mail.Send' || value=='offline_access'].{value:value, id:id}"
GRAPH_RESOURCE_APP_ID="00000003-0000-0000-c000-000000000000"
MAIL_SEND_SCOPE_ID="e383f46e-2787-4529-855e-0e479a3ffac0"
OFFLINE_ACCESS_SCOPE_ID="7427e0e9-2fba-42fe-b0c0-848c9e6a8182"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SECRETS_DIR="$REPO_ROOT/secrets"
REFRESH_TOKEN_FILE="$SECRETS_DIR/microsoft_mail_refresh_token.txt"

echo "== 1. 檢查 az CLI =="
if ! command -v az >/dev/null 2>&1; then
  echo "找不到 az CLI，先安裝（Windows: winget install Microsoft.AzureCLI）" >&2
  exit 1
fi

if ! az account show >/dev/null 2>&1; then
  echo "尚未登入。個人帳號沒有 Azure 訂閱會踩到已知 CLI bug（Azure/azure-cli#31992），" >&2
  echo "請先手動執行：az login --tenant <你的租戶 GUID> --allow-no-subscriptions" >&2
  exit 1
fi

echo "== 2. 找既有 App（依 displayName，找不到才建立新的） =="
APP_ID=$(az ad app list --display-name "$DISPLAY_NAME" --query "[0].appId" -o tsv)

if [ -z "$APP_ID" ]; then
  echo "找不到既有 App，建立新的：$DISPLAY_NAME"
  APP_ID=$(az ad app create \
    --display-name "$DISPLAY_NAME" \
    --sign-in-audience PersonalMicrosoftAccount \
    --is-fallback-public-client true \
    --query "appId" -o tsv)
  echo "已建立 App，appId=$APP_ID"
else
  echo "沿用既有 App，appId=$APP_ID"
fi

echo "== 3. 套用設定（redirect URI / token 版本 / API 權限），重跑安全 =="
az ad app update --id "$APP_ID" \
  --sign-in-audience PersonalMicrosoftAccount \
  --is-fallback-public-client true \
  --public-client-redirect-uris "$REDIRECT_URI" \
  --requested-access-token-version 2 \
  >/dev/null

# az ad app permission add 本身不是冪等的（重跑會產生重複的 resourceAccess 項目），
# 先查現有權限清單，兩個 scope id 都已存在才跳過。
EXISTING_SCOPES=$(az ad app permission list --id "$APP_ID" --query "[0].resourceAccess[].id" -o tsv 2>/dev/null || echo "")
if echo "$EXISTING_SCOPES" | grep -q "$MAIL_SEND_SCOPE_ID" && echo "$EXISTING_SCOPES" | grep -q "$OFFLINE_ACCESS_SCOPE_ID"; then
  echo "Mail.Send / offline_access 權限已存在，跳過"
else
  az ad app permission add --id "$APP_ID" \
    --api "$GRAPH_RESOURCE_APP_ID" \
    --api-permissions "${MAIL_SEND_SCOPE_ID}=Scope" "${OFFLINE_ACCESS_SCOPE_ID}=Scope" \
    >/dev/null
fi

echo "App 設定完成：appId=$APP_ID"

echo "== 4. 產生 PKCE 參數 =="
CODE_VERIFIER=$(openssl rand -hex 32)
CODE_CHALLENGE=$(printf '%s' "$CODE_VERIFIER" | openssl dgst -sha256 -binary | openssl base64 | tr '+/' '-_' | tr -d '=')
echo "已產生（長度：${#CODE_VERIFIER}）"

echo "== 5. 開瀏覽器完成登入同意（唯一的人工步驟） =="
# REDIRECT_URI 是腳本頂端固定常數（http://localhost:8400），URL-encode 後的值直接寫死，
# 不用動態編碼（避免依賴 python/sed 這類環境不保證存在的工具）。
REDIRECT_URI_ENCODED="http%3A%2F%2Flocalhost%3A8400"
AUTH_URL="https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize?client_id=${APP_ID}&response_type=code&redirect_uri=${REDIRECT_URI_ENCODED}&response_mode=query&scope=offline_access%20https%3A%2F%2Fgraph.microsoft.com%2FMail.Send&state=setup&code_challenge=${CODE_CHALLENGE}&code_challenge_method=S256"

echo ""
echo "請把下面這個網址貼到瀏覽器開啟、登入、按同意："
echo ""
echo "$AUTH_URL"
echo ""
echo "同意後瀏覽器會導向 http://localhost:8400/?code=...（顯示「無法連上這個網站」是正常的）。"
read -r -p "把網址列整段複製貼在這裡，按 Enter： " REDIRECTED_URL

CODE="${REDIRECTED_URL#*code=}"
CODE="${CODE%%&*}"

if [ -z "$CODE" ]; then
  echo "沒有解析到 code，確認貼的是完整網址（含 ?code=...）" >&2
  exit 1
fi

echo "== 6. 用 code 換 token =="
TOKEN_RESPONSE=$(curl -s -X POST "https://login.microsoftonline.com/consumers/oauth2/v2.0/token" \
  --data-urlencode "client_id=${APP_ID}" \
  --data-urlencode "scope=https://graph.microsoft.com/Mail.Send offline_access" \
  --data-urlencode "code=${CODE}" \
  --data-urlencode "redirect_uri=${REDIRECT_URI}" \
  --data-urlencode "grant_type=authorization_code" \
  --data-urlencode "code_verifier=${CODE_VERIFIER}")

REFRESH_TOKEN=$(echo "$TOKEN_RESPONSE" | grep -o '"refresh_token":"[^"]*"' | sed 's/"refresh_token":"//;s/"$//')

if [ -z "$REFRESH_TOKEN" ]; then
  echo "換 token 失敗，回應內容：" >&2
  echo "$TOKEN_RESPONSE" >&2
  exit 1
fi

echo "== 7. 存 refresh token =="
mkdir -p "$SECRETS_DIR"
printf '%s' "$REFRESH_TOKEN" > "$REFRESH_TOKEN_FILE"
echo "已寫入 $REFRESH_TOKEN_FILE"

echo ""
echo "完成。App 設定："
echo "  MicrosoftMail:TenantId = consumers"
echo "  MicrosoftMail:ClientId = $APP_ID"
echo "  MicrosoftMail:RefreshTokenFile = $REFRESH_TOKEN_FILE"
