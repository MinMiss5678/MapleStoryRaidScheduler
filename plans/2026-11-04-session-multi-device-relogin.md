# 管理員重新登入迴圈 ＋ session 快取汙染

> 建立：2026/11/04｜狀態：**✅ 修法與 case 表已核可（2026/11/04）｜⏸ 等 TDD hook 啟用（PR 5）後才實作**
> 範圍：`Infrastructure/Services/SessionService.cs`、`Test/SessionServiceTests.cs`、`web/e2e/authorization-matrix.spec.ts`
> 主序位置：**PR 5b**（`.claude/tdd.json` 啟用之後、階段 0 之前）——見 `2026-11-04-refresh-token-and-reconnect-resync.md` §七

---

## 一、問題

### 1. 管理員重新登入會卡在登入迴圈（最長 15 分鐘）

`SessionService.GetAsync` 的 Redis 快取**以 discordId 為 key**，一個使用者只存得下一筆 session；
但資料庫允許**同一使用者有多筆 session**（每登入一次就新增一筆）。兩邊對不上：

```
電腦登入 → S1；第一個請求 → 快取[discordId] = S1
筆電登入 → S2（資料庫兩筆都有效）
筆電請求 → 快取命中 S1 ≠ S2 → 直接回 null → 401
        → 前端 apiClient 收到 401 導回首頁 → 再登入 → S3 → 又 401……
```

直到快取過期（`SessionPolicy.CacheFreshness` = 15 分鐘）才會恢復。換瀏覽器、清 cookie 後重新登入都會觸發。
只影響管理員（一般玩家走 JWT，不經 session 快取）。

**這是 `c0e0311` 修補的副作用**：修補把「快取命中就放行」改成「快取命中但 sessionId 不符就拒絕」，
擋住了偽造的 sessionId，但也一併擋住了**同一人另一個合法的 session**——光看快取分不出兩者。

### 2. 快取可以被汙染（既有問題，嚴重度低）

快取**未命中**時，`SessionQuery.GetAsync(sessionId)` 只用 sessionId 查，**沒有核對這筆 session 是否屬於 cookie 裡的 discordId**，
查到後就寫進 `快取[cookie 的 discordId]`。

→ 管理員 X 送「cookie 的 discordId ＝ 管理員 Y ＋ X 自己的 sessionId」，就能把 X 的 session 寫進 Y 的快取位置，
Y 之後的請求全部 sessionId 不符 → 401（阻斷服務）。

不會提權：`AuthenticationMiddleware` 用 `session.DiscordId` 決定身分，不是 cookie 的 discordId。
而且只有管理員才有 session，所以攻擊者必須本身是管理員。

---

## 二、修法（已核可：方案 A-2）

| 項目 | 決定 |
|---|---|
| 快取命中、sessionId 不符 | **不直接拒絕，改用 sessionId 查資料庫**；存在、未過期、且屬於同一 discordId 才放行 |
| 不符時確認有效後，要不要把快取換成新的 session | **不覆寫**（A-2）。快取維持原本那筆，第二台裝置每次查資料庫 |
| 加一道歸屬核對 | 查到的 `session.DiscordId` 必須等於 cookie 的 discordId，**快取命中與未命中兩條路徑都要**；不符就拒絕且不寫快取 |

### 為什麼選 A-2

- **不改產品行為**：資料庫本來就支援同一人多筆 session（`DeleteAsync` 也只刪單一 session）
- **`c0e0311` 的防護保留**：偽造的 sessionId 在資料庫查不到 → 仍然 401
- **不覆寫**：若覆寫（A-1），兩台裝置交替使用時快取會來回翻，每個請求都「查資料庫＋寫快取」，快取等於失效。
  不覆寫的代價是第二台裝置每次多查一次資料庫，以管理員的量可忽略

### 未採用：方案 B（單一 session，最後登入的有效）
登入時刪掉所有舊 session。會改變產品行為（新登入把舊裝置登出），範圍也較大。

### 登入流程本身不動
登入（`CreateSessionAsync`）只寫資料庫、不碰快取，維持原狀。快取只在之後的請求中寫入。

---

## 三、★ Case 表（✅ 已核可）

### A 組 —— `SessionService.GetAsync`（單元測試，`Test/SessionServiceTests.cs`）

| # | 情境 | 期望 |
|---|---|---|
| A1 | 快取命中、sessionId 不符、資料庫有這個 sessionId、有效、屬於同一 discordId | **回傳資料庫的 session** ⚠️ 預期紅（目前回 null） |
| A2 | 快取命中、sessionId 不符、資料庫查無 | null（守 `c0e0311`；既有測試 `GetAsync_快取命中_sessionId不符_回Null_擋session冒用`） |
| A3 | 快取命中、sessionId 不符、資料庫有但已過期 | null |
| A4 | 快取命中、sessionId 不符、資料庫有，但屬於**另一個** discordId | null ⚠️ 預期紅 |
| A5 | 快取**未命中**、資料庫有，但屬於另一個 discordId | null，且**不寫入快取** ⚠️ 預期紅（擋快取汙染） |
| A6 | 快取命中、sessionId 相符、有效 | 回快取的 session（既有行為，回歸） |
| A7 | 同 A1（不符、資料庫確認有效） | 放行，但**不寫入快取**（快取仍是原本那筆） |

### B 組 —— 端到端（`web/e2e/authorization-matrix.spec.ts`，新增 G11）

| # | 情境 | 期望 |
|---|---|---|
| B1 | 同一管理員登入兩次（S1、S2）；先用 S1 請求一次讓快取記住 S1，再用 S2 請求 | **200** ⚠️ 預期紅（目前 401） |
| B2 | 接著再用 S1 請求 | 200（兩個 session 都能用，不互相踢掉） |
| B3 | 管理員 X 用自己的 sessionId，但 cookie 的 discordId 填管理員 Y | 401，且 **Y 接下來的請求仍然 200**（快取沒被汙染） |

既有 G8.1 / G8.2（偽造 sessionId → 401）必須維持綠燈。

**為什麼兩層都要**：A 組會 mock 快取與查詢——`c0e0311` 當初就是因為 mock 才漏掉。B 組走真的 Redis 與資料庫，確認實際行為。

---

## 四、執行順序（PR 5b，在 TDD hook 治理下）

> 前置：PR 3–4（xUnit v3 遷移）＋ PR 5（`.claude/tdd.json` 啟用）完成。
> 鐵則：case 表已核可 → 轉錄成測試 → **跑出有效紅燈** → 才寫實作到綠。

| 階段 | 內容 | 預期 |
|---|---|---|
| 1 | 轉錄 A 組到 `Test/SessionServiceTests.cs` | A1、A4、A5、A7 **紅**；A2、A3、A6 綠 |
| 2 | 轉錄 B 組到 `authorization-matrix.spec.ts`（G11），用 `scripts/e2e-local.sh --grep "G11"` 跑 | B1、B3 **紅** |
| 3 | 改 `SessionService.GetAsync`：不符 → 查資料庫；兩條路徑加歸屬核對；不符路徑不寫快取 | A 組、B 組轉綠 |
| 4 | **牙齒驗證**：暫時還原成「不符就回 null」→ B1 應紅；暫時拿掉歸屬核對 → B3 應紅；還原 | — |
| 5 | 更新 `authorization-matrix.spec.ts` 開頭的註解：「同一 discordId 同時只能有一個活躍 session」不再成立（身分 id 仍維持每次執行唯一，避免平行測試互相干擾） | — |
| 6 | `dotnet format` → 全部單元／整合測試 → `scripts/e2e-local.sh` 完整套件 | 全綠 |

### 驗收標準
- [ ] A1、B1：第二台裝置的合法 session 可用（迴圈消失）
- [ ] A2 與既有 G8.1/G8.2：偽造 sessionId 仍然 401（`c0e0311` 不退化）
- [ ] A4、A5、B3：歸屬不符一律拒絕，且快取不被汙染
- [ ] A7：不符路徑不寫快取
- [ ] 牙齒驗證：拿掉修正 → 對應測試變紅
