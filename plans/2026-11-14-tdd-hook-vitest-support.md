# TDD hook 支援 Vitest（前端邏輯層納入 test-first 稽核）

> 狀態：**草案**（排在 PR 5 之後；PR 5 先把 `web/**` 列為 ignore，2026/11/14 決定）
> 範圍：全域 hook `~/.claude/hooks/tdd/` ＋ MSRS `web/` 的測試設定與 `.claude/tdd.json`

---

## 一、為什麼要做

PR 5 把整個前端列為 ignore，原因是 hook 看不懂 Vitest 的輸出，**不是前端不需要先寫測試**。回顧前端近期修正（2026/11/10 盤點）：

| commit | 問題 | 先寫單元測試抓得到嗎 |
|---|---|---|
| `56bb7e4` | proxy 白名單漏 `me` → `/api/Me/*` 全被擋成 403 | ✅ `proxy.test.ts` 本來就在測白名單 |
| `2a0bc5a` | 401 導向不存在的 `/login`（404） | ✅ 測 `apiClient` 的 401 處理 |
| `82d78e7` | 首頁截止 banner 誤報「已截止」 | ✅ 日期計算 |
| `f46d95a` | mutation 失敗沒提示、清單沒刷新 | 🔶 元件測試，mock 成本高 |
| `05998f5` | 帶隊頁接受後要手動重整 | 🔶 較適合 E2E |
| `a743d9f` | 截止星期下拉選單 | ❌ 純 UI |

前 3 個是**邏輯錯誤**，都是 E2E 或實際操作才發現；先寫測試的話，本機幾秒內就會紅燈。

**結論**：test-first 的效益集中在前端**邏輯層**；畫面排版交給 E2E 與人眼，不納入稽核。

---

## 二、分類（MSRS `tdd.json`）

| 分類 | 路徑 | 理由 |
|---|---|---|
| test | `web/__tests__/**` | Vitest 測試 |
| **impl（受稽核）** | `web/services/**`、`web/hooks/**`、`web/utils/**`、`web/lib/**`、`web/app/api/**`（proxy）、`web/constants/**` | 邏輯層：輸入輸出明確，測試快、穩 |
| ignore | `web/app/**`（`app/api/**` 除外）、`web/components/**`、`web/types/**`、`web/e2e/**`、樣式、設定檔 | 畫面排版、型別宣告、E2E（hook 不解析 Playwright） |

> ⚠️ 分類先後（已查 `tdd_core.classify`）：**ignore 優先於 test，test 優先於 impl**，沒有「例外」寫法。所以 ignore **不能**寫成 `web/app/**`（會連 `app/api/**` 一起吃掉），要逐一列出畫面層的檔案樣式，例如 `web/app/**/page.tsx`、`web/app/**/layout.tsx`、`web/app/**/*.css`，或列出 `app/` 底下除了 `api/` 以外的子目錄。

---

## 三、技術方案：Vitest 怎麼產出 hook 讀得懂的報告

hook 已能讀 **CTRF**（`plans/2026-11-05-tdd-hook-mtp-ctrf-support.md`），所以目標是讓 Vitest 也產出 CTRF，或讓 hook 多認一種格式。

| 方案 | 做法 | 評估 |
|---|---|---|
| A. 第三方 `vitest-ctrf-json-reporter` | `npm i -D`，在 `vitest.config.ts` 加報告器 | ⚠️ 版本 0.0.3、2025/09 後無更新、未宣告 peerDependencies（不確定支援 Vitest 4）；為了稽核引入不成熟的相依，不划算 |
| B. hook 多認 Vitest 內建 JSON 報告（`--reporter=json --outputFile=…`） | hook 新增一個解析器 | 不用加相依；但 hook 要維護第二種格式，日後 bats（JUnit）又是第三種 |
| **C. 專案內寫一個小型自訂報告器，輸出 CTRF（建議）** | `web/test-reporters/ctrf-reporter.ts`，用 Vitest 的 Reporter API 把結果轉成 CTRF | hook 維持**只認 CTRF 一種格式**；不加第三方相依；約 40–60 行。日後 bats 也可比照（轉成 CTRF），hook 不用再擴充格式 |

選 C 時，報告器本身也要有測試（它是稽核的一環，錯了會默默判錯）：用 Vitest 跑一組已知結果的測試，比對輸出的 CTRF。

---

## 四、hook 需要的改動

### 1. 一個專案多種測試指令

目前 `test_report` 只有一份設定。MSRS 會同時有 `dotnet test`（CTRF 在 `TestResults/`）與 Vitest（CTRF 在 `web/` 底下），要改成依指令對應：

```json
"test_reports": [
  { "command_regex": "dotnet\\s+test\\s+--project\\s+…--report-xunit-ctrf", "path": "TestResults/tdd.ctrf.json" },
  { "command_regex": "vitest\\s+run\\b.*--reporter",                        "path": "web/test-results/tdd.ctrf.json" }
]
```

- 維持相容：舊的單一 `test_report` 寫法照常可用（ChipHunter 不受影響）。
- pre 的 `RUN_START` 不用分指令（同一時間只會跑一個測試指令）。

### 2. 「紅的是不是本段新測試」怎麼對應

現行稽核用**類別名稱**對應：測試檔被編輯時記下宣告的類別（C# `class`、Rust `#[test] fn`），紅燈的失敗名單要有其中之一。Vitest 沒有類別，要換對應方式：

| 選項 | 做法 |
|---|---|
| **以測試檔路徑對應（建議）** | CTRF 每筆測試有 `filePath`；編輯測試檔時記下檔案路徑，紅燈的失敗測試要有一筆落在本段編輯過的測試檔 |
| 以 `describe` 名稱對應 | 解析 TS 原始碼抓 `describe('…')`，脆弱 |

`declared_classes` 需要新增 `language: "typescript"`（或依副檔名判斷）的分支。

### 3. 編譯／轉譯失敗

Vitest 轉譯 TS 失敗時不會產出測試結果 → 沒有新報告 → 現行規則判 `invalid`，不是 `compile_fail`。要決定：是否辨識 Vitest 的轉譯錯誤訊息當作 `compile_fail`（讓「新測試引用不存在的模組 → stub」這條流程能用）。

---

## 五、待定問題（實作前要拍板）

1. 方案 C（自訂報告器）是否採用？
2. 對應方式：以測試檔路徑對應是否可以？
3. Vitest 轉譯失敗要不要算 `compile_fail`（影響 stub 流程能不能用在前端）？
4. 前端畫面層要不要另外訂「至少要有 E2E」的約定（不由 hook 稽核）？

---

## 六、步驟（定案後）

1. 定案 §五，寫 case 表（比照 CTRF 計畫的 M 組，前綴 V 或 W）→ 使用者核可
2. 自訂報告器＋其測試（MSRS `web/`）
3. hook：`test_reports` 多組設定、TS 測試檔路徑對應、（視 §五-3）轉譯失敗判定 → 先紅後綠；既有測試全綠
4. MSRS `tdd.json` 加入 `web/` 分類與 Vitest 指令
5. 實機驗證：Rider MCP 跑 Vitest 紅／綠／轉譯失敗／舊報告

工作量估計：1–1.5 天。
