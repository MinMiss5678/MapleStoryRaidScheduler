# Project Guidelines

## Project Overview
MapleStory Raid Scheduler is a web application designed to help players organize and schedule boss raids.

### Tech Stack
- **Backend:** .NET 9 (C# 13)
- **Frontend:** Next.js (App Router, TypeScript)
- **Database:** PostgreSQL (implied by Dapper/Infrastructure)
- **Other:** Docker, Discord integration (Infrastructure/Discord)

### Project Structure
The solution follows a layered architecture:

- **Domain:** Contains core entities, interfaces, and business logic.
- **Application:** Contains DTOs, interfaces, services, and queries.
- **Infrastructure:** Implements external concerns like database access (Dapper), background jobs, and Discord integration.
- **Presentation.WebApi:** The ASP.NET Core Web API project (Controllers, Middleware).
- **web:** The Next.js frontend application.
- **Test:** Contains unit and integration tests.

### Language
- **使用中文**: 請使用中文進行溝通與回覆。

### Development Guidelines
- Follow the existing coding style for both C# and TypeScript.
- Ensure new features are accompanied by relevant tests where possible.
- Use Docker for local development environment (see `compose.yaml`).
- Use Unit Test

### 支援深色模式
- 在 Next.js 中，可以使用 `useTheme` hook 來判斷使用者的系統主題設定，並根據需要調整應用程式的主題。
- 請確保所有 UI 元素都支援深色模式，包括按鈕、輸入框、下拉選單等。

### Database data types
- Use `DateTimeOffset` for timestampz.