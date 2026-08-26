# k8s/rollout.ps1 — 重新 build + push（SHA + latest 雙標籤）+ set image 到該 SHA
#
# 與 CD（.gitlab-ci.yml deploy job）一致：映像用不可變的 git short SHA 打 tag，
# 用 kubectl set image 換版 → rollout undo 能真的退回上一個 SHA，線上版本可追溯。
#
# 用法：
#   .\k8s\rollout.ps1 backend
#   .\k8s\rollout.ps1 frontend
#   .\k8s\rollout.ps1 bot

param(
    [Parameter(Mandatory)][ValidateSet("backend","frontend","bot")]
    [string]$Service
)

$ns = "maple-raid"
$root = "$PSScriptRoot/.."

# 任何一步失敗就整個中止，避免 docker/kubectl 出錯了還繼續跑、最後謊報更新完成
function Assert-Ok($msg) {
    if ($LASTEXITCODE -ne 0) { throw $msg }
}

# 部署前安全檢查（main 分支、無未提交變更、與遠端同步）
. "$PSScriptRoot/assert-deploy-safety.ps1"

# 必填 secret 檢查（secret 由先前 deploy.ps1 已建於 cluster）——缺/空就中止換版
. "$PSScriptRoot/assert-required-secrets.ps1"

$config = @{
    backend  = @{ repo = "minqq/presentation.webapi"; dockerfile = "Presentation.WebApi/Dockerfile"; context = "." }
    frontend = @{ repo = "minqq/frontend";            dockerfile = "web/Dockerfile";                 context = "web" }
    bot      = @{ repo = "minqq/presentation";        dockerfile = "Presentation/Dockerfile";        context = "." }
}

$svc = $config[$Service]

# 版本 = 當前 git short SHA（對應 CD 的 $CI_COMMIT_SHORT_SHA）
$sha = (git -C $root rev-parse --short HEAD).Trim()
$img = "$($svc.repo):$sha"

Write-Host "==> Building $Service ($sha)..."
docker build -f "$root/$($svc.dockerfile)" -t $img -t "$($svc.repo):latest" "$root/$($svc.context)"
Assert-Ok "Build $Service 失敗"

Write-Host "==> Pushing $img (+ latest)..."
docker push $img
Assert-Ok "Push $img 失敗"
docker push "$($svc.repo):latest"
Assert-Ok "Push $($svc.repo):latest 失敗"

Write-Host "==> Setting image -> $img ..."
kubectl set image deployment/$Service "$Service=$img" -n $ns
Assert-Ok "set image 失敗"
kubectl rollout status deployment/$Service -n $ns --timeout=120s
Assert-Ok "$Service rollout 未能就緒"

Write-Host "OK $Service 更新完成 ($sha)"
