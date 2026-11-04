# JWT 續期路徑重新設計：隱式驗簽 → 顯式驗簽

> 建立：2026/11/04｜狀態：**🔶 大部分已被取代，僅 §四 A 組仍有效**
> 範圍：`AuthenticationMiddleware:76-100`、`JwtService`、`IJwtService`

## 🔶 取代說明（2026/11/04 決議）

本計畫的**主體（`ValidateIgnoringLifetime` ＋ 改寫內聯續期）已被取代**：
`plans/2026-11-04-refresh-token-and-reconnect-resync.md`（階段 3）會把續期整個搬到明確端點、**移除中介層內聯續期**，屆時 `ReadJsonWebToken` 直接刪除 → 不需要 `ValidateIgnoringLifetime`。

**仍然有效且已搬入階段 3（作為其階段 0.5）的部分：**

> **§四 A 組 —— `JwtService` 真實驗簽行為測試。**
> 理由與續期架構無關：現有測試 `InvalidSignature_DoesNotRefresh` **mock 掉 `ValidateToken`**，直接「規定」簽章錯誤會回 signature 例外，而**真實實作到底回什麼例外從未被測試證明**（與 session 冒用漏洞同一形態的 mock drift）。
> 階段 3 的 refresh 端點要用「過期但驗簽有效」自證，**那個前提必須被測試釘住，不能靠假設**。

本檔保留作為設計記錄（含 §二 mock drift 的分析與 §七 面試講法），**不單獨執行**。

> 關聯：`2026-11-04-refresh-token-and-reconnect-resync.md`（取代者）、`2026-11-04-split-authentication-authorization.md`（階段 0，根因修法）、`2026-11-04-authorization-matrix.md`（護欄）

---

## 一、先講清楚：**現在不是漏洞**

### 現況為什麼安全
`JwtService.ValidateToken` 用 `JsonWebTokenHandler.ValidateTokenAsync`，而 Microsoft.IdentityModel 內部是
**先驗簽章 → 再驗 payload（lifetime / issuer / audience）**。

所以 middleware 第 78 行的條件：
```csharp
if (!validateTokenResult.IsValid && validateTokenResult.Exception is SecurityTokenExpiredException)
```
只有在**簽章已經通過**的情況下才會成立 → 第 80 行 `ReadJsonWebToken` 讀出的 `DiscordId` **確實可信**。

### 基線比想像中好——已經有的保護
| 既有測試 | 守住什麼 |
|---|---|
| `AuthenticationMiddlewareTests.InvalidSignature_DoesNotRefresh` | 簽章錯誤**不進續期**，且明確 `Verify(ReadJsonWebToken, Times.Never)` |
| `ExpiredJwt_RefreshesAndEnforcesNewRole`（Theory ×3） | 續期後**仍執行角色檢查**（`60faf7b` 的回歸，已存在） |
| `ExpiredJwt_RefreshFailureOrInvalidResult_Returns401`（含 `wrong-account`） | 續期失敗 / 回錯帳號 → 401 |
| `AuthService.RefreshToken` | role 來自 **Discord guild 即時查詢 + `ResolveRoleAsync`**，不是沿用舊 token → 有撤銷語意（退出公會 → 續期失敗） |

> **所以這個 plan 不是修漏洞，是消除一個隱式假設。** 優先度：中。
> ⚠️ 面試不要講成「我有個漏洞」——要講成「**我發現安全性建立在一個沒被測試證明的假設上**」。

---

## 二、★ 真正的問題：mock drift（跟 session 漏洞同一形態）

`InvalidSignature_DoesNotRefresh` 這個測試是這樣寫的：

```csharp
_jwt.Setup(j => j.ValidateToken("forged"))
    .Returns(new JwtValidationResult { Exception = new SecurityTokenInvalidSignatureException() });
```

它 **mock 掉 `ValidateToken`**，直接「規定」簽章錯誤會回 `SecurityTokenInvalidSignatureException`。

> 🔴 **但「真實的 `JwtService` 對一個簽章錯誤的 token 到底回哪個例外」，沒有任何測試證明。**

`Test/JwtServiceTests.cs` 目前有：
- ✅ 新鮮 token → valid
- ✅ 過期 token → invalid
- ✅ **畸形**字串（`"not-a-valid-token"`）→ invalid
- ❌ **結構正確但用錯誤金鑰簽的 token** → **沒有測**

**這正是 session 冒用漏洞的同一個病**：單元測試 mock 掉下游，mock 說的跟現實是否一致，從沒被驗證。

若 Microsoft.IdentityModel 某次升版把簽章失敗的例外改成別的型別（或改成先驗 lifetime）：
- 真實行為：簽章錯誤的 token 會丟 `SecurityTokenExpiredException`（若它也過期）→ **進入續期路徑** → `ReadJsonWebToken` 不驗簽 → **用攻擊者指定的 discordId 發出合法 JWT**
- 而 `InvalidSignature_DoesNotRefresh` **仍然綠的**，因為它 mock 了

→ **這是這個 plan 最重要的一條：先把「真實驗簽行為」釘住。**

---

## 三、設計方案

### 方案比較

| 方案 | 內容 | 評估 |
|---|---|---|
| **A. 顯式驗簽（建議）** | 新增 `ValidateIgnoringLifetime`：`ValidateLifetime = false`、其餘全驗。續期前用它取得**驗簽過的** `DiscordId`。刪除 `ReadJsonWebToken` | ✅ 小改動、保證變顯式、移除誤用來源 |
| B. 維持現狀 ＋ 只補測試 | 只補「真實驗簽」測試，不改程式 | ⚠️ 假設仍然隱式；`ReadJsonWebToken` 這把槍還留在專案裡 |
| C. 真正的 refresh token | 新表儲存可撤銷的長效 token ＋ rotation | ❌ 需新表、撤銷、輪替機制。而且現況已有撤銷語意（Discord guild 查詢）。**收益 < 成本**，不做 |

**選 A。** 理由：改動面極小（`ReadJsonWebToken` 在**正式程式只有 1 個呼叫點**），而換到的是「安全性不再依賴函式庫的例外型別與驗證順序」。

### 改動後的 middleware 流程

```
cookie 有 jwtToken
  ↓
ValidateToken(token)
  ├─ IsValid → 用它的 DiscordId / Role 建 identity
  └─ 不 valid
      └─ 是 SecurityTokenExpiredException？
          ├─ 否 → 401
          └─ 是 → ValidateIgnoringLifetime(token)        ← ★ 顯式驗簽
                  ├─ 失敗 → 401                            （防禦縱深：萬一順序變了也擋得住）
                  └─ 成功 → DiscordId 可信
                            → RefreshToken(discordId)
                            → 新 token 再 ValidateToken
                            → 仍比對 DiscordId 一致（保留）
                            → 寫 cookie、用新 token 的 role 走角色檢查
```

### 設計決定

**① 保留第 85 行的 `refreshed.DiscordId != claims.DiscordId` 比對**
改用顯式驗簽後，新舊 `DiscordId` 理論上必然相同（`RefreshToken` 是用舊的發的）。
**但仍保留**：這是防「`RefreshToken` 實作被改錯、回了別人帳號」的縱深防禦，成本是一個整數比較。
> 原則：**不要為了精簡刪掉安全檢查。** 現有測試 `wrong-account` case 也靠它。

**② 刪除 `IJwtService.ReadJsonWebToken`**
正式程式唯一呼叫點是 middleware:80，改完即無人使用。
留著等於在專案裡留一把「不驗簽就讀 claim」的槍——下一個人（或 AI）很可能在別處誤用。

**③ 修掉 claim 讀取的 `null → 0`**
`JwtService:74` 與 `:89` 都是 `Convert.ToUInt64(…?.Value)`，缺 claim 時 **回 0 而非拋例外**。
改成：缺少 `discordId` claim 或無法解析 → **視為驗證失敗**（`IsValid = false`），不要回 0。
> 目前不可利用（我們自己發的 token 一定有這個 claim，且 `RefreshToken(0)` 查不到 player → 落到 401），但是脆弱寫法。

**④ 不在範圍內：Discord API 在認證熱路徑**
`AuthService.RefreshToken` 每次都打 `GetGuildMemberAsync`。
這是真實的延遲 / 速率限制風險，但屬**效能與可用性**議題，另開 plan。

---

## 四、★ Case 表（等待核可）

> 期望值是「安全上正確的行為」，不是目前實作的行為。

### A 組 — `JwtService` 真實行為（★ 最重要：填補 mock drift）

| # | 輸入 | 期望 |
|---|---|---|
| A1 | 用**錯誤金鑰**簽的 well-formed token（未過期） | `IsValid = false`，且 `Exception` **不是** `SecurityTokenExpiredException` |
| A2 | 用**錯誤金鑰**簽的 well-formed token（**且已過期**） | `IsValid = false`，且 `Exception` **不是** `SecurityTokenExpiredException` ← **釘住「簽章先驗」這個前提** |
| A3 | 正確金鑰、**issuer 錯誤** | `IsValid = false` |
| A4 | 正確金鑰、**audience 錯誤** | `IsValid = false` |
| A5 | 正確金鑰、已過期 | `IsValid = false`，`Exception` **是** `SecurityTokenExpiredException` |
| A6 | 正確金鑰、沒有 `discordId` claim | `IsValid = false`（**不得** `DiscordId = 0`）⚠️ 預期紅 |
| A7 | 正確金鑰、`discordId` claim 是非數字字串 | `IsValid = false`（**不得丟未處理例外**）⚠️ 預期紅 |
| A8 | `null` / 空字串 | `IsValid = false`（不得 `NullReferenceException`） |

### B 組 — `ValidateIgnoringLifetime`（新方法）

| # | 輸入 | 期望 |
|---|---|---|
| B1 | 正確金鑰、**已過期** | `IsValid = true`，`DiscordId` / `Role` 正確 ← 這是它存在的理由 |
| B2 | **錯誤金鑰**、已過期 | `IsValid = false` |
| B3 | 正確金鑰、**issuer 錯誤**、已過期 | `IsValid = false` |
| B4 | 正確金鑰、**audience 錯誤**、已過期 | `IsValid = false` |
| B5 | 正確金鑰、未過期 | `IsValid = true`（忽略 lifetime 不代表拒絕有效 token） |
| B6 | 畸形 / null | `IsValid = false` |
| B7 | 正確金鑰、過期、**無 `discordId` claim** | `IsValid = false` |

### C 組 — `AuthenticationMiddleware` 續期路徑（改寫後）

| # | 情境 | 期望 |
|---|---|---|
| C1 | 過期且驗簽成功 → `RefreshToken` 回新 token（role=user）、端點無角色要求 | 200、`Set-Cookie` 有新 token、identity 用**新 token** 的 claims |
| C2 | 過期且驗簽成功 → 新 token role=user、端點要求 admin | **403**（續期**不得**跳過角色檢查）← `60faf7b` 回歸 |
| C3 | 過期且驗簽成功 → 新 token role=admin、端點要求 admin | 200 |
| C4 | **過期但 `ValidateIgnoringLifetime` 失敗**（簽章錯） | **401**，且 `RefreshToken` **Times.Never** ← ★ 新的縱深防禦 |
| C5 | 簽章錯誤（非過期例外） | **401**，`ValidateIgnoringLifetime` **Times.Never**、`RefreshToken` **Times.Never** |
| C6 | 過期、驗簽成功，但 `RefreshToken` 回 `null` / 空字串 | **401**，不得寫 cookie |
| C7 | 過期、驗簽成功，`RefreshToken` 回的新 token 驗證失敗 | **401**，不得寫 cookie |
| C8 | 過期、驗簽成功，新 token 的 `DiscordId` **與舊的不符** | **401** ← 保留的縱深檢查 |
| C9 | 無 cookie（匿名） | **401**，`ValidateIgnoringLifetime` / `RefreshToken` 皆 **Times.Never** |

### D 組 — 介面清理後的回歸

| # | 案例 | 期望 |
|---|---|---|
| D1 | `IJwtService` **不再有** `ReadJsonWebToken` | 編譯通過，全專案無呼叫點 |
| D2 | `Test/JwtServiceTests.cs` 原本用 `ReadJsonWebToken` 驗 claim 的兩條測試（`:53`、`:109`） | 改用 `ValidateToken` / `ValidateIgnoringLifetime` 驗 claim，**不得刪掉斷言** |

---

## 五、執行順序

> test-first 鐵則：**case 表核可 → 轉錄測試 → 有效紅燈 → 才改實作**。

| 階段 | 內容 | 預期顏色 |
|---|---|---|
| **1** | 轉錄 **A 組**（真實驗簽行為）到 `Test/JwtServiceTests.cs` | A1-A5、A8 **綠**（釘住現況）；**A6、A7 紅** |
| **2** | 修 `JwtService` 的 claim 讀取（缺 claim / 非數字 → `IsValid=false`） | A6、A7 轉綠 |
| **3** | 轉錄 **B 組** → `ValidateIgnoringLifetime` 不存在 → **編譯失敗** → 建 `NotImplementedException` 樁 → **跑出紅燈** | 紅 |
| **4** | 實作 `ValidateIgnoringLifetime`（複製 `ValidateToken`，`ValidateLifetime = false`） | B 組轉綠 |
| **5** | 轉錄 **C 組**（含新增的 C4/C5/C9 動線斷言）→ 紅 | 紅 |
| **6** | 改寫 `AuthenticationMiddleware` 續期段落 | C 組轉綠 |
| **7** | 刪 `IJwtService.ReadJsonWebToken` ＋ `JwtService` 實作；改 D2 兩條測試 | 全綠 |
| **8** | `dotnet format` → 全測試 → **E2E 本機跑過**（動到認證路徑，`auth.spec.ts` 必須驗） | 全綠 |

### ⚠️ 階段 3 的 TDD 注意事項
新測試引用不存在的 `ValidateIgnoringLifetime` → **先跑一次讓它編譯失敗** → 才建 `NotImplementedException` 樁 → 再跑出**真紅燈**（執行且斷言失敗）→ 才實作。
**直接從樁跳到綠 = 違規。**

### 驗收標準
- [ ] **A2 是這個 plan 的核心**：真實的 `JwtService` 對「簽章錯誤 ＋ 已過期」的 token 不回 `SecurityTokenExpiredException` → 證明「簽章先驗」不再只是假設
- [ ] C4 存在：即使順序假設被打破，`ValidateIgnoringLifetime` 仍會擋下 → **兩層防護**
- [ ] `ReadJsonWebToken` 全專案消失
- [ ] 每條新測試都確認「拿掉實作會紅」
- [ ] E2E `auth.spec.ts` 綠（本機 Docker 跑過，不是只靠 CI）

### 不在範圍
- Discord API 在認證熱路徑的延遲 / 速率限制（另開）
- 真正的 refresh token ＋ 撤銷機制（方案 C，不做）
- JWT 15 分鐘有效期內無法撤銷 — **已知限制**，接受（role 變更最多延遲 15 分鐘生效）
- 授權矩陣 E2E（見 `2026-11-04-authorization-matrix.md`）

---

## 六、對授權矩陣 plan 的影響

`2026-11-04-authorization-matrix.md` 的 §四之二 措辭要校準：

| 原措辭 | 改成 |
|---|---|
| 「把未驗證的輸入當身分使用」 | 「**保證是隱式的**——『簽章驗過了』是從例外型別推論出來的，程式碼沒有一行宣告它」 |
| 「設計是脆弱的」（三點並列） | 主要論點是**隱式保證 ＋ mock drift（真實驗簽行為沒被測）**；依賴驗證順序是次要、風險低 |

另外 G8.3 / G8.4（E2E 層的簽章錯誤 / role 竄改）**仍然要做**——
它們測的是「**端到端對外行為**」，跟本 plan 的 A 組（`JwtService` 單元真實行為）互補：
- A 組：真實的驗簽**實作**是否如預期
- G8：**整條管線**串起來後，攻擊者拿不到任何東西

---

## 七、面試可講的點

> 我在自己的認證中介層發現一件事：JWT 過期要續期時，程式會從**過期的 token** 讀出使用者 ID 去換新 token。
>
> 乍看危險，但其實安全——因為函式庫是**先驗簽章、再驗有效期**，所以「拿到過期錯誤」本身就隱含「簽章是對的」。
>
> **問題是這個保證是隱式的**：程式碼沒有任何一行宣告「我驗過簽章」，它是從**例外型別**推論出來的。而且更糟的是——我們有一個測試叫 `InvalidSignature_DoesNotRefresh`，但它 **mock 掉了驗證器**，直接規定「簽章錯誤會回 signature 例外」。**真實的實作到底回什麼例外，從來沒有測試證明過。**
>
> 這跟我之前抓到的 session 漏洞是**同一個形態**：mock 說的跟現實是否一致，沒有人驗證。
>
> 所以我的做法是：① 先補「真實驗簽」的測試，釘住「簽章錯誤 ＋ 已過期 → 不會回 expired 例外」這個前提；② 再把隱式保證改成顯式——續期前用「忽略有效期但仍驗簽」重驗一次，讓使用者 ID 來自可信來源；③ 順手刪掉那個「不驗簽就讀 claim」的方法，免得下一個人在別處誤用。
>
> 這樣安全性就只依賴「簽章是我們發的」這個明確事實，**不再依賴第三方函式庫的驗證順序**——而那個順序不是公開契約，升版可能改。
