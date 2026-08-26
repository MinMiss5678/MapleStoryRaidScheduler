# prod 部署前檢查必填 secret（preflight，不動 app code）

> 輕量 plan（動手前 spec）：目標 / 背景 / 決策 / 範圍 / 驗收 / 工時 / 非範圍。（決策已定 → 無風險段）
> 關聯：`k8s/assert-deploy-safety.ps1`、`k8s/deploy.ps1`、`k8s/setup-secrets.ps1`、`k8s/rollout.ps1`、`k8s/secrets.yaml`。
> 觸發：必填 secret 現況防守是拼盤（bot token `?? throw` / db 靠 readiness probe / **jwt、client_secret 缺了靜默**，pod Ready 卻登不進）。與其在 app code 加 Production-gated fail-fast（污染程式碼、rollout 後才 CrashLoop），不如在**部署前**於 prod 層擋掉——最早、最省、零 code 改。

## 目標

在 **rollout 之前**驗證 prod cluster 的 `maple-secrets` **必填 key 都存在且非空**；缺或空 → **中止部署**並指名是哪個 key。把「必填 secret 忘填 → pod 靜默壞（jwt/client_secret）或 not Ready（db）」提前到「**根本不讓你 rollout**」。

## 背景

- 現況必填防守不一致（讀 source 確認）：`discord_bot_token` 有 `?? throw`（bot startup 炸）；`db_connection` 只有 `!`（null-forgiving，非守衛）＋靠 **readiness probe** 擋；`jwt_secret_key` / `discord_client_secret` **無守衛 → lazy 靜默**（pod Ready 但認證/登入壞）。
- prod 已有兩層可補：`assert-deploy-safety.ps1`（部署前檢 branch/context/未提交）+ 計畫強制 **prod smoke**（真 OAuth / bot DM / health）。preflight secret 檢查填補「rollout 前」這個更早的點。
- **不走 app code fail-fast**：單人手動部署，「有人繞過部署流程」屬 YAGNI；code 加 Production-gated 分支＝污染。放部署腳本最貼合。

## 決策

1. **防守在 prod 部署腳本、不動 app code**。
2. **檢 cluster 的 `maple-secrets`**（權威＝實際會掛進 pod 的），非本機 `secrets/*.txt`（可能與 cluster 漂移）。`kubectl get secret maple-secrets -o json` → 各必填 key base64 解碼後 trim 非空。
3. **必填清單（7）**：`db_connection`、`db_connection_url`、`postgres_password`、`discord_bot_token`、`discord_client_secret`、`jwt_secret_key`、`cloudflared_token`。
   **選填明確不檢**：`sentry_dsn`、`discord_hash_key`、`microsoft_mail_refresh_token`、`microsoft_mail_webhook_secret`（缺＝功能關、app 照跑，見整包掛契約）。
4. **新 helper `k8s/assert-required-secrets.ps1`**（dot-source 或 function）：檢查失敗即 `throw`（中止）。
5. **接入時機**（secret 需已存在才檢得到）：
   - `deploy.ps1`：在 `setup-secrets.ps1`（建 secret）**之後**、`kubectl apply -k`（rollout）**之前**呼叫。
   - `rollout.ps1`：在 `assert-deploy-safety.ps1` **之後**呼叫（secret 由先前 deploy.ps1 已建、存在）。
6. **補強非取代**：readiness probe（db runtime）+ prod smoke（功能面）照留；三層防禦。

## 範圍

- 新增 `k8s/assert-required-secrets.ps1`：`kubectl get secret maple-secrets -n maple-raid -o json` → 逐必填 key 檢「data 有該 key 且 base64 解碼 trim 後非空」→ 收集缺/空清單 → 有則 `throw "prod maple-secrets 必填有問題：<key(缺key/空值)> …"`。
- `deploy.ps1`：setup-secrets 後、apply -k 前 dot-source 呼叫。
- `rollout.ps1`：assert-deploy-safety 後呼叫。
- （選配）`k8s/secrets.yaml` 註解補一行：必填/選填清單 + 「preflight 會擋必填缺項」。

## 驗收

- [ ] 故意把某必填 key（如 jwt_secret_key）在 cluster 清空或刪 → 跑 rollout.ps1 → **被 abort、訊息指名該 key**、未觸 rollout。
- [ ] 必填齊全 → preflight 通過、照常 build/set image/rollout。
- [ ] 選填缺（sentry_dsn 空/無）→ preflight **不擋**、部署照跑。
- [ ] deploy.ps1（首次）+ rollout.ps1（更新）兩條路都接上、時機正確（secret 已存在才檢）。
- [ ] 純 PowerShell、無 app code 改動；prod-only（本機/E2E 不受影響）。

## 工時估
- helper + 兩處接線 + 本機對 prod cluster 手測 ≈ 半天內。真驗證需實際跑一次 rollout（你的手動步驟）。

## 非範圍（YAGNI）
- **不加 app code fail-fast**（見決策 1；jwt 的靜默由此 preflight + prod smoke 覆蓋）。
- **不驗選填**（缺＝功能關是刻意契約）。
- **不驗值的「正確性」**（只驗非空；token 對不對交給 prod smoke 的真 OAuth / bot DM）。
- 不引入 admission controller / external secrets 驗證（單節點手動部署過重）。
