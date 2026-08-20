# ConfigChanged Outbox 死碼清理

> 輕量 plan（動手前 spec）：目標 / 範圍 / 決策 / 驗收 / 工時。做完可丟。
> 定位：period-less 收尾殘留清理。**不改行為**（ConfigChanged 這條現在本來就 no-op），純拔死碼。

## 目標

移除 period-less（deadline 退場）後變成**殘留**的 `ConfigChanged` outbox 路徑：事件照 enqueue、handler 照跑、`ConfigChangeNotifier.Notify()` 照呼叫，但 `OnChanged` **全無訂閱者 = no-op**，喚醒不了任何東西。保留仍活著的 `TeamNotification` outbox（發 Discord DM）與整套 Outbox 基礎設施。

## 背景（為何是死碼）

- `ConfigChanged` 原用途：管理員在 **API** 調整「報名截止時間」→ 同交易寫 outbox → **bot** 端 handler 喚醒 `RegistrationDeadlineJob` 重讀 config 重算截止。
- period-less Phase 4c/4d：`Period` / deadline / `RegistrationDeadlineJob` 整包退場 → 訂閱 `ConfigChangeNotifier` 的 job 不存在了。
- 現在 `SystemConfig` 只剩**退團率警示參數**（`LeaveRateWarnEnabled/WindowMonths/Threshold/MinSample`），且在 `TeamLeaderService.GetCandidatesAsync:193` **每次查詢即時從 DB 讀最新值** → 沒有記憶體快取要失效、沒有背景 job 要喚醒 → **不需要跨行程喚醒機制**。

## 決策

- **拔掉，不留**（YAGNI）：無消費者、無功能依賴；leave-rate config 即時讀 DB。
- **可逆**：Outbox 基礎設施（`Outbox`/`OutboxDispatcher`/`OutboxRetentionJob`/`OutboxEventType.TeamNotification`）全留著、`TeamNotification` 仍活；未來若真有「config 變更要跨行程喚醒」需求，再加一個具名事件 + 真訂閱者即可。
- **不動 schema**：`SystemConfig` 的 LeaveRate 欄位保留（設定本身有用，只是不再 enqueue outbox）。

## 範圍

### A. 移除（code）
1. 刪檔 `Application/Events/ConfigChangeNotifier.cs`（無訂閱者）。
2. 刪檔 `Infrastructure/BackgroundJobs/ConfigChangedOutboxHandler.cs`。
3. `Application/Events/OutboxEventType.cs`：移除 `ConfigChanged` 常數（**保留** `TeamNotification`）。
4. `Infrastructure/Services/SystemConfigService.cs`：移除 `IOutbox _outbox` 欄位 + 建構子參數 + line 68 `EnqueueAsync(ConfigChanged, config)`。建構子收斂成 `SystemConfigService(DbContext dbContext)`。（`_outbox` 僅此一處用，可整個拔。）
5. `Presentation/Program.cs`：移除 `AddSingleton<ConfigChangeNotifier>()`（:88）、`AddSingleton<IOutboxHandler, ConfigChangedOutboxHandler>()`（:114）、修 :111 註解。
6. `Presentation.WebApi/Extensions/ServiceCollectionExtensions.cs`：移除 `AddSingleton<ConfigChangeNotifier>()`（:19）。

### B. 測試
7. `Test/SystemConfigServiceTests.cs`：移除 `Mock<IOutbox> _outboxMock`（:17,:26）、建構子 outbox 參數（:27）、刪 `UpdateAsync_發ConfigChanged事件到Outbox`（:101-112）。確認 `UpdateAsync` 其餘測試（寫入 config）仍綠。
8. `Test.Integration/OutboxIntegrationTests.cs`：把當測試素材用的字面字串 `"ConfigChanged"`（:38,45,54,56,75,87,122,123,177）改成中性型別（如 `"TestEvent"`）或 `"TeamNotification"` —— 這些測的是 **Outbox 機制本身**（原子、派發、`SKIP LOCKED`、retention），跟事件型別無關，不可因此連帶弱化。移除常數後字面字串仍可編譯，但語意上不該再指一個已刪的型別。

### C. 文件
9. `docs/architecture.md` §7：line 221 兩個用途改成只剩 `TeamNotification`；line 241 mermaid `Type=ConfigChanged` → `TeamNotification`；line 268 冪等範例改成 DM handler（誠實：非天生冪等）。
10. `docs/business-rules.md`：規則 **N3**（:71 `ConfigChanged` outbox → bot 喚醒重讀）移除或改「設定變更即時寫 DB，讀取時直接讀最新值（無 outbox 喚醒）」。
11. `Documents/job/技術補強/技術面試補強_MSRS架構參照.md` §9：本次已標「殘留」，清完改「已移除」（去掉 `ConfigChangedOutboxHandler`、`ConfigChangeNotifier` 相關描述，留 `TeamNotification` 為唯一活例）。

### 非範圍（YAGNI）
- 不動 `Outbox`/`OutboxDispatcher`/`OutboxRetentionJob`/`TeamNotification`。
- 不動 `SystemConfig` schema 與退團率警示功能。
- 不加「未來 config 跨行程」的任何預留。

## 驗收（✅ 2026-08-20 完成）
- [x] `dotnet build` 綠；`.cs` 唯一殘留是 `SystemConfigService` 一句說明註解（非引用）。
- [x] `dotnet test`（Test 專案）287 全綠；`SystemConfigServiceTests` 4 過（config 寫入測得到、已無 outbox 斷言）。
- [x] **Docker 整合測 47 全綠**（Testcontainers 真 Postgres 18）：`OutboxIntegrationTests` 字串改 `"TestEvent"` 後原子/派發/`SKIP LOCKED`/crash 重送/retention 全過；`TeamLeaderServiceIntegrationTests` 單參數建構子過。
- [x] `SystemConfigService.UpdateAsync` 仍寫 LeaveRate（`UpdateAsync_When*` 測試涵蓋），已無 enqueue。
- [x] docs：architecture §7（改組隊通知為主例 + mermaid + 冪等註解）、business-rules N3/N4、補強 §9 皆同步。
- [x] 額外：連帶拔掉 bot `Program.cs` 已無人用的 `AddSingleton<IOutbox, Outbox>()`（bot 純消費端不 enqueue）+ 兩處多餘 `using`。

## 未解決
- 無。`OutboxIntegrationTests` 素材字串定案用 `"TestEvent"`（純中性、不綁業務型別）。
