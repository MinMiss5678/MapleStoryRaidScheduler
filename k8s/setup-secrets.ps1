# k8s/setup-secrets.ps1 — 從 secrets/ 目錄建立 K8s Secret
# 使用前確認 secrets/ 目錄下各 txt 檔已填入正確值
# secrets/ 已在 .gitignore，不會提交至 git

$ns = "maple-raid"
$secretName = "maple-secrets"
$dir = "$PSScriptRoot/../secrets"

# 從 postgres_password.txt 自動產生 URL encoded 的 db_connection_url.txt
$pw = Get-Content "$dir/postgres_password.txt" -Raw
$encoded = [System.Uri]::EscapeDataString($pw)
"postgresql://postgres:${encoded}@database:5432/presentationdb?sslmode=disable" | Set-Content "$dir/db_connection_url.txt" -NoNewline
Write-Host "==> db_connection_url.txt 已更新"

# 刪除舊 Secret（如存在）
kubectl delete secret $secretName -n $ns --ignore-not-foun
# 建立新 Secret
kubectl create secret generic $secretName -n $ns `
  --from-file=db_connection="$dir/db_connection.txt" `
  --from-file=db_connection_url="$dir/db_connection_url.txt" `
  --from-file=postgres_password="$dir/postgres_password.txt" `
  --from-file=discord_bot_token="$dir/discord_bot_token.txt" `
  --from-file=discord_client_secret="$dir/discord_client_secret.txt" `
  --from-file=jwt_secret_key="$dir/jwt_secret_key.txt" `
  --from-file=cloudflared_token="$dir/cloudflared_token.txt"

Write-Host "✅ Secret 建立完成"
