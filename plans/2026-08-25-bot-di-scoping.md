# bot DI 導入 per-operation scope（DB 路徑並發隔離）

> 輕量 plan（動手前 spec）：目標 / 背景 / 決策 / 範圍 / 驗收 / 工時。（無待你決策的未決事項 → 略去風險段）
> 關聯：`2026-08-25-discord-inline-actions.md` —— 此重構是那個 pilot「方案 B（用 scope）」的**前置 enabler**；做完，按鈕 handler 就能靠 `CreateScope` 拿專屬 DbContext，不用方案 A 手動 new。
> 檔案：`Presentation/Program.cs`（DI + DSharpPlus 事件註冊）、`Infrastructure/Dapper/{DbContext,UnitOfWork}.cs`、`Presentation/Infrastructure/Discord/Handlers/*`、`Infrastructure/BackgroundJobs/OutboxDispatcher.cs`。

## 目標

bot 的每個「DB 操作單元」（Discord 事件、outbox 消費、未來按鈕互動）拿到**專屬的 DbContext/連線**，而非共用一顆 singleton 連線 → 並發安全、與 API 的「每請求一 scope」對齊。

## 背景（現況 = 潛在 race）

bot 的 DB 鏈**全 `AddSingleton`**（`IDbConnection` / `DbContext` / `IUnitOfWork` / 所有 repo / DB-touching service）→ **整個 bot 共用一顆連線**。

- `MemberUpdatedHandler` / `MemberRemovedHandler` 是 gateway 事件驅動、**可能並發**，卻共用那顆連線 → 一條連線同時跑多個操作 → Npgsql「command already in progress」的潛在 race（目前量低沒踩到）。
- OutboxDispatcher **不受影響**（它自開 raw `NpgsqlConnection`，沒用 DbContext）。
- 按鈕互動（未來）更需要**每次一條連線**：`AcceptInviteAsync` 的 `AcquireTeamSlotEditLockAsync` 是**連線層級 advisory lock**，靠不同連線互相排隊序列化 —— 共用單連線則機制失效。

## 決策

0. **（已驗）DSharpPlus v5 每事件自動開 scope**：官方源 `DefaultEventDispatcher.DispatchAsync` 每次 dispatch `serviceProvider.CreateScope()`、handlers 從該 scope 的 `ServiceProvider` resolve、跑完 `scope.Dispose()`（`master` 分支=v5 線,對應本專案 `5.0.0-nightly-02542`）。→ **事件/互動 handler 直接建構子注入 scoped service 即可,不用手動 `CreateScope`**;每事件自動拿到專屬 scoped 實例(專屬 DbContext/連線)。
1. **（已做）連線建立集中到 `IDbConnectionFactory`**：`NpgsqlConnectionFactory` 持有連線字串、`Create()` 回**未開啟的 `DbConnection`**（回抽象型別、不外洩 Npgsql；async API 可用）。兩類消費者共用同一份設定：① scoped `IDbConnection`（決策 2）；② 三個背景 poller（`OutboxDispatcher`/`OutboxRetentionJob`/`LfgIntentCleanupJob`）從「吃 raw 連線字串」改「注入工廠」，**仍各自 `Create()` 自開專屬連線、設計不變**（不併入 scoped DbContext）。附帶：三 poller 相依全可 DI 解析 → 改 `AddHostedService<T>()`、免手動 new。
   - ⚠️ 這一條**推翻**了原「不動 OutboxDispatcher」的判斷（見非範圍）——經與使用者確認選 A：工廠若不餵那 3 個 job 就只剩單一消費者＝過度抽象，故決定碰。改動是**機械式**（字串→工廠），dispatcher 的獨立連線 + `SKIP LOCKED` + 批次交易行為 100% 不變。
2. **DB 鏈改 Scoped**：`IDbConnection`（每 scope 經工廠 `Create()` 一條新連線）、`DbContext`、`IUnitOfWork`、所有 repo、DB-touching service（`SessionService`/`PlayerService`/`SystemConfigService`/`SessionQuery` 等）→ `AddScoped`。純 Discord/Redis（`IDiscordService`、`ISessionCache`、`IConnectionMultiplexer`）維持 singleton。
3. **只有「非事件-handler」的長命 singleton 碰 DB 才手動開 scope**：靠決策 0 的 per-event scope,**事件/互動 handler 不用手動**(直接注入即可)。真需手動的僅「不在 handler 路徑、卻要碰 DB 的 singleton」(注入 `IServiceScopeFactory` + `CreateScope`)。OutboxDispatcher 自開專屬連線、不受影響。
4. **開 DI scope 驗證**：`ValidateScopes = true`（+ `ValidateOnBuild`）→ 啟動即抓 captive dependency（singleton 誤吃 scoped）。

## 範圍

- **（已做）** 新增 `IDbConnectionFactory`/`NpgsqlConnectionFactory`；3 個背景 poller 改注入工廠（含整合測 5 個 call site）。
- 改 `Presentation/Program.cs` 的生命週期註冊（singleton→scoped 分類；`IDbConnection` 經工廠）。
- Discord 事件 handler：**不用改**（DSharpPlus 已 per-event scope,決策 0）—— 相依註冊 scoped、直接注入即可。
- 加 scope 驗證。
- **保留** OutboxDispatcher 的獨立連線設計（不併入 scoped DbContext），連線建立改走工廠（決策 1）。

## 驗收

- [x] `ValidateScopes=true`（+ `ValidateOnBuild`）啟動無 captive-dependency 例外——實跑 bot：三個 background service 印出 "is starting"（＝`Build()` 驗證通過後才會發生），僅假 token 連 Discord 才失敗（DI 驗證之後）。
- [x] 單元 277 綠；整合 47 綠（含工廠改動的 Outbox/Retention/LfgIntentCleanup 打真 Postgres）。
- [~] 每操作獨立連線/交易的**機制**已建（scoped 註冊 + DSharpPlus per-event scope，對齊 API 已驗模式）；live 並發壓測未做 → 併入 discord-inline-actions pilot 或日後壓測。
- [ ] `MemberRemoved` 撤 session 行為：邏輯未改（只改連線生命週期）、無 bot 整合測覆蓋 → pilot 一併手動驗。
- [ ] 按鈕互動用 per-event scope 走 `AcceptInviteAsync`（交給 discord-inline-actions pilot 驗）。

## 工時估
- 盤點 + 改生命週期（singleton→scoped）+ 抓 captive dep ≈ 半天～一天（DSharpPlus per-event scope 已驗、handler 免改）；並發驗證 ≈ 半天。

## 非範圍（YAGNI）
- 不把 OutboxDispatcher 併入 scoped DbContext / request-UoW —— 維持獨立連線設計（連線建立已改走工廠＝機械式、非設計變更；見決策 1）。
- 不為此引入完整 request-pipeline 抽象；只補「每操作 scope」這一層。
