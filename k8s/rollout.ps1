# k8s/rollout.ps1 — 重新 build + push + rollout 指定服務
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

$config = @{
    backend  = @{ image = "minqq/presentation.webapi:latest"; dockerfile = "Presentation.WebApi/Dockerfile"; context = "." }
    frontend = @{ image = "minqq/frontend:latest";            dockerfile = "web/Dockerfile";                 context = "web" }
    bot      = @{ image = "minqq/presentation:latest";        dockerfile = "Presentation/Dockerfile";        context = "." }
}

$svc = $config[$Service]

Write-Host "==> Building $Service..."
docker build -f "$root/$($svc.dockerfile)" -t $svc.image "$root/$($svc.context)"

Write-Host "==> Pushing $($svc.image)..."
docker push $svc.image

Write-Host "==> Rolling out $Service..."
kubectl rollout restart deployment/$Service -n $ns
kubectl rollout status deployment/$Service -n $ns --timeout=120s

Write-Host "✅ $Service 更新完成"
