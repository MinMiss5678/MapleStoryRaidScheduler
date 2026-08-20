# 部署 runbook（rollback / migrate 失敗恢復 / 向後相容）

> 部署操作步驟見 `docs/deployment.md`（本機 `k8s/deploy.ps1` / `rollout.ps1`，經 SSH tunnel 連 Lightsail k3s）。
> 本檔是與操作分開的 runbook：每次部署都可能用到的 rollback、migrate 失敗恢復、向後相容紀律。

## rollback

```bash
kubectl rollout undo deployment/backend -n maple-raid     # 退回上一個 SHA（真 rollback，映像不可變）
kubectl rollout status deployment/backend -n maple-raid
# 或指定退到某個 SHA：
kubectl set image deployment/backend backend=minqq/presentation.webapi:<舊SHA> -n maple-raid
```

> **readiness 探針是安全網**：新 pod `/health/ready` 沒綠，k8s 不會把它加進 Service、也不收舊 pod → 壞版本不會接到流量（見 `k8s/backend.yaml`）。但 `rollout status` 會等到 timeout 才失敗，別以為卡住。

## 設計說明（常被誤認成坑，其實是刻意設計）

- **`imagePullPolicy` / SHA tag**：`kustomization.yaml` 的 `newTag` committed 值是 `latest`（佔位），部署時 `sed` 成 SHA。SHA 是新 tag → kubelet 沒有 → 一定拉；rollback 到舊 SHA 命中節點快取（不可變、正確）。不需額外設 policy。
- **兩條路徑一致（無漂移）**：`deploy.ps1` `apply -k` 同一份 kustomization（映像 = 當次 git SHA）；`rollout.ps1` 是單一服務快速更新，用 `set image`（重建那一個服務的 SHA）。因映像都 pin git SHA，重跑任一路徑得到的都是該 commit 的版本 → 不會意外打回 `:latest`。

## 已知坑 / 未決

- **前提：SHA 映像須先存在**：`deploy.ps1` 部署當前 commit 的 SHA 前，該映像要已推上 Docker Hub。全新 bootstrap（還沒任何映像）先用 `rollout.ps1` 各建一次或推 `:latest`。
- **frontend `NEXT_PUBLIC_*`（server 端使用、runtime 帶入）**：這些變數目前**只在 server 端 route handler**（`app/api/auth/discord`）使用；prod 走 standalone `node server.js`，在 request 時讀 **runtime env**（由 `k8s/frontend.yaml` 的 `env` / compose 的 `environment:` 提供）。**換域名只需改 k8s env，不必 rebuild / build-arg。**
  - ⚠️ footgun：`next build` **沒烤入任何值** → 若日後在 **client component** 用這些 `NEXT_PUBLIC_*`，prod 會是 `undefined`、且 runtime env 救不了（命名誤導）。那時才需要在 build 階段用 `--build-arg` + Dockerfile `ARG/ENV` 注入。
- **migrate 失敗**：`deploy.ps1` 會讓部署中止在 migration 那步（滾動更新不會跑）→ 半發版風險低（映像已推但沒 rollout）。⚠️ **不能直接重跑**——golang-migrate 會把版本標 dirty、擋住 `up`。恢復步驟見下方 runbook。
- **rollout 後無完整業務煙霧測試**：readiness 查 `/health/ready`（已加深為查核心表 `"Boss"`，見 `DatabaseHealthCheck`）→ schema/migration 沒套用也會被擋。仍未做的是「完整業務端點驗證」（需 auth token），對此規模刻意不加。

---

## Migrate 失敗恢復（runbook）

> 「修好重跑」少了關鍵一步：golang-migrate 失敗會把版本標 **dirty**，直接 `up` 會報 `Dirty database version N. Fix and force version.` → **必須先清 dirty**。

**1. 看為什麼失敗**
```bash
kubectl logs job/migrate -n maple-raid   # 哪句 SQL、什麼錯
```

**2. 看 dirty 與版本**（psql / exec 進 DB pod）
```sql
SELECT * FROM schema_migrations;   -- version + dirty
```

**3. 判斷實際套用到哪**
- Postgres 交易式 DDL + golang-migrate 每個 migration 包交易 → 失敗通常**自己 rollback**，DB 停在 `N-1`、只是被標 dirty。
- 例外：**非交易語句**（`CREATE INDEX CONCURRENTLY` 等）會**部分套用** → 手動查 schema 確認半套了什麼。

**4. 清 dirty**
- 沒部分套用（常見）→ force 回上一個好版本：
  ```bash
  # 用同一個 migrate image 跑一次性 force（args 換成 force N-1，不是 up）
  kubectl run migrate-fix --rm -it --image=minqq/migrate:<SHA> \
    --env="DB_URL=<同 Job 的連線>" -- \
    -path=/migrations -database=$DB_URL force <N-1>
  ```
  或直接改表（force 底層就是這個）：
  ```sql
  UPDATE schema_migrations SET dirty = false, version = <N-1>;
  ```
- 有部分套用 → 先手動撤掉半套的物件（drop 半建的 index / 表）→ 再 force `N-1`。

**5. 修 forward、重建映像、重跑**
```bash
# 修好 db/migrations/000xxx.up.sql（或補一版修正 migration）
docker build -f db/Dockerfile.migrate -t minqq/migrate:<新SHA> db/ && docker push ...
# 重跑 migrate Job（up）→ 乾淨套用
```
> 一律**往前修**，不在 prod 跑 `migrate down`——`down.sql` 是開發工具；prod rollback 靠 DB snapshot / PITR。

## 讓 migration 失敗不致命：向後相容（expand-contract）

真正把「migration 失敗 / rollback」的殺傷力降到最低的，不是換工具，是**紀律**：

- **加不刪、不在同一次 deploy 又改又用**：新 schema 讓**舊 code 仍能跑**、新 code 也能跑舊 schema。
- 破壞性變更拆兩步：先 **expand**（加新欄位 / 表、必要時雙寫）→ 部署新 code、確認穩 → 下一版才 **contract**（刪舊）。
- 好處：`migrate` 先於 `rollout` 已保護「失敗 → 舊 code + 舊 schema 續跑」；再加向後相容，即使 migration 半套或需 rollback，**線上舊 pod 也不會因 schema 對不上而爆**。
