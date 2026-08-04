# k8s/deploy.ps1 — 首次部署所有服務
# 使用前確認 secrets/ 目錄下各 txt 檔已填入正確值
#
# 應用（backend/frontend/bot）走 Kustomize、映像 pin 到當前 git SHA（與 CD 一致 → 無漂移）。
# 前提：backend/frontend/bot 的該 SHA 映像已推上 Docker Hub（由 CD 或 rollout.ps1）。
# 若是「還沒有任何映像」的全新 bootstrap，先用 rollout.ps1 各建一次、或先推 :latest。
#
# migrate 映像則由本腳本自己 build+push（步驟 6）——它不在 rollout.ps1 涵蓋範圍內，
# 過去 manual 部署會吃到過時的 minqq/migrate:latest 導致 schema 停在舊版，故內建重建。

$ns = "maple-raid"
$root = "$PSScriptRoot/.."

# 任何一步失敗就整個中止，避免像先前那樣 kubectl 出錯了還繼續跑、最後謊報部署完成
function Assert-Ok($msg) {
    if ($LASTEXITCODE -ne 0) { throw $msg }
}

# 0. 部署前安全檢查（main 分支、無未提交變更、與遠端同步）
. "$PSScriptRoot/assert-deploy-safety.ps1"

# 1. 建立 namespace（要先於 Secret，setup-secrets.ps1 的 kubectl create 需要 namespace 已存在）
Write-Host "==> 建立 namespace..."
kubectl apply -f "$PSScriptRoot/namespace.yaml"
Assert-Ok "建立 namespace 失敗"

# 2. 建立 Secret（真值由 secrets/ 的 txt 產生，不走 kustomize）
Write-Host "==> 建立 Secret..."
& "$PSScriptRoot/setup-secrets.ps1"
Assert-Ok "建立 Secret 失敗"

# 3. 基礎服務（database / seq / redis / cloudflared）
Write-Host "==> 部署基礎服務..."
kubectl apply -f "$PSScriptRoot/database.yaml" -f "$PSScriptRoot/seq.yaml" -f "$PSScriptRoot/redis.yaml" -f "$PSScriptRoot/cloudflared.yaml"
Assert-Ok "部署基礎服務失敗"

# 4. 等待 database 就緒
Write-Host "==> 等待 database 就緒..."
kubectl rollout status deployment/database -n $ns --timeout=120s
Assert-Ok "database 未能就緒"

# 5. 應用（backend / frontend / bot）— Kustomize，映像 pin 到當前 git SHA
Write-Host "==> 部署應用..."
$sha = (git -C $root rev-parse --short HEAD).Trim()
# 用 .NET File I/O 明確指定 UTF-8（無 BOM）讀寫，避免 Get-Content/Set-Content
# 依系統預設編碼（Windows PowerShell 5.1 常是 Big5）誤判，把中文註解寫壞
# 導致 kustomize 解析失敗（曾經發生：yaml: invalid leading UTF-8 octet）
$kustomizationPath = "$PSScriptRoot/kustomization.yaml"
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
$kustomizationContent = [System.IO.File]::ReadAllText($kustomizationPath, [System.Text.Encoding]::UTF8)
$kustomizationContent = $kustomizationContent -replace 'newTag: latest', "newTag: $sha"
[System.IO.File]::WriteAllText($kustomizationPath, $kustomizationContent, $utf8NoBom)
try {
    kubectl apply -k "$PSScriptRoot"
    Assert-Ok "部署應用（backend/frontend/bot）失敗"
} finally {
    git -C $root checkout -- k8s/kustomization.yaml   # 還原佔位，保持 git 乾淨
}

# 6. build + push migrate 映像
# migrations/ 目錄是 build 時烤進 image 的（見 db/Dockerfile.migrate），改了 migration 檔
# 就必須重 build，否則 migrate.sh 會跑到過時的 minqq/migrate:latest（migrations 停在舊版）、
# 回報「成功」卻沒真的更新 schema。對齊 CD（deploy.yml 每次都 build+push migrate）。
Write-Host "==> build + push migrate 映像 ($sha)..."
docker build -f "$root/db/Dockerfile.migrate" -t "minqq/migrate:$sha" -t "minqq/migrate:latest" "$root/db"
Assert-Ok "build migrate 映像失敗"
docker push "minqq/migrate:$sha"
Assert-Ok "push migrate:$sha 失敗"
docker push "minqq/migrate:latest"
Assert-Ok "push migrate:latest 失敗"

# 7. 執行 migration（migrate-job.yaml 用 :latest，imagePullPolicy 預設 Always → 拉到剛推的新版）
Write-Host "==> 執行 migration..."
bash "$PSScriptRoot/migrate.sh"
Assert-Ok "migration 失敗"

Write-Host "OK 部署完成 ($sha)"
