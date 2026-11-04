# 認證與授權分離（階段 0）

> 建立：2026/11/04｜狀態：**⏳ case 表待核可**｜預估 **0.5 天**
> 範圍：`Presentation.WebApi/Middleware/AuthenticationMiddleware.cs`、新增 `RoleAuthorizationMiddleware`、`Program.cs` 管線註冊
> 關聯：`2026-11-04-refresh-token-and-reconnect-resync.md`（階段 3，**本計畫是它的前置**）、`2026-11-04-authorization-matrix.md`（護欄）

---

## 一、根因（git diff 驗證過，非推測）

MSRS 的授權層出過三次問題。其中兩次是**同樣三行、同一位置**：

```diff
-                    context.User = new ClaimsPrincipal(identity);
-                    await next(context);
-                    return;
```

| commit | 日期 | 內容 |
|---|---|---|
| `0ed4125` | 2026/07/10 | 刪掉上面那三行（＋授權改為比對 role claim） |
| `f0f32fb` | — | 重構「JWT 改真無狀態、role 寫入 token claim」→ **把那三行帶回來了** |
| `60faf7b` | 2026/09/29 | **再刪一次**同樣三行 |

`60faf7b` 的 commit 訊息寫明了攻擊：

> 原本 JWT 過期 → RefreshToken 成功後直接 `await next()` 並 return，跳過下方 AuthorizeRole 檢查：
> **一般使用者帶過期 JWT 即可打 admin 端點**（每次重送同一過期 token 都會重走續期分支）。

### 兩種耦合

| 耦合 | 內容 | 修在 |
|---|---|---|
| **語意耦合** | 授權決策**就是**認證方式：`roleAttribute != null && identity.AuthenticationType != "session"` → 403。意思是「用 session 登入的就是管理員」 | `0ed4125` |
| **結構耦合** | 授權檢查在**認證函式的尾端**。任何**認證成功**的路徑只要自己呼叫 `await next()` 並 `return`，就繞過授權 | `0ed4125` → **復發** → `60faf7b` |

### ⚠️ 不是「early return 太多」
`AuthenticationMiddleware` 在授權檢查（L120-125）之前有五個 `return`，但**它們全部是正確的**：

| 行 | 情境 | 為什麼正確 |
|---|---|---|
| L30 | `AllowAnonymous` | 端點公開，刻意不做授權 |
| L45 | sessionId 為空 → 401 | **認證失敗**，不可能也不應該授權 |
| L52 | session 查無 → 401 | 同上 |
| L87 | 續期結果無效／帳號不符 → 401 | 同上 |
| L116 | 無任何 claims → 401 | 同上 |

**bug 的形狀不是「認證失敗 → 跳過授權」（那是對的），而是「認證成功 → 跳過授權」。**

---

## 二、為什麼拆開不是「更小心」，而是「結構上不可能」

> **下游 middleware 無法被上游的 early return 跳過。**

| | 授權在同一函式尾端（現況） | 授權在下游 middleware |
|---|---|---|
| 新增一條認證成功分支 | **必須記得讓它落到底** | 怎麼寫都繞不過 |
| 分支裡 `await next(); return;` | **跳過授權** 🔴 | **授權仍然執行** ✅ |
| 分支裡直接 `return`（不呼叫 next） | 請求結束 | 請求結束（fail-closed） |
| 忘記檢查 | **可能** | **不可能** |

### ★ 殺手論據
**那三行 bug 的程式碼，在「授權在下游」的結構下，完全無害**——因為 `await next(context)` **就是在呼叫授權 middleware**。

同樣的程式碼、同樣的直覺、同樣的疏忽 → **零影響**。

`0ed4125` 與 `60faf7b` 的修法都是「**記得不要自己 next()**」。那是紀律修補，所以九週後被一次重構帶回來。**拆開才能讓它不可能復發。**

### 旁證
ASP.NET Core 本身就把這兩件事分成 `UseAuthentication()` 與 `UseAuthorization()` 兩個 middleware，正是為了這個理由。合在一起等於把框架幫你擋掉的坑又挖回來。

### 時機
**階段 3（refresh token）會大幅改寫這個 middleware**（移除內聯續期）——而歷史顯示「大改這個函式」正是那三行復發的條件（`f0f32fb` 就是）。
→ **先拆，再動續期。**

---

## 三、設計

### 現況管線（`Program.cs:322-327`）
```
ExceptionHandler → Idempotency → AuthenticationMiddleware（認證＋授權） → RateLimiter → UnitOfWork → Controllers
```

### 改成
```
ExceptionHandler → Idempotency → AuthenticationMiddleware（只認證） → RoleAuthorizationMiddleware（只授權） → RateLimiter → UnitOfWork → Controllers
```

### 職責切分

| | `AuthenticationMiddleware` | `RoleAuthorizationMiddleware`（新） |
|---|---|---|
| 做什麼 | 讀 cookie → 驗 session／JWT → 建 `ClaimsIdentity` → 寫 `context.User` | 讀 `AuthorizeRoleAttribute` → 比對 `context.User` 的 role claim |
| 可以回的狀態碼 | **只有 401** | **只有 403**（＋防禦性 401，見下） |
| 還讀 `AuthorizeRoleAttribute` 嗎 | ❌ **不再讀**（刪掉 L36 與 L120-125） | ✅ |

### 設計決定

**① `AuthenticationMiddleware` 絕不回 403**
拆開後它沒有任何理由知道「角色」這件事。這條規則會寫成測試（見 case C1），把職責邊界變成可執行的斷言。

**② `RoleAuthorizationMiddleware` 要自己處理 `AllowAnonymous`**
否則公開端點會被要求角色。做法與現況一致：`endpoint?.Metadata.GetMetadata<IAllowAnonymous>()` 非 null → 直接 `next()`。

**③ 防禦性 401：授權 middleware 發現沒有身分時回 401，不要放行**
理論上認證 middleware 已經攔掉了（無 claims → 401），所以這條到不了。但**fail-closed**：萬一日後有人在認證端加了「不設 `context.User` 也 `next()`」的路徑，授權端要擋住，而不是讓請求穿過去。

**④ 維持現有的 `AuthorizeRoleAttribute`，不遷移到框架的 `[Authorize]` ＋ policy**
框架版要為「session cookie ＋ JWT 混合」寫自訂 `AuthenticationHandler`，不成比例。
**拿到 fail-safe 性質只需要「拆開」，不需要換框架機制。**

**⑤ 註冊順序不可顛倒**
`RoleAuthorizationMiddleware` 必須在 `AuthenticationMiddleware` **之後**（要讀 `context.User`）、`RateLimiter` **之前**（被擋的請求不該消耗限流配額）。

---

## 四、★ Case 表（⏳ 待核可）

> 期望值是「安全上正確的行為」，不是目前實作的行為。
> 測試位置：`Test/AuthenticationMiddlewareTests.cs`（改寫）＋ `Test/RoleAuthorizationMiddlewareTests.cs`（新增）

### A 組 — `RoleAuthorizationMiddleware`（新）

| # | 情境 | 期望 |
|---|---|---|
| A1 | 端點無 `AuthorizeRoleAttribute`、`context.User` 有身分 | **放行**（`next` 被呼叫） |
| A2 | 端點要求 `admin`、`context.User` role claim = `admin` | **放行** |
| A3 | 端點要求 `admin`、role claim = `user` | **403**，`next` **不**被呼叫 |
| A4 | 端點要求 `admin`、role claim **為空字串** | **403** |
| A5 | 端點要求 `admin`、**完全沒有 role claim** | **403** |
| A6 | 端點標 `AllowAnonymous`、無身分 | **放行**（不得 401／403） |
| A7 | 端點標 `AllowAnonymous` 且同時有 `AuthorizeRoleAttribute` | **放行**（`AllowAnonymous` 優先，與現況一致） |
| A8 | **`context.User` 沒有任何身分**、端點要求 `admin` | **401**（防禦性 fail-closed，不得放行）⭐ |
| A9 | `AuthorizeRoleAttribute` 的 `Roles` 為空陣列 | **放行**（與現況 `Roles.Length > 0` 判斷一致） |
| A10 | 端點要求多個角色之一、role claim 符合其中一個 | **放行** |

### B 組 — `AuthenticationMiddleware`（改寫後）

| # | 情境 | 期望 |
|---|---|---|
| B1 | 有效 session cookie | 建立身分（`discordId` ＋ role claim）、**放行** |
| B2 | 有效 JWT | 建立身分、**放行** |
| B3 | 無任何 cookie | **401** |
| B4 | sessionId 為空字串 | **401** |
| B5 | session 查無 | **401** ＋ 刪除 cookie |
| B6 | JWT 簽章錯誤 | **401** |
| B7 | JWT 過期 → 續期成功 | 建立身分（用**新 token** 的 claims）、寫 cookie、**放行**（此行為在階段 3 會改，本階段維持） |
| B8 | JWT 過期 → 續期失敗 | **401** |
| B9 | JWT 過期 → 續期後帳號不符 | **401** |
| B10 | `AllowAnonymous` 端點 | **放行**，不嘗試認證 |

### C 組 — 職責邊界（★ 防止復發）

| # | 情境 | 期望 |
|---|---|---|
| **C1** | **任何**輸入組合下，`AuthenticationMiddleware` 的回應狀態碼 | **絕不是 403** ⭐ 把職責邊界變成可執行斷言 |
| **C2** | 端點要求 `admin`、帶 role=`user` 的**有效** JWT，**只跑認證 middleware** | **放行**（200）—— 證明認證端已不再做授權 |
| **C3** | 同 C2，但**跑完整管線**（認證 → 授權） | **403** —— 證明授權在下游生效 |
| **C4** | 端點要求 `admin`、帶 role=`user` 的**過期** JWT（續期後仍為 user），跑完整管線 | **403** ⭐ `60faf7b` 的回歸：續期不得繞過授權 |
| **C5** | 認證 middleware 在成功路徑上呼叫 `next()` 之後 | 下游授權 middleware **仍然執行**（結構保證）⭐ |

> **C4 ＋ C5 是本計畫的核心驗收**：C4 證明行為正確，C5 證明**結構上不可能再錯**。

### D 組 — 管線註冊

| # | 情境 | 期望 |
|---|---|---|
| D1 | `Program.cs` 的 middleware 順序 | 認證 **在** 授權之前、授權 **在** `UseRateLimiter()` 之前 |
| D2 | `RoleAuthorizationMiddleware` 的 DI 註冊 | 已在 `ServiceCollectionExtensions` 註冊（`IMiddleware` 需要）|

### E 組 — 端到端回歸（既有 E2E，不新增）

| # | 案例 | 期望 |
|---|---|---|
| E1 | `web/e2e/auth.spec.ts` 等 11 支 | 全綠（行為不得改變） |
| E2 | 授權矩陣 **G1/G2/G8**（PR 2 先建立） | 全綠 ⭐ 這就是本階段的護欄 |

---

## 五、執行順序

> **本計畫在 TDD hook 治理下**（PR 6，hook 已於 PR 5 啟用）。
> 鐵則：case 表核可 → 轉錄測試 → **跑出有效紅燈** → 才寫實作到綠。
> 新測試引用不存在的 `RoleAuthorizationMiddleware`：**先跑一次讓它編譯失敗** → 才建 `NotImplementedException` 樁 → 跑出真紅燈 → 才實作。**從樁直接跳綠＝違規。**

| 階段 | 內容 | 預期顏色 |
|---|---|---|
| 1 | 轉錄 A 組 → `RoleAuthorizationMiddleware` 不存在 → **編譯失敗** → 建樁 → **紅燈** | 紅 |
| 2 | 實作 `RoleAuthorizationMiddleware` | A 組綠 |
| 3 | 轉錄 C 組（職責邊界）→ 紅（目前認證端仍會回 403） | 紅 |
| 4 | 從 `AuthenticationMiddleware` 刪除 L36（讀 attribute）與 L120-125（403 判斷） | C 組綠 |
| 5 | 改寫 B 組既有測試（移除對 403 的斷言） | B 組綠 |
| 6 | `Program.cs` 註冊 ＋ DI；D 組驗證 | 綠 |
| 7 | `dotnet format` → 全測試 → **本機 Docker 跑 E2E**（動到認證管線） | 全綠 |

### 驗收標準
- [ ] **C1**：`AuthenticationMiddleware` 絕不回 403 —— 職責邊界有測試守住
- [ ] **C4**：帶過期 JWT 的一般使用者打 admin 端點 → 403（`60faf7b` 回歸）
- [ ] **C5**：認證端呼叫 `next()` 後，授權仍執行 —— 結構保證有測試
- [ ] 既有 11 支 E2E 全綠（**行為零變化**）
- [ ] 授權矩陣 G1/G2/G8 全綠
- [ ] 每條新測試都確認「拿掉實作會紅」

### 不在範圍
- 遷移到框架的 `[Authorize]` ＋ policy（設計決定 ④）
- 移除內聯續期（階段 3）
- 資源層級的所有權授權（IDOR）——那在 service 層，見授權矩陣 G3/G6
