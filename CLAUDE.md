# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Language

使用**繁體中文**進行溝通與回覆。

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
dotnet test --filter "FullyQualifiedName~RegisterServiceTests"
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
- **Read/Write split (CQRS-lite)**: Query interfaces (`ICharacterQuery`, `IPeriodQuery`, etc. in `Application/Queries/`) handle reads; service interfaces handle writes.
- **Auto-scheduling engine**: `RegisterService.CreateAsync()` calls `TeamSlotAutoAssignService.AutoAssignAsync()` — when a player registers, the system immediately tries to match each character to an existing auto-created team slot (`Source = "auto"`, see `TeamSlotSource`) or creates a new one. Matching (`FindMatchingTeam`) is purely by **same boss + slot not full (capacity = `Boss.RequireMembers`) + availability overlap** — it does **not** consider job category or `BossTemplateRequirement`. `IsManual = true` protects manually-assigned members from batch re-scheduling (which produces `Source = "admin"` slots).
- **Fill system (補位)**: Whenever a team slot has vacancies (current members < the boss's `RequireMembers`), players can fill the vacant roles — there is no publish gate (the old `IsPublished` flag was removed in migration `000003`; team lifecycle is now driven by `TeamSlot.Source`). The frontend computes missing job categories from `BossTemplateRequirement`; filled members are flagged `IsManual = true`.
- **JobCategory is display-only + optional per job**: `JobCategory` (job → category) and `BossTemplateRequirement` only drive the **frontend fill-suggestion hint** (`getMissingSlots` in `PlayerRaidTeamCard.tsx`). Neither auto-scheduling nor the `/Fill` endpoint enforces category composition — the backend only counts members vs `RequireMembers`. Key design points:
  - **Only jobs that belong to a meaningful group need a `JobCategory` row** (e.g. 劍士 = 英雄/黑騎士/聖騎士, 法師 = 火毒/冰雷/主教, 高單體 = 箭神/槍神). Jobs shouted individually (夜使者, 拳霸, …) can have **no row at all**.
  - The authoritative job list is the `JOBS` constant in `web/constants/jobs.ts` — the template requirement dropdown and admin add-character filter source individual-job options from `JOBS`, **not** from `Object.keys(jobMap)`. So an uncategorized job still appears and is targetable by specific job.
  - `getMissingSlots` maps a filled character's job to its category via `jobMap[job] || job` — a job with no category falls back to itself, so it only matches specific-job requirements (correct). Unspecified slots up to `RequireMembers` auto-become generic "補位" (any DPS).
  - Keep categories coarse and only for real groupings; do **not** build fine-grained stat-based (力/敏/賊/法) or multi-tag categories — a template can target a specific job directly when needed.
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

- Backend tests: `Test/` — one file per service (e.g., `RegisterServiceTests.cs`). Uses Moq to mock repositories and service dependencies.
- Frontend tests: `web/__tests__/` — Vitest + React Testing Library.
- New features should have accompanying unit tests.
- **E2E already exists — use it, don't hand-roll browser automation.** `web/e2e/` (Playwright) covers the core flows end-to-end: `auth`, `register` (報名 → 自動排團), `fill` (補位), `admin-conflict` / `admin-rebuild`, `schedule`, `smoke`. Runs against `compose.e2e.yaml` (see `docs/e2e-testing-setup.md`); CI runs it on every PR. To validate a flow (or check a change doesn't regress), run these — do **not** reconstruct the flow by scripting a browser (CDP/Puppeteer) from scratch. They log in via the `/api/test/login` backdoor, which is **disabled when `ASPNETCORE_ENVIRONMENT=Production`**, so E2E runs locally/CI, not against prod.
- **Run E2E locally with Docker BEFORE pushing / before claiming a change is verified — do NOT defer E2E to CI.** If a change touches any E2E-covered flow (register/fill/admin/schedule/auth), or E2E goes red, verify it locally first; don't push on unit+integration + reasoning alone and let CI find the regression. Steps (from `docs/e2e-testing-setup.md`): `docker compose -f compose.e2e.yaml up -d --build e2e-frontend` → wait `curl localhost:5230/health/ready`=200 → seed: `docker compose -f compose.e2e.yaml exec -T e2e-db env PGPASSWORD=e2e psql -U postgres -d presentationdb < db/seed-e2e.sql` → run (CI-identical, containerised): `docker compose -f compose.e2e.yaml --profile ci run --build --rm e2e-playwright` → teardown: `docker compose -f compose.e2e.yaml --profile ci down -v`. Read the run's actual output (`… passed`), not just the exit code.

## Deploying & verifying (learned the hard way)

- **A prod deploy verifies the *seams*, not the logic.** CI (unit / integration / E2E) already proves the business logic (auto-scheduling, fill, conflict handling) works — and logic is identical in every env, so do **not** re-test it manually on prod. A prod smoke test should only cover what differs from CI and what E2E can't reach:
  1. rollout healthy — pods Ready, no crash loop;
  2. **migrations actually applied** (check `schema_migrations`) **+ reference data seeded** (`DiscordRoleMapping`, `JobCategory` — no admin UI, not in migrations; unseeded ⇒ *nobody can log in*);
  3. secrets / config wired (real DB conn, JWT, Discord client id/secret);
  4. **real Discord OAuth** — test-login bypasses it entirely, so this is the one genuinely prod-only functional check; plus bot / mail / Sentry;
  5. external reachability — public DNS → cloudflared tunnel → frontend → backend.
- **Monitor CI with `gh run watch <run-id> --exit-status` (server-side blocking wait), never by polling `gh run list` in a loop.** Pair with `run_in_background: true` so it doesn't hold the turn; on red, `gh run view <id> --log-failed`.
- Manual deploy = `k8s/deploy.ps1` (first time) / `k8s/rollout.ps1 <svc>` (update); full flow incl. the mandatory post-deploy seed is in `docs/deployment.md`.
