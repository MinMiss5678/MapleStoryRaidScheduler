# k8s prod secret 交付：env（secretKeyRef）→ 檔案（volume mount）

> 輕量 plan（動手前 spec）：目標 / 背景 / 決策 / 範圍 / 驗收 / 工時。（scope 已定＝bot+backend；無待你決策的未決 → 無風險段）
> 關聯：`k8s/bot.yaml`、`k8s/backend.yaml`、`k8s/secrets.yaml`（maple-secrets）、`compose.yaml`（早已檔案模式、作對照）。
> 觸發：Discord 設定清理討論延伸（已併 PR #102＝E2E dummy 移除）。安全性依據見該討論 + web 查證（volume 掛載較 env 少洩漏面）。

## 目標

把 prod（Lightsail k3s）的 secret 從 `valueFrom: secretKeyRef`（注入成**環境變數**）改成 **volume 掛載成檔案**，走較安全的交付。**code 零改動**——8 個 secret 的 `*File` 分支早已存在、compose.yaml 就在用。

## 背景

- **現況**：`k8s/bot.yaml` + `backend.yaml` 用 `secretKeyRef` → secret 變 env。env 交付洩漏面較大：`/proc/<pid>/environ` 可讀、子行程繼承、易隨 crash dump/log 帶出、輪替要重啟 pod。
- **compose.yaml 早就是檔案模式**（`*File=/run/secrets/<key>` + `secrets:`），只有 k8s 用 env → **code 本就雙模式、`*File` 全支援**。
- 安全增益在單節點 k3s 小 app 屬**邊際**，但方向正確、成本低（只改 manifest）、且統一 compose/k8s 交付風格。K8s Secret 本身仍是 base64（非加密）——encryption-at-rest / 外部 secrets store 是更大題，**本計畫不含**。

## 決策

1. **只改 manifest、code 不動**：8 個 secret 全部已有 `*File` code 路徑（下表），保留 value+file **雙模式 code**（E2E/本機仍靠 value——如 `compose.e2e.yaml` 的連線字串是值——**不可砍 value 分支**，否則 E2E/本機炸）。
2. **每個 deployment 加 secret volume + volumeMount**：`maple-secrets` 掛到 `/run/secrets`（`readOnly: true`），env 從 `secretKeyRef` 改成 `*File=/run/secrets/<key>`（路徑/命名對齊 compose.yaml）。
3. **`maple-secrets` Secret 物件不動**（同 key、值來源不變）。
4. **範圍限 bot + backend**（我方 app）；其餘吃 maple-secrets 的第三方 image 見非範圍。
5. **檔案要讓 container 執行 user 讀得到**：k8s secret volume 預設 root 擁有；若 container 跑非 root，需設 `defaultMode`（如 `0444`）或 pod `securityContext.fsGroup`，否則 app 讀不到 → 驗收要 `kubectl exec` 實測讀得到。

映射（env → 檔案，key 名不變）：

| deployment | secret key | 現 env | 改成 |
|---|---|---|---|
| bot + backend | db_connection | `ConnectionStrings__DefaultConnection` | `...__DefaultConnectionFile=/run/secrets/db_connection` |
| bot + backend | discord_bot_token | `Discord__BotToken` | `Discord__BotTokenFile=/run/secrets/discord_bot_token` |
| backend | discord_client_secret | `Discord__ClientSecret` | `Discord__ClientSecretFile=…` |
| backend | jwt_secret_key | `Jwt__SecretKey` | `Jwt__SecretKeyFile=…` |
| backend | microsoft_mail_refresh_token | `MicrosoftMail__RefreshToken` | `…__RefreshTokenFile=…` |
| backend | microsoft_mail_webhook_secret | `MicrosoftMail__WebhookSecret` | `…__WebhookSecretFile=…` |
| backend | sentry_dsn | `Sentry__Dsn` | `Sentry__DsnFile=…` |
| backend | discord_hash_key | `Sentry__DiscordIdHashKey` | `Sentry__DiscordIdHashKeyFile=…` |

## 範圍

- 改 `k8s/bot.yaml`、`k8s/backend.yaml`：加 `volumes`(secret `maple-secrets`) + `volumeMounts`(`/run/secrets`, readOnly)，上表 8 條 env `secretKeyRef` → `*File`。
- 視情況設 `defaultMode`/`fsGroup`（決策 5）。
- 更新 `docs/deployment.md` / `k8s/secrets.yaml` 註解（若有描述 env 交付）。

## 非範圍（YAGNI / 另議）

- **cloudflared / database / migrate-job**（也吃 maple-secrets）：第三方 image，`*File` 形式各異（postgres 原生 `POSTGRES_PASSWORD_FILE`、cloudflared `--token-file`、migrate 需改 command 讀檔）→ 非我方 code、驗法不同，**另開議**。
- **不動 code**（雙模式保留）、不動 `maple-secrets` 值、不動 `compose*.yaml`。
- 不引入 external secrets manager / encryption-at-rest。
- **可選後續（本計畫不含）——抽「file-or-value」共用 helper**：「`{key}File` 有檔就讀檔、否則讀 `{key}` 值」這個 pattern 在 bot + WebApi 重複、且散在連線字串 / Redis `ConfigurationFile` / Discord token / WebApi `PostConfigure`（Jwt/Sentry…）多處。可抽一個 `ResolveFileOrValue(config, key)` 一次收斂兩 app + 所有 secret。**邏輯簡單又穩定 → YAGNI，別單獨開工**；若日後剛好要碰這些設定載入再順手抽（邊際成本低）。
- **不統一 `IDbConnectionFactory`**：bot 用 factory（因有 3 個背景 poller 共用連線設定）、WebApi 用行內 `new NpgsqlConnection`——差異是**需求不同、可辯護**，硬套 factory 給 WebApi = 為統一而加抽象（無收穫），維持現狀。

## 驗收

- [ ] `kubectl apply --dry-run=server -f k8s/bot.yaml -f k8s/backend.yaml` 通過。
- [ ] rollout 後 pod **Ready、無 CrashLoop**；`kubectl exec … -- ls -l /run/secrets` 確認 8 個檔案存在、**且 app user 讀得到**（決策 5）。
- [ ] **prod smoke（k8s 不在 E2E、只能 prod 驗）**：backend `health/ready`=200（連得上 DB）、**真 Discord OAuth 登入成功**、bot **DM 送得出**（leader-led 通知，證 BotToken 檔案讀對）。
- [ ] **rollback 就緒**：出錯即 `kubectl rollout undo deploy/backend`（+ bot）回上一版 env 交付。
- [ ] 部署照 `docs/deployment.md`（deploy.ps1/rollout.ps1；`assert-deploy-safety` 檢查 context=maple-prod、main、無未提交）。

## 工時估

- 改 2 份 manifest + dry-run ≈ 1~2 小時；部署 + prod smoke + 觀察/必要時 rollback ≈ 半天。
