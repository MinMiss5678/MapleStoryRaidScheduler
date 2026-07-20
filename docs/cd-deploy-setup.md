# CD 部署設定筆記（GitLab CI → k8s 滾動更新）

`.gitlab-ci.yml` 的 `deploy` job：把 `k8s/rollout.ps1`（build + push `minqq/*` → `kubectl rollout restart`）＋ `migrate-job` 搬進 CI。
**CD 到 prod 一律人工點擊**（`when: manual`）、只在 `main`。等於「一鍵發版」，但那一鍵是人按的。

> 手動部署版（本機 `deploy.ps1` / `rollout.ps1`）見 `docs/deployment.md`——本 stage 就是把它搬進 CI。

> ⚠️ **EC2 叢集目前已移除**，`deploy` job 無法執行（kubeconfig 指向的叢集不存在）。重新佈建 k8s 叢集並更新 `KUBECONFIG_B64` 後可再啟用。

## 前置：CI/CD Variables（Settings → CI/CD → Variables，全設 protected + masked）

| 變數 | 用途 | 怎麼拿 |
|---|---|---|
| `DOCKERHUB_USER` | 推 `minqq/*` 到 Docker Hub（k8s 從這拉映像） | Docker Hub 帳號 |
| `DOCKERHUB_TOKEN` | 同上（用 access token 非密碼） | Docker Hub → Account → Security → New Access Token |
| `KUBECONFIG_B64` | 讓 CI 連得到叢集 | `base64 -w0 ~/.kube/config`（貼整串）|

> masked 需符合遮罩規則（無換行、長度足夠）；base64 的 kubeconfig 一般 OK。protected 確保只有 protected branch（main）能讀到。

## 流程（job 內做的事）

1. buildx（docker-container driver + `network=host`）——與 e2e job 同套路（見 `e2e-testing-setup.md`）。
2. 登入 Docker Hub（推映像）＋ GitLab Registry（存 layer cache，與 e2e 共用）。
3. **build + push 4 映像**：backend / frontend（**prod 目標**，非 e2e 的 `--target dev`）/ bot / migrate。backend·frontend 走 registry layer cache。
4. **migration**：Job spec immutable → 先 `kubectl delete job migrate` 再 `apply` → `kubectl wait complete`。
5. **滾動更新**：`kubectl rollout restart` 三個 deployment → `rollout status` 逐一等綠。

## 觸發

`main` 有新 commit → pipeline 出現 `deploy`（manual，灰色）→ 進 pipeline 頁點 ▶ 才會跑。
或 `glab ci trigger deploy`。

## 回滾

```bash
kubectl rollout undo deployment/backend -n maple-raid     # 退回上一版
kubectl rollout status deployment/backend -n maple-raid
```

> **readiness 探針是安全網**：新 pod `/health/ready` 沒綠，k8s 不會把它加進 Service、也不收舊 pod → 壞版本不會接到流量（見 `k8s/backend.yaml`）。但 `rollout status` 會等到 timeout 才失敗，別以為卡住。

## 已知坑 / 未決

- **frontend `NEXT_PUBLIC_*`**：Next.js 這類變數是 **build 時**烤進去的。目前 build 沒帶 `--build-arg`（照 `rollout.ps1`）→ 靠 Dockerfile 內建預設值；若要換域名得改 Dockerfile 或加 build-arg。
- **`imagePullPolicy`**：deployment 用 `:latest` → 預設 `Always`，`rollout restart` 才會拉到新映像。若改用固定 tag 要自己設 policy。
- **migrate 失敗**：job 失敗會讓 deploy 中止在 migration 那步（滾動更新不會跑）→ 半發版風險低（映像已推但沒 rollout）。修好 migration 重跑即可。
- **首次跑**：kubectl 是每次 `curl` 下載（docker:27 alpine 無內建）——想省時可改用含 kubectl 的映像。
