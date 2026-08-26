# k8s/assert-required-secrets.ps1 — 部署前檢查 prod maple-secrets 必填 key 存在且非空
# 供 deploy.ps1 / rollout.ps1 dot-source 呼叫（. "$PSScriptRoot/assert-required-secrets.ps1"）。
#
# 必填缺/空 → throw 中止 rollout（比 pod 啟動 CrashLoop 更早、比 app code fail-fast 更不污染）。
# 選填（sentry_dsn / discord_hash_key / microsoft_mail_*）刻意不檢：缺＝對應功能關、app 照跑
#   （對齊 secret volume 整包掛的契約——缺選填 key 不會讓 pod 起不來）。
# 只驗「非空」；值對不對（token 有效性）交給部署後的 prod smoke（真 OAuth / bot DM）。
# 前提：maple-secrets 已存在於 cluster（deploy.ps1 在 setup-secrets 之後呼叫；rollout.ps1 secret 由先前 deploy 已建）。

$ns = "maple-raid"
$requiredKeys = @(
    "db_connection", "db_connection_url", "postgres_password",
    "discord_bot_token", "discord_client_secret", "jwt_secret_key", "cloudflared_token"
)

Write-Host "==> 檢查 prod maple-secrets 必填 secret..."
$secretJson = kubectl get secret maple-secrets -n $ns -o json 2>$null
if ($LASTEXITCODE -ne 0 -or -not $secretJson) {
    throw "找不到 secret 'maple-secrets'（namespace $ns）。請先執行 k8s/setup-secrets.ps1 建立。"
}
$secret = $secretJson | ConvertFrom-Json

$problems = @()
foreach ($key in $requiredKeys) {
    $b64 = $secret.data.$key
    if (-not $b64) {
        $problems += "$key（缺 key）"
        continue
    }
    $value = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64)).Trim()
    if (-not $value) {
        $problems += "$key（空值）"
    }
}

if ($problems.Count -gt 0) {
    throw "prod maple-secrets 必填 secret 有問題：$($problems -join '、')。請補齊（見 k8s/secrets.yaml 必填清單 / setup-secrets.ps1）後再部署。"
}

Write-Host "==> 必填 secret 檢查通過（$($requiredKeys.Count) 個皆存在且非空）"
