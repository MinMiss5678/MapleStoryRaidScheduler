# Session 與 Discord Token TTL 解耦計畫

> 輕量 plan（動手前的 spec）：目標 / 範圍 / 決策 / 驗收 / 工時。做完可丟；穩定規則再收進 `docs/`。
> **定位誠實**：現況能動，但這不只是 readiness——它移除一個**真實（低機率）的失敗耦合**（session 驗證依賴 Discord OAuth 端點）＋ 把 session 政策歸自己管。

## 目標

把「系統 session 壽命」與「Discord OAuth token TTL」**解耦**：session 用**自己的 expiry（我的政策）**、驗證**不再依賴 Discord OAuth 端點**（移除 hot path 的刷新呼叫）。

## 現況（已驗證）

- `session.Expiry` = **Discord AccessToken 過期**；`SessionService.GetAsync` 在過期時打 Discord `RefreshTokenAsync` 續期 + 更新 DB/快取。
- Redis 快取 TTL = access token 壽命（≈ 7 天，Discord `expires_in`）。
- **AccessToken 登入後沒用**：唯一拿它打 Discord API 的地方是 `AuthService:36 GetUserAsync`（登入當下抓身分）；身分組走 **bot token**（`GetUserRolesAsync(discordId)`），授權走 session/JWT。
- → 刷新在**刷一個沒人用的憑證**，還把 Discord 依賴塞進 auth hot path、讓 Discord 決定我的 session 長度。

## 範圍（分階段）

### Phase 1：session 有自己的 expiry（核心）
- `session` 表加 `SessionExpiry timestamptz`（= 我的政策，例：登入 + 30 天絕對過期）。
- `SessionService.GetAsync` 改用 **`SessionExpiry` 判斷有效**；**每次讀取（含 cache hit）**檢查 `now < SessionExpiry`，過期回 null，**不再打 Discord 刷新**。
- Redis 快取 TTL = **短固定值（例 10~60 分）**——這是**快取新鮮度 + 撤銷殘留上界**，**不綁** `SessionExpiry`（session 有效性由上面的欄位檢查管）。保險：`ttl = min(freshnessTTL, SessionExpiry - now)`，快取不活過有效期。
- 移除 `GetAsync` 裡的 `RefreshTokenAsync` 呼叫（那條 hot-path Discord 依賴）。

> ⚠️ **cache TTL ≠ session 有效性**（兩個獨立概念，別綁）：session 活多久是 `SessionExpiry`（我的政策，例 30 天，讀取時檢查）；cache 副本活多久是短 TTL（純加速）。若把 cache TTL 設成 30 天 → 撤銷若遇 Redis 刪除失敗，殘留窗口變 30 天；短 TTL 把它壓到分鐘級。cache miss 便宜（查 DB、不打 Discord），短 TTL 代價低。

### Phase 2（選配）：token 當按需憑證
- AccessToken/RefreshToken 欄位**保留**（未來可能有「代呼叫 Discord API」的功能）；但**只在真的要打 Discord 時才 on-demand 刷新**，不在 session 驗證時刷。
- 現況沒有這種功能 → 可先完全不刷。

### Phase 3（選配）：sliding expiry
- 每次活動延展 `SessionExpiry`（滑動視窗）→ UX 更好，但**每次讀要寫**（更新 expiry）→ 跟讀穿快取衝突，要評估寫入成本。v1 先絕對過期。

### 非範圍（YAGNI）
- 不做 refresh token rotation / 進階 OAuth 安全（現況 token 沒用）。
- 不動撤銷路徑（已 OK）。

## 關鍵決策

### session expiry 政策
- **v1 = 絕對過期**（登入 + N 天，例 30）——最簡單、無 write-on-read。sliding 列 Phase 3。

### token 存不存
- **保留欄位、但 session 驗證不刷**。要不要繼續存 token：
  - **存（建議）**：未來加 Discord-on-behalf 功能不用改 schema；但明確「**不當 session 壽命依據**」。
  - 不存：最乾淨（反正沒用），但未來要用得重改登入流程。
  - → 建議**保留欄位、只切斷刷新耦合**，務實。

### 好處（為何值得，不只 readiness）
- session 驗證**不再依賴 Discord OAuth 端點**（端點掛/慢不再拖垮續期）。
- session 長度 = **我的政策**，不受 Discord `expires_in` 擺布。
- 移除**無用的刷新呼叫**（刷沒人用的 token）。
- 撤銷殘留（Redis 刪除失敗）的上界 = **短 cache TTL（分鐘級）**，不是 session 有效性、更不是 Discord 的 7 天。

## 資料庫
- migration：加 `session."SessionExpiry" timestamptz NOT NULL`（現有列回填 = `Expiry` 或 `now + policy`）。
- 編號：目前最新 `000004`；SaaS 計畫也預定 `000005` → **先實作者拿 000005，後者順延**（避免又撞號）。
- （選配）背景清理：刪 `"SessionExpiry" < now` 的陳舊列（現況 DB session 無自動清理）。

## 驗收
- [ ] session 過期由 `SessionExpiry`（我的政策）決定，**讀取時檢查（含 cache hit）**，與 Discord token 無關。
- [ ] `GetAsync` **不再呼叫 `RefreshTokenAsync`**（Discord OAuth 端點停擺，session 驗證仍正常）。
- [ ] Redis 快取 TTL = **短固定值（不綁 `SessionExpiry`）**；撤銷殘留上界 = 該短 TTL（分鐘級）。
- [ ] 登入流程仍正常（access token 只在登入抓身分用）。
- [ ] 撤銷仍即時（刪 DB + Redis）。
- [ ] 測試更新：`SessionServiceTests` 的刷新分支改成「過 `SessionExpiry` → 回 null」。

## 工時估
- Phase 1（migration + `SessionService` 改 + 測試）≈ 半天～一天。
