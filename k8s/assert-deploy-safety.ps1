# k8s/assert-deploy-safety.ps1 — 部署前安全檢查
# 供 deploy.ps1 / rollout.ps1 用 dot-source 呼叫（. "$PSScriptRoot/assert-deploy-safety.ps1"）
#
# 確保：
# 1. 在 main 分支、沒有未提交變更（否則 docker build 會把未提交內容打進 image，
#    但 image 卻標記著目前 commit 的 SHA，SHA 對不上實際內容，以後沒法追溯）
# 2. 本機 main 跟 origin/main 同步（避免部署到過期或本機獨有、還沒推上去的版本）
# 3. kubectl 目前指向的 context 是正式環境（$expectedContext），避免手滑對到本機測試叢集
#    （例如本機用 kind/Docker Desktop 測 k8s manifest 時建立的 context）

$expectedContext = "maple-prod"

$branch = (git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne "main") {
    throw "目前在分支 '$branch'，不是 main。部署只能從 main 執行，避免部署到還在修改中的分支。"
}

$status = git status --porcelain
if ($status) {
    throw "工作目錄有未提交的變更，會被一併 build 進 image 但 SHA tag 對不上實際內容。請先 commit 或 stash：`n$status"
}

Write-Host "==> 檢查本機 main 是否與 origin/main 同步..."
git fetch origin main --quiet
$localSha = (git rev-parse main).Trim()
$remoteSha = (git rev-parse origin/main).Trim()
if ($localSha -ne $remoteSha) {
    throw "本機 main（$localSha）跟 origin/main（$remoteSha）不同步，請先 git pull 或確認是否要推上去再部署。"
}

$currentContext = (kubectl config current-context).Trim()
if ($currentContext -ne $expectedContext) {
    throw "kubectl 目前指向 context '$currentContext'，不是正式環境 '$expectedContext'。請先執行 kubectl config use-context $expectedContext，避免部署到錯的叢集（例如本機測試用的 kind/Docker Desktop）。"
}

Write-Host "==> 安全檢查通過：main 分支、無未提交變更、與遠端同步（$localSha）、context 為 $expectedContext"
