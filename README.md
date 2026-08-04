# AI Factory Command Center

Locked-scope portfolio implementation for a factory planning and risk workflow. The current repository contains the Day 1 foundation only.

## Architecture

- `AI.Factory.Web`: the only executable ASP.NET Core host; Blazor Interactive Server is global.
- `AI.Factory.Api`: endpoint assembly referenced by the web host; it has no `Program.cs`.
- `AI.Factory.Core`: domain entities, enums, rules contracts, and deterministic time abstractions.
- `AI.Factory.Infrastructure`: EF Core, SQL Server, Identity, dependency registration, and migrations.
- `AI.Factory.UnitTests` and `AI.Factory.IntegrationTests`: foundation and host smoke verification.

There is intentionally no `.Client` project. UI components must not access `AppDbContext` directly.

## Prerequisites

- .NET SDK 10.0.302 or a compatible later 10.0 SDK
- SQL Server Express or SQL Server Express LocalDB

The default development connection uses Windows authentication with `(localdb)\\MSSQLLocalDB`. Override it without committing a secret:

```powershell
$env:AI_FACTORY_CONNECTION_STRING = 'your-connection-string'
```

## Verify Day 1

```powershell
dotnet restore AI.Factory.CommandCenter.sln
dotnet build AI.Factory.CommandCenter.sln --no-restore
dotnet test AI.Factory.CommandCenter.sln --no-build
dotnet ef database update --project src/AI.Factory.Infrastructure --startup-project src/AI.Factory.Infrastructure
```

The schema source of truth is the EF migration under `src/AI.Factory.Infrastructure/Persistence/Migrations`. The generated idempotent fallback is `deploy/database.sql`.

## Scope discipline

The project is locked to one business flow, 11 modules, 11 screens, four roles, four read-only AI tools, 14 application tables, three machines, and 15 required business tests. New scope is prohibited until the locked Definition of Done passes.
