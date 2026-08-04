# 部署流程（手動）

> 這是**手動**部署（本機 `deploy.ps1` / `rollout.ps1`）。
> **CI 自動化 CD**（GitHub Actions `deploy.yml`，`workflow_dispatch` 手動觸發：推映像 → migrate → 滾動更新）見 `docs/cd-deploy-setup.md`——兩者做同一件事，CD 版把這裡的步驟搬進 workflow。

## 前置條件

- Docker Desktop 已安裝並登入 Docker Hub
- kubectl 已設定連到目標 K8s cluster
- `secrets/` 目錄已建立並填入各 txt 檔（已在 .gitignore，建立方式參考 `k8s/secrets.yaml` 註解）

### 連到正式環境（Lightsail k3s）

正式環境是 AWS Lightsail 上的單節點 k3s，**k8s API 不對外開放**，透過 SSH tunnel 連：

```bash
# 1. SSH tunnel：把本機 6443 轉發到 EC2 上的 k3s API（k3s kubeconfig 的 server 本來就是 127.0.0.1:6443，不用改）
ssh -i <lightsail-key.pem> -N -L 6443:127.0.0.1:6443 ec2-user@<EC2_IP>

# 2. 首次：抓 k3s kubeconfig（/etc/rancher/k3s/k3s.yaml），把 context/cluster/user 改名為 maple-prod 後併進 ~/.kube/config
#    （避免與本機測試用的 docker-desktop / kind context 混淆）

# 3. 部署前務必切到正式 context
kubectl config use-context maple-prod
```

> `deploy.ps1` / `rollout.ps1` 的 `assert-deploy-safety.ps1` 會強制檢查 context = `maple-prod`、在 main 分支、無未提交變更、與 origin 同步，才放行——避免手滑部署到本機測試叢集或未提交的程式碼。

## 首次部署

**前提**：`deploy.ps1` 對 backend/frontend/bot 是**假設該 git SHA 的映像已推上 Docker Hub**（走 Kustomize pin SHA）。全新 bootstrap（Docker Hub 上還沒有任何映像）要**先各 build+push 一次**：

```powershell
.\k8s\rollout.ps1 backend
.\k8s\rollout.ps1 frontend
.\k8s\rollout.ps1 bot
```

（migrate 映像不用先推——`deploy.ps1` 會自己 build+push，見下方流程步驟 6。）

接著跑首次部署：

```powershell
.\k8s\deploy.ps1
```

自動執行：安全檢查 → 建立 namespace → 建立 Secret → 部署基礎服務（db/seq/redis/cloudflared）→ 等待 DB 就緒 → `apply -k` 部署三個應用（Kustomize，映像 pin 當前 git SHA）→ **build+push migrate 映像** → 執行 migration

> ⚠️ **部署完成後還沒結束**——必須手動 seed 參考資料，否則「誰都登不進去」。見下一節。

---

## Seed 參考資料（首次部署後**必做**）

有兩張表**沒有後台 UI，也不在 migration 的 seed 裡**（值是每個 Discord 伺服器各自不同，寫不進通用 migration），全新環境必須手動塞，否則系統不可用：

### 1. DiscordRoleMapping（不 seed → 沒有任何人登得進去）

`AuthAppService.LoginAsync` 靠這張表把 Discord 身分組 ID 解析成系統角色（admin/user）；解析不到就登入失敗。角色 ID 從 Discord（開發者模式 → 右鍵身分組 → 複製 ID）取得：

```bash
kubectl exec -n maple-raid deployment/database -- psql -U postgres -d presentationdb -c \
  'INSERT INTO "DiscordRoleMapping" ("DiscordRoleId","Role","Priority") VALUES
     (<管理員身分組ID>, '"'"'admin'"'"', 10),
     (<玩家身分組ID>,   '"'"'user'"'"',  0)
   ON CONFLICT ("DiscordRoleId") DO UPDATE SET "Role"=EXCLUDED."Role", "Priority"=EXCLUDED."Priority";'
```

### 2. JobCategory（不 seed → 補位提示的分類分組失效）

只放「真的要分組喊」的職業（見 CLAUDE.md「JobCategory is display-only」）；單獨喊的職業（拳霸/夜使者…）可不放。範例：

```bash
kubectl exec -n maple-raid deployment/database -- psql -U postgres -d presentationdb -c \
  'INSERT INTO "JobCategory" ("JobName","CategoryName") VALUES
     ('"'"'英雄'"'"','"'"'劍士'"'"'),('"'"'黑騎士'"'"','"'"'劍士'"'"'),('"'"'聖騎士'"'"','"'"'劍士'"'"'),
     ('"'"'火毒大魔導士'"'"','"'"'法師'"'"'),('"'"'冰雷大魔導士'"'"','"'"'法師'"'"'),('"'"'主教'"'"','"'"'法師'"'"'),
     ('"'"'箭神'"'"','"'"'高單體'"'"'),('"'"'槍神'"'"','"'"'高單體'"'"');'
```

---

## 更新程式碼

```powershell
.\k8s\rollout.ps1 backend    # 後端
.\k8s\rollout.ps1 frontend   # 前端
.\k8s\rollout.ps1 bot        # Discord Bot
```

自動執行：docker build（SHA + latest 雙標籤）→ docker push → kubectl set image 到該 SHA → 等待完成

---

## 新增 Migration

```bash
# 1. 產生 SQL 檔
bash db/create-migration.sh <MigrationName>

# 2. 確認產生的 SQL 內容正確
# db/migrations/<version>_<MigrationName>.up.sql
# db/migrations/<version>_<MigrationName>.down.sql

# 3. 重新 build + push migrate image（SHA + latest 雙標籤，與 CD / deploy.ps1 一致、可追溯）
#    ⚠️ 一定要重 build：migrations/ 是 build 時烤進 image 的，改了 migration 沒重 build，
#    migrate.sh 會跑到過時的 :latest、回報「成功」卻沒真的更新 schema。
SHA=$(git rev-parse --short HEAD)
docker build -f db/Dockerfile.migrate -t minqq/migrate:$SHA -t minqq/migrate:latest db/
docker push minqq/migrate:$SHA
docker push minqq/migrate:latest

# 4. 執行 migration
bash k8s/migrate.sh
```

> 上面是「只補一條 migration、單獨套用」的流程。**完整部署走 `deploy.ps1` 的話，步驟 3+4 已內建**（步驟 6 會自己 build+push migrate 再跑），不必手動做。

---

## 改密碼（保留資料）

```bash
# 1. 先在 DB 裡改密碼
kubectl exec -it -n maple-raid deployment/database -- psql -U postgres -c "ALTER USER postgres PASSWORD '新密碼';"
```

```powershell
# 2. 更新 secrets/postgres_password.txt 為新密碼
# 3. 重建 Secret 並重啟 database
.\k8s\setup-secrets.ps1
kubectl rollout restart deployment/database -n maple-raid
```

---

## 重置 DB（清空所有資料）

```powershell
# 警告：以下操作會清空所有資料
kubectl delete secret maple-secrets -n maple-raid
kubectl delete pvc db-data -n maple-raid
kubectl delete deployment database -n maple-raid
kubectl delete svc database -n maple-raid

# 重新部署
.\k8s\deploy.ps1
```

> ⚠️ 清空 DB 會一併清掉 `DiscordRoleMapping` / `JobCategory`，重新部署後要**再做一次「Seed 參考資料」那節**，否則一樣沒人登得進去。

---

## 常用查詢指令

```powershell
# 查看所有 Pod 狀態
kubectl get pods -n maple-raid

# 查看 migration log
kubectl logs -n maple-raid job/migrate

# 查看某服務 log
kubectl logs -n maple-raid deployment/backend
kubectl logs -n maple-raid deployment/frontend
kubectl logs -n maple-raid deployment/bot
```
