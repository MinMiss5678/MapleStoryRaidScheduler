# 401 原因記錄 ＋ 交易提交必須早於回應送出

> 建立：2026/11/16｜狀態：**✅ 已核可（2026/11/16）**：A–G 組；log 等級＝歸屬不符 Warning、其餘 Information；C 組採**修法 B（action filter）**並加 C5／C6；G 組只涵蓋 HTTP 請求
> 起因：Redis fail-open PR 的 E2E 出現一次 G11.1 flaky（第二個 session 401），事後**無法判斷原因**；
> 追查時發現寫入回應帶 `Content-Length`，回應可能早於 `UnitOfWorkMiddleware` 的 commit 送出。

---

## 一、追查結論（2026/11/16 實測）

| 實驗 | 結果 |
|---|---|
| 對 E2E 後端連續 200 次「管理員登入 → 立刻用新 sessionId 打 `/api/Me/Teams`」 | 0/200 失敗 |
| `--grep G11 --repeat-each 30`（無重試） | 60/60 通過 |
| 全套 `--repeat-each 5`（265 筆） | G11 全過（另 8 筆是 `instant-lfg`／`register` 第 2–5 輪，固定 seed 資料不可重複跑，與此無關） |

- 「回應先到、交易未提交」**解釋不了**那次 flaky；flaky 本身重現不出來。
- 後端只記「401」，**不記原因**——這是事後無法判斷的根本原因 → **第一部分**。
- 寫入回應實際帶 `Content-Length: 15`（Newtonsoft 先緩衝再一次寫出）→ 用戶端讀滿位元組就算完成，**可能早於 commit**；若 commit 失敗，用戶端已拿到 200、資料卻沒寫入，且無任何錯誤 → **第二部分**。尚未證實。

---

## 二、第一部分：401 原因記錄

### 設計
- `AuthenticationMiddleware` 每個回 401 的分支，記一筆 log 說明原因。
- `SessionService.GetAsync` 回 null 的三種原因（查無、歸屬不符、過期）由它自己記，因為中介層只看到 null。
- **絕不記憑證本身**：sessionId、JWT 都是可直接冒用的憑證，log 外洩等於憑證外洩。只記 discordId（cookie 帶來的、或 token 解出的）與原因。
- 等級：**歸屬不符記 Warning**（只有冒用時才會發生，值得注意）；其餘記 Information（正常使用也會發生，例如 cookie 過期）。

### A 組 — 認證 401 原因（`Test/AuthenticationMiddlewareTests.cs`，以假 logger 擷取）

| # | 情境 | 期望 log |
|---|---|---|
| A1 | 沒有任何憑證 | Information，原因「沒有憑證」 |
| A2 | sessionId cookie 為空字串 | ~~原因「sessionId 為空」~~ → **Information，原因「沒有憑證」**，含 cookie 的 discordId（2026/11/17 規格修正：ASP.NET Core 解析 cookie 時，值為空的 cookie 不會出現在 `Request.Cookies`，等同沒帶 sessionId；`AuthenticationMiddleware` 的 `string.IsNullOrEmpty(sessionId)` 分支因此永遠走不到，一併刪除） |
| A3 | session 不存在（服務回 null） | Information，原因「session 無效」，含 discordId |
| A4 | JWT 驗證失敗（簽章錯等，非過期） | Information，原因「JWT 無效」 |
| A5 | JWT 過期、續期失敗 | Information，原因「續期失敗」，含 token 解出的 discordId |
| A6 | JWT 過期、續期結果無效或帳號不符 | Information，原因「續期結果無效」，含 discordId |
| A7 | 任何 401 的 log | **不含** sessionId 與 JWT 的值 |
| A8 | 認證成功 | **不記** 401 原因 |

### B 組 — session 無效的細分原因（`Test/SessionServiceTests.cs`）

| # | 情境 | 期望 log |
|---|---|---|
| B1 | 資料庫查無此 sessionId | Information，原因「查無 session」 |
| B2 | session 屬於另一個 discordId | **Warning**，原因「session 歸屬不符」，含 cookie 的 discordId 與 session 實際的 discordId |
| B3 | session 已過期 | Information，原因「session 已過期」 |
| B4 | B1–B3 的 log | 不含 sessionId 的值 |

---

## 三、第二部分：commit 必須早於回應送出

### 先證實（C 組，整合測試，`WebApplicationFactory`）
以假的 `IUnitOfWork` 取代真的：

| # | 情境 | 期望 |
|---|---|---|
| C1 | 一般寫入請求成功（例如 test-login），假 UoW 在 `CommitAsync` 被呼叫時記下 `Response.HasStarted` | **`HasStarted == false`**：commit 時回應尚未開始送出 ⚠️ 預期紅 |
| C2 | 同 C1，但假 UoW 的 `CommitAsync` 丟例外 | 用戶端收到 **500**（不是 200） ⚠️ 預期紅 |
| C3 | 寫入請求回 4xx（例如驗證失敗） | 呼叫 `RollbackAsync`、不呼叫 `CommitAsync`（現行行為，回歸） |
| C4 | 讀取請求（GET） | 不開交易（現行行為，回歸） |

> `HasStarted` 與傳輸層無關（TestServer 也會在寫出回應時設為 true），可穩定判斷「commit 時回應是否已送出」。

### 修法（C1／C2 確認為紅後才做，二擇一）

| 選項 | 做法 | 評估 |
|---|---|---|
| **A. 寫入請求的回應先緩衝（建議）** | `UnitOfWorkMiddleware` 對寫入請求把 `Response.Body` 換成記憶體緩衝 → `await next` → 依狀態碼 commit／rollback → **commit 成功後**才把緩衝內容寫到真正的回應 | 改動集中在一個 middleware；狀態碼判斷維持現行（看 `Response.StatusCode`）；commit 失敗時回應尚未開始，例外處理能正常回 500。代價：寫入回應整份放記憶體（寫入回應都很小） |
| B. commit 移到 MVC result filter | `IAsyncResultFilter` 在結果寫出前 commit | 要從 `IActionResult` 推斷狀態碼；非 MVC 端點不涵蓋；改動散到 filter 與 middleware 兩處 |

### 回歸
- 全部 E2E 通過（寫入流程的行為不得改變）。
- 牙齒：拿掉緩衝 → C1／C2 變紅。

---

## 四、第三部分：交易範圍（2026/11/16 追加）

### 現況查證

| # | 問題 | 方向 | 證據 |
|---|---|---|---|
| 1 | 登入時三次 Discord 網路呼叫在已開啟的 DB 交易內 | 範圍太大 | 寫入請求一進 controller 前 `UnitOfWorkMiddleware` 就 `BeginAsync`（立刻租連線＋`BEGIN`）；`AuthAppService.LoginAsync` 依序 ExchangeCode（Discord token＋@me）→ 讀 DB → GetGuildMember（Discord）→ 寫 DB |
| 2 | 冪等標記在交易外，失敗不撤銷 | 交易管不到 | `IdempotencyMiddleware` 先 `TryMarkAsync` 才 `next`；rollback／500 後標記留到 TTL。同一把 key 重送 → 409「已提交」，`CharacterForm` 收到 409 **靜默略過** → 寫入失敗但使用者沒看到錯誤 |
| — | advisory lock、outbox | 無問題 | middleware 包住整個 action，鎖持有到 commit；outbox 與資料同交易 |

### 原則：兩個方向各一道「切錯就報錯」
交易邊界放哪一層都會切錯（HTTP 層傾向太大；業務層明確包範圍時容易太小，且太小是默默的正確性問題）。
→ 不搬邊界，先在兩個方向放檢查（任何一層都有效）；搬移邊界另議。

### D 組 — 範圍太小：沒有交易就報錯（整合測試，真 DbContext）

| # | 情境 | 期望 |
|---|---|---|
| D1 | `TeamSlotEditLock.AcquireTeamSlotEditLockAsync`，`DbContext` 沒有進行中的交易 | 丟 `InvalidOperationException`（訊息說明需在交易內）——原本 `SET LOCAL` 只發 WARNING、鎖立刻釋放 |
| D2 | `Outbox.EnqueueAsync`，沒有進行中的交易 | 丟 `InvalidOperationException` |
| D3 | 兩者在交易內 | 正常（回歸） |

### G 組 — 範圍太大：交易中不准發對外 HTTP（單元測試）
做法：`DelegatingHandler`（`ConfigureHttpClientDefaults` 掛到所有 `HttpClient`），送出前經 `IHttpContextAccessor` 取本請求的 `DbContext`，`Transaction != null` 就丟例外、不送出請求。
限制：只涵蓋 HTTP 請求內（bot、背景工作沒有 HttpContext，不檢查）。

| # | 情境 | 期望 |
|---|---|---|
| G1 | 本請求的 DbContext 有進行中的交易時發出 HTTP 請求 | 丟 `InvalidOperationException`，**請求未送出** |
| G2 | 沒有進行中的交易 | 照常送出 |
| G3 | 不在 HTTP 請求內（沒有 HttpContext） | 照常送出 |
| G4 | 所有經 `IHttpClientFactory`／注入的 `HttpClient` 都掛上這個 handler（DI 檢查） | 是 |

### E 組 — 修正 1：登入的 Discord 呼叫移出交易
做法：新增端點標記（例如 `[NoAmbientTransaction]`），`UnitOfWorkMiddleware` 遇到就不開交易（現有的 `SkipUow` 旗標沒有任何地方設定，一併改用標記）；登入改由 `AuthAppService` 在 Discord 呼叫**之後**自己開交易寫 DB。

| # | 情境 | 期望 |
|---|---|---|
| E1 | 登入成功 | Discord 呼叫（ExchangeCode、GetGuildMember）都在 `BeginAsync` **之前**；DB 寫入在之後；`CommitAsync` 一次 |
| E2 | Discord 呼叫失敗 | 不開交易（`BeginAsync` 未被呼叫），回登入失敗 |
| E3 | DB 寫入失敗 | `RollbackAsync`，例外往外拋 |
| E4 | 有 `[NoAmbientTransaction]` 的寫入端點 | `UnitOfWorkMiddleware` 不開交易；沒標的寫入端點照舊開 |

### F 組 — 修正 2：失敗時撤銷冪等標記（`IIdempotencyStore` 新增 `RemoveAsync`）

| # | 情境 | 期望 |
|---|---|---|
| F1 | 請求結束時狀態碼 ≥ 400 | 撤銷此 key → 同一把 key 重送可再次處理 |
| F2 | 請求丟出例外 | 撤銷此 key，例外照常往外拋 |
| F3 | 成功（< 400） | 保留 → 同一把 key 重送回 409（回歸） |
| F4 | 請求本身就是重複（409） | **不得**撤銷原請求的 key |
| F5 | 撤銷時 Redis 不可用 | fail-open：記 log、不丟例外、不影響回應 |

---

## 五、PR 拆分與順序

| PR | 內容 | 理由 |
|---|---|---|
| ① | A、B 組：401 原因 log | 風險最小，先讓下次 flaky 能自我說明 |
| ② | D、G 組（兩道檢查）→ E 組（G 先讓登入紅，再修）→ F 組 | 同屬「交易範圍」；G 會讓現行登入路徑變紅，E 修好轉綠，正好走先紅後綠 |
| ③ | C 組：commit 早於回應（修法 A：寫入回應先緩衝） | 動 `UnitOfWorkMiddleware` 核心；放最後，前面的檢查已就位 |

每個 PR：全部單元／整合測試＋本機 E2E 全綠才送出。

---

## 六、C 組修正（2026/11/16 定案：修法 B）

「回應先緩衝」（修法 A）是補償：commit 位置不變（middleware 包在 MVC 外，必然晚於寫回應），只是用緩衝掩蓋。
改採 **修法 B**：全域 `IAsyncActionFilter`，action 執行前 `BeginAsync`、action 執行後／result 寫出前依結果 commit 或 rollback——結構上保證回應晚於 commit。
判斷規則同現行（≥ 400 或例外 → rollback），改看 action 的結果：`IStatusCodeActionResult.StatusCode`，無狀態碼者視為 200。

| # | 情境 | 期望 |
|---|---|---|
| C5 | commit 成功、但寫回應時失敗（例如序列化例外） | 資料已提交，用戶端收到 5xx（記錄此取捨：比「收到 200 但資料沒存」好，重試由冪等／DB 約束擋重複） |
| C6 | 標有 `[NoAmbientTransaction]` 的寫入端點 | filter 不開交易（E 組的標記改由 filter 判斷） |

`UnitOfWorkMiddleware` 移除（PR ③）；E 組在 PR ② 先以 middleware 判斷標記，PR ③ 時一併移到 filter。

---

## 七、PR ① 實作紀錄（2026/11/17，A／B 組）

- `AuthenticationMiddleware` 注入 `ILogger`，每個 401 分支記 `認證失敗（401）：{Reason}，discordId={DiscordId}`；`SessionService` 記 session 無效的細分原因（歸屬不符 Warning，其餘 Information）。只記原因與 discordId，不記 sessionId／JWT。
- 測試：新增 `Test/CapturingLogger.cs`；A1–A8、B1–B4。先編譯失敗（建構子多一個參數）→ 只加參數的樁 → 12 紅 → 實作。
- **規格缺口（A2）**：實作後 A2 紅——值為空的 cookie 不會出現在 `Request.Cookies`，「sessionId 為空」那個分支永遠走不到。使用者核可改為「等同沒帶憑證」並刪死碼。
  流程：hook 規定先綠燈才能改測試 → 先依**原規格**實作（從原始 Cookie header 判斷空 sessionId）跑出綠燈結束該段 → 更新 case 文件 → 新段改 A2 期望值 → 紅 → 刪掉該特判與死碼 → 綠。
- 結果：單元 407（+12）、整合 82、E2E 53 passed。E2E 後端 log 的 401 原因與刻意製造的情境一致：匿名打 API → 「沒有憑證」；G8 偽造 sessionId → 「session 無效」＋「查無 session」；G11.3 冒充 → 「session 歸屬不符」（Warning，含兩個 discordId）。

---

## 八、PR ② 實作紀錄（2026/11/17，D／G／E／F 組）

- **D 組**：`TeamSlotEditLock.AcquireAsync`、`Outbox.EnqueueAsync` 開頭檢查 `DbContext.Transaction`，沒有交易就丟 `InvalidOperationException`。
  - 守衛一上就抓到 `TeamLeaderServiceIntegrationTests` 有 8 筆測試在交易外呼叫寫入（正式環境 UoW 一定有交易，測試沒模擬到）。以 `tdd-testfix` 登記：加 `InTxAsync` helper（Begin → 動作 → Commit；例外 → Rollback 後原樣拋出），把 30 處寫入呼叫包進去；**期望值未改**。
- **G 組**：`NoHttpCallInTransactionHandler`（`DelegatingHandler`，經 `IHttpContextAccessor` 取本次請求的 `DbContext`，有交易就丟例外），以 `ConfigureHttpClientDefaults` 掛到所有 `HttpClient`。單元 G1–G4＋整合 `TransactionGuardWiringIntegrationTests`（驗 Program.cs 真的有接線）。
- **E 組**：`AuthAppService` 自己管交易——Discord 呼叫與角色解析在交易外，之後才 `BeginAsync` → 建玩家／session → Commit（失敗 Rollback）；`AuthController.LoginAsync` 標 `[NoAmbientTransaction]`，`UnitOfWorkMiddleware` 改看端點 metadata（取代原本的 `SkipUow` Items 旗標）。
- **F 組**：`IIdempotencyStore.RemoveAsync`（Redis `DEL`，失敗 fail-open 記 Warning）；`IdempotencyMiddleware` 放行後若例外或狀態碼 ≥ 400 就撤銷標記，成功保留；「本身就是重複」的 409 提早 return，不會撤銷原請求的標記。
- **順手修的測試基礎設施**：新增整合測試後，3 個 `WebApplicationFactory<Program>` 測試類別平行建 host，搶 Program.cs 設的全域 Serilog bootstrap logger → 「The logger is already frozen」。新增 `[Collection("webapp-host")]`（不平行）把三者收進同一集合。正式環境一個行程一個 host，不受影響。
- 結果：單元 427、整合 88、build 0 警告、`dotnet format` 無差異、本機 E2E 53 passed（`scripts/e2e-local.sh`，playwright exit=0）。
- 附帶發現（不屬本 PR）：從 Git Bash 執行 `scripts/e2e-local.sh` 時，腳本最後是 `exit 0`，外層卻拿到 1。原因是 WSL 端以 `bash -l` 執行，登出時跑 `~/.bash_logout` 的 `clear_console -q`（沒有終端機而失敗），它的回傳值蓋掉了腳本的結束碼（`bash -l -x` 追蹤已證實）。屬於結束碼傳遞的假紅，另開 PR 處理。

---

## 九、PR ③ 實作紀錄（2026/11/18，C 組，修法 B）

- **先證實**：新增 `Test.Integration/TransactionBoundaryIntegrationTests.cs`（真 MVC 管線＋測試專用 `TxProbeController`＋記錄用假 `IUnitOfWork`，commit 當下記 `Response.HasStarted`）。對舊的 `UnitOfWorkMiddleware` 跑：
  - C1（POST／PUT／PATCH／DELETE 四種）全紅：`commit(started=True)`——**commit 時回應已開始送出，假設證實**。
  - C2 紅：commit 失敗時用戶端拿不到 500（回應已開始，例外處理設不了狀態碼）。
  - C5 紅：序列化失敗時舊做法 rollback（action 其實成功了）。
  - C3（4xx／例外 → rollback）、C4（GET 不開交易）、C6（`[NoAmbientTransaction]`）綠＝回歸保護。
- **實作**：新增全域 `Presentation.WebApi/Filters/UnitOfWorkActionFilter.cs`（`IAsyncActionFilter`），`Program.cs` 以 `AddControllers(o => o.Filters.Add<…>())` 註冊；刪除 `UnitOfWorkMiddleware` 與其單元測試（契約全部改由上面的整合測試覆蓋，登入端點標記的反射測試一併搬過去）。
  - 失敗判斷：未處理例外，或 `IStatusCodeActionResult.StatusCode ≥ 400`；沒有狀態碼的結果視為 200。
  - `[ApiController]` 的模型驗證 400 由框架 filter 在本 filter 之前短路 → 不開交易（舊做法是開了再 rollback）。
- 文件：`CLAUDE.md` 請求生命週期、`docs/architecture.md` §3 與管線圖／時序圖、`docs/business-rules.md` T1–T3（新增 T3a、T3b）。
- 結果：單元 420（刪除 7 支 middleware 單元測試）、整合 99（+11）、build 0 警告、`dotnet format` 無差異。
- E2E：第一次全套 52 passed＋1 flaky（`leader-led-auto-revoke` 候選頁 5 秒內沒出現 P-Full-A，重試通過；該次全套耗時 2.8 分，平常約 1 分，機器偏慢）；重跑全套 53 passed（1.3 分）。卡住的步驟是候選頁的 GET 讀取，不走交易；此改動只讓寫入的 commit 更早，不會讓讀取看不到資料。未能重現，先記錄。
- Rider MCP 跑「整合測試全套」連續兩次逾時（同指令在 Bash 72 秒跑完 99/99）；稽核用的綠燈改以分類別跑（含本 PR 動到的類別）取得。
