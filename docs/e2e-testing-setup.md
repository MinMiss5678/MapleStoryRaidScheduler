# E2E 測試設定筆記（Playwright + 全 stack）

Playwright 打**全端**（瀏覽器 → Next.js `/api` proxy → .NET API → Postgres），驗核心流程整條通不通。
重點記**坑**——跑法照抄不難，難在時間相依業務規則、secure-context 這些。
計畫/進度看 `plans/2026-07-18-e2e-tests.md`；這份是「怎麼跑 + 為什麼這樣接」的參考。

> CI 現在跑 **GitHub Actions**（`.github/workflows/ci.yml` 的 `e2e` job），ubuntu runner 內建 Docker
> daemon，沒有 dind、沒有 buildx TLS/network 這類問題。dind 時代踩過的坑收在 `docs/gitlab-selfhost-ci-setup.md`
> （已淘汰的自架方案，僅存原理參考），這裡只留現在還會踩到的坑。

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
| `web/e2e/*.spec.ts` | 8 支測試（見下） |
| `web/e2e/helpers/auth.ts` | `loginAs()` — 呼叫 test-login 拿 cookie |
| `Presentation.WebApi/Controller/TestAuthController.cs` | `POST /api/test/login`（鎖非 Production） |
| `db/seed-e2e.sql` | E2E seed（單一未來 period + 4 隻獨立王） |
| `compose.e2e.yaml` | 全 stack + `e2e-playwright`(profile ci) |
| `web/Dockerfile`（`dev` target）/ `web/Dockerfile.e2e` | 前端 dev / playwright 執行器映像 |
| `.github/workflows/ci.yml`（`e2e` job）| ubuntu runner 內建 Docker，直接 compose 跑 E2E（PR 觸發，純文件變更會被 `changes` job 跳過） |

## 認證接縫（繞過 Discord OAuth）

`POST /api/test/login`（body: `discordId`/`discordName`/`role`）依捏造身分**直接發跟正式流程一樣的 cookie**（玩家 `jwtToken`；admin seed session + `sessionId{discordId}`），全程不碰 Discord。
🔴 **只在非 Production**（`_env.IsProduction()` → 404）+ 前端白名單 `test` 只在 `NODE_ENV!=production` 開放（雙保險）。

## Seed 模型（`db/seed-e2e.sql`）

- **TRUNCATE 所有交易資料** → seed 當下只留**一個** period（`GetActivePeriodAsync` 回最新 StartDate 的）。
- period 設 **`CURRENT_DATE + 10 ~ +17`（未來一週）**，一石二鳥：
  - 報名截止日（period 前一週）落在未來 → 報名開著。
  - StartDate `+10` **永遠晚於** `WeeklyPeriodJob` 會插的「下個重製日」（週二，≤ `+7`；見 `SlotDateCalculator.ResetDay`）→ 就算 backend 起來後 job 補插一顆 period（用 `/health/ready` 等待時 seed 可能早於 job 首次 tick），seed 這顆仍是**最新 StartDate = active** → 測試不受影響。
- **四隻獨立王隔離**平行測試互相干擾：`E2E王`（讀取/報名）、`E2E王2`（補位）、`E2E王3`（重排）、`E2E王4`（管理員存檔衝突——admin-conflict 會把隊的最後一人移除觸發連帶砍團，不可跟其他測試共用同一隊，否則平行跑會互踩）。

## 怎麼跑

### 本機（快，日常）
```bash
docker compose -f compose.e2e.yaml up -d                 # db + backend + frontend
# 等 backend /health/ready = 200（起完 + DB 連得到）後灌 seed：
docker compose -f compose.e2e.yaml exec -T e2e-db \
  env PGPASSWORD=e2e psql -U postgres -d presentationdb < db/seed-e2e.sql
cd web && npm run e2e                                     # 跑 8 支（reuse compose 前端）
```

### 容器化（CI 用的機制，本機可驗）
```bash
docker compose -f compose.e2e.yaml --profile ci run --build --rm e2e-playwright
```

### GitHub Actions CI
`.github/workflows/ci.yml` 的 `e2e` job：`docker compose --profile ci build` → `up -d e2e-frontend` → **等 backend `/health/ready`**（e2e-frontend 的 node 在 compose 網路內 fetch backend readiness 探針；補 `service_started` ≠ app ready 的洞）→ 灌 seed → `docker compose --profile ci run e2e-playwright`。ubuntu runner 原生 Docker，不需要 dind、不需要額外的 buildx TLS/network 設定。

### 收工
```bash
docker compose -f compose.e2e.yaml --profile ci down -v   # 含資料一起清
```

## 測試清單（8 支）

| spec | 驗 |
|---|---|
| `smoke` | 首頁未登入 Landing + 登入鈕 |
| `auth`（×2） | 玩家 → Dashboard；admin → `/admin/schedule` |
| `schedule` | seeded 玩家在排團結果看到自己的隊（讀取整串） |
| `register` | 新玩家報名 → 自動分配 → 入隊（寫入整串） |
| `fill` | 玩家補位進未滿的隊（E2E王2） |
| `admin-rebuild` | 管理員自動排團（E2E王3） |
| `admin-conflict` | 管理員存檔時隊伍已被異動/消失 → 顯示衝突提示，不假裝成功（E2E王4） |

## 🔴 踩過的坑（這份最值錢的部分）

| 坑 | 症狀 | 解 |
|---|---|---|
| **`GetActivePeriodAsync` = 最新 StartDate 非「含今天」** | 多 period 並存時 `GetByDiscordId` 回空 | seed **TRUNCATE 成單一 period** |
| **報名截止時間**（`GetDeadlineForPeriod` = period 前一週） | 報名回 500「已超過截止」 | period 設**未來一週** |
| **報名表單 Step2 選角色後 row 重新分組**（角色 select 消失） | `.nth(1)` 找不到 | **先選 boss 再選角色** |
| **補位場數規則**（`validateAddCharacter` #5：補位者場數 = 首位成員場數） | 補位靜默失敗、無 PUT | seed dummy 成員 `Rounds=0` |
| **secure-context**：非 localhost HTTP 存取 → `crypto.randomUUID`（idempotency key）+ Secure cookie 失效 | 寫入測試全掛 | `e2e-playwright` 用 **`network_mode: "service:e2e-frontend"`** → 走 `localhost:3000` |
| **前端 prod build** `NODE_ENV=production` 擋 `test` 白名單 | proxy 403 | `web/Dockerfile` 加 **`dev` target** |
| **stale next dev 卡 3000 / stack 停掉** | test-login 500 | 殺 3000 佔用 / `up -d` 重起 |

## CI on GitHub Actions（現況）

CI 已從自架 GitLab CE → gitlab.com 官方托管，最終遷到 **GitHub Actions**（`.github/workflows/ci.yml`）——
不用再管 runner 佈建，GitHub-hosted `ubuntu-latest` 內建 Docker + Docker Compose v2，e2e job 直接
`docker compose --profile ci build/up/run`，不需要 dind、不需要額外的 buildx 設定。

- **觸發**：PR 開/更新 + push `main`（post-merge 守門）。純文件變更（`docs/`、`plans/`、根目錄 `*.md`）
  由 `changes` job（`dorny/paths-filter`）判定，`e2e` 跟其餘 6 個必要 job 標記 `skipped`——
  GitHub 把 skipped 視為通過，不擋 merge，但也不用真的跑一次全 stack。
- **layer cache**：GitHub Actions 原生快取（`docker/build-push-action` 的 `cache-from/cache-to: type=gha`），
  比原本 GitLab dind 時代要另外接 registry cache（`type=registry` + 額外 registry 登入）簡單。
- **required status checks**：`main` 分支保護要求 `format`/`build`/`unit-test`/`integration-test`/
  `frontend-test`/`coverage`/`e2e` 全綠才能 merge，`enforce_admins` 開啟（admin 也不能跳過）。

## 用 gh 驅動/除錯 CI（CLI）

```bash
gh run list --branch <branch> --limit 5                 # 列這個分支最近幾次 run
gh run watch <run-id> --exit-status                      # 阻塞等待 run 完成，非輪詢（配 run_in_background）
gh run view <run-id> --json jobs                          # 看各 job 的 status/conclusion（含 skipped）
gh run view <run-id> --log-failed                         # 只看失敗 job 的 log
gh api repos/<owner>/<repo>/actions/jobs/<job-id>/logs    # 抓單一 job 的完整原始 log（run 未結束時也可用單 job）
gh workflow run deploy.yml                                # 手動觸發 deploy（workflow_dispatch）
```

## 未決

- 平行測試靠「四隻獨立王 + 唯一 discordId」隔離；測試變多時考慮每 spec 重置（Respawn 式）。
- 再加速可預建映像 push 成正式 image（e2e 直接 pull）；目前 4.5 分已達標故未做（YAGNI）。
