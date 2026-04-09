# 前端重構計畫 — 2026-04-10（後端驗證版）

## 審查範圍
- 路徑: `web/`
- 審查檔案: 57 個（頁面 × 19、API route × 2、Provider × 4、UI 元件 × 4、Hook × 8、Service × 7、Type/Constant × 6、Util × 1、Test × 7）
- 技術棧: Next.js 15 (App Router) + TypeScript + TanStack React Query + Tailwind CSS + Shadcn/UI
- 後端驗證：已閱讀 Controllers、Middleware、AuthAppService、DTOs、Program.cs

---

## 後端驗證後的修正說明

| 原始發現 | 修正結果 |
|---------|---------|
| C-3「Cookie 均非 HttpOnly」| **完全錯誤，移除**：後端 `AuthController` 明確設定所有 Cookie 為 `HttpOnly=true, Secure=true, SameSite=Strict` |
| C-2「管理員角色驗證漏洞」| **降級為 Low**：`layout.tsx` 是 Server Component，伺服器端讀取 HttpOnly Cookie 是正確做法；後端每個請求都獨立驗證，不存在安全繞過 |
| M-7「DeleteCharacterRegisterIds 大小寫」| **修正說明**：後端 `Register.cs` 確實使用 PascalCase，ASP.NET Core + Newtonsoft.Json 預設以 CamelCase 序列化輸出，前端讀取到的 JSON key 是 `deleteCharacterRegisterIds`，但 TypeScript 型別卻寫 `DeleteCharacterRegisterIds`，讀取時會 undefined——仍是 bug |
| 待確認「後端 Cookie flags」| **已確認**：HttpOnly、Secure、SameSite=Strict 全部設定正確 |
| 待確認「scheduleService.saveSchedule 授權」| **發現新 Bug**：`TeamSlotController` 使用 `User.IsInRole("admin")`（小寫），後端儲存的 role 是 `"Admin"`（大寫），`IsInRole` 大小寫敏感，永遠回傳 false |
| 待確認「JOBS 常數同步」| **確認需改善**：後端有 `GET /api/JobCategory/GetJobMap` 動態回傳職業對應表 |

---

## Critical（立即修復）

### C-1 API Proxy 無路徑白名單 — 路徑穿越風險
- **位置**：`app/api/[...path]/route.ts`
- **問題**：
  1. Proxy 直接拼接路徑無驗證，可構造異常路徑繞過至未預期的後端路由
  2. `NEXT_PUBLIC_API_URL` 使用 `NEXT_PUBLIC_` 前綴，若是 Docker 內部地址（如 `http://backend:5230`），此 URL 會被打包進前端 JS bundle 暴露給瀏覽器
- **後端確認的合法路徑**（來自 Controllers）：`auth`, `character`, `boss`, `register`, `schedule`, `teamSlot`, `period`, `SystemConfig`, `JobCategory`
- **修法**：
  ```typescript
  const ALLOWED = new Set(['auth','character','boss','register','schedule',
                           'teamslot','period','systemconfig','jobcategory']);
  if (!ALLOWED.has(path[0]?.toLowerCase())) {
      return new NextResponse('Forbidden', { status: 403 });
  }
  ```
  將環境變數改名為 `BACKEND_API_URL`（移除 `NEXT_PUBLIC_` 前綴）

---

## High（架構問題，近期修復）

### H-1 後端 Bug：`TeamSlotController.UpdateAsync` role 比對大小寫錯誤
- **位置**：後端 `Presentation.WebApi/Controller/TeamSlotController.cs:40`
- **後端代碼**：`var isAdmin = User.IsInRole("admin");`（小寫）
- **實際 role**：後端儲存 `"Admin"`（大寫），`AuthAppService.LoginAsync:62` 確認 `if (role == "Admin")`
- **問題**：`ClaimsIdentity.IsInRole()` 大小寫敏感，永遠回傳 `false`，所有使用者（包含 Admin）都被當作一般用戶處理，`TeamSlotService.UpdateAsync` 的 admin 路徑從未被執行
- **修法**：後端改為 `User.IsInRole("Admin")` 或 `string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)`
- **前端影響**：此 bug 修復前，管理員補位功能與一般玩家行為相同（無法設定 `IsManual = true`）

### H-2 大量頁面手動實作 fetch 邏輯而非使用 React Query
- **位置**：`schedule/page.tsx`、`scheduleResult/page.tsx`、`admin/boss/page.tsx`、`admin/schedule/page.tsx`、`admin/templates/page.tsx`、`character/page.tsx`
- **問題**：`hooks/queries/` 已有 `useBosses`、`useCharacters` 等 hooks，但頁面卻自行用 `useEffect + useState` 實作相同邏輯，DRY 違反，且缺少快取、staleTime、自動重試
- **修法**：將 6 個頁面的 `useEffect` 資料載入改用對應的 React Query hooks，刪除/更新後用 `queryClient.invalidateQueries`

### H-3 `useEffect` missing dependency（潛在 stale closure）
- **位置**：`hooks/useRegisterForm.ts:37-40`
- **問題**：`updateForm` 未加入 dependency array，React strict mode 下可能觸發問題
- **修法**：將 `updateForm` 包裝為 `useCallback` 或加入 deps

### H-4 `registerService.getByQuery` 回傳 `any`
- **位置**：`services/registerService.ts:50`
- **後端確認**：`IRegisterService.GetByQueryAsync` 回傳 `IEnumerable<TeamSlotCharacter>`
- **問題**：前端 `Promise<any>` 使型別安全鏈斷裂，但正確型別已可從後端 interface 得知
- **修法**：改為 `Promise<TeamSlotCharacter[]>`

### H-5 `PlayerRaidTeamCard` Props 型別定義不一致
- **位置**：`app/schedule/components/PlayerRaidTeamCard.tsx:12-33`
- **問題**：`templates` 用 intersection type 補丁而非加入 interface
- **修法**：直接在 `PlayerRaidTeamCardProps` 中加入 `templates: BossTemplate[]`

### H-6 測試引用不存在的型別匯出（編譯錯誤）
- **位置**：`__tests__/CharacterBossList.test.tsx:6`
- **問題**：`Character` 已移至 `@/types/character`，測試仍從 `@/types/raid` 引入（`raid.ts` 第 26 行有注釋「Character 已移至 character.ts」）
- **修法**：修正 import 路徑，同步更新相關 mock 型別

### H-7 `CharacterForm.test.tsx` 更新角色測試失敗
- **位置**：`__tests__/CharacterForm.test.tsx:137`
- **後端確認**：`CharacterController.UpdateAsync` 路由為 `[HttpPut("{id}")]`，URL 為 `/api/character/{id}`
- **問題**：測試預期 URL 為 `/api/character`（PUT），實際應為 `/api/character/${character.id}`
- **修法**：修正測試的 fetch URL 斷言

### H-8 臨時 ID 使用 `Math.random()` 有碰撞風險
- **位置**：`app/admin/schedule/page.tsx:109`
- **問題**：`Math.floor(Math.random() * 1_000_000_000)` 可能碰撞，導致 React key 衝突
- **修法**：改用 `crypto.randomUUID()`

---

## Medium（代碼品質）

### M-1 `CharacterPage` 不使用 React Query，資料更新後無快取失效
- **位置**：`app/character/page.tsx`
- **問題**：刪除/編輯後手動更新本地陣列，可能導致資料過時；`useCharacters()` hook 已存在卻未使用
- **修法**：改用 `useCharacters()` hook，操作後呼叫 `queryClient.invalidateQueries(['characters'])`

### M-2 `scheduleResult/page.tsx` 的 `isMyTeam` 應用 `useCallback`
- **位置**：`app/scheduleResult/page.tsx:39`
- **問題**：`useMemo(() => (team) => { ... }, [])` 是 anti-pattern（useMemo 包住函式）
- **修法**：改為 `useCallback((team: TeamSlot) => { ... }, [myCharacters])`

### M-3 `getMissingSlots()` 複雜邏輯在 render 階段執行
- **位置**：`app/schedule/components/PlayerRaidTeamCard.tsx:67`
- **問題**：每次 render 都重新計算，且直接 mutate 陣列
- **修法**：提取為 `useMemo`，用 `filter` 取代 `splice`

### M-4 `useTimeSelection.ts` 中 `quickFill("last_week")` 混合 async/sync
- **位置**：`hooks/useTimeSelection.ts:86-119`
- **問題**：同步函式 `quickFill` 內部對 `last_week` 分支呼叫非同步邏輯，呼叫者無法知道是非同步操作
- **修法**：拆分為 `quickFill()`（sync）和 `fillFromLastWeek()`（async，回傳 Promise）

### M-5 `confirm()` / `alert()` 阻塞式對話框分散各處
- **位置**：`CharacterCard.tsx`、`TimePicker.tsx`、`useRegisterForm.ts` 等
- **問題**：阻塞式對話框在 Next.js SSR 環境下不佳，且無法自訂樣式
- **修法**：統一使用自訂確認 Modal 元件或 toast 通知

### M-6 `CharacterForm` 接受 `setLoading` prop 而非使用 Context
- **位置**：`app/character/page.tsx` → `CharacterForm.tsx`
- **問題**：`useLoading` 是 context，form 元件直接呼叫即可，傳遞 `setLoading` prop 造成不必要耦合
- **修法**：移除 `setLoading` prop，`CharacterForm` 直接呼叫 `useLoading()`

### M-7 `RegisterFormState.DeleteCharacterRegisterIds` 大小寫不一致（有 runtime bug）
- **位置**：`types/register.ts:26`
- **後端確認**：後端 `Register.cs` 有 `DeleteCharacterRegisterIds`（PascalCase），但 ASP.NET Core + Newtonsoft.Json 預設以 CamelCase 序列化輸出，回傳 JSON 為 `deleteCharacterRegisterIds`
- **問題**：TypeScript 型別使用 PascalCase，但 API 回傳 camelCase，讀取 `form.DeleteCharacterRegisterIds` 在 runtime 會是 `undefined`；其餘欄位均為 camelCase
- **修法**：改為 `deleteCharacterRegisterIds`，同步更新所有使用處（含測試）

### M-8 成員上限硬編碼 `6`
- **位置**：`app/scheduleResult/components/ResultTeamCard.tsx:44`、`app/admin/schedule/components/AdminRaidTeamCard.tsx:24`
- **後端確認**：`Boss.requireMembers` 已是後端 entity 欄位，前端 `Boss` type 也有 `requireMembers: number`
- **問題**：硬編碼 `6` 忽視後端已有的動態值
- **修法**：從 props 傳入 `requireMembers` 取代硬編碼數字

### M-9 `scheduleResult/page.tsx` 多個 effect 無 AbortController
- **位置**：`app/scheduleResult/page.tsx:48-65`
- **問題**：`selectedBossId`、`showOnlyMine` 改變時觸發多個 effect，舊請求未取消，可能導致競態條件
- **修法**：遷移至 React Query（帶 queryKey 依賴），或加入 AbortController 取消舊請求

### M-10 `QueryProvider` 缺少 retry 設定與全域錯誤處理
- **位置**：`app/providers/QueryProvider.tsx`
- **問題**：僅設定 `staleTime`，預設 3 次重試可能讓 UX 感覺卡頓，無全域 `onError`
- **修法**：加入 `retry: 1`，並設定全域錯誤 toast 通知

---

## Low（命名、風格）

- **L-1**：`next.config.ts` 中 `swcMinify: true` 在 Next.js 15 已廢棄，應移除
- **L-2**：首頁 `app/page.tsx` 僅顯示 "home"，應導覽至適當頁面或顯示歡迎內容
- **L-3**：`TimePicker.tsx:93` 的 `[0, 1, 2, 3, 4, 5, 6]` 改為 `Array.from({length: 7}, (_, i) => i)`
- **L-4**：`NavBar.tsx` 中 `NavLink` 在函式內定義，應提取至外部
- **L-5**：`constants/jobs.ts` 職業列表硬編碼，後端已有 `GET /api/JobCategory/GetJobMap` 回傳職業→職業分類對應表，應改為動態載入
- **L-6**：`layout.tsx:44` `<html lang="en">` 應改為 `lang="zh-TW"`
- **L-7**：`layout.tsx` 讀到 `sessionId` 設定 `initialRole = "admin"` 但 `setRole` 在 callback 時用 `data.role.toLowerCase()`；兩者邏輯一致（都是小寫），但 `RolesProvider` 的 role 值應有統一的型別定義（`type Role = "admin" | "user" | ""`）

---

## 執行計畫

### 第一週 — Critical + 高風險 Bug
- [ ] C-1：API Proxy 加入路徑白名單，`NEXT_PUBLIC_API_URL` 改為 `BACKEND_API_URL`
- [ ] H-1：**後端**修正 `TeamSlotController` `User.IsInRole("admin")` → `"Admin"`

### 第二週 — 測試修復與型別正確性
- [ ] H-6：修正 `CharacterBossList.test.tsx` import 路徑與缺失 props
- [ ] H-7：修正 `CharacterForm.test.tsx` 更新角色 URL 斷言（`/api/character/${id}`）
- [ ] H-4：`registerService.getByQuery` 改為 `Promise<TeamSlotCharacter[]>`
- [ ] H-5：`PlayerRaidTeamCard` interface 加入 `templates`
- [ ] M-7：`DeleteCharacterRegisterIds` 改為 `deleteCharacterRegisterIds`（全域替換）
- [ ] H-8：`Math.random()` 改為 `crypto.randomUUID()`

### 第三週 — React Query 遷移
- [ ] H-2：`admin/boss/page.tsx`、`admin/schedule/page.tsx`、`admin/templates/page.tsx` 改用 `useBosses()` hook
- [ ] M-1：`character/page.tsx` 改用 `useCharacters()` hook
- [ ] M-6：`CharacterForm` 移除 `setLoading` prop，改用 `useLoading()` context
- [ ] M-9：`scheduleResult/page.tsx` 遷移至 React Query 或加 AbortController
- [ ] M-10：`QueryProvider` 加入 `retry: 1` 與全域錯誤處理

### 第四週 — 架構清理與 UX 統一
- [ ] M-4：`useTimeSelection` 拆分 `last_week` 為獨立 async function
- [ ] M-5：統一 `confirm()` / `alert()` 為自訂 Modal 或 toast
- [ ] M-2：`isMyTeam` 改用 `useCallback`
- [ ] M-8：成員上限從 `boss.requireMembers` 讀取，移除硬編碼 `6`
- [ ] L-1：移除 `next.config.ts` 中的 `swcMinify: true`
- [ ] L-5：`constants/jobs.ts` 改為從 `GET /api/JobCategory/GetJobMap` 動態載入
- [ ] L-6：`<html lang="zh-TW">`
- [ ] L-7：新增 `type Role = "admin" | "user" | ""` 統一 role 型別

---

## 已確認沒問題的項目（後端驗證後移除）

- ~~C-3 Cookie 非 HttpOnly~~：後端明確設定 `HttpOnly=true, Secure=true, SameSite=Strict`，問題不存在
- ~~C-2 管理員 UI 偽造~~：`layout.tsx` 為 Server Component，伺服器端讀取 HttpOnly Cookie 是正確做法；後端每個 API 請求皆獨立驗證，不存在安全繞過，充其量是 UX 問題（過期 session 短暫顯示 admin nav）

---

## 正面觀察
- Hook 架構拆分清晰，`useRegisterForm` 組合 `useTimeSelection` + `useBossAssignment`，職責分離良好
- 共用 UI 元件（`FormControls`、`Card`、`Modal`）設計完整，API 友好
- `formatDateTime` 工具函式設計簡潔且可測試
- React Query hooks 目錄已建立，方向正確
- `useRaidValidation` 驗證邏輯集中管理，測試覆蓋完整
- TypeScript 型別定義整體完整，`any` 僅 1 處（`registerService.getByQuery`）
- 暗色模式支援一致，使用 CSS 變數設計

---

## 測試現狀
- 測試結果：1 failed / 29 passed（`CharacterForm` update URL 不符）
- TypeScript 編譯錯誤：9 個（全在測試檔案）
- 缺少測試：`useTimeSelection`、`useBossAssignment`、service 層、admin 頁面整合測試
