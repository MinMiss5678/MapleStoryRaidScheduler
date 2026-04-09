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
- **Auto-scheduling engine**: `RegisterService.AutoAssignAsync()` → `TeamSlotService` — when a player registers, the system immediately tries to match the character to an existing temporary team slot or creates a new one. `IsManual = true` protects manually-assigned members from batch re-scheduling.
- **Fill system (補位)**: After a slot is published, players can fill vacant roles. The frontend computes missing job categories from `BossTemplateRequirement`; filled members are flagged `IsManual = true`.
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
