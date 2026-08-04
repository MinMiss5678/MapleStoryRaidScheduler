# k8s/deploy.ps1 — 首次部署所有服務
# 使用前確認 secrets/ 目錄下各 txt 檔已填入正確值
#
# 應用（backend/frontend/bot）走 Kustomize、映像 pin 到當前 git SHA（與 CD 一致 → 無漂移）。
# 前提：該 SHA 的映像已推上 Docker Hub（由 CD 或 rollout.ps1）。若是「還沒有任何映像」的
# 全新 bootstrap，先用 rollout.ps1 各建一次、或先推 :latest。

$ns = "maple-raid"
$root = "$PSScriptRoot/.."

# 0. 部署前安全檢查（main 分支、無未提交變更、與遠端同步）
. "$PSScriptRoot/assert-deploy-safety.ps1"

# 1. 建立 namespace（要先於 Secret，setup-secrets.ps1 的 kubectl create 需要 namespace 已存在）
Write-Host "==> 建立 namespace..."
kubectl apply -f "$PSScriptRoot/namespace.yaml"

# 2. 建立 Secret（真值由 secrets/ 的 txt 產生，不走 kustomize）
Write-Host "==> 建立 Secret..."
& "$PSScriptRoot/setup-secrets.ps1"

# 3. 基礎服務（database / seq / redis / cloudflared）
Write-Host "==> 部署基礎服務..."
kubectl apply -f "$PSScriptRoot/database.yaml" -f "$PSScriptRoot/seq.yaml" -f "$PSScriptRoot/redis.yaml" -f "$PSScriptRoot/cloudflared.yaml"

# 4. 等待 database 就緒
Write-Host "==> 等待 database 就緒..."
kubectl rollout status deployment/database -n $ns --timeout=120s

# 5. 應用（backend / frontend / bot）— Kustomize，映像 pin 到當前 git SHA
Write-Host "==> 部署應用..."
$sha = (git -C $root rev-parse --short HEAD).Trim()
(Get-Content "$PSScriptRoot/kustomization.yaml") -replace 'newTag: latest', "newTag: $sha" | Set-Content "$PSScriptRoot/kustomization.yaml"
try {
    kubectl apply -k "$PSScriptRoot"
} finally {
    git -C $root checkout -- k8s/kustomization.yaml   # 還原佔位，保持 git 乾淨
}

# 6. 執行 migration
Write-Host "==> 執行 migration..."
bash "$PSScriptRoot/migrate.sh"

Write-Host "OK 部署完成 ($sha)"
