# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Language

使用**繁體中文**進行溝通與回覆，並用**台灣慣用技術詞、避免大陸用語**（溝通、程式碼註解、commit、計畫文件一律適用；寫前自查）。常見對照：

| 大陸用語 | 台灣用語 |
|---|---|
| 落地（實現/導入）、全棧 | 實作／導入／實現、全端 |
| 數據、信息、網絡、用戶 | 資料、資訊、網路、使用者 |
| 服務器、內存、硬盤、緩存、隊列 | 伺服器、記憶體、硬碟、快取、佇列 |
| 事務（DB）、回滾、默認/缺省、部署 | 交易、回溯／復原、預設、部署 |
| 智能、反饋、文件夾、視頻、屏幕、質量 | 智慧、回饋、資料夾、影片、螢幕、品質 |
| 對象（OOP）、函數、數組、指針、調用 | 物件、函式、陣列、指標、呼叫 |

（「對象」僅 OOP 情境改「物件」；作「目標/對象」解時保留。「映射」CS 情境可保留或用「對應」。）

## Commands

### Backend

```bash
# Run the API (from Presentation.WebApi/)
dotnet run

# Build the solution
dotnet build

# Run all tests
dotnet test

# Run a single test class
dotnet test --filter "FullyQualifiedName~TeamLeaderServiceTests"
```

### Frontend

```bash
cd web
npm install
npm run dev        # dev server (Turbopack)
npm run build
npm run test       # Vitest
```

### Docker (recommended for local dev)

```bash
docker compose up -d     # start all services
docker compose down      # stop all services
```

Services: `database` (PostgreSQL 18, port 5432), `backend` (.NET 9, port 5230), `frontend` (Next.js, port 3000), `bot` (Discord bot), `cloudflared` (tunnel).

### 本機 WSL Docker（Windows）踩雷要點

本機 Docker 跑在 WSL2 Ubuntu（原生 dockerd、非 Docker Desktop）。多步驟本機作業（E2E / demo / 壓測）**一定要知道的一件事**：

- **別讓 WSL 閒置自動關閉停掉容器**：每個 `wsl … bash -lc '…'` 是獨立呼叫，呼叫「之間」若沒常駐程序撐著，WSL2 閒置就關整個 distro → 容器被停（症狀：db/容器 **exit 0**、你上一條指令結束後約 1–2 分死，害 backend/bot 連不到 DB）。**要活一段時間的本機 stack 用「前景 `docker compose up`（不加 `-d`）當背景常駐」撐住 WSL，別用 `up -d`；平常也別手動 `wsl --shutdown`（`autoMemoryReclaim` 會自己收記憶體，shutdown 反而觸發冷啟網路不穩）。**

## Architecture

Clean Architecture with DDD — dependency flows inward: Presentation → Application → Domain ← Infrastructure.

| Project | Role |
|---|---|
| `Domain/` | Entities, repository interfaces, no external dependencies |
| `Application/` | DTOs, service interfaces (`Interface/`), query interfaces (`Queries/`), `AuthAppService` |
| `Infrastructure/` | Dapper repository implementations, services, background jobs, Discord integration |
| `Presentation.WebApi/` | ASP.NET Core controllers, middleware |
| `Presentation/` | Discord bot console app (DSharpPlus) |
| `web/` | Next.js 15 frontend (App Router, Tailwind, Shadcn/UI) |
| `Test/` | xUnit + Moq unit tests |
| `Utils/` | SQL builder helpers, JSON converters |

### Request Lifecycle

1. `UnitOfWorkMiddleware` opens a DB transaction before the controller runs, commits on success, rolls back on exception.
2. `AuthenticationMiddleware` validates JWT (regular users) or SessionId (admins) before protected endpoints.
3. Controllers call **Application service interfaces**; implementations live in `Infrastructure/Services/`.
4. **Repository interfaces** are in `Domain/Repositories/`; Dapper implementations inject `DbContext` (the UoW connection wrapper).

### Key Patterns

- **Unit of Work**: `IUnitOfWork` / `UnitOfWork` wraps a single `NpgsqlConnection` + `NpgsqlTransaction`. All repositories receive it via DI.
- **Read/Write split (CQRS-lite)**: Query interfaces (`ICharacterQuery`, `ITeamCandidateQuery`, `ITeamMembershipQuery`, etc. in `Application/Queries/`) handle reads; service interfaces handle writes.
- **Leader-led team formation (core engine)**: `TeamLeaderService`. Any logged-in user opens a team (`Source = "leader"`, see `TeamSlotSource`) with `Kind = Scheduled` (agreed `SlotDateTime`, **must not be in the past** — period-less: teams no longer resolve/bind a `Period`) or `Kind = Instant` (time = now, `ExpiresAt = now + 3h` TTL). Recruitment requirements (`TeamSlotRequirement`: `Count` + `MinClearCount` + `Jobs[{Job, MinAttackPower}]`) only drive **candidate filtering + the frontend recruitment hint** — they do **not** enforce team composition; capacity is solely `Boss.RequireMembers`. Two directions: **Pull** (leader `InviteMember` → `Invited` → player `AcceptInvite`/`DeclineInvite`) and **Push** (player `Apply` → `Applied` → leader `Approve`/`Reject`). Confirming (`ConfirmMemberAsync`, shared by accept/approve) takes a per-team advisory lock (`AcquireTeamSlotEditLockAsync`), re-reads confirmed count vs `RequireMembers`, then flips status via `xmin` optimistic lock; cross-team same-slot double-booking is blocked by `uq_tsc_confirmed_overlap` (23505 → 409). Plus `LeaveTeam` and leader transfer.
- **Candidate filtering**: Scheduled candidates = `IsSeekingRaid` characters × `PlayerAvailabilityStanding` (standing weekly slots, overridden per-date by `PlayerAvailabilityOverride`) overlapping the team time, then filtered by job / `MinAttackPower` / `MinClearCount` (summed from `CharacterBossClear`). Instant candidates come from the `LfgIntent` board (skip time matching). The authoritative job list is the `JOBS` constant in `web/constants/jobs.ts` (`/teams/new` uses it as a multi-select; leaders can save reusable requirement presets in localStorage — no backend). `CharacterBossClear` (per character per boss clear count) is self-entered at `/character` via `POST /api/Character/{id}/BossClears`.
- **Retired (period-less Phase 4c/4d)**: the old 每週報名→自動排團 engine (`RegisterService`/`TeamSlotAutoAssignService`/`ScheduleService`/`TeamSlotMergeService`/`TeamSlotService`), 補位/fill, 範本 (`BossTemplate`/`BossTemplateRequirement`), `JobCategory`, `Period`/`WeeklyPeriodJob`, and 報名截止 (deadline) are all removed (code + tables). Do **not** reference them; see `plans/2026-08-12-period-less-phase4cd-cleanup.md`.
- **Discord dual-auth**: Regular players receive a JWT; admins receive a `SessionId` stored in the `session` table. Role mapping from Discord role IDs → system roles is in `DiscordRoleMapping`.

### Database

- PostgreSQL 18 via Dapper (no EF Core migrations — SQL is hand-written).
- Always use `DateTimeOffset` (C#) / `timestamptz` (Postgres) for timestamps.
- Custom `TimeOnlyTypeHandler` in `Infrastructure/Dapper/` handles `TimeOnly` ↔ Postgres mapping.
- Custom JSON converter in `Utils/` handles `long`/`ulong` serialization for JavaScript number precision.

### Frontend Notes

- All UI components must support dark mode via `next-themes` (`useTheme` hook).
- Data fetching uses TanStack React Query.
- Components are built on Shadcn/UI + Lucide React.

## Testing

- Backend tests: `Test/` — one file per service (e.g., `TeamLeaderServiceTests.cs`). Uses Moq to mock repositories and service dependencies.
- Frontend tests: `web/__tests__/` — Vitest + React Testing Library.
- New features should have accompanying unit tests.
- **E2E already exists — use it, don't hand-roll browser automation.** `web/e2e/` (Playwright) covers the core flows end-to-end: `auth`, `smoke`, `register` (=profile: standing availability + seeking opt-in), `leader-led` (Push apply→approve), `leader-led-candidates` (Pull invite→accept), `leader-led-transfer`, `leader-led-auto-revoke`, `availability-override`, `instant-lfg`. Runs against `compose.e2e.yaml` (see `docs/e2e-testing-setup.md`); CI runs it on every PR. To validate a flow (or check a change doesn't regress), run these — do **not** reconstruct the flow by scripting a browser (CDP/Puppeteer) from scratch. They log in via the `/api/test/login` backdoor, which is **disabled when `ASPNETCORE_ENVIRONMENT=Production`**, so E2E runs locally/CI, not against prod.
- **Run E2E locally with Docker BEFORE pushing / before claiming a change is verified — do NOT defer E2E to CI.** If a change touches any E2E-covered flow (profile/leader-led/instant/availability-override/auth), or E2E goes red, verify it locally first; don't push on unit+integration + reasoning alone and let CI find the regression. Steps (from `docs/e2e-testing-setup.md`): `docker compose -f compose.e2e.yaml up -d --build e2e-frontend` → wait `curl localhost:5230/health/ready`=200 → seed: `docker compose -f compose.e2e.yaml exec -T e2e-db env PGPASSWORD=e2e psql -U postgres -d presentationdb < db/seed-e2e.sql` → run (CI-identical, containerised): `docker compose -f compose.e2e.yaml --profile ci run --build --rm e2e-playwright` → teardown: `docker compose -f compose.e2e.yaml --profile ci down -v`. Read the run's actual output (`… passed`), not just the exit code.

## Deploying & verifying (learned the hard way)

- **A prod deploy verifies the *seams*, not the logic.** CI (unit / integration / E2E) already proves the business logic (auto-scheduling, fill, conflict handling) works — and logic is identical in every env, so do **not** re-test it manually on prod. A prod smoke test should only cover what differs from CI and what E2E can't reach:
  1. rollout healthy — pods Ready, no crash loop;
  2. **migrations actually applied** (check `schema_migrations`) **+ reference data seeded** (`DiscordRoleMapping` — no admin UI, not in migrations; unseeded ⇒ *nobody can log in*);
  3. secrets / config wired (real DB conn, JWT, Discord client id/secret);
  4. **real Discord OAuth** — test-login bypasses it entirely, so this is the one genuinely prod-only functional check; plus bot / mail / Sentry;
  5. external reachability — public DNS → cloudflared tunnel → frontend → backend.
- **Monitor CI with `gh run watch <run-id> --exit-status` (server-side blocking wait), never by polling `gh run list` in a loop.** Pair with `run_in_background: true` so it doesn't hold the turn; on red, `gh run view <id> --log-failed`.
- Manual deploy = `k8s/deploy.ps1` (first time) / `k8s/rollout.ps1 <svc>` (update); full flow incl. the mandatory post-deploy seed is in `docs/deployment.md`.
