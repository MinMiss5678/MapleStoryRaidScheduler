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
| `web/e2e/*.spec.ts` | 8 支測試（見下） |
| `web/e2e/helpers/auth.ts` | `loginAs()` — 呼叫 test-login 拿 cookie |
| `Presentation.WebApi/Controller/TestAuthController.cs` | `POST /api/test/login`（鎖非 Production） |
| `db/seed-e2e.sql` | E2E seed（單一未來 period + 4 隻獨立王） |
| `compose.e2e.yaml` | 全 stack + `e2e-playwright`(profile ci) |
| `web/Dockerfile`（`dev` target）/ `web/Dockerfile.e2e` | 前端 dev / playwright 執行器映像 |
| `.gitlab-ci.yml`（`e2e` job）| dind 起 compose 跑 E2E（coverage 全過後自動觸發） |

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

### GitLab CI
`.gitlab-ci.yml` 的 `e2e` job（coverage 全過後自動觸發）：dind 起 compose → **等 backend `/health/ready`**（e2e-frontend 的 node 在 compose 網路內 fetch backend readiness 探針；補 `service_started` ≠ app ready 的洞）→ seed → 跑 `e2e-playwright`。

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
| `register` | 新玩家報名 → 自動排隊 → 入隊（寫入整串） |
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
| **dind volume 掛載**看不到 job 容器檔案 | CI 掛載跑不了 | Playwright 映像走 **build context**（`Dockerfile.e2e` COPY 源碼） |
| **前端 prod build** `NODE_ENV=production` 擋 `test` 白名單 | proxy 403 | `web/Dockerfile` 加 **`dev` target** |
| **stale next dev 卡 3000 / stack 停掉** | test-login 500 | 殺 3000 佔用 / `up -d` 重起 |
| **buildx 讀不到 env TLS**（dind+TLS，CI） | `could not create a builder instance with TLS data loaded from environment` | `docker context create` 包 TLS，再 `buildx create <ctx>` |
| **buildkit RUN 無對外網路**（dind，CI） | `dotnet restore`/`npm ci` NU1301 timeout（連不到 nuget/npm） | builder 加 `--driver-opt network=host` |

## CI on gitlab.com（官方托管）

自架 GitLab CE 閒置吃 ~4GB → 改用 **gitlab.com 官方托管**（GitLab + runner 都在他們機器，本機 RAM 全省）。
- **runner**：免費預設 `saas-linux-small-amd64` = **2 vCPU / 8 GB / 30 GB**，內建 Docker（dind 可跑）。
- **遷移**：GitHub 匯入 project（或加 gitlab.com remote 推）；免費 CI 要**綁卡驗證**才給 shared runner。
- `.gitlab-ci.yml` 的 `e2e` job（coverage 全過後自動觸發，不再 `when: manual`）已實跑綠：**7 passed**。

### layer cache（已驗證綠）
buildx registry cache（`--cache-from/--cache-to type=registry`，`docker-container` driver + registry login）+ compose 改 `image:` 引用（`E2E_REGISTRY=$CI_REGISTRY_IMAGE`）。dind 每次全新 → 靠 registry cache 讓 `dotnet restore`/`npm ci` 層命中跳過。

| 跑 | 時間 | 結果 |
|---|---|---|
| 第 1 跑（無 cache，建 + 推 cache） | 5.4 分 | 7 passed |
| 第 2 跑（大量 `CACHED` 命中） | **4.5 分** | 7 passed（**<5 分達標**） |

只快 ~17%：base image 拉取 + `--cache-to` 每次重推 + compose up/測試（~2 分固定）不受 cache 影響。想再快 → 預建映像 push 成正式 image、e2e 直接 `pull`（YAGNI，4.5 分已達標）。

## 用 glab 驅動/除錯 CI（CLI）

```bash
glab auth status                                        # 確認登入 gitlab.com
glab ci status                                          # 當前 branch 最新 pipeline
glab ci list -R <group/project>                         # 列 pipelines
glab ci lint .gitlab-ci.yml                             # 驗 CI 設定語法
glab api "projects/<id>/pipelines/<pid>/jobs"           # 找 job id（trigger 要數字 id 不是名字）
glab ci run                                              # 重觸發整條 pipeline（e2e 已自動，不需 trigger）
glab api "projects/<id>/jobs/<job-id>/trace" | tail     # 非阻塞讀 log（trace 會阻塞跟到結束）
glab api --method POST "projects/<id>/jobs/<job-id>/cancel"   # 取消
```
（project 數字 id 可從任一 job log 的 `project-<id>` 看到。）

## 未決

- 平行測試靠「四隻獨立王 + 唯一 discordId」隔離；測試變多時考慮每 spec 重置（Respawn 式）。
- 再加速可預建映像 push 成正式 image（e2e 直接 pull）；目前 4.5 分已達標故未做（YAGNI）。
