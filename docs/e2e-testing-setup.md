# E2E 測試設定筆記（Playwright + 全 stack）

Playwright 打**全端**（瀏覽器 → Next.js `/api` proxy → .NET API → Postgres），驗核心流程整條通不通。
重點記**坑**——跑法照抄不難，難在時間相依業務規則、secure-context、dind 這些。
計畫/進度看 `plans/2026-07-18-e2e-tests.md`；這份是「怎麼跑 + 為什麼這樣接」的參考。

## 架構

```
compose.e2e.yaml
├─ e2e-db          Postgres 18（測試 DB）
├─ e2e-migrate     golang-migrate 套 db/migrations（一次性）
├─ e2e-backend     .NET API，ASPNETCORE_ENVIRONMENT=Development（test-login 才生效）
├─ e2e-frontend    Next.js dev（web/Dockerfile 的 dev target；NODE_ENV=development → proxy 白名單含 test）
└─ e2e-playwright  Playwright 執行器（profile: ci；web/Dockerfile.e2e）— CI 才啟動

本機：前端由 compose 起（或 playwright webServer 起 next dev），Playwright 從 host 跑。
CI  ：e2e-playwright 容器共用 e2e-frontend 網路（network_mode）→ 走 localhost:3000 跑。
```

## 檔案清單

| 檔 | 作用 |
|---|---|
| `web/playwright.config.ts` | testDir `e2e/`；`PLAYWRIGHT_BASE_URL` / `PLAYWRIGHT_NO_WEBSERVER` 可切本機/CI |
| `web/e2e/*.spec.ts` | 7 支測試（見下） |
| `web/e2e/helpers/auth.ts` | `loginAs()` — 呼叫 test-login 拿 cookie |
| `Presentation.WebApi/Controller/TestAuthController.cs` | `POST /api/test/login`（鎖非 Production） |
| `db/seed-e2e.sql` | E2E seed（單一未來 period + 3 隻獨立王） |
| `compose.e2e.yaml` | 全 stack + `e2e-playwright`(profile ci) |
| `web/Dockerfile`（`dev` target）/ `web/Dockerfile.e2e` | 前端 dev / playwright 執行器映像 |
| `.gitlab-ci.yml`（`e2e` job）| dind 起 compose 跑 E2E（`when: manual`） |

## 認證接縫（繞過 Discord OAuth）

`POST /api/test/login`（body: `discordId`/`discordName`/`role`）依捏造身分**直接發跟正式流程一樣的 cookie**（玩家 `jwtToken`；admin seed session + `sessionId{discordId}`），全程不碰 Discord。
🔴 **只在非 Production**（`_env.IsProduction()` → 404）+ 前端白名單 `test` 只在 `NODE_ENV!=production` 開放（雙保險）。

## Seed 模型（`db/seed-e2e.sql`）

- **TRUNCATE 所有交易資料** → 只留**一個** period（`GetActivePeriodAsync` 回最新 StartDate 的，須確保唯一）。
- period 設 **`CURRENT_DATE + 10 ~ +17`（未來一週）** → 報名截止日（period 前一週）落在未來 → 報名開著。
- **三隻獨立王隔離**平行測試互相干擾：`E2E王`（讀取/報名）、`E2E王2`（補位）、`E2E王3`（重排）。

## 怎麼跑

### 本機（快，日常）
```bash
docker compose -f compose.e2e.yaml up -d                 # db + backend + frontend
# 等 backend 起完（WeeklyPeriodJob 建 period）後灌 seed：
docker compose -f compose.e2e.yaml exec -T e2e-db \
  env PGPASSWORD=e2e psql -U postgres -d presentationdb < db/seed-e2e.sql
cd web && npm run e2e                                     # 跑 7 支（reuse compose 前端）
```

### 容器化（CI 用的機制，本機可驗）
```bash
docker compose -f compose.e2e.yaml --profile ci run --build --rm e2e-playwright
```

### GitLab CI
`.gitlab-ci.yml` 的 `e2e` job（`when: manual`）：dind 起 compose → 等 period → seed → 跑 `e2e-playwright`。

### 收工
```bash
docker compose -f compose.e2e.yaml --profile ci down -v   # 含資料一起清
```

## 測試清單（7 支）

| spec | 驗 |
|---|---|
| `smoke` | 首頁未登入 Landing + 登入鈕 |
| `auth`（×2） | 玩家 → Dashboard；admin → `/admin/schedule` |
| `schedule` | seeded 玩家在排團結果看到自己的隊（讀取整串） |
| `register` | 新玩家報名 → 自動排隊 → 入隊（寫入整串） |
| `fill` | 玩家補位進未滿的隊（E2E王2） |
| `admin-rebuild` | 管理員自動排團（E2E王3） |

## 🔴 踩過的坑（這份最值錢的部分）

| 坑 | 症狀 | 解 |
|---|---|---|
| **`GetActivePeriodAsync` = 最新 StartDate 非「含今天」** | 多 period 並存時 `GetByDiscordId` 回空 | seed **TRUNCATE 成單一 period** |
| **報名截止時間**（`GetDeadlineForPeriod` = period 前一週） | 報名回 500「已超過截止」 | period 設**未來一週** |
| **報名表單 Step2 選角色後 row 重新分組**（角色 select 消失） | `.nth(1)` 找不到 | **先選 boss 再選角色** |
| **補位場數規則**（`validateAddCharacter` #5：補位者場數 = 首位成員場數） | 補位靜默失敗、無 PUT | seed dummy 成員 `Rounds=0` |
| **secure-context**：非 localhost HTTP 存取 → `crypto.randomUUID`（idempotency key）+ Secure cookie 失效 | 寫入測試全掛 | `e2e-playwright` 用 **`network_mode: "service:e2e-frontend"`** → 走 `localhost:3000` |
| **dind volume 掛載**看不到 job 容器檔案 | CI 掛載跑不了 | Playwright 映像走 **build context**（`Dockerfile.e2e` COPY 源碼） |
| **前端 prod build** `NODE_ENV=production` 擋 `test` 白名單 | proxy 403 | `web/Dockerfile` 加 **`dev` target** |
| **stale next dev 卡 3000 / stack 停掉** | test-login 500 | 殺 3000 佔用 / `up -d` 重起 |

## 未決

- GitLab `e2e` job 本身（dind + `docker:27` 的 compose 可用性 + seed 時序 quoting）**待首次 pipeline 實跑驗證**——compose + 容器 playwright 機制已本機證明綠。
- 平行測試靠「三隻獨立王 + 唯一 discordId」隔離；若之後測試變多，考慮每 spec 重置（Respawn 式）或 `--profile ci` 專屬 seed。
