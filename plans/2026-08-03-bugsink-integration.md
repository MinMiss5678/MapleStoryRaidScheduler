# 錯誤追蹤整合（stack-trace 去重 + resolved/unresolved 工作流程）

> 輕量 plan（動手前的 spec）：目標 / 範圍 / 決策 / 驗收 / 工時。做完可丟；穩定規則再收進 `docs/`。

## 背景（決策已變更，見下方「決策異動」）

`plans/2026-07-31-error-alerting.md` 完成的 Seq Alerting 解決了「有人通知我」，但 Seq 沒有自動 stack-trace 指紋去重、也沒有 resolved/unresolved 工作流程。原本選擇自架 Bugsink（Sentry 協定相容）補這塊，本機驗證跑到一半，深入查證後改為 **sentry.io 官方雲端免費版**，理由見下。

## 決策異動：自架 Bugsink → sentry.io 免費版

排查「Bugsink 會不會遺失錯誤事件」時，查到 Bugsink 內部背景任務佇列 `snappea` 是 **at-most-once**（`foreman.py` docstring 明講：任務被撈出時先從 DB 刪紀錄、才開始真正執行），container 剛好在「紀錄已刪、任務還沒執行完」的瞬間被砍掉，該任務會永久遺失、不會自動重試。

評估過的替代方案：

| 方案 | 結論 |
|---|---|
| 自己 fork 改 Bugsink/GlitchTip 原始碼（ack late） | 授權允許，但要一併補冪等性 + poison-pill 重試上限，等於重寫任務執行器一部分，長期維護成本高，不划算 |
| GlitchTip | 查證後，目前架構（`django-vtasks`，2026 取代 Celery）官方自己承認「no retry mechanism」，**跟 Bugsink 同一等級風險**，換了等於白換 |
| 自己接 Kafka/RabbitMQ | 補不到 Bugsink 內部 digest 任務遺失這段（ingest API 回 202 在 digest 完成前），且跟自建整套去重/resolved（早就因為 YAGNI 排除）一樣不划算，Kafka 尤其重，跟本專案資源規模不成比例 |
| 自架完整版 Sentry（Kafka+ClickHouse+Celery+Redis） | 官方文件最低需求 16GB RAM（建議 32GB），跟本專案 `database` container 限制 256MB 的規模差兩個數量級，直接排除 |
| **sentry.io 官方雲端免費版** | **採用**：核心 ingest 走 Kafka，是真正的 at-least-once，沒有上述遺失風險；業界主流（錯誤追蹤市佔 78-95%）；`Sentry.Serilog` 整合原封不動,只換 DSN；免自架，直接砍掉 Postgres 新資料庫、container、日後的 k8s 部署 |

## 目標

錯誤發生時，Seq 繼續負責「寄信通知我」（已完成），Sentry 負責「這個錯誤是不是同一個 bug、修了沒」——兩者並存，職責不重疊。

## 範圍

- **後端整合**：`Sentry.Serilog`（`Presentation.WebApi/Program.cs`）DSN 指向 sentry.io 給的值，不是自架服務。
- **只做後端**：前端沿用舊決策不做（`@sentry/nextjs` 撞 Turbopack 204 bug）。
- **不自架**：不需要 Postgres 新資料庫、不需要 container、不需要 k8s 部署——這是這次決策異動比原計畫**少做**的部分。
- **PII/敏感資料 scrub**：`BeforeSend` hook 白名單挑欄位轉 tag（目前只有 `DiscordId`）+ 正則擋掉連線字串/JWT 格式內容再送出。

### 非範圍（YAGNI）

- **不用 Sentry 自己的 email 通知**：Seq Alerting 已經負責。
- **前端錯誤追蹤**：維持不做。
- **不自建去重/resolved**：評估過自建成本 16-25 小時+長期維護，沒有理由。
- **不開 Sentry Logs（結構化日誌）取代 Seq**：免費額度 5GB/月無超額選項、功能才 GA 一年，且本專案 bot/backend 是各自獨立 process 共用 DB/Redis 狀態、不是網路呼叫鏈，Sentry Logs 標榜的 trace correlation 用不太到。

## 已完成的額外修復（意外發現，非本次原始範圍）

排查過程中發現 `Presentation`（bot）完全沒有接 Serilog/Seq，只有 Console log——代表 Outbox 派發失敗/重試放棄這類事件（`OutboxDispatcher`）只印在 container console，進不了 Seq、也不會觸發 Alerting。已修復：

- `Presentation/Presentation.csproj`：加 `Serilog.Extensions.Hosting`、`Serilog.Settings.Configuration`、`Serilog.Sinks.Console`、`Serilog.Sinks.Seq`
- `Presentation/Program.cs`：`ConfigureLogging` 換成 `UseSerilog`，跟 backend 同一套設定（Console + Seq）
- `compose.yaml` / `k8s/bot.yaml`：補 `Seq:ServerUrl` 環境變數
- 已驗證：`docker compose up -d --build bot` 後,Seq 網頁介面能查到 bot 送來的事件

## 驗收

- [x] `Presentation.WebApi/Program.cs`：`Sentry.Serilog` 設定沿用、加上 `BeforeSend` scrub/tag hook（已完成，`dotnet build` 通過）
- [x] 拆除自架 Bugsink 基礎設施：`compose.yaml` 移除 `bugsink-db-init`/`bugsink` service，`secrets/bugsink.env` 刪除，`secrets/bugsink_dsn.txt` → `secrets/sentry_dsn.txt`（已完成）
- [x] bot 補接 Serilog + Seq（意外發現的額外修復，已驗證）
- [x] 去 sentry.io 建 Organization（US region）+ Project（ASP.NET Core，只勾 Error monitoring），DSN 寫進 `secrets/sentry_dsn.txt`
- [x] 本機重啟 backend 後，用臨時 `/debug/throw-test` 端點觸發例外，sentry.io 介面上看得到這筆事件
- [x] 同一個例外連續觸發，Sentry 正確歸成同一個 issue（去重驗證：3 次觸發 → 1 個 issue、Events=3）
- [x] 手動標記 issue 為 resolved，再次觸發同一個例外，狀態變回「Regressed」（復發偵測驗證通過）——臨時測試端點已移除
- [x] `k8s/backend.yaml` 補上 `Sentry__Dsn`（沿用既有 `maple-secrets` Secret 加 `sentry_dsn` key，`optional: true`，跟 MicrosoftMail 兩把選填 secret 同一套慣例）；`k8s/secrets.yaml`、`k8s/setup-secrets.ps1` 同步更新

## 未解問題

（無，本機 Postgres 殘留的 `bugsink` 資料庫已手動 `DROP DATABASE bugsink;` 清除）
