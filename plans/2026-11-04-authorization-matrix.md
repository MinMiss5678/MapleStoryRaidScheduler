# 授權矩陣測試（Authorization Matrix）

> 建立：2026/11/04｜狀態：**✅ case 表已核可**（2026/11/04）
> **執行切分（2026/11/04 決議）**：
> - **PR 2（先行，護欄）**：**G1 / G2 / G8 / G10** 以 Playwright E2E 實作，**不受 hook 治理 → 不必等 xUnit v3 遷移**。目的是在改認證主幹（階段 0、階段 3）**之前**先立起端到端斷言
> - **PR 13（階段 3 之後）**：G3 / G4 / G5 / G6 / G7 / G9 以 .NET 整合測試實作（`WebApplicationFactory`），受 hook 治理
> - 完整 PR 順序見 `2026-11-04-refresh-token-and-reconnect-resync.md` §七
> 目標：把「身分 × 端點 → 該被允許還是拒絕」做成 CI 閘門，取代一次性滲透測試。

---

## 一、為什麼做這個

這個系統的**授權層已經出過三次問題**，全部在同一個區域：

| commit | 漏洞 |
|---|---|
| `8f3cae4` + `c0e0311` | `SessionService` 快取命中**沒比對 sessionId** → session 冒用 |
| `0ed4125` | 管理員授權沒比對 role claim，**refresh 路徑繞過授權** |
| `60faf7b` | **JWT 過期續期後跳過角色授權** → 越權 |

三次都在授權路徑，不是運氣問題，是結構問題：

- **兩套認證**：JWT（玩家）＋ SessionId cookie（管理員）
- **多種角色**：匿名 / 玩家 / 隊長 / 管理員
- **多條路徑**：正常驗證 / JWT 過期續期 / session 快取命中
- **組合爆炸**，而目前只有 4 個端點有宣告式的 `[AuthorizeRole("admin")]`，**其餘「誰能動什麼」全靠 service 層自己判斷** → 沒有統一的攔截點，就沒有統一的保證

### 為什麼不用現成工具
| 工具 | 為什麼不選 |
|---|---|
| Burp Pro | $449/年，核心價值（Autorize）Community 版就有，但**手動、一次性、不進 CI** |
| OWASP ZAP Access Control | 要另外維護權限矩陣設定檔，且仍是外部工具 |
| CodeQL | 已在 CI（`codeql.yml`）。但「少了一個授權判斷」是**缺少的業務邏輯**，SAST 結構性抓不到（OWASP API #1 BOLA） |
| 外部委託 | 個人專案不成比例 |

> **一次性滲透測試只能證明「那天沒問題」；進 CI 的授權矩陣能證明「每次改動後都還沒問題」。**
> 而且地基已經有了：`web/e2e/`（Playwright 11 支）＋ `/api/test/login` 測試登入後門。

---

## 二、技術方案

### ⚠️ 更正（2026/11/04）：`WebApplicationFactory` 其實已經存在

本計畫原先寫「`Test.Integration/` **沒有** `WebApplicationFactory`，要測 middleware 得拉整個 host＝新基礎設施」——**那是錯的**。

- `Test.Integration/RateLimiterIntegrationTests.cs` **已經在用** `WebApplicationFactory`
- `Presentation.WebApi/Program.cs:331` 還特地留了 `public partial class Program;`，註解寫「讓整合測試（`WebApplicationFactory<Program>`）能參照到 top-level Program 類別」

→ 所以「.NET 整合測試」是**可行**選項，而且在某些案例上**更好**（見下）。

### 選定：**分兩個層次**，各取所長

| 層次 | 檔案 | 測什麼 | 受 TDD hook 治理 |
|---|---|---|---|
| **① Playwright E2E**（**PR 2 先行，當護欄**） | `web/e2e/authorization-matrix.spec.ts` | **整條真實鏈路**：瀏覽器 → Next proxy → 後端。G1 / G2 / G8 / G10 | ❌（`web/**` 列 ignore） |
| **② .NET 整合測試**（階段 3 之後） | `Test.Integration/AuthorizationMatrixIntegrationTests.cs` | 不經瀏覽器與 proxy 的細緻案例：G3 / G4 / G5 / G6 / G7 / G9 | ✅ |

**為什麼護欄用 Playwright 先行**：它**不受 hook 治理**，所以**不必等 xUnit v3 遷移**，可以立刻寫；而護欄的價值就是「在改認證主幹之前先立起來」。而且它順便成為 **xUnit v3 遷移本身的回歸保護**（遷移不該改變任何行為，E2E 綠即證明）。

**為什麼細緻案例改用整合測試**：更快、不經 proxy（排除「403 來自路由而非授權」的歧義）、而且能用 `WebApplicationFactory` 覆寫環境（直接解決 G9.1，見下）。

### ⚠️ 關鍵陷阱：不要經過 Next.js proxy

現有 `helpers/auth.ts` 用的是相對路徑 `/api/test/login`，**會走前端 proxy**。
而 proxy 有白名單（歷史 bug：白名單漏 `me` → `/api/Me/*` 全 403）。

> **如果矩陣經過 proxy，一個 403 可能來自 proxy 路由，不是後端授權** → 測試會「假通過」，完全失去意義。

**作法**：
1. **主矩陣直打後端** `http://e2e-backend:5230`（compose 網路內）或對外埠，繞過 proxy
2. **另加少量 proxy 白名單案例**：經前端打 `/api/Me/*` 等，斷言**不被 proxy 擋** ← 防那個歷史 bug 回歸

這兩層測的是不同東西，不能混。

### 身分怎麼取得

| 代號 | 身分 | 取得方式 | 可行性 |
|---|---|---|---|
| `ANON` | 匿名 | 不帶 cookie | ✅ |
| `USER_A` | JWT 玩家 A | test-login `role:'user'`, discordId 900001 | ✅ |
| `USER_B` | JWT 玩家 B | test-login `role:'user'`, discordId 900002 | ✅ |
| `ADMIN` | Session 管理員 | test-login `role:'admin'`, discordId 900003 | ✅ |
| `SESSION_FORGED` | cookie `discordId`=ADMIN 的 id ＋ `sessionId{id}`= 亂數 UUID | 手動種 cookie（`request.newContext`） | ✅ |
| `JWT_FORGED_SIG` | 用錯誤金鑰自簽的 JWT | 測試端自簽 | ✅ |
| `JWT_ROLE_TAMPERED` | 把 ADMIN 的 role claim 改成 `admin`，簽章因此失效 | 改 payload 重編碼 | ✅ |
| `SESSION_EXPIRED` | 已過期的 session | 需直接改 DB 或等待 | ⚠️ Phase 2 |
| `JWT_EXPIRED_VALID_SIG` | **過期但簽章正確**的 JWT | 需 test-login 支援 `ttlSeconds` 參數（＝改實作） | ⚠️ Phase 2，獨立 TDD 循環 |

> `JWT_EXPIRED_VALID_SIG` 是驗 `60faf7b`（續期後仍受角色檢查）唯一的路。
> 但它需要**改 test-login 加參數**——那是實作變更，必須走自己的「case 表 → 紅燈 → 實作」循環，不混進這次。

---

## 三、★ Case 表（等待核可）

> **期望值是「安全上正確的行為」，不是「目前實作的行為」。**
> 標 ❓ 的是**需要你裁決**的設計決定；標 ⚠️ 的是**我預期會紅**（即可能是真漏洞）。

### G1 — 匿名邊界

| # | 身分 | 動作 | 期望 |
|---|---|---|---|
| 1.1 | ANON | 所有**非** `[AllowAnonymous]` 端點（Character / Profile / TeamSlot / LfgIntent / AvailabilityOverride / Boss 寫入 / SystemConfig 寫入） | **401** |
| 1.2 | ANON | `POST /api/Auth/login` | 非 401（有自己的驗證） |
| 1.3 | ANON | `POST /api/Auth/logout` | 非 401 |
| 1.4 | ANON | `POST /api/test/login` | 200（非 Production） |
| 1.5 | ANON | `POST /api/internal/alert-mail` **不帶** `X-Alert-Secret` | **401**（controller 自驗共用密鑰，見裁決 3） |
| 1.6 | ANON | `POST /api/internal/alert-mail` 帶**錯誤的** `X-Alert-Secret` | **401** |

### G2 — admin-only 端點（唯 4 個有宣告式角色檢查）

| # | 身分 | 動作 | 期望 |
|---|---|---|---|
| 2.1 | USER_A | `POST /api/Boss` | **403** |
| 2.2 | USER_A | `PUT /api/Boss/{id}` | **403** |
| 2.3 | USER_A | `DELETE /api/Boss/{id}` | **403** |
| 2.4 | USER_A | `POST /api/SystemConfig` | **403** |
| 2.5 | ADMIN | 上述四者 | **非 403** |
| 2.6 | USER_A | `GET /api/Boss/GetAll`、`GET /api/SystemConfig` | **非 403**（讀取不限管理員） |

### G3 — Character 所有權（IDOR）⚠️ 最高風險區

前置：USER_B 建一個角色 `charB`。以 USER_A 身分操作 `charB`。

| # | 動作 | 期望 | 附帶斷言（**比狀態碼更重要**） |
|---|---|---|---|
| 3.1 | `PUT /api/Character/charB` | **403** | **`charB` 的欄位未被改動** |
| 3.2 | `DELETE /api/Character/charB` | **403** | **`charB` 仍存在** |
| 3.3 | `GET /api/Character/charB/BossClears` | **403** | **不得回傳 B 的通關數** |
| 3.4 | `POST /api/Character/charB/BossClears` | **403** | **B 的通關數未被改動** |
| 3.5 | `GET /api/Character/charB/PreferredBosses` | **403** | **不得回傳 B 的偏好** |
| 3.6 | `PUT /api/Character/charB/PreferredBosses` | **403** | **B 的偏好未被改動** |

> 📌 **預期這組會紅**，而且紅燈就是目的。
> `CharacterController` 把認證身分的 `discordId` 連同路由 `id` 傳進 service（註解寫「只能讀寫自己的角色」）。
> 但如果 service 只在 WHERE 加條件 → 影響 0 列 → controller 仍 `return Ok()` → 實際是**靜默無效果 200**。
> **裁決 1 = 一律 403** → 這組紅燈之後要改實作（service 要能區分「不屬於你」並讓 controller 回 403）。
> **統一回 403、不回 404**，避免用狀態碼做資源探測。

### G4 — 隊長專屬動作

前置：USER_A 開一隊 `teamA`（`Kind=Scheduled`）。以 **USER_B（非隊長、非成員）** 操作。

| # | 動作 | 期望 |
|---|---|---|
| 4.1 | `POST /api/TeamSlot/teamA/Invitations`（邀請） | **403** |
| 4.2 | `PUT /api/TeamSlot/teamA/Applications/{memberId}`（核准/駁回） | **403** |
| 4.3 | `POST /api/TeamSlot/teamA/TransferLeader`（發起轉移） | **403** |
| 4.4 | `DELETE /api/TeamSlot/teamA`（解散） | **403** |
| 4.5 | `GET /api/TeamSlot/teamA/Applications`（申請者名單） | **403**（他人的申請是隱私） |
| 4.6 | `GET /api/TeamSlot/teamA/Candidates`（候選人名單） | **403**（隊長的挑人工具；含他人可用時段與攻擊力） |
| 4.7 | `GET /api/TeamSlot/teamA/RecruitmentGap` | **非 403**（招募資訊，潛在申請者需要看） |
| 4.8 | `GET /api/TeamSlot/teamA/Roster` | **403** — **隊長限定**（controller 註解：驗隊長擁有；§9.11 roster 含 discordId/name） |
| 4.9 | `GET /api/TeamSlot/teamA/Members` | **403** — **成員或隊長限定**（controller 註解：驗成員或隊長） |
| 4.10 | 以 **已確認隊員 USER_C** 身分 `GET /api/TeamSlot/teamA/Members` | **非 403**（§9.11「已成隊隊員彼此可見」） |
| 4.11 | 以 **已確認隊員 USER_C** 身分 `GET /api/TeamSlot/teamA/Roster` | **403**（Roster 是隊長轉讓工具，隊員不得取用含身分的名冊） |

### G5 — 本人專屬動作

| # | 情境 | 動作 | 期望 |
|---|---|---|---|
| 5.1 | A 邀請 B；以 **USER_A** 身分 | `PUT /api/TeamSlot/teamA/Invitations/{B的memberId}`（代替 B 接受） | **403** |
| 5.2 | A 開隊、B 已確認入隊；以 **USER_A** 身分 | `POST /api/TeamSlot/teamA/Leave` 讓 **B** 離隊 | **403**（隊長踢人應該是另一個端點，不能借 Leave） |
| 5.3 | A 發起轉移給 B；以 **第三人 USER_C** 身分 | `PUT /api/TeamSlot/teamA/TransferLeader`（接受） | **403** |

### G6 — 自有資源刪除

前置：USER_B 各建一筆。

| # | 動作 | 期望 | 附帶斷言 |
|---|---|---|---|
| 6.1 | USER_A `DELETE /api/AvailabilityOverride/{B的id}` | **403** | **B 的 override 仍存在** |
| 6.2 | USER_A `DELETE /api/LfgIntent/{B的id}` | **403** | **B 的 intent 仍存在** |

### G7 — `/api/Me/*` 不得洩漏他人資料

前置：A、B 各有邀請 / 隊伍 / 轉移請求。

| # | 以 USER_A 身分 | 期望 |
|---|---|---|
| 7.1 | `GET /api/Me/Invitations` | 回應**不含 B 的任何邀請** |
| 7.2 | `GET /api/Me/Teams` | **不含 B 的隊伍** |
| 7.3 | `GET /api/Me/LedTeams` | **不含 B 帶的隊** |
| 7.4 | `GET /api/Me/LeaderTransfers` | **不含 B 的轉移請求** |

### G8 — 認證繞過（三個已修漏洞的回歸測試）★

| # | 身分 | 動作 | 期望 | 守哪個 commit |
|---|---|---|---|---|
| 8.1 | `SESSION_FORGED`（真 discordId ＋ 亂數 sessionId） | 任一受保護端點 | **401** | `c0e0311` session 冒用 |
| 8.2 | `SESSION_FORGED` | `POST /api/Boss`（admin-only） | **401**，絕不可 200 | `c0e0311` |
| 8.3 | `JWT_FORGED_SIG` | 任一受保護端點 | **401** | — |
| 8.4 | `JWT_ROLE_TAMPERED`（role 改 admin、簽章失效） | `POST /api/Boss` | **401 或 403，絕不可 200** | `0ed4125` role claim |
| 8.5 | USER_A（role=user，簽章正確） | `POST /api/Boss` | **403** | `0ed4125` |
| 8.6 | ⚠️ `JWT_EXPIRED_VALID_SIG`（role=user） | `POST /api/Boss` | **403** — 續期**不得**跳過角色檢查 | `60faf7b` · **Phase 2** |
| 8.7 | ⚠️ `JWT_EXPIRED_VALID_SIG` | 一般受保護端點 | **200**（續期應成功，不能把合法使用者擋掉） | `60faf7b` · **Phase 2** |

> 8.6 / 8.7 需 test-login 支援 `ttlSeconds` → **Phase 2 獨立循環**。
> 💡 另一個值得釘的細節：`AuthenticationMiddleware:78-80` 只在例外是 `SecurityTokenExpiredException` 時才走續期，然後用**未驗簽**的 `ReadJsonWebToken` 取 DiscordId。
> 理論上簽章錯誤會先丟 signature 例外、進不了續期路徑——**但那依賴 MS handler 的驗證順序**。8.3 / 8.4 就是在釘這個前提，**不要只靠推理**。

### G9 — 後門與內部端點

| # | 案例 | 期望 | 備註 |
|---|---|---|---|
| 9.1 | `POST /api/test/login` 在 **Production** 環境 | **404** | 🔶 **做法升級**（原本裁決 4 是「mock `IWebHostEnvironment` 的單元測試」）→ 改用 **`WebApplicationFactory` 覆寫 `ASPNETCORE_ENVIRONMENT=Production`** 的整合測試。走完整管線、真實度高得多，而且 `WebApplicationFactory` 已經存在。放 `Test.Integration/` |
| 9.2 | 設了 `E2E:TestLoginSecret` 時，`X-Test-Secret` 錯誤 | **401** | 可在 e2e 用專屬 compose override 測 |
| 9.3 | `POST /api/internal/alert-mail` 的 `X-Alert-Secret` 鎖 | **401**（無 / 錯誤 secret） | 已有保護（裁決 3），此為回歸測試。另加：**`WebhookSecret` 未設定時也必須 401**（fail-closed） |

### G10 — Proxy 白名單回歸（經前端打，不繞過）

| # | 動作 | 期望 |
|---|---|---|
| 10.1 | USER_A 經**前端 proxy** 打 `/api/Me/Invitations`、`/Teams`、`/LedTeams`、`/LeaderTransfers` | **非 403**（proxy 不得擋掉合法路徑） |
| 10.2 | USER_A 經前端 proxy 打 `/api/Character/GetWithDiscordName`、`/api/Profile` | **非 403** |

---

## 四、★ 裁決結果（2026/11/04 已定案）

### ✅1. 越權一律回 **403**（選項 A）

影響 G3 全部、G6 全部。

**後果：這次不只是加測試，包含實作變更。**
`CharacterController` 等目前把認證身分的 `discordId` 連同路由 `id` 傳進 service，service 很可能只在 WHERE 加條件 → 影響 0 列 → controller 仍 `return Ok()`。改成 403 要：
- service 層能區分「不存在」與「不屬於你」（或統一回「拒絕」）
- controller 依結果回 403

**流程**：先轉錄 G3/G6 的 case → **跑出紅燈**（證明目前不是 403）→ 才改實作到綠。
不洩漏「資源是否存在」的考量：統一回 **403**（不區分 404），避免用狀態碼做資源探測。

### ✅2. 隊伍資訊可見範圍 — **沿用既有設計決定，不另開議**

查到 `plans/2026-08-05-leader-led-team-formation.md` 已有核可過的隱私原則：

> **§9.11 隱私：PII 最小化、承諾才揭露**。`discordId`／`discordName` 屬敏感。
> 未承諾只顯示**組成**（職業/攻擊/時段/角色名），**藏聯絡身分**；**已成隊隊員彼此可見**；
> 開隊/發邀＝已自我承諾曝光（隊長名字可顯示）。
>
> **§9.12** 候選 DTO 回傳 角色名/職業/攻擊/時段 ＋ 本王通關數 ＋ 楓葉祝福等級，**不含 discord 身分**。

且兩個端點的 controller 註解已宣告意圖：
- `GET {id}/Roster` — 「隊長轉讓──某隊 Confirmed 名冊（**服務內驗隊長擁有**）」
- `GET {id}/Members` — 「**已入隊成員**看本隊組成（不露 Discord 身分）。**服務內驗「成員或隊長」**」

**所以推導出的規則**：

> **身分（discordId / discordName）＝ 承諾後才揭露；組成（職業/攻擊/角色名/時段）＝ 給需要決策的人。**

| 端點 | 期望 | 依據 |
|---|---|---|
| `GET {id}/Roster` | **隊長限定** → 非隊長 **403** | controller 註解「驗隊長擁有」＋ §9.11「認領後才把 roster 的 discordId/name 給召集人」 |
| `GET {id}/Members` | **成員或隊長限定** → 非成員 **403** | controller 註解「驗成員或隊長」 |
| `GET {id}/Candidates` | **隊長限定** → **403** | 功能權限（隊長的挑人工具）＋ 含他人可用時段/攻擊力 |
| `GET {id}/Applications` | **隊長限定** → **403** | 他人申請是隱私 |
| `GET {id}/RecruitmentGap` | **登入即可** → 非 403 | 招募資訊，潛在申請者要看得到才知道要不要申請 |
| `GET /api/TeamSlot/Open` | **登入即可** | 開放隊伍列表 |

→ **G4.7 / G4.8 的 ❓ 全部解除**，期望值如上表。

### ✅3. `/api/internal/alert-mail` — **我判斷錯了，它已經有鎖**

更正：`AlertMailController:35` 已經有 **shared secret** 保護：

```csharp
if (string.IsNullOrEmpty(_options.WebhookSecret) || secret != _options.WebhookSecret)
    return Unauthorized();
```

`[AllowAnonymous]` 只是繞過 JWT/Session middleware（呼叫方是 Seq 的 `Seq.App.HttpRequest`，不是登入使用者），**controller 自己驗 `X-Alert-Secret`**。
而且是 **fail-closed**：secret 未設定也回 401。設計正確，**無需改動**。

→ 但仍值得留回歸測試（G9.3 改為驗證這個鎖）。

### ✅4. Production 的 test-login 回 404 — **單元測試（選項 A）**

`Test/TestAuthControllerTests.cs`：mock `IWebHostEnvironment` 的 `EnvironmentName = "Production"` → 斷言回 `NotFoundResult`。
成本低、覆蓋關鍵分支。日後若建了 `WebApplicationFactory` 再升級成整合測試。

---

## 四之二、★ 新發現：JWT 續期路徑的設計問題（`AuthenticationMiddleware:76-100`）

### 結論：**目前不可被利用，但設計是脆弱的。**

### 為什麼目前安全
`JwtService.ValidateToken` 用 `JsonWebTokenHandler.ValidateTokenAsync`，而 Microsoft.IdentityModel 的內部順序是
**先驗簽章（`ValidateSignature`）→ 再驗 payload（lifetime / issuer / audience）**。

所以簽章錯誤的 token 會丟 `SecurityTokenInvalidSignatureException` / `SecurityTokenSignatureKeyNotFoundException`，
**不是** `SecurityTokenExpiredException` → 進不了 middleware 第 78 行的續期分支。

### 為什麼仍然脆弱

| # | 問題 | 份量 |
|---|---|---|
| **1** | **保證是隱式的**：「簽章驗過了」是從**例外型別推論**出來的，程式碼沒有任何一行**宣告**它。讀到 `ReadJsonWebToken`（不驗簽）的人，不會知道它安全的前提在上面那個 `if` 裡 | ⭐⭐⭐ 主要論點 |
| **2** | **mock drift**：既有測試 `InvalidSignature_DoesNotRefresh` **mock 掉 `ValidateToken`**，直接規定「簽章錯誤回 signature 例外」。**真實的 `JwtService` 到底回什麼例外，沒有任何測試證明** — 跟 session 漏洞同一形態 | ⭐⭐⭐ |
| 3 | **沒有第二道防線**：安全性 100% 壓在那個 `if` 條件上，有人改它就失守，且不會有警訊 | ⭐⭐ |
| 4 | 依賴函式庫的驗證順序（不是公開契約，升版可能改） | ⭐ 風險低 |
| 5 | `JwtService:74` 與 `:89` 的 `Convert.ToUInt64(…?.Value)` — 缺 claim 時回 **0** 而非拋例外。目前不可利用（我們發的 token 一定有 `discordId`，且 `RefreshToken(0)` 查不到 player → 落到 401），純粹脆弱寫法 | ⭐ |

> ⚠️ **這不是可被利用的漏洞**（簽章確實先驗）。問題在於安全性建立在一個**沒被測試證明的假設**上。

### 建議修法（最小且正確）
續期前**用「忽略 lifetime 但仍驗簽 + 驗 issuer/audience」的參數重驗一次**，讓 `DiscordId` 來自**可信來源**，而不是靠例外型別推斷：

```
ValidateIgnoringLifetime(token)   // ValidateLifetime = false，其餘全開
  → 失敗 → 401
  → 成功 → 這個 DiscordId 可信，才去 RefreshToken
```

這樣安全性就只依賴「簽章是我們發的」這個明確事實，**不再依賴函式庫的驗證順序**。

> 更正統的做法是改用**伺服器端儲存、可撤銷的 refresh token**，但那是另一個規模的改動，不在這個 plan 內。

### 在這個 plan 裡怎麼處理
- **本 plan 只做 E2E 層**：G8.3（簽章錯誤 → 401）、G8.4（role 竄改 → 絕不可 200）、G8.8（無 `discordId` claim → 401）、G8.9（非數字 claim → 401 不得 500）
- **實作變更與單元層測試已另開 plan** → `plans/2026-11-04-jwt-refresh-redesign.md`
- 分工：
  - 本 plan 的 G8 測「**整條管線串起來後，攻擊者拿不到東西**」（對外行為）
  - redesign plan 的 A 組測「**真實的 `JwtService` 驗簽實作是否如預期**」（填補 mock drift）
  - 兩者互補，不重複

### 新增 case

| # | 身分 | 動作 | 期望 |
|---|---|---|---|
| 8.8 | 簽章正確但**沒有 `discordId` claim** 的 token | 任一受保護端點 | **401** |
| 8.9 | 簽章正確但 `discordId` claim 是**非數字字串** | 任一受保護端點 | **401**（不得 500） |

---

## 五、執行順序（核可後）

> 遵守 test-first 鐵則：**case 表核可 → 轉錄成測試 → 跑出有效紅燈 → 才改實作**。
> ⚠️ 本專案目前沒有 `.claude/tdd.json`，hook 稽核未啟用，但流程照走。

| 階段 | 內容 | 產出 |
|---|---|---|
| ~~0~~ | ✅ case 表核可 ＋ 4 個問題裁決完畢 | 本文件定稿 |
| **1** | 建測試骨架：直打後端的 request context、偽造 cookie/JWT 的 helper（`web/e2e/helpers/forge.ts`） | 新 helper |
| **2** | 轉錄 G1 / G2 / G8（匿名邊界、admin-only、認證繞過回歸）→ **跑紅燈** | `authorization-matrix.spec.ts` |
| **3** | 轉錄 G4 / G5 / G7（隊長權限、本人權限、Me 不洩漏）→ 跑紅燈 | 同檔 |
| **4a** | 轉錄 G3 / G6（IDOR）→ **跑紅燈**（預期紅：目前是靜默 200） | 同檔 |
| **4b** | 🔧 **實作變更**：service／controller 改成越權回 **403** → 綠 | `CharacterService` 等 ＋ controller |
| **5** | G9.1（Production → 404）單元測試；G9.3 alert-mail 密鑰回歸；G10 proxy 白名單 | `Test/TestAuthControllerTests.cs`、`Test/AlertMailControllerTests.cs` ＋ spec |
| **6** | 確認每條都「拿掉防護會紅」；接進 `ci.yml` 的 e2e job | CI 閘門 |

### 後續（獨立 plan，不在本次）
| 項目 | 為什麼分開 |
|---|---|
| **JWT 續期改「忽略 lifetime 但驗簽」重驗**（見 §四之二） | 實作變更，要自己的 TDD 循環；但**本次的 G8.3/8.4/8.8/8.9 先把護欄立起來** |
| **test-login 支援 `ttlSeconds`** → 解鎖 G8.6 / G8.7（`60faf7b` 回歸） | 同上：改 test-login 是實作變更 |
| `zaproxy/action-baseline` 掃 security header / cookie flag | 與本 plan 互補，不同性質 |

### 驗收標準
- [ ] 每個 case **拿掉對應防護會紅**（不是只看綠）
- [ ] 三個已修漏洞（`c0e0311` / `0ed4125` / `60faf7b`）各有至少一條回歸測試
- [ ] 矩陣**不經過** Next proxy（除 G10 專測 proxy）
- [ ] 越權的附帶斷言「**他人資料未被改動**」都有驗，不只看狀態碼
- [ ] 跑在 CI 的 e2e job，PR 必跑

### 不在範圍內
- 速率限制繞過（`RedisRateLimiter`）— 另開
- 併發/競態（已有 `TeamSlotEditLockIntegrationTests` 等覆蓋）
- XSS / SQLi（Dapper 參數化 ＋ CodeQL 已覆蓋）
- security header / cookie flag — 建議另用 `zaproxy/action-baseline`，與本 plan 互補
