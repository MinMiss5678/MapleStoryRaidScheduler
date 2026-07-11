# 部署流程

## 前置條件

- Docker Desktop 已安裝並登入 Docker Hub
- kubectl 已設定連到目標 K8s cluster
- `secrets/` 目錄已建立並填入各 txt 檔（已在 .gitignore，建立方式參考 `k8s/secrets.yaml` 註解）

## 首次部署

```powershell
.\k8s\deploy.ps1
```

自動執行：建立 Secret → 部署所有服務 → 等待 DB 就緒 → 執行 migration

---

## 更新程式碼

```powershell
.\k8s\rollout.ps1 backend    # 後端
.\k8s\rollout.ps1 frontend   # 前端
.\k8s\rollout.ps1 bot        # Discord Bot
```

自動執行：docker build → docker push → kubectl rollout restart → 等待完成

---

## 新增 Migration

```bash
# 1. 產生 SQL 檔
bash db/create-migration.sh <MigrationName>

# 2. 確認產生的 SQL 內容正確
# db/migrations/<version>_<MigrationName>.up.sql
# db/migrations/<version>_<MigrationName>.down.sql

# 3. 重新 build + push migrate image
docker build -f db/Dockerfile.migrate -t minqq/migrate:latest db/
docker push minqq/migrate:latest

# 4. 執行 migration
bash k8s/migrate.sh
```

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
