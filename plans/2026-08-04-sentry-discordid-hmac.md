# Sentry 敏感資料防護補強（DiscordId HMAC 雜湊 + breadcrumb scrub）

> 輕量 plan（動手前的 spec）：目標 / 範圍 / 決策 / 驗收 / 工時。做完可丟；穩定規則再收進 `docs/`。

## 背景

`plans/2026-08-03-bugsink-integration.md` 完成 BugSink → sentry.io 遷移後，跑了一輪全 codebase audit（背景 agent 掃描所有 `_logger.Log*` 結構化屬性 + 例外訊息 + secret 清單），抓到幾個目前送去 Sentry 但沒被 `BeforeSend`/`ScrubSensitive` 保護到的缺口：

- **`DiscordId`**：目前明碼轉成 Sentry tag，還沒真的雜湊（先前只有寫 plan，沒動手）
- **`ClientIp`**（`Program.cs:157`，限流觸發時的 fail-open log）：Warning 等級，達到 `MinimumBreadcrumbLevel=Warning` 門檻，會夾帶成 Sentry breadcrumb 送出去，真實使用者 IP 完全沒被 scrub 到（`ScrubSensitive` 現在只掃主要例外訊息，不掃 breadcrumb）
- **`ex.Message` 被當結構化屬性再記一次**（`ExceptionHandlerMiddleware.cs:42`，業務例外分支）：同樣是 Warning、同樣會變成 breadcrumb，內容不受控（哪個 throw 站點放了什麼文字進例外訊息無法保證）
- **OAuth token JSON 解析失敗**（`DiscordOAuthClient.cs` 三處 `JsonConvert.DeserializeObject`/`JsonSerializer.Deserialize`、`MicrosoftMailService.cs` 一處）：理論上、低機率——解析失敗時例外訊息可能夾帶原始回應內容的片段（含 token 值），且 Discord/MS 的 token 不是 JWT 格式，現有的 JWT regex 抓不到

## 目標

一次性把「送去 Sentry 的資料」這個問題**在單一關卡（`BeforeSend`）修好**，不用逐一改每個 log 呼叫點——之後任何新的 log 只要走同一條 Serilog → Sentry 管線，都自動受到保護。

## 範圍

1. **DiscordId → HMAC-SHA256**：新增密鑰,`SetTag("DiscordId", raw)` 換成 `SetTag("discord_id_hash", HMAC-SHA256(密鑰, raw))`
2. **`ScrubSensitive` 擴充兩種 pattern**：
   - IPv4 位址（涵蓋 `ClientIp` 外洩）
   - JSON 格式的 token 欄位（`"access_token":"..."`、`"refresh_token":"..."` 這類 key-value，不管 token 是不是 JWT 格式都抓得到，涵蓋 OAuth 解析失敗的邊界情況）
3. **`BeforeSend` 新增 breadcrumb scrub**：現有邏輯只掃 `sentryEvent.SentryExceptions[].Value`（主要例外訊息），新增一段掃 `sentryEvent.Breadcrumbs[].Message`，套用同一個 `ScrubSensitive`——**涵蓋 `ex.Message` 重複記錄跟 `ClientIp` 這兩個 breadcrumb 缺口**，不用去改 `ExceptionHandlerMiddleware.cs`/`Program.cs:157` 的個別 log 呼叫

### 非範圍

- **不用密碼雜湊演算法**（bcrypt/scrypt/Argon2）：HMAC 的安全性建立在「密鑰未外洩」，不是「雜湊算得夠慢」——沒有密鑰，攻擊者連候選值都算不出來，跟雜湊速度無關。
- **Seq 端不用雜湊/scrub**：Seq 是內部系統，維持明碼，需要完整除錯細節時去 Seq 用時間戳+例外類型對照（沿用 `plans/2026-08-03-bugsink-integration.md` 已建立的比對方式）。
- **不改 `DiscordOAuthClient.cs`/`MicrosoftMailService.cs` 個別呼叫點**：用 `BeforeSend` 的 JSON token regex 統一擋，不用逐一包 try/catch 重寫例外訊息——效果相同、改動面小很多。
- **`Breadcrumb.Data`（結構化字典）不處理**：只確認 `Breadcrumb.Message`（顯示在 Sentry UI 的主要文字）可寫入,`Data` 是 `IReadOnlyDictionary`,沒有安全把握能不能就地改值,列在「未解問題」。

## 決策：為什麼是 HMAC-SHA256，不是純 SHA256

Discord ID（snowflake）是結構化數字（時間戳 42 bit + worker/process 10 bit + 遞增序號 12 bit），不是高熵亂數——純 SHA256 的話，攻擊者列舉合理時間範圍內所有組合（每個時間戳約 400 萬種可能）硬算比對，能反推回原始 ID，等於沒雜湊。HMAC 帶密鑰，沒有密鑰攻擊者連候選值都算不出來，才是真的不可逆。

## 驗收

- [ ] 新密鑰產生、寫入 `secrets/discord_hash_key.txt`，`compose.yaml`/`k8s/secrets.yaml`/`k8s/setup-secrets.ps1`/`k8s/backend.yaml` 補上掛載
- [ ] `Program.cs`：`BeforeSend` 改用 HMAC-SHA256 + breadcrumb scrub + `ScrubSensitive` 擴充,`dotnet build` 通過
- [ ] 本機觸發一次帶 `DiscordId` 的例外，sentry.io 上 tag 顯示雜湊值（16 進位字串），不是明碼 Discord ID
- [ ] 同一個 Discord ID 觸發兩次不同例外，兩筆事件的雜湊 tag 值相同（確認可比對性沒壞）
- [ ] 觸發一個帶假 IP/JSON token 字樣的測試 breadcrumb，確認 Sentry 上顯示 `[Filtered IP]`/`[Filtered]`，不是明碼

## 工時估

- 密鑰產生 + secrets 掛載（compose + k8s 三個檔案）：約 15 分鐘
- `Program.cs` 改 HMAC + breadcrumb scrub + regex 擴充：約 20 分鐘
- 本機驗證：約 15 分鐘
- 小計：約 50 分鐘

## 未解問題

- `Breadcrumb.Data`（結構化字典,不是顯示文字的 `Message`）沒有處理到,不確定它是不是唯讀——如果之後發現 Sentry UI 展開 breadcrumb 細節時看得到未 scrub 的原始屬性值,要再另外處理。
