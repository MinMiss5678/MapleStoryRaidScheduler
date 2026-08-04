# 錯誤通知計畫

> 輕量 plan（動手前的 spec）：目標 / 範圍 / 決策 / 驗收 / 工時。做完可丟；穩定規則再收進 `docs/`。

## 背景

現況 prod 出錯只能靠翻 Seq log 或等 Discord 使用者回報，沒有主動通知機制。

## 目標

Production 環境的 Error 級例外事件觸發時，自動寄 email 通知，不必巡 log 或等回報。

## 決定採用：Seq Alerting，寄信管道待定（見下方子決策）

- **機制**：對既有的「Errors」signal 設 Alert 查詢（`@Level = 'Error'`，`group by` 時間窗，同一段時間內多筆錯誤合併成一則通知，避免炸信箱）；觸發時透過 Seq 的 mail app 送信。
- **零新增第三方**：Seq 本來就自架（`k8s/seq.yaml`），免費 Individual 版已含 Alerting，不用另外裝新服務、不用另外管一組 API key。已查證 Individual 免費授權允許單人正式環境使用，不受限。
- **後端已有的素材可直接沿用**：`Program.cs` 的 `Enrich.FromLogContext()` + `UseSerilogRequestLogging()` 已提供 request 關聯性，Seq 裡用同一個 correlation/discordId 查前後幾筆 log，可手動重建「案發前後文」。

### 子決策：寄信管道——**已定案，走選項 2（Microsoft OAuth2，個人帳號）**

實測完成：個人 Outlook/Hotmail 帳號走 **Authorization Code flow + PKCE + Delegated `Mail.Send`/`offline_access`**，
後端加內部端點 `POST /api/internal/alert-mail`（`AlertMailController` + `MicrosoftMailService`），已驗證真的能寄出信到
Gmail（HTTP 202，中文內容正確顯示）。設定步驟、踩過的坑（IIS port 80 衝突、`consumers` vs 租戶 GUID、
`requestedAccessTokenVersion` 順序限制、PKCE 手動轉錄出錯）全部寫在 **`docs/microsoft-mail-oauth2-setup.md`**，
之後 refresh token 失效要重做時照那份操作，這裡不重複。

保留 Gmail + App Password 當備案記錄（見下方「選項 1」），沒有實際採用。

## OAuth2 調查結論（2026-08-01，避免以後重查一次）

為了避免 Gmail App Password 跳出的「安全性較低」警告，完整查過「有沒有更好的 OAuth2 選項」，結論是**個人 Gmail 沒有划算的路**：

- **Gmail 所有 OAuth2 flow 都撞牆**：`gmail.send` 屬於 Google 的敏感／受限範圍。走「已安裝的應用程式」（Installed app，需要真人互動登入）這條路，只要 App 停在 Testing 模式（不走完整審核），拿到的 refresh token **7 天就過期**，不符合背景服務需求；要拿到不過期的 refresh token，必須把 App 送 Google **完整審核**（2-6 週起跳，`gmail.send` 這種等級可能還要 CASA Tier 2 第三方付費安全稽核）。走「服務帳戶」（Service Account，免真人參與）這條路，服務帳戶要代表個人 Gmail 寄信需要 **Google Workspace 網域範圍委派**，只有 Workspace（企業版）網域管理員能開，個人 `@gmail.com` 帳號沒有這個後台，完全用不了。
- **換掉 Seq／Grafana 都沒用**：查過 Grafana（比 Seq 資源多、社群更大的專案）的 Email Alerting，**一樣不支援任何 provider 的 OAuth2 SMTP**——這是自架告警工具生態系共同的洞，不是 Seq 選錯工具，2026 年 3 月微軟強制停用 M365 Basic SMTP Auth 後，用 Grafana + M365 的人也會直接壞掉（GitHub feature request 開在 2025-10，至今未解）。
- **自己寫自訂 Seq app（Gmail API + OAuth2）不划算**：估過工時約 4-6 小時（學 Seq app SDK + Google Cloud OAuth Client 設定 + 寫 app + 測試），但這只解決「寫程式」那塊，沒解決上面「Testing 模式 7 天到期／要走 Google 審核」這個根本限制——寫完一樣要面對審核或每 7 天重新登入，白做工。
- **`simonrob/email-oauth2-proxy` 這類代理工具也沒用**：查過文件，這個工具**不內建已驗證的 OAuth client**，一樣要你自己去 Google Cloud Console 註冊（或「小心地」重用別人的 client，屬於規則灰色地帶，不建議），Google 的審核/7 天限制完全沒繞過；還額外多一個要常駐執行、自己維護的服務，比自訂 Seq app 更重，不如不做。
- **Microsoft 個人帳號是唯一看起來划算的 OAuth2 替代方案**：Authorization Code flow + Delegated 權限不需要組織租戶/admin consent，個人 Outlook/Hotmail 帳號就能走；且 Microsoft 官方文件寫「Web/原生應用程式的 refresh token 未指定固定存留期，通常有效期很長」，沒有 Google 那個 7 天測試期地雷。

**結論**：Gmail OAuth2（任何 flow）、自訂 Seq app、代理工具，全部不划算或走不通；**Gmail + App Password 依然是預設答案**，除非你願意花額外時間走 Microsoft 個人帳號那條路換掉「安全性較低」警告——那個警告本身風險可接受（見對話紀錄：App Password 外流頂多能寄信、不能登入帳號本身，可單獨撤銷）。

## 非範圍 / 已知缺口（YAGNI，先不做）

- **自動 stack-trace 指紋去重 + resolved/unresolved 工作流程**：Seq 沒有這塊。查過免費替代方案：
  - **Bugsink**（推薦）：self-hosted、single container（~512MB RAM），跟 Seq 一樣走「自架單一 container」路線，非 SaaS。
  - **GlitchTip**：功能更多（含 performance/uptime monitoring）但要 3 個 container（app + Postgres + Redis），較重。
  - 兩者都只補「錯誤事件的分組/去重/resolved 工作流程」，不能取代 Seq——就算裝了也只是輕量附帶收 log（標題=第一行文字），沒有結構化欄位查詢/dashboard，跟 Seq 角色不重疊，會是並存而非二選一。
  - **這次不做**：單公會規模，「有人通知」已經是主要需求，去重/resolved 工作流程是加分而非必要，之後真的常態性收到大量 Error 通知造成困擾再考慮加。
- **前端錯誤追蹤**：前端目前完全不寫 log 到 Seq，這塊還沒有覆蓋，留待下方「未解問題」決定。
- **Microsoft OAuth2 寄信端點**：只在確認要走這條路時才動手實作，不預先寫（YAGNI）。

## 驗收

**選項 2（Microsoft OAuth2，個人帳號，已採用）**：
- [x] 確認帳號類型（個人 Outlook/Hotmail），決定走自訂端點（`Seq.App.Mail.Microsoft365` 需要商務租戶，用不到）
- [x] 完成一次性 Authorization Code + PKCE flow 人工登入，取得 refresh token（存於 `secrets/microsoft_mail_refresh_token.txt`）
- [x] `Presentation.WebApi` 加內部寄信端點（`AlertMailController` + `MicrosoftMailService`），驗證 refresh token 換 access token + 呼叫 Graph API `sendMail` 成功（HTTP 202，中文內容正確）
- [x] 301+ 個既有 unit test 全過，新增 `IdempotencyMiddleware` 路徑排除的回歸測試
- [x] Seq 裝 `Seq.App.HttpRequest`，接上 Error alert 打 `/api/internal/alert-mail`，觸發驗證真的收到信（本機 docker compose 完整驗證過，過程踩的坑見 `docs/microsoft-mail-oauth2-setup.md`）
- [x] Time grouping（5 分鐘）+ Suppression time（30 分鐘）設定完成，避免同一時間窗/持續出錯轟炸信箱
- [x] k8s 部署設定接上：`k8s/backend.yaml` 加 `MicrosoftMail__*` 環境變數、`k8s/secrets.yaml`/`setup-secrets.ps1` 補 `microsoft_mail_refresh_token`/`microsoft_mail_webhook_secret`（皆標記 optional，secret 未設定不會讓 pod 啟動失敗）

**選項 1（Gmail + App Password，備案，未採用）**：
- [ ] ~~Seq 裝好 `Seq.App.Mail.Smtp`，SMTP 帳密設定完成（Gmail App Password）~~——已改走選項 2，不執行

## 工時估

**選項 1**：
- Seq App 安裝 + SMTP 設定：約 30 分鐘（UI 操作，非程式碼）
- Alert 查詢撰寫 + 測試觸發：約 30 分鐘

**選項 2（個人帳號路線，較貴）**：
- Azure App Registration + 一次性人工登入拿 refresh token：約 1 小時
- 後端內部端點（DTO + Graph API 呼叫 + 密鑰保護）：約 1-2 小時
- Seq 接 `Seq.App.HttpRequest` + Alert 設定 + 測試：約 30-60 分鐘
- 小計：約 3-4 小時（比選項 1 多 2.5-3.5 小時，換到的只有「不跳 Google 安全性警告」，功能無差異）

## 未解問題

- **正式 k8s 環境還沒真的部署過這個功能**：程式碼/設定都已就緒（本機 docker compose 端對端驗證成功），但正式 k8s cluster 上還沒跑過 `setup-secrets.ps1` 帶入這兩把新 secret、也還沒實際部署過新版 backend image，需要下一次部署時一併帶上。
- **正式環境的 Seq 也要重新設定一次**：本機這次設定的 `Seq.App.HttpRequest` instance + Alert 是在本機 docker compose 的 Seq 上，正式環境的 Seq（k8s）是完全獨立的實例，要照 `docs/microsoft-mail-oauth2-setup.md` 的步驟在正式環境的 Seq 上重做一次。
- **前端錯誤通知要不要做、怎麼做**：選項是（a）前端 proxy route 也發一份結構化 log 到 Seq（走既有的 Seq HTTP ingest API，非走 Serilog）、（b）維持現況不做，前端錯誤面本身很薄、優先度低。尚未拍板。
- **要不要之後加 Bugsink**：先不做，等 Seq Alerting 實際跑一陣子、真的覺得「去重/resolved 工作流程」是痛點再評估。
