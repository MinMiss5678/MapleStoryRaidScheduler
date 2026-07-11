# 多版本楓之谷支援計畫

**日期**：2026-05-27  
**更新**：2026-05-27（修正產品核心價值定位）  
**範疇**：支援 TMS / GMS / KMS / CMS / JMS 等各版本楓之谷

---

## 產品核心價值重新定位

### 原始假設 vs 真實使用行為

週重置遊戲（楓之谷、FFXIV）玩家的實際行為是**固定團文化**：同一批人、每週同一天、同一時間打同一隻王。這代表自動分配引擎的使用頻率遠低於原本預期。

| 使用情境 | 頻率 | 真實痛點 |
|---|---|---|
| 首次建團（新賽季） | 極低（每幾個月一次） | 人多時段複雜，手排痛苦 |
| 補位（有人退坑） | 中 | 找時段相符的替補 |
| 請假代打 | 高（每週都有） | 臨時找人，急迫性高 |
| **固定團週通知** | **極高（每週）** | **提醒今天幾點打王** |

### 功能重要性重排

```
原設計重心：自動分配引擎（複雜、工程量大、低頻）
真實使用重心：週通知 + 補位媒合（簡單、高頻、高黏著）
```

**自動分配引擎** = 獲客工具（「哇能自動排！」的第一印象），建團後鮮少再用  
**週通知 + 補位** = 留客工具，決定用戶是否持續付費

### 對 Phase 優先序的影響

在多版本擴張前，應先補齊高頻功能缺口（見 Phase 0）。

---

## 目標版本

| 版本 | 市場 | 語言 | 時區 | 職業數量 |
|---|---|---|---|---|
| TMS（台灣） | 台服 | 繁中 | UTC+8 | ~12（目前） |
| KMS（韓國） | 韓服 | 韓文 | UTC+9 | 50+ |
| GMS（全球） | NA/EU/SEA | 英文 | 多時區 | 50+ |
| CMS（中國） | 大陸 | 簡中 | UTC+8 | ~40 |
| JMS（日本） | 日服 | 日文 | UTC+9 | ~40 |

---

## 現況分析

### 已資料驅動（低成本）

- `Boss` 表 — 王已可 CRUD 管理，各版本直接新增資料即可
- `BossTemplate` + `BossTemplateRequirement` — 陣容需求可配置
- `JobCategory` 表 — 職業分類已 DB 化，可自由定義

### 硬編碼問題點

**前端：**

```ts
// web/constants/jobs.ts — 只有 12 個古典職業
export const JOBS = ['主教', '火毒大魔導士', '冰雷大魔導士', ...]

// web/constants/register.ts — 台服週消費上限硬編碼
export const MAX_ROUNDS = 14;

// web/constants/register.ts — 繁中硬編碼
export const WEEKDAYS = ["日", "一", "二", "三", "四", "五", "六"];
```

**後端：**

```csharp
// TeamSlotAutoAssignService.cs:92 — 時區硬編碼 UTC+8
var twTime = ts.SlotDateTime.ToOffset(TimeSpan.FromHours(8));

// TeamSlotAutoAssignService.cs:130
SlotDateTime = new DateTimeOffset(targetDateTime, TimeSpan.FromHours(8)).ToOffset(TimeSpan.Zero)
```

---

## 實作計畫

### Phase 0：高頻功能補齊（3-4 天，優先於多版本）

**目的**：解決真實留客痛點，確保用戶建團後持續使用。

#### 0-A：請假 / 代打系統（2 天）

現況缺口：固定團有人請假時，完全沒有流程，靠 Discord 手動溝通。

**後端：**
- `TeamSlotCharacter` 加 `IsAbsent boolean` 欄位
- 新增 `POST /api/TeamSlot/{id}/absence` — 成員標記本週請假
- 新增 `GET /api/TeamSlot/{id}/vacancies` — 回傳缺人的隊伍清單

**前端：**
- 成員可在排程頁點「本週請假」
- 管理員可看到缺人清單，一鍵發 Discord 徵補位公告

**Discord Bot：**
- 自動發送「隊伍 X 本週缺 [職業]，有興趣補位請點 ✅」

#### 0-B：固定團週通知優化（1-2 天）

現況：`DailyNotificationService` 每天掃描並通知，但格式固定、無法客製。

**改善：**
- `SystemConfig` 加 `NotificationMessage` 欄位（可自訂通知文字範本）
- 通知加入「本週打王時間」＋「成員清單」＋「請假人員」三段資訊
- 支援提前 N 小時通知（預設 1 小時前）

---

### Phase 1：時區設定化（2 天）

**目的**：GMS（UTC-5）、KMS（UTC+9）排程時段計算正確。

**資料庫：**

```sql
ALTER TABLE "SystemConfig" ADD COLUMN "TimeZoneId" text NOT NULL DEFAULT 'Asia/Taipei';
```

**後端：**

- `SystemConfigDbModel` 新增 `TimeZoneId` 欄位
- `TeamSlotAutoAssignService` 改用 `TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId)` 取代 `TimeSpan.FromHours(8)` 硬編碼
- `SlotDateCalculator` 同步修正

**測試案例：**
- UTC-5 邊界（跨日排程）
- UTC+9 週邊界計算

---

### Phase 2：職業系統 API 化（1 天）

**目的**：移除前端 `JOBS` 硬編碼，支援 50+ 職業的版本。

**後端：**

- 新增 `GET /api/JobCategory/jobs` — 回傳所有 `JobName` 列表（`JobCategory` 表已有）

**前端：**

- `web/constants/jobs.ts` 廢棄 `JOBS` 常數
- 改用 `useQuery(['jobs'], fetchJobs)` 從 API 取得職業列表
- 所有使用 `JOBS` 的下拉選單改為動態資料

---

### Phase 3：MAX_ROUNDS 設定化（0.5 天）

**目的**：各版本週消費上限可能不同。

**資料庫：**

```sql
ALTER TABLE "SystemConfig" ADD COLUMN "MaxRoundsPerWeek" integer NOT NULL DEFAULT 14;
```

**後端：** `SystemConfig` 加欄位，透過既有 `GET /api/SystemConfig` 回傳。  
**前端：** `MAX_ROUNDS` 常數改從 SystemConfig API 讀取。

---

### Phase 4：UI 多語言 i18n（5-7 天）

**目的**：支援 en / ko / ja / zh-TW / zh-CN 介面語言。

**技術選型**：`next-intl`（已與 Next.js App Router 整合佳）

**步驟：**

1. 安裝 `next-intl`，設定 `i18n.ts`、`middleware.ts` 路由
2. 建立語系資料夾結構：`messages/zh-TW.json`、`messages/en.json`、`messages/ko.json`
3. 遍歷所有 components 將硬編碼繁中字串抽出至翻譯 key
4. 翻譯工作（可外包給各版本熟悉玩家）

**語系路由策略：** `/[locale]/...`，`defaultLocale: 'zh-TW'`，保持現有 URL 相容

---

### Phase 5：職業資料填充（2-3 天，資料工作）

**目的**：各版本完整職業 + 職業分類 seed data。

透過 `JobCategory` 表新增各版本職業，不需程式碼修改：

```sql
-- 範例：GMS 職業
INSERT INTO "JobCategory" ("JobName", "CategoryName") VALUES
('Bishop', 'Mage'),
('Ice/Lightning Archmage', 'Mage'),
('Hero', 'Warrior'),
...
```

各版本資料可分開維護（搭配多租戶 `guild_id` 後可各自獨立）。

---

## 成本彙總

| 工項 | 工程天數 | 優先序 | 說明 |
|---|---|---|---|
| Phase 0-A：請假 / 補位系統 | 2 天 | **P0**（留客） | 固定團最高頻痛點 |
| Phase 0-B：週通知優化 | 1-2 天 | **P0**（留客） | 每週都在用 |
| Phase 1：時區設定化 | 2 天 | P1（正確性） | GMS/KMS 上線前提 |
| Phase 2：職業系統 API 化 | 1 天 | P1 | 移除硬編碼 |
| Phase 3：MAX_ROUNDS 設定化 | 0.5 天 | P1 | 小改動 |
| Phase 4：UI i18n | 5-7 天 | P2 | GMS 進入前提 |
| Phase 5：職業資料填充 | 2-3 天 | P2（資料工） | 各版本職業清單 |
| **總計** | **13-17 天** | — | — |

> **執行順序**：Phase 0（留客功能）→ 多租戶 `guild_id` migration → Phase 1-3 → Phase 4-5

---

## 與多租戶計畫的關聯

本計畫與 [SaaS 多租戶評估](reports/2026-04-18-saas-architecture-evaluation.md) 並行不衝突：

- Phase 1-3 修改的 `SystemConfig` 欄位，未來加 `guild_id` 後可讓每個公會各自設定時區與週消費上限
- Phase 5 的職業資料，搭配多租戶後可讓各公會選用對應版本的職業集合

**建議執行順序：** 多租戶 `guild_id` migration → Phase 1-3 → Phase 4-5

---

## 未解決問題

1. **版本共存**：同一個 SaaS 平台是否允許 TMS 公會與 GMS 公會共存？若是，`JobCategory` 需要 `version_tag` 欄位讓各公會選擇職業集合。
2. **GMS 多時區**：GMS 玩家分佈 NA/EU/SEA，同一公會成員可能跨多個時區，「公會時區」設定不能解決所有排程衝突。
3. **韓文/日文字符集**：PostgreSQL 現有 `text` 型別支援 Unicode，應無問題，但需驗證 Discord Bot 訊息的字符集渲染。
4. **請假代打流程邊界**：代打者是否需要是公會成員？代打是否計入本人的週消費次數？需與管理員 UX 確認。
5. **固定團 vs 自動分配並存**：建團後進入固定團模式，自動分配引擎是否應自動停用（避免每週覆蓋手動調整）？目前 `IsManual` 旗標只保護個別成員，無法保護整個隊伍。
