# TDD hook 支援 Microsoft.Testing.Platform（以 CTRF 報告判定紅綠燈）

> 狀態：**hook 已實作（2026/11/06）**，§六 步驟 1–4 完成；步驟 5（實機驗證）併入 MSRS PR 5（啟用 `tdd.json` 時做）。
> case 表 §五 已核可（2026/11/06）；PR 4 覆蓋率決定：不排除、刪 `coverlet.runsettings`。
> 實作：`tdd_core.parse_ctrf`、`pre_impl_decision` 帶出無效原因；`tdd_hook` pre 記 `RUN_START`、post 依 `test_report` 讀報告。
> 測試 190 → 214 筆全綠；拿掉「報告須晚於 RUN_START」→ M8 三筆變紅、拿掉 exit code 交叉檢查 → M10／M11 變紅。修改前備份：`~/.claude/hooks/tdd.bak-20261106/`（hook 目錄不在 git 版控）。
> 範圍：全域 hook `~/.claude/hooks/tdd/`（`tdd_core.py`、`tdd_hook.py` 與其測試）。MSRS 是第一個使用者；ChipHunter、Emberstone 共用同一份 hook，**不得受影響**。

---

## 一、問題

hook 判定一次測試執行是紅、綠還是無效，靠的是比對**主控台文字**（`tdd_core.py:8-10`）：

- 摘要：`Total: N, Errors: N, Failed: N, Skipped: N, Not Run: N`
- 失敗測試：`<名稱> [FAIL]`

這是 xUnit v3 **原生執行器**（`dotnet run --project X`）的格式。Microsoft.Testing.Platform（MTP，`dotnet test` 在 `global.json` 開啟 MTP 後走的路）的輸出格式不同，而且**會跟著系統語系變成繁中**。2026/11/05 實測（同一個必定失敗的測試）：

| 執行方式 | 輸出 | `parse_run` |
|---|---|---|
| `dotnet test`（MTP，繁中語系） | `已失敗 Test.ZzTempRedCheck.Red`、`測試回合摘要： 失敗!  總計: 369  失敗: 1` | `invalid` |
| `dotnet test`（MTP，`DOTNET_CLI_UI_LANGUAGE=en`） | `failed Test.ZzTempRedCheck.Red`、`Test run summary: Failed!  total: 369  failed: 1` | `invalid` |
| `dotnet run --project Test`（原生） | `Test.ZzTempRedCheck.Red [FAIL]`、`Total: 369, Errors: 0, Failed: 1, …` | `red` ✅ |

`invalid` 是**安全的失敗**（改實作會被擋下，不會放錯），但後果是：專案只要用 MTP 跑測試，**永遠拿不到有效紅燈，每次改實作都被擋**。結果是專案被迫只能用原生執行器，放棄 MTP 的擴充功能（覆蓋率、重試、HangDump、TRX）。

另一個既有的隱憂：xUnit 在 AppVeyor／Azure DevOps／TeamCity 環境會**自動切換輸出格式**（`Test.exe -?` 說明：「An automatically enabled reporter will override a manually selected reporter」），文字比對同樣會失效。

---

## 二、目標與非目標

**目標**
1. 用 MTP 跑測試（`dotnet test`）時，hook 能正確判定紅／綠／無效，並取得失敗測試名單（供「紅的是不是本段新測試」的稽核）。
2. 判定**不受語系、輸出排版、自動切換的報告器影響**。
3. 維持既有的安全性質：**任何判定不了的情況一律 `invalid`**，絕不猜成紅或綠。
4. 沒有開啟新功能的專案（ChipHunter、Emberstone）**行為完全不變**。

**非目標**
- 解析 MTP 的主控台文字（語系、排版都會變，見 §三）。
- Rust（cargo 沒有原生 CTRF 輸出）。
- MTP 的 server 模式（JSON-RPC，給 IDE 用，對 hook 太重）。

---

## 三、方案比較

| 方案 | 做法 | 評估 |
|---|---|---|
| A. 解析 MTP 主控台文字 | 多寫一組樣式比對中英文的 `總計／total`、`已失敗／failed` | ❌ 語系、版本、`dotnet test` 與直接執行 exe 的排版都不同；同樣受自動報告器影響。只是把問題換個地方 |
| **B. 讀 CTRF 結構化報告（建議）** | 測試指令加上產生 CTRF（JSON）報告的參數，hook 執行後讀報告判定 | ✅ 結構化、不受語系影響；**原生執行器與 MTP 產出的 CTRF 完全相同**（見下） |
| C. MTP server 模式 | hook 以 JSON-RPC 驅動測試 | ❌ hook 是被動觀察者（只看指令與輸出），改成主動執行要重寫架構 |

**CTRF**（Common Test Report Format）是跨語言的通用測試報告 JSON 格式。xUnit v3 兩個入口都能產生，2026/11/05 實測同一組測試（1 失敗、1 略過）：

```bash
dotnet run --project Test -- -result-ctrf <檔案>                                         # 原生執行器
dotnet test --project Test --report-xunit-ctrf --report-xunit-ctrf-filename <檔名> \
            --results-directory <目錄>                                                  # MTP
```

兩份報告的 `results.summary` 都是 `{tests: 2, passed: 0, failed: 1, pending: 0, skipped: 1, other: 0, start, stop, …}`；每筆測試都有 `name`（`Test.ZzTempRedCheck.Red`）、`status`（`failed`／`skipped`／`passed`）、`extra.type`（`Test.ZzTempRedCheck`）、`extra.method`（`Red`）。

→ **一個解析器同時支援兩個入口**，專案用哪個都可以。

---

## 四、設計

### 1. 設定（`.claude/tdd.json` 新增選填欄位）

```json
"test_report": { "format": "ctrf", "path": "TestResults/tdd.ctrf.json" }
```

- `path` 相對於專案根目錄。
- **沒有這個欄位 → 走原本的文字解析**（ChipHunter、Emberstone 不受影響）。
- 有這個欄位時，`test_command_regex` 應寫成要求指令帶上報告參數，例如 MSRS：
  `dotnet\s+test\s+--project\s+(\./)?Test(\.Integration)?\b.*--report-xunit-ctrf\b`

### 2. 「報告是不是這次跑出來的」——最重要的防線

讀結果檔最典型的默默判錯，是**讀到上一次留下的舊報告**（例如這次編譯失敗沒產生新檔，卻讀到上次的紅燈）。對策：

- **pre**：遇到測試指令，在 ledger 記一筆 `{"t": "RUN_START", "ts": <現在時間>}`。（hook 的 pre 目前對測試指令不做事，這是新增的。）
- **post**：報告檔的修改時間必須**晚於**最近一筆 `RUN_START`；否則視為舊檔 → `invalid`（輸出含 `error CS` 時 → `compile_fail`）。
- 不讓 hook 刪除或改名使用者的檔案（避免 hook 產生副作用）。

### 3. 判定規則（CTRF）

依序：
1. 找不到新的報告：輸出含 `error CS` → `compile_fail`；否則 → `invalid`
2. 報告不是合法 JSON，或缺 `results.summary` / `results.tests` → `invalid`
3. `tests == 0` → `invalid`（篩選沒選到任何測試）
4. `failed > 0` → `red`，失敗名單取 `extra.type + "." + extra.method`（沒有 `extra` 時用 `name`）
5. `pending + skipped + other > 0` → `invalid`（比照現行 A5／A6：有略過就不算乾淨的紅綠）
6. 其餘 → `green`

**exit code 交叉檢查**（比照 Rust 的 RA8／RA9；Rider MCP 的 `command_exit_code`）：
- `failed > 0` 但 exit code 為 0 → `invalid`
- `failed == 0` 但 exit code 非 0 → `invalid`
- 沒有 exit code → 只依報告判定

### 4. 無效原因要說得出來

`RUN` 事件新增 `reason` 欄位（例如「找不到本次的 CTRF 報告：TestResults/tdd.ctrf.json」「報告比本次執行舊」「略過 1 筆」）。之後改實作被擋時，擋下訊息帶出**最近一次無效執行的原因**，不必猜為什麼沒有紅燈。

### 5. 不變的部分

- 紅燈要落在本段新測試類別的稽核（`failed_class`、`declared_classes`）照舊；CTRF 給的是 `型別.方法`，`failed_class` 現有邏輯即可處理。
- 只記錄 Rider MCP `execute_terminal_command` 執行的測試（現行規則）。

---

## 五、case 表（待核可）

| # | 情境 | 期望 |
|---|---|---|
| M1 | 有 `test_report`，報告為本次產生：`tests>0`、`failed=0`、無略過，exit 0 | `green` |
| M2 | 同上但 `failed≥1`，exit 非 0 | `red`，`failed` ＝ 各失敗測試的 `extra.type.extra.method` |
| M3 | M2 但測試沒有 `extra` 欄位 | `red`，`failed` 改用 `name` |
| M4 | `tests=0` | `invalid` |
| M5 | `failed=0` 且 `skipped`／`pending`／`other` 任一 >0 | `invalid` |
| M6 | 報告不存在，輸出含 `error CS` | `compile_fail` |
| M7 | 報告不存在，輸出不含 `error CS` | `invalid`，原因含報告路徑 |
| M8 | 報告存在但修改時間早於本次 `RUN_START`（舊檔） | `invalid`（輸出含 `error CS` 則 `compile_fail`），原因註明「舊報告」 |
| M9 | 報告不是合法 JSON，或缺 `results.summary` | `invalid` |
| M10 | `failed>0` 但 exit 0 | `invalid` |
| M11 | `failed=0` 但 exit 非 0 | `invalid` |
| M12 | 回應沒有 exit code | 只依報告判定（M1／M2 結果不變） |
| M13 | pre：測試指令 → ledger 記 `RUN_START`；非測試指令 → 不記 | 如述 |
| M14 | 沒有 `test_report` 設定 | 走原本文字解析，A1–A8 全部維持原結果 |
| M15 | 原生執行器（`-result-ctrf`）與 MTP（`--report-xunit-ctrf`）產生的報告 | 同一解析器、同樣結果（用實測的兩份報告當測試資料） |
| M16 | 最近一次執行 `invalid` 後去改實作 | 擋下，訊息包含該次的無效原因 |

---

## 六、實作與驗證步驟

1. 使用者核可 §五 case 表。
2. 測試資料：用 2026/11/05 實測產生的兩份真 CTRF 報告（原生、MTP，各含失敗＋略過）＋人工改寫的變體（全綠、0 筆、壞 JSON、無 `extra`）。
3. 轉錄成 `test_tdd_core.py`（解析規則 M1–M12、M14–M15）與 `test_tdd_hook.py`（pre 記錄、舊檔判定、擋下訊息 M8、M13、M16）→ **跑出紅燈** → 實作 → 綠燈。既有 190 筆測試必須全綠（確認 ChipHunter／Rust 路徑不受影響）。
4. **拿掉防護會紅**：把「報告修改時間晚於 `RUN_START`」的檢查拿掉，M8 必須變紅。
5. **實機驗證**（MSRS）：設好 `tdd.json` 後，透過 Rider MCP 實際跑一次紅、一次綠、一次編譯失敗、一次沿用舊報告，檢查 ledger 的 `RUN` 事件。
6. 更新 ChipHunter／MSRS 的 CLAUDE.md 測試指令說明（ChipHunter 不改設定，只確認不受影響）。

工作量估計：0.5–1 天。

---

## 七、對 MSRS 進行中工作的影響

PR 4（`chore/xunit-v3-integration`，未 commit）目前的狀態是「`dotnet test` 走 MTP ＋ 覆蓋率擴充套件」，曾考慮改成「只用 `dotnet run`」（方向 2）來配合 hook。

本計畫完成後，**hook 能直接讀 MTP 的 CTRF 報告**，方向 2 的理由就不存在了。建議順序：

| 順序 | 工作 |
|---|---|
| 1 | PR 4 維持現狀（MTP），決定覆蓋率排除（建議不排除、刪 `coverlet.runsettings`）後送出 |
| 2 | 本計畫：hook 支援 CTRF（全域 hook，不在 MSRS repo） |
| 3 | PR 5：MSRS 的 `.claude/tdd.json`，帶 `test_report` 設定，`test_command_regex` 只認帶 CTRF 參數的 `dotnet test --project …` |

這樣 MSRS 只有一個跑測試的入口（`dotnet test`），本機、hook、CI 都一樣；`dotnet run` 仍可用，但不算 hook 的測試執行。

---

## 八、風險

| 風險 | 對策 |
|---|---|
| Rider MCP 終端機的工作目錄不是專案根目錄，`--results-directory` 相對路徑落點不同 | `path` 一律相對專案根目錄；指令範例用相對專案根目錄的路徑；實機驗證步驟 5 涵蓋 |
| 一個指令跑多個測試專案，產生多份報告互相覆蓋 | `test_command_regex` 要求 `--project`，一次只跑一個專案 |
| `extra` 是 xUnit 自訂欄位，其他框架（MSTest、NUnit）可能沒有 | M3：退回用 `name` |
| 檔案修改時間的精度、時鐘問題 | 同一台機器、hook 自己記時間。⚠️ 實作時發現：NTFS 時間戳欄位雖是 100ns，但寫入時取的是**較粗的系統時鐘**，檔案修改時間可能比前一刻 `time.time()` 還早數毫秒——單元測試「pre 後立刻寫檔」因此 40 次中偶發 3 次失敗（已修測試資料：模擬執行 1 秒）。實際測試執行至少數秒，不受影響；判定維持嚴格的「晚於」不放寬 |
| 開發者忘了加 CTRF 參數 | `test_command_regex` 要求參數存在，不符合就不算測試執行；擋下訊息（M16）說明原因 |
