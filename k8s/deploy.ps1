# k8s/deploy.ps1 — 首次部署所有服務
# 使用前確認 secrets/ 目錄下各 txt 檔已填入正確值

$ns = "maple-raid"
$root = "$PSScriptRoot/.."

# 1. 建立 Secret
Write-Host "==> 建立 Secret..."
& "$PSScriptRoot/setup-secrets.ps1"

# 2. 建立 namespace
Write-Host "==> 建立 namespace..."
kubectl apply -f "$PSScriptRoot/namespace.yaml"

# 3. 部署所有服務
Write-Host "==> 部署所有服務..."
kubectl apply -f "$PSScriptRoot/"

# 4. 等待 database 就緒
Write-Host "==> 等待 database 就緒..."
kubectl rollout status deployment/database -n $ns --timeout=120s

# 5. 執行 migration
Write-Host "==> 執行 migration..."
bash "$PSScriptRoot/migrate.sh"

Write-Host "✅ 部署完成"
