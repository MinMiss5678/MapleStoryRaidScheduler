# 部署流程（手動 SSH）

> 本機 `deploy.ps1` / `rollout.ps1`，經 SSH tunnel 連 Lightsail k3s。
> rollback / migrate 失敗恢復 / expand-contract 向後相容見 `docs/cd-deploy-setup.md`（部署 runbook）。

> ⚠️ **工具落差（現況，2026-08 起）**：本機 **Docker Desktop 已解除安裝（改 WSL2 原生 docker）、也沒裝本機 kubectl** → `deploy.ps1` / `rollout.ps1` 在 Windows 直接跑不了（`docker` / `kubectl` 不在 PATH）。
> 目前實際的部署走法見下方 [〈現況實際走法：WSL build + SSH 進機器〉](#現況實際走法無本機-docker-desktop--kubectl)。以下 `deploy.ps1` / SSH tunnel 段落是「本機已裝好 Docker Desktop + kubectl」時的原始設計，保留供之後補齊工具時參考。

## 前置條件

- Docker 可用（現況：WSL2 Ubuntu 原生 docker，見記憶 `local-docker-native-wsl`；Git Bash 打 `docker` 會 command not found，要包 `wsl -d Ubuntu -u jeremy bash -lc '…'`）並登入 Docker Hub（`minqq`）
- 能操作目標 cluster：本機 kubectl 連上（原始設計，見下）**或** 直接 SSH 進機器用 `sudo k3s kubectl`（現況）
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

### 現況實際走法（無本機 Docker Desktop / kubectl）

沒有本機 kubectl，就**不建 tunnel、不跑 `deploy.ps1`**，改成「WSL 出 image + SSH 進機器套用」兩步。這是 2026-08 這次上線實際採用的路徑：

```bash
# 前置：登入 Docker Hub（互動式帳密，用 ! 前綴讓輸出進 session）
wsl -d Ubuntu -u jeremy bash -lc 'docker login'

# 1. WSL 內 build + push 四個 image（標當前 git SHA + latest）
SHA=$(git rev-parse --short HEAD)
wsl -d Ubuntu -u jeremy bash -lc "cd /mnt/c/Users/jerem/RiderProjects/MapleStoryRaidScheduler && \
  docker build -f Presentation.WebApi/Dockerfile -t minqq/presentation.webapi:$SHA -t minqq/presentation.webapi:latest . && \
  docker build -f web/Dockerfile            -t minqq/frontend:$SHA -t minqq/frontend:latest web && \
  docker build -f Presentation/Dockerfile   -t minqq/presentation:$SHA -t minqq/presentation:latest . && \
  docker build -f db/Dockerfile.migrate     -t minqq/migrate:$SHA -t minqq/migrate:latest db && \
  docker push minqq/presentation.webapi:$SHA && docker push minqq/presentation.webapi:latest && \
  docker push minqq/frontend:$SHA && docker push minqq/frontend:latest && \
  docker push minqq/presentation:$SHA && docker push minqq/presentation:latest && \
  docker push minqq/migrate:$SHA && docker push minqq/migrate:latest"

# 2. SSH 進機器（非 tunnel），在 box 上直接用 sudo k3s kubectl 套用
#    manifest 本機用 sed 把 :latest 換成 :SHA 後 pipe 進去 apply（保持版本可追溯）
KEY=~/Downloads/LightsailDefaultKey-ap-northeast-1.pem   # 帳號 ec2-user、IP 見下
sed "s|minqq/\([a-z.]*\):latest|minqq/\1:$SHA|g" k8s/backend.yaml \
  | ssh -i "$KEY" ec2-user@13.115.11.237 'sudo k3s kubectl apply -f -'
# migrate 同法（先跑 migration，再換 backend/bot code——新 code 依賴新欄位）
ssh -i "$KEY" ec2-user@13.115.11.237 'sudo k3s kubectl rollout status deploy/backend -n maple-raid --timeout=120s'
```

- **migration 先於 code**：先套 migrate job 等它完成，再換 backend/bot image；新 code 依賴新欄位，反過來會在 rollout 空窗炸。additive 的 `ADD COLUMN` 可回退，舊 pod 會撐到新 pod Ready，零中斷。
- **IP / key**：`13.115.11.237`（可能變動）、`ec2-user`、key 在 `~/Downloads/LightsailDefaultKey-ap-northeast-1.pem`。完整脈絡見記憶 `prod-deployment-lightsail-k3s`。

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

`DiscordRoleMapping` **沒有後台 UI，也不在 migration 的 seed 裡**（值是每個 Discord 伺服器各自不同，寫不進通用 migration），全新環境必須手動塞，否則沒有任何人登得進去。

> period-less（Phase 4d）後 `JobCategory` 表已退場，不再需要 seed 職業分類。

`AuthAppService.LoginAsync` 靠這張表把 Discord 身分組 ID 解析成系統角色（admin/user）；解析不到就登入失敗。角色 ID 從 Discord（開發者模式 → 右鍵身分組 → 複製 ID）取得：

```bash
kubectl exec -n maple-raid deployment/database -- psql -U postgres -d presentationdb -c \
  'INSERT INTO "DiscordRoleMapping" ("DiscordRoleId","Role","Priority") VALUES
     (<管理員身分組ID>, '"'"'admin'"'"', 10),
     (<玩家身分組ID>,   '"'"'user'"'"',  0)
   ON CONFLICT ("DiscordRoleId") DO UPDATE SET "Role"=EXCLUDED."Role", "Priority"=EXCLUDED."Priority";'
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

## Secret 交付（檔案掛載 + 部署前 preflight）

Secret 走**檔案掛載**（非 env `secretKeyRef`）：`maple-secrets` 整包掛成唯讀檔，code 讀 `{Key}File` 指到的路徑（見 `k8s/backend.yaml` / `k8s/bot.yaml`、plan `2026-08-25-k8s-secrets-file-mount.md`）。

- **掛載點是 `/etc/maple-secrets`，不是 `/run/secrets`。** k8s 會把 serviceaccount token 掛在 `/var/run/secrets/kubernetes.io/serviceaccount`（`/var/run`→`/run`，落在 `/run/secrets` 底下）；若把 secret volume **唯讀**掛在 `/run/secrets`，kubelet 無法在唯讀 mount 裡建那個 mountpoint → 容器 `RunContainerError`（read-only file system）起不來。compose 用 `/run/secrets` 沒這問題（無 serviceaccount 掛載）。**首次 file-mount 上 prod 踩過這坑**，記錄在 `k8s/backend.yaml` 註解。
- **整包掛（不用 `items:`）**：只把 secret 現有的 key 變成檔。選填 secret（`sentry_dsn` / `discord_hash_key` / `microsoft_mail_*`）沒建該 key → 沒那個檔 → code 讀不到當未設定、pod 照起；用 `items:` 逐 key 挑會因缺 key 掛載失敗、pod 起不來。
- **部署前 preflight**（`k8s/assert-required-secrets.ps1`，plan `2026-08-27-prod-required-secrets-preflight.md`）：`deploy.ps1`（建 secret 後）與 `rollout.ps1`（安全檢查後）都會呼叫，檢 cluster 的 `maple-secrets` **必填 7 key** 存在且非空（`db_connection`、`db_connection_url`、`postgres_password`、`discord_bot_token`、`discord_client_secret`、`jwt_secret_key`、`cloudflared_token`），缺/空就**中止部署並指名**——把「jwt/client_secret 忘填 → pod Ready 卻登不進」的靜默壞提前擋在 rollout 前。選填 key 刻意不檢（缺＝功能關）。

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

> ⚠️ 清空 DB 會一併清掉 `DiscordRoleMapping`，重新部署後要**再做一次「Seed 參考資料」那節**，否則一樣沒人登得進去。

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
