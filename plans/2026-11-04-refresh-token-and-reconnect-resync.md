# Refresh Token（輪替＋重用偵測）＋ Discord 重連補齊

> 建立：2026/11/04｜狀態：**⏳ 等待 case 表核可**｜預估 **5-8 天**
> 範圍：新資料表、`/api/Auth/refresh` 端點、`AuthenticationMiddleware` 續期段落改寫、前端 `apiClient`、bot 重連補齊 handler
> 關聯：`2026-11-04-split-authentication-authorization.md`（**階段 0，前置**）、`2026-11-04-jwt-refresh-redesign.md`（A 組已搬入本計畫階段 0.5）、`2026-11-04-authorization-matrix.md`（護欄）

---

## 一、目標與動機

### ★ 這是練習，不是為了解決現有痛點

**MSRS 目前沒有實際使用者，是作品集兼自用。** 所以要誠實記下：

| 常見的技術動機 | 在 MSRS 的真實狀況 |
|---|---|
| Discord 速率限制是天花板（粗估幾千～一兩萬活躍使用者才會打到） | ❌ **永遠碰不到** |
| Discord 中斷 → 過期的使用者續不了期、等於被登出 | ⚠️ **理論風險**，重新登入即可 |
| 撤銷延遲一個 access token 壽命（最多 15 分鐘） | ❌ **沒有人在意** |
| 部署後現有使用者被登出一次 | ❌ **無影響** |

**→ 真正的動機是：在一個真實但零風險的系統上，把正規的 refresh token 完整做對一次。**
具體要練到的東西，第 1、2 階（只改角色來源讀資料庫）**都學不到**：

- **輪替（rotation）＋ 重用偵測** —— 以及它帶來的誤判風險
- **用資料庫唯一索引做併發冪等**（而非加鎖）
- **寬限窗口**解決成群搶進
- **Path 限定 cookie** 縮小暴露面
- **前端單飛（single-flight）＋ 重送**
- **推送 ＋ 拉取**維持資料新鮮度（重連補齊、定期對帳）

### 要換到的能力（技術面，與規模無關）
- 續期**只讀自己的資料庫**，不依賴 Discord
- **可立即撤銷**（刪除 refresh token 即失效）
- **被竊偵測**：refresh token 重複使用 → 撤銷整條鏈
- 角色新鮮度改由**推送事件 ＋ 重連補齊 ＋ 定期對帳**維持

### ⚠️ 一併承擔的代價（誠實列出）
| 代價 | 說明 |
|---|---|
| **依賴對調，不是消除** | 從「Discord API」換成「自己的資料庫」。資料庫掛了同樣續不了期——但這個我能監控、能擴充 |
| **推送事件會漏** | Discord gateway 事件**不保證送達**。bot 離線期間（部署、崩潰、斷線重連）的角色變更收不到，而且**不會知道自己漏了** → 所以**重連補齊是本計畫的必要組成，不是加分項** |
| **引入全新的失效模式** ⭐ | **rotation 是自己製造的問題**：現況沒有「用過即作廢」就沒有重用偵測、也沒有誤判。加了 rotation 之後，**合法使用者只因前端同時發幾個請求，就可能被判定為被竊而整條鏈撤銷**。唯一索引與寬限窗口不是在修既有缺陷，是在**收拾新機制的副作用** |
| **新增攻擊面** | 多一個資料表、一個端點、一套輪替邏輯。實作錯誤的後果比現在嚴重 |

---

## 〇、前置條件（必須先完成，依序）

| # | 前置 | 為什麼 | PR |
|---|---|---|---|
| **1** | **授權矩陣 G1/G2/G8/G10（Playwright E2E）** | 護欄。本計畫大改認證主幹，先要有端到端斷言。不受 hook 治理 → 不必等遷移 | PR 2 |
| **2** | **xUnit v3 遷移** | **TDD hook 的測試輸出解析器只認 xUnit v3 In-Process Runner**（`Total: N, Errors: N, …`）。MSRS 現在是 xunit 2.9.3 ＋ VSTest，輸出是 `已通過! - 失敗: 0，通過: 6…`（繁中、全形逗號）→ `parse_run` 永遠回 `invalid` → **永遠沒有有效紅燈 → 每次改實作都被擋**。<br>⚠️ **遷移本身不受 hook 治理**（雞生蛋：它改的就是測試套件）；遷完 CI 綠才啟用 hook | PR 3（`Test/`）、PR 4（`Test.Integration/` ＋ CI） |
| **3** | **`.claude/tdd.json` 啟用** | 之後所有工作都在 test-first 稽核下 | PR 5 |
| **4** | **階段 0：認證與授權分離** | **根因修法。** 本計畫會大幅改寫 `AuthenticationMiddleware`，而歷史顯示「大改這個函式」正是那三行（`context.User = …; await next(); return;`）復發的條件（`f0f32fb` 就是）。**先拆，再動續期。**<br>詳見 `2026-11-04-split-authentication-authorization.md` | PR 6 |
| **5** | **階段 0.5：`JwtService` 真實驗簽行為測試** | 本計畫的 refresh 端點要用「**過期但驗簽有效**」自證。而現有測試 `InvalidSignature_DoesNotRefresh` **mock 掉 `ValidateToken`**，真實實作到底回什麼例外**從未被證明**（與 session 冒用漏洞同一形態的 mock drift）。**這個前提必須先釘住。**<br>case 表見 `2026-11-04-jwt-refresh-redesign.md` §四 A 組 | PR 7 |

### xUnit v3 遷移範圍（PR 3-4）
- 2 個 csproj：`xunit` 2.9.3 ＋ `xunit.runner.visualstudio` → **`xunit.v3`**，加 `<OutputType>Exe</OutputType>`
- 執行方式：`dotnet test` → **`dotnet run --project`**（In-Process Runner）
- **6 個檔案**用 `IAsyncLifetime` / `ITestOutputHelper`（v3 簽章改為 `ValueTask`）
- **CI**：`ci.yml:154`、`:178` 的 `--collect:"XPlat Code Coverage"`（VSTest datacollector，MTP 不吃）→ 改用 **`Microsoft.Testing.Extensions.CodeCoverage`** ＋ `--coverage-output-format cobertura`
  → **輸出格式相同，`reportgenerator` 段（`:231-240`）完全不動**
- **PR 3 的過渡設定（PR 4 要收掉）**：本機是 .NET 10 SDK，其 `dotnet test` 預設仍走 VSTest（MTP 要在 `global.json` 以 `"test": { "runner": "Microsoft.Testing.Platform" }` 開啟），而 xUnit v3 預設把專案標成 MTP 應用程式 → `dotnet test` 直接報錯。PR 3 在 `Test/Test.csproj` 設 `IsTestingPlatformApplication=false`，保留 `xunit.runner.visualstudio`／`Microsoft.NET.Test.Sdk`／`coverlet.collector`，讓 CI 覆蓋率照舊。
  PR 4：加 `global.json` 開啟 MTP、兩個測試專案都改 MTP ＋ `Microsoft.Testing.Extensions.CodeCoverage`，**刪掉該設定與上述三個 VSTest 套件**（對照 ChipHunter：只裝 `xunit.v3`、一律 `dotnet run`）
  ✅ 2026/11/05 已完成。連帶：CI 的 `DOTNET_VERSION` 改為 10.0.x（`global.json` 的 `test.runner` 只有 SDK 10 認得；專案仍為 net9.0、Dockerfile 仍用 SDK 9 建置）。覆蓋率引擎由 coverlet 換成 Microsoft Code Coverage，**數字不可與舊值直接比較**（行數計法不同：可計行 4270 → 3971）
- **順便決定**：CI 的 `dotnet test` 沒帶 `--settings Test/coverlet.runsettings` → 該檔的排除清單在 CI 從未生效；改覆蓋率工具時一併決定要不要套用
  ✅ 2026/11/06 決定：**不排除、刪除該檔**。CI 合併 unit＋integration 報告，排除 Repositories／Query 會藏掉整合測試的覆蓋；清單還排除了 `AuthService`／`SessionService` 等認證程式碼，不該隱藏
- **Stryker 擱置**：`Domain/stryker-config.json`、`Infrastructure/stryker-config.json` 可能壞，但**不在 CI**（0 次提及）、非關鍵路徑 → 標 TODO，下次要跑變異測試再修
- `CLAUDE.md` 的指令區塊要更新

### `.claude/tdd.json` 分類（PR 5）
`classify()` 對三類都沒列到的路徑回 `unclassified` → 直接擋下，所以必須全覆蓋。2026/11/04 追加：
- **`scripts/**/*.sh` 列 `impl`、`scripts/tests/**` 列 `test`** —— 否則改入口腳本會被當成未分類擋下；
  列成 impl 也讓下次改腳本時必須先有 bats 紅燈（`scripts/e2e-local.sh` 當初就是沒先寫測試，事後才補）
- ⚠️ hook 的解析器只認 xUnit v3 / Rust 輸出，**不認 bats 的 TAP 輸出**（`ok 1 …` / `not ok 2 …`）
  → bats 跑出的紅燈不會被 hook 認定為「有效紅燈」。啟用時要決定：擴充 hook 解析 TAP，或改 shell 腳本時以 `tdd-exempt` 登記並以 bats 自律
- ✅ 2026/11/14 定案並啟用（PR 5）：
  - `tdd.json` 進版控（`.claude/` 仍整個忽略，`tdd.json` 以 `git add -f` 追蹤）
  - 測試走 CTRF：`dotnet test --project Test … --report-xunit-ctrf …`，報告 `TestResults/tdd.ctrf.json`（`plans/2026-11-05-tdd-hook-mtp-ctrf-support.md`）
  - scripts：`.sh`／`.py` 為 impl、`scripts/tests/**` 為 test，改腳本以 `tdd-exempt` 登記＋bats 自律
  - `db/seed-e2e.sql` 為 test；`db/migrations/**` 為 impl，`db/create-migration.sh` 視同產生 migration
  - 前端 `web/**` 先 ignore（hook 看不懂 Vitest）；邏輯層納入稽核另見 `plans/2026-11-14-tdd-hook-vitest-support.md`

---

## 二、★ 關鍵發現：現有架構與標準流程不相容

### 現在是「中介層內聯續期」，不是「客戶端換發」

| | 標準 refresh token 流程 | **MSRS 現況** |
|---|---|---|
| 誰發起續期 | **客戶端**（收到 401 後呼叫 `/auth/refresh`，再重送原請求） | **伺服器端中介層**（`AuthenticationMiddleware:78-100` 內聯換發，原請求直接繼續） |
| 前端知道嗎 | 知道（要寫攔截器 ＋ 重送） | **完全不知道**（只是收到新的 `Set-Cookie`） |
| 前端現況 | — | `apiClient.ts:9` 收到 401 直接跳首頁，**沒有重送邏輯** |

### 這造成兩個後果

**後果一：前端單飛（single-flight）在現況下沒有意義**

我原先說「thundering herd 靠前端單飛解決」——**在內聯續期架構下不成立**：
5 個併發請求各自進入中介層，各自發現 token 過期，**各自去輪替** → **成群搶進發生在伺服器端**，前端攔不到。

**後果二：refresh token 會跟著每一個請求送出 → 失去「縮小暴露面」這個核心好處**

真正的 refresh token 之所以安全，一半來自「**只送往一個端點**」。內聯續期等於把它放進每個請求。

### → 所以必須選一個

| 方案 | 內容 | 評估 |
|---|---|---|
| **A. 維持內聯續期** | refresh token cookie 跟著每個請求；成群搶進改用 Redis 在伺服器端做單飛 | ⚠️ **半套**：拿到可撤銷性，卻放棄暴露面好處，而且還要自己寫伺服器端單飛 |
| **B. 改成明確端點 ＋ Path 限定 cookie**（建議） | `Set-Cookie: refreshToken=…; **Path=/api/Auth/refresh**; HttpOnly; Secure; SameSite=Strict`<br>→ **瀏覽器只會把它送到那一個路徑**。前端收 401 → 呼叫 refresh → 重送原請求 | ✅ **忠於設計**：暴露面縮小、可撤銷、前端單飛變得有意義且可測 |

**選 B。** 理由：A 同時承擔兩種設計的成本（既要寫輪替，又要寫伺服器端單飛，還不省暴露面）。
`Path` 屬性是瀏覽器原生機制，不需要額外程式碼就能把 cookie 限制在一個端點。

> 代價要說清楚：B 要改前端 `apiClient`（攔截 401 → 單飛 refresh → 重送），而且**動到認證主幹，E2E 必須本機跑過**。

---

## 三、資料表設計

```sql
-- db/migrations/000028_refresh_token.up.sql
CREATE TABLE "RefreshToken" (
    "Id"          bigserial   PRIMARY KEY,
    "TokenHash"   text        NOT NULL,   -- 只存 SHA-256 雜湊，不存明文
    "FamilyId"    uuid        NOT NULL,   -- 同一條鏈（一次登入 = 一條）
    "DiscordId"   bigint      NOT NULL,
    "ParentId"    bigint      NULL REFERENCES "RefreshToken"("Id"),
    "IssuedAt"    timestamptz NOT NULL DEFAULT now(),
    "ExpiresAt"   timestamptz NOT NULL,
    "RotatedAt"   timestamptz NULL,       -- 已被用來換發（作廢）的時間
    "RevokedAt"   timestamptz NULL        -- 整鏈撤銷時間
);

CREATE UNIQUE INDEX "uq_refresh_token_hash"   ON "RefreshToken" ("TokenHash");
-- ★ 原子保證：一個 parent 只能有一個後繼 → 併發輪替第二者撞 23505
CREATE UNIQUE INDEX "uq_refresh_token_parent" ON "RefreshToken" ("ParentId") WHERE "ParentId" IS NOT NULL;
CREATE INDEX "ix_refresh_token_family"        ON "RefreshToken" ("FamilyId");
CREATE INDEX "ix_refresh_token_discord"       ON "RefreshToken" ("DiscordId");
```

### 設計決定

**① 只存雜湊，不存明文**
資料庫外洩時攻擊者拿不到可用的 token。用 **SHA-256 即可**，不需要 bcrypt／加鹽——token 是 256 位元隨機值（高熵），不是低熵密碼，沒有字典攻擊的空間。

**② 輪替的原子性靠唯一索引，不靠鎖** ⭐
`uq_refresh_token_parent` 讓「同一個 parent 只能產生一個後繼」。兩個併發請求同時輪替同一個 token：
- 第一個插入成功
- 第二個撞 **23505** → **改讀既有的後繼並回傳同一組 token**（冪等）

→ **這就是伺服器端的成群搶進解法，而且不需要 advisory lock**。
與既有的 `uq_tsc_confirmed_overlap` → 23505 → 轉譯 的處理模式一致。

**③ 寬限窗口（grace window）**
`RotatedAt` 後 **30 秒**內，重複使用舊 token **回傳同一個後繼**（而非判定被竊）。
超過窗口才算**重用偵測** → `RevokedAt` 整條 `FamilyId` 撤銷。

**④ 有效期**
| Token | 壽命 |
|---|---|
| access token（JWT） | **15 分鐘**（沿用 `CreateToken` 預設） |
| refresh token | **30 天**（絕對上限，輪替不延長 `FamilyId` 的總壽命） |

**⑤ 角色來源改為資料庫**
`Player.Role`（已由 `MemberUpdatedHandler:42` 推送維持）。續期時讀它，**不呼叫 Discord**。
→ `AuthService.RefreshToken` 不再注入 `IDiscordOAuthClient`／`GetGuildMemberAsync`。

**⑥ `MemberRemovedHandler` 要加一件事**
目前只清 session（對 JWT 使用者沒作用）。改為**同時**：
- 清掉 `Player.Role`（或設不活躍標記）→ 續期讀不到角色 → 拒絕
- **撤銷該使用者所有 refresh token** → 立即生效

---

## 四、重連補齊（Reconnect Re-sync）

### 為什麼是必要組成
拔掉 Discord 的同步查詢後，角色新鮮度**完全依賴推送事件**。而 gateway 事件不保證送達 → 必須有拉取式的自我修正，否則資料會慢慢偏離且無人察覺。

### 三層架構

| 層 | 觸發 | 作用 |
|---|---|---|
| **推送** | `MemberUpdatedHandler` / `MemberRemovedHandler`（已存在） | 秒級生效 |
| **重連補齊** | `GuildDownloadCompleted`（新增） | 補上離線期間漏接的 |
| **定期對帳** | 排程任務（每日一次） | 最後的自我修正 |

### 實作要點

**掛在 `GuildDownloadCompletedEventArgs`**
DSharpPlus 在連線／重連後送完公會資料時觸發 → 此時成員清單是最新的。
新增 `Infrastructure/Discord/Handlers/GuildDownloadCompletedHandler.cs`，註冊到 `Presentation/Program.cs:141-144` 的 `ConfigureEventHandlers`。

**⚠️ 只能在 Gateway 角色跑**
`Program.cs:54-55` 有角色拆分（`All` / `Gateway` / `Dispatcher`），**Dispatcher 永不連線 gateway**。
補齊邏輯只在 `isGateway` 時註冊，否則多 pod 會重複跑。

**節流**
`GuildDownloadCompleted` 每次重連都會觸發，可能很頻繁。
→ 距上次補齊**不足 5 分鐘就跳過**（記錄最後補齊時間；多 pod 下放 Redis，單 pod 放記憶體即可）。

**不可阻塞事件迴圈**
全量比對可能耗時 → 用背景任務執行，不要在 handler 內同步跑完。

**補齊邏輯**
1. 列舉公會成員 → `ResolveRoleAsync` 解出系統角色
2. 與 `Player.Role` 比對 → 不一致則更新
3. **在資料庫但已不在公會**的使用者 → 清角色 ＋ **撤銷其 refresh token**
4. 寫入記錄檔：本次更新筆數、撤銷筆數（**漏接量是可觀測指標**，長期為 0 才代表推送可靠）

**定期對帳**
重用同一份補齊邏輯，掛到既有的單一實例背景任務機制（Gateway 角色）。

---

## 五、改動後的流程

```
一般請求
  → cookie 有 jwtToken → ValidateToken → 有效 → 放行（無狀態，不碰資料庫）✅ 熱路徑不變

access token 過期
  → 中介層回 401（不再內聯續期）
  → 前端 apiClient 攔截 401
      → 單飛閘門（同時只有一個 refresh 在飛，其餘等它）
      → POST /api/Auth/refresh（瀏覽器只對這個 Path 送 refreshToken cookie）
          → 雜湊比對查 RefreshToken
          → RevokedAt 非空 → 401（整鏈已撤銷）
          → RotatedAt 非空
              ├─ 在 30 秒寬限窗口內 → 回傳既有後繼（冪等）
              └─ 超過窗口 → 重用偵測 → 撤銷整條 FamilyId → 401
          → 未使用過 → 插入後繼（撞 23505 則改讀既有後繼）
                      → 讀 Player.Role（不呼叫 Discord）
                      → 角色為空 → 401（已離開公會）
                      → 發新 access token ＋ 新 refresh token
      → 重送原請求（**只重送一次**，避免無限迴圈）
  → refresh 失敗 → 跳首頁（維持現有行為）
```

---

## 六、★ Case 表（等待核可）

### A 組 — RefreshToken 儲存層（整合測試，真資料庫）

| # | 情境 | 期望 |
|---|---|---|
| A1 | 建立新鏈 | 產生 `FamilyId`、`ParentId` 為 null、`TokenHash` 寫入、`ExpiresAt` = now + 30 天 |
| A2 | 以明文查詢 | **查不到**（資料表只有雜湊） |
| A3 | 正常輪替 | 舊列 `RotatedAt` 被設、新列 `ParentId` 指向舊列、`FamilyId` 相同 |
| A4 | **併發輪替同一個 token（兩條連線同時）** | 其中一個撞 **23505**；**兩者最終回傳同一個後繼**（冪等）⭐ |
| A5 | 在寬限窗口內重複使用已輪替的 token | 回傳**同一個後繼**，**不**撤銷鏈 |
| A6 | 超過寬限窗口使用已輪替的 token | **整條 `FamilyId` 的 `RevokedAt` 全被設** |
| A7 | 使用已撤銷鏈中的任一 token | 拒絕 |
| A8 | 使用已過期的 refresh token | 拒絕，且**不**撤銷鏈（過期不等於被竊） |
| A9 | 輪替**不**延長 `FamilyId` 總壽命 | 新列 `ExpiresAt` 不超過該鏈最初的上限 |
| A10 | 撤銷某使用者全部 token | 該 `DiscordId` 所有鏈皆 `RevokedAt` 非空 |
| A11 | 不存在的雜湊 | 拒絕（不得丟出未處理例外） |

### B 組 — `/api/Auth/refresh` 端點

> 🔶 **實作方式更正**：原本寫「`Test/AuthControllerRefreshTests.cs`（單元測試）」。
> 但 **`WebApplicationFactory` 已經存在**（`Test.Integration/RateLimiterIntegrationTests.cs`，`Program.cs:331` 還留了 `public partial class Program;`）
> → B 組改為 **`Test.Integration/AuthRefreshIntegrationTests.cs`**，走**完整 middleware 管線 ＋ 真資料庫**。
> 理由：B2（cookie 的 `Path` / `HttpOnly` / `Secure` / `SameSite` 屬性）、B11（`AllowAnonymous` 是否真的生效）、B12（Idempotency 排除路徑）**都需要真實管線才測得準**，單元測試會漏掉管線層的行為。

| # | 情境 | 期望 |
|---|---|---|
| B1 | 帶有效 refresh token | **200**，`Set-Cookie` 同時含新 `jwtToken` 與新 `refreshToken` |
| B2 | 新的 refreshToken cookie 屬性 | **`Path=/api/Auth/refresh`**、`HttpOnly`、`Secure`、`SameSite=Strict` ⭐ |
| B3 | 無 refresh token cookie | **401** |
| B4 | refresh token 格式錯誤／不存在 | **401** |
| B5 | 已撤銷的 refresh token | **401** |
| B6 | 已過期的 refresh token | **401** |
| B7 | **`Player.Role` 為空**（已離開公會） | **401**，且**不**發新 token |
| B8 | `Player.Role` = `admin` | 新 access token 的 role claim = `admin` |
| B9 | 輪替後**舊的 refresh token 再打一次**（超過寬限窗口） | **401**，且該使用者**所有**鏈被撤銷 |
| B10 | 端點**不呼叫 Discord API** | 以替身驗證 `GetGuildMemberAsync` **Times.Never** ⭐ |
| B11 | 此端點為 `[AllowAnonymous]`（靠 cookie 自證，不經一般認證） | 匿名可達，但無 cookie 回 401 |
| B12 | 此端點須帶 `X-Idempotency-Key`？ | ❌ **否** — 輪替本身已由唯一索引冪等化；要求前端帶 key 反而讓單飛更複雜 → **加入 `IdempotencyMiddleware` 的排除路徑** |

### C 組 — `AuthenticationMiddleware`（移除內聯續期）

| # | 情境 | 期望 |
|---|---|---|
| C1 | access token 有效 | 放行，**不碰資料庫**、**不碰 RefreshToken 表** |
| C2 | access token **過期** | **401**（不再內聯續期）⚠️ 行為變更 |
| C3 | access token 簽章錯誤 | **401** |
| C4 | 過期 ＋ 端點要求 admin | **401**（不是 403——還沒通過認證） |
| C5 | 無任何 cookie | **401** |
| C6 | admin 的 session cookie 路徑 | **不受影響**（session 機制維持原狀） |
| C7 | `IJwtService.ReadJsonWebToken` | **已移除**（續期不再需要解析過期 token）⭐ 連帶完成 `jwt-refresh-redesign` 的目標 |

### D 組 — 前端 `apiClient` 單飛與重送

| # | 情境 | 期望 |
|---|---|---|
| D1 | 單一請求收到 401 | 呼叫 refresh → 成功 → **重送原請求一次** → 回傳原請求結果 |
| D2 | **5 個併發請求同時收到 401** ⭐ | **只發出 1 次** refresh 呼叫，5 個請求都在它完成後重送 |
| D3 | refresh 失敗（401） | 不重送，跳首頁（維持 `apiClient.ts:9` 現行行為） |
| D4 | 重送後**又**收到 401 | **不再重試**（避免無限迴圈），跳首頁 |
| D5 | 403 | 照舊拋 `ApiError(403)`，**不**觸發 refresh |
| D6 | 寫入操作（POST/PUT/DELETE）重送 | **沿用同一把 `X-Idempotency-Key`**（不可重新產生，否則後端去重失效）⭐ |
| D7 | 伺服器端算繪（SSR）情境 | `typeof window === 'undefined'` 時不嘗試 refresh，直接拋出 |

### E 組 — Discord 事件推送（既有行為 ＋ 新增）

| # | 情境 | 期望 |
|---|---|---|
| E1 | 成員角色由 admin 降為一般 | 清除 session（既有）**＋ 撤銷其 refresh token**（新增） |
| E2 | 成員被踢出／離開公會 | 清 session（既有）**＋ 清 `Player.Role` ＋ 撤銷 refresh token**（新增） |
| E3 | 角色變更且有對應系統角色 | `Player.Role` 更新（既有） |
| E4 | E2 之後該使用者呼叫 refresh | **401**（立即生效，不等 15 分鐘）⭐ 這是本計畫的主要收益 |

### F 組 — 重連補齊

| # | 情境 | 期望 |
|---|---|---|
| F1 | `GuildDownloadCompleted` 觸發，資料庫角色與公會一致 | 零更新、零撤銷 |
| F2 | 離線期間某成員角色變了 | 補齊後 `Player.Role` 與公會一致 |
| F3 | 離線期間某成員離開公會 | 補齊後其角色被清 ＋ refresh token 被撤銷 |
| F4 | **5 分鐘內重複觸發** | **第二次跳過**（節流）⭐ |
| F5 | 距上次補齊超過 5 分鐘 | 執行 |
| F6 | 以 **Dispatcher 角色**啟動 | **不註冊**此 handler（`Times.Never`）⭐ |
| F7 | 補齊過程中單一成員處理失敗 | **其餘繼續**，失敗寫入記錄檔（不可整批中止） |
| F8 | 觀測性 | 記錄更新筆數與撤銷筆數 |

### G 組 — 端到端（Playwright，`compose.e2e.yaml`）

| # | 情境 | 期望 |
|---|---|---|
| G1 | 登入後正常操作 | 既有 `auth.spec.ts` 全綠 |
| G2 | access token 過期後繼續操作 | **自動續期成功，使用者無感**（不跳首頁） |
| G3 | refreshToken cookie 的 `Path` | 瀏覽器**不會**把它送往 `/api/Character` 等路徑 ⭐ |
| G4 | refresh token 被撤銷後操作 | 跳首頁 |

---

## 七、★ 完整 PR 順序（全專案主序，各計畫共用）

| PR | 內容 | 計畫 | hook 治理 | 預估 |
|---|---|---|---|---|
| **1** | 四份計畫文件 | — | — | 0.5 天 |
| **2** | 授權矩陣 **G1/G2/G8/G10**（Playwright E2E，**護欄**） | authorization-matrix | ❌ | 1 天 |
| **3** | xUnit v3 遷移 — `Test/` ＋ 覆蓋率驗證 | 本計畫 §〇 | ❌ | 0.5-1 天 |
| **4** | xUnit v3 遷移 — `Test.Integration/` ＋ CI | 本計畫 §〇 | ❌ | 0.5-1 天 |
| **5** | `.claude/tdd.json` 啟用 | — | — | 0.1 天 |
| **5b** | **管理員重新登入迴圈 ＋ session 快取汙染**（2026/11/04 追加，case 表已核可） | session-multi-device-relogin | ✅ | 0.5 天 |
| **6** | **階段 0：認證與授權分離** | split-authentication-authorization | ✅ | 0.5 天 |
| **7** | **階段 0.5：`JwtService` 真實驗簽行為** | jwt-refresh-redesign §四 A 組 | ✅ | 0.5 天 |
| **8** | 階段 3a：`RefreshToken` 表 ＋ 儲存層（A 組） | 本計畫 | ✅ | 1-2 天 |
| **9** | 階段 3b：`/api/Auth/refresh` ＋ `AuthService` 改讀資料庫（B 組） | 本計畫 | ✅ | 1-2 天 |
| **10** | 階段 3c：移除內聯續期 ＋ 刪 `ReadJsonWebToken`（C 組） | 本計畫 | ✅ | 1 天 |
| **11** | 階段 3d：前端 `apiClient` 單飛 ＋ 重送（D 組） | 本計畫 | ❌（`web/**`） | 1 天 |
| **12** | 階段 3e：事件撤銷 ＋ 重連補齊 ＋ 每日對帳 ＋ G 組 E2E（E/F/G 組） | 本計畫 | ✅ | 1-2 天 |
| 13 | 授權矩陣其餘組別 G3/G4/G5/G6/G7/G9（.NET 整合測試） | authorization-matrix | ✅ | 2-3 天 |

> **總計約 11-16 天**（PR 1-12）。
> **PR 5b 起每個 PR 都在 hook 治理下、有自己的審核閘**：「case 表是唯一審核點」→ 先給 case 表核可，才轉錄測試、才實作（5b 的 case 表已於 2026/11/04 核可）。

---

## 七之二、本計畫內部階段（PR 8-12）

> test-first 鐵則：**case 表核可 → 轉錄測試 → 跑出有效紅燈 → 才寫實作到綠**。
> 新測試引用不存在的型別時：**先跑一次讓它編譯失敗** → 才建 `NotImplementedException` 樁 → 跑出真紅燈 → 才實作。**從樁直接跳綠＝違規。**

| 階段 | 內容 | 檔案 |
|---|---|---|
| **1** | A 組測試 → 紅 | `Test.Integration/RefreshTokenRepositoryIntegrationTests.cs` |
| **2** | 遷移檔 ＋ 儲存庫實作 → A 組綠。**遷移須可逆**（`.down.sql`，由既有 `MigrationReversibilityIntegrationTests` 覆蓋） | `db/migrations/000028_refresh_token.{up,down}.sql`、`Domain/Repositories/IRefreshTokenRepository.cs`、`Infrastructure/Repositories/` |
| **3** | B 組測試 → 紅 | `Test/AuthControllerRefreshTests.cs`（含 B10 替身驗證不呼叫 Discord） |
| **4** | `/api/Auth/refresh` 端點 ＋ `AuthService` 改讀 `Player.Role` → B 組綠。`IdempotencyMiddleware` 加排除路徑 | `Presentation.WebApi/Controller/AuthController.cs`、`Infrastructure/Services/AuthService.cs` |
| **5** | C 組測試 → 紅 | `Test/AuthenticationMiddlewareTests.cs`（改寫既有續期測試） |
| **6** | 移除中介層內聯續期 ＋ **刪除 `IJwtService.ReadJsonWebToken`** → C 組綠 | `Presentation.WebApi/Middleware/AuthenticationMiddleware.cs`、`Application/Interface/IJwtService.cs`、`Infrastructure/Services/JwtService.cs` |
| **7** | E 組測試 → 紅 → 改兩個 handler → 綠 | `Test/MemberRemovedHandlerTests.cs`、`Test/MemberUpdatedHandlerTests.cs` |
| **8** | F 組測試 → 紅 → 實作補齊 handler ＋ 節流 ＋ 註冊（僅 Gateway） | `Infrastructure/Discord/Handlers/GuildDownloadCompletedHandler.cs`、`Presentation/Program.cs` |
| **9** | D 組測試 → 紅 → 改 `apiClient`（單飛 ＋ 重送 ＋ 沿用 idempotency key） | `web/__tests__/apiClient.test.ts`、`web/services/apiClient.ts` |
| **10** | 定期對帳任務（重用階段 8 的邏輯） | `Infrastructure/BackgroundJobs/` |
| **11** | G 組端到端 ＋ `dotnet format` ＋ 全測試 ＋ **本機 Docker 跑 E2E** | `web/e2e/auth.spec.ts` 等 |
| **12** | 部署注意事項（見下） | `docs/deployment.md` |

### 部署注意事項
- **遷移必須先上**（新資料表），再上應用程式
- **現有使用者的 access token 仍有效**，但過期後沒有 refresh token → **會被登出一次**，需重新用 Discord 登入
  → 可接受（一次性），但要寫進部署記錄並選離峰時段
- 若要避免，需過渡期同時支援兩種續期路徑 → **不做**（複雜度不值得）

### 驗收標準
- [ ] A4（併發輪替冪等）、D2（前端單飛）**都有測試覆蓋** — 這兩條是成群搶進的雙重保險
- [ ] B10 證明續期路徑**不呼叫 Discord API**（本計畫的主要目的）
- [ ] E4 證明撤銷**立即生效**
- [ ] F6 證明 Dispatcher 角色不跑補齊
- [ ] F8 的漏接量有記錄 → 可長期觀察推送可靠度
- [ ] D6 重送沿用同一把 idempotency key（否則會製造重複寫入）
- [ ] 每條測試都確認「拿掉實作會紅」
- [ ] E2E **本機 Docker 跑過**（動到認證主幹，不可只靠 CI）

### 不在範圍
- 多裝置各自一條鏈的管理介面（資料結構已支援，不做 UI）
- 「登出所有裝置」功能（撤銷邏輯已具備，不接 UI）
- session（管理員）機制改動 — 維持原狀
- 授權矩陣（見 `2026-11-04-authorization-matrix.md`）

---

## 八、四份計畫的關係

| 計畫 | 角色 | PR |
|---|---|---|
| `2026-11-04-split-authentication-authorization.md` | **階段 0 — 根因修法。本計畫的前置。**<br>認證與授權混在同一 middleware → 兩種耦合（語意：授權＝認證方式；結構：授權在認證函式尾端，認證成功路徑自己 `next()` 就繞過）。**同樣三行出錯兩次**（`0ed4125` → 被 `f0f32fb` 重構帶回 → `60faf7b` 再修）。<br>拆開後那三行**完全無害**，因為 `next()` 就是在呼叫授權 | **6** |
| `2026-11-04-jwt-refresh-redesign.md` | 🔶 **主體已被本計畫取代**（移除內聯續期後 `ReadJsonWebToken` 直接刪，不需 `ValidateIgnoringLifetime`）。<br>**僅 §四 A 組「真實驗簽行為」仍有效**，搬入本計畫前置 → PR 7。本計畫的 refresh 端點用「過期但驗簽有效」自證，**那個前提必須先被測試釘住** | **7** |
| `2026-11-04-authorization-matrix.md` | **護欄。** G1/G2/G8/G10 用 Playwright E2E 先行（不受 hook 治理 → 不必等 xUnit v3 遷移），在改認證主幹之前立起端到端斷言；其餘組別改用 .NET 整合測試，排在最後 | **2** ＋ **13** |
| **本計畫** | 階段 3 — refresh token ＋ 重連補齊 | **8-12** |

→ 完整順序見 §七。
