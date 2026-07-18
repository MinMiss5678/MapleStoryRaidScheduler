# 整合測試計畫：用 Testcontainers 覆蓋持久層

## Context

持久層（`*Repository` / `*Query` / Dapper 手寫 SQL）目前 unit 覆蓋 **0%**——這是對的，SQL 對 mock 測不出東西。正確做法是**整合測試打真 PostgreSQL**：驗 SQL 正確性、`SqlBuilder` 產出的 SQL 是合法 Postgres、UoW 交易行為，順帶驗 migration 能套。

**原則**：整合測試為了驗**真行為**（SQL/接線/交易）而寫，覆蓋率上升是副產品，不是目標。邏輯邊界仍靠 unit test，持久層靠 integration——兩者互補。

**目標**：把 Infrastructure 的 Repositories/Queries 從 0% 用**對的方式**點亮；unit + integration 兩份覆蓋率報告合併。

---

## Approach

- **Testcontainers for .NET**（`Testcontainers.PostgreSql`）：每次測試 run 起一個拋棄式 PostgreSQL 容器。
- **獨立測試專案 `Test.Integration/`**：與 unit 的 `Test/` 分開——unit 保持快、integration 需要 Docker 可單獨跑/CI 閘控。
- **共用容器 fixture**（xUnit `ICollectionFixture` + `IAsyncLifetime`）：一個 collection 一顆容器（容器啟動慢，不能每測試一顆），啟動時套 schema。
- **schema 套用**：fixture 初始化時**依序執行 `db/migrations/*.up.sql`**（順帶驗 migration 可套）。
- **測試間隔離**：每個測試前 `TRUNCATE` 相關表（或用 `Respawn` 套件）歸零，避免互相污染。
- **分類標記**：`[Trait("Category","Integration")]` → 本機無 Docker 可 `dotnet test --filter "Category!=Integration"` 跳過。

---

## Phase 1：Scaffold + 容器 fixture（MVP 地基）

- 新專案 `Test.Integration/`（參照 `Infrastructure`、`Domain`、`Application`；套件 `Testcontainers.PostgreSql`、`xunit`、`coverlet.collector`）。
- `PostgresFixture`（`IAsyncLifetime`）：
  1. `new PostgreSqlBuilder().WithImage("postgres:18").Build()` → `StartAsync()`
  2. 取 `ConnectionString`
  3. 讀 `db/migrations/*.up.sql`（依編號排序）逐檔對容器執行
  4. 提供 `CreateDbContext()`（`new DbContext(new NpgsqlConnection(connStr))`）與 `ResetAsync()`（TRUNCATE 所有表）
- `[CollectionDefinition("pg")]` 綁定 fixture。

## Phase 2：第一支 repository 測試（驗證 harness + 點亮覆蓋）

- `TeamSlotRepositoryIntegrationTests`（`[Collection("pg")]`）：
  - `CreateAsync` → `GetByIdAsync` 撈回、欄位正確（含新的 `Source`）
  - `GetIncompleteTeamsAsync` 只回 `Source=auto` 且有空位的隊（驗 WHERE + Source 過濾）
  - `GetByPeriodIdAsync` 週期範圍過濾
- 跑 `dotnet test --collect:"XPlat Code Coverage"` → 確認 `TeamSlotRepository` 從 0% 亮起來。

## Phase 3：擴到 SQL 邏輯重的 repository / query

- **Queries（JOIN/GroupBy 最容易錯）**：`PlayerRegisterQuery`（大 JOIN + GroupBy 去重）、`TeamSlotQuery`（GetByPeriodAndBossId / DiscordId 分組）、`PeriodQuery`（now/date/last 三種）、`CharacterQuery`、`SessionQuery`。
- **Repositories**：`TeamSlotCharacterRepository`（空隊自動清除的 `Source=auto` + NOT EXISTS 邏輯）、`CharacterRepository`、`PlayerRegisterRepository`、`PlayerAvailabilityRepository`。
- 重點驗：過濾條件、JOIN 正確、時區/`timestamptz`、`TimeOnly` type handler、`SqlBuilder` 產的 SQL 真的能跑。

## Phase 4：UoW 交易 + migration 可逆

- `UnitOfWork`：一個交易內多表寫入 → Commit 後查得到；Rollback 後查不到（驗真交易邊界，不是 mock）。
- Migration 可逆：對乾淨 DB 套全部 `up` → 再套對應 `down` → schema 回到前一版不報錯（守 `down.sql` 正確性）。

## Phase 5：覆蓋率合併 + CI 閘控

- 合併 unit + integration 兩份 cobertura：
  ```bash
  reportgenerator -reports:"Test/**/coverage.cobertura.xml;Test.Integration/**/coverage.cobertura.xml" \
    -targetdir:coverage -reporttypes:TextSummary
  ```
- CI：整合測試需 Docker → 在有 Docker 的 job 跑；本機預設 `--filter "Category!=Integration"` 可跳過。

---

## 驗證

1. `dotnet test Test.Integration` 綠燈（容器起得來、migration 套得上、SQL 跑得過）。
2. 合併報告：`Infrastructure.Repositories.*` / `Infrastructure.Query.*` 從 0% → 有覆蓋；Infrastructure 整體與 branch 明顯上升。
3. 確認 unit run 仍快（integration 不拖慢日常 `dotnet test`）。

## 進度

- ✅ Phase 1–2：`Test.Integration` + `PostgresFixture`（Testcontainers postgres:18 + 套 up.sql + TRUNCATE 隔離）+ `TeamSlotRepository` 2 測試。coverage：`TeamSlotRepository` 0%→54%。
- ✅ Phase 3（部分）：`TeamSlotCharacterRepository` 空隊清除（auto 刪 / admin 留）2 測試 + 共用 `Seed` 播種工具。
- ⚠️ Phase 3 `PlayerRegisterQuery`：2 測試 **Skip**（見下方發現）。
- ✅ Phase 4（部分）：`UnitOfWorkIntegrationTests` 2 測試 **2/2 綠**（Docker 起容器後 `dotnet test` 驗證，409ms）——①Commit 持久 + 提交前其他連線看不到（隔離）②Rollback 跨兩張表原子撤銷。用真 `UnitOfWork` + 真 `TeamSlotRepository` 共用同一交易。
- ✅ Phase 4（後半）：`MigrationReversibilityIntegrationTests` **綠**——同容器內開臨時 DB，依序套所有 `*.up.sql` → 反向套所有 `*.down.sql`，驗全程不報錯且最後 public schema 歸零（守 down.sql 正確性）。全 8 支整合測試一起跑 0 失敗。

## ✅ 整合測試抓到並修好的潛在 bug

`PlayerRegisterQuery.GetByNowPeriodIdAsync` 物化 `PlayerRegisterDbRow` 失敗：
- **根因**：record 建構子**參數順序 ≠ SELECT 欄位順序** → Dapper 對此 record 走「位置比對」，型別對不上（position 2 record 要 `DiscordId:long`、欄位是 `CharacterId:string`）而拋例外。
- **這是 unit + mock 測不到、只有打真 DB 才會爆的問題**——正是整合測試的價值。
- **影響**：`ScheduleService.AutoScheduleWithTemplateAsync`（重排）呼叫它，當期有報名資料時會在物化時丟例外——潛在 prod bug。
- **修法（已做）**：把 `PlayerRegisterDbRow` 建構子參數重排成與 SELECT 欄位一致（record 僅由 Dapper 物化、無其他具名建構點，改動安全）。順序對齊後位置比對成功、`TimeOnlyTypeHandler` 也順利把 `time`(TimeSpan)→TimeOnly。兩支 Skip 已解除，6/6 綠。

## 未決問題

- 隔離用 `TRUNCATE` 手寫還是引入 `Respawn`？（Respawn 方便但多一個依賴；表不多的話手寫 TRUNCATE 夠）
- schema 套用走「讀 up.sql 逐檔執行」還是「跑 migrate 容器」？建議前者（in-process、順帶驗 migration、無 docker-in-docker）。
- CI 環境是否保證有 Docker daemon？（決定 integration 是必跑還是 nightly）
- 範圍：先做 Phase 1–2（MVP，證明 harness + 點亮一支）就停、還是一路做到 Phase 3 的關鍵 query？
