# AI Factory Command Center

Locked-scope portfolio implementation for a factory planning and risk workflow. Days 1-5 now cover the foundation through transactional Production Plan creation.

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

## Verify

```powershell
dotnet restore AI.Factory.CommandCenter.sln
dotnet build AI.Factory.CommandCenter.sln --no-restore
dotnet test AI.Factory.CommandCenter.sln --no-build
dotnet ef database update --project src/AI.Factory.Infrastructure --startup-project src/AI.Factory.Infrastructure
```

Seed the locked demo identities (safe to run repeatedly):

```powershell
dotnet run --project src/AI.Factory.Web -- --seed-identity
dotnet run --project src/AI.Factory.Web -- --seed-master-data
dotnet run --project src/AI.Factory.Web -- --seed-customer-orders
dotnet run --project src/AI.Factory.Web -- --seed-production-plans
```

Demo users are `admin.demo`, `manager.demo`, `planner.demo`, and `viewer.demo`; the locked demo-only password is `Demo@12345`. These credentials must never be reused outside the demo environment.

Authentication uses the single-host Identity cookie with `HttpOnly`, `Secure`, and `SameSite=Lax`. Every form write requires an antiforgery token. API authorization failures return HTTP 401/403 and are written to the append-only audit log.

The master-data seed adds the locked 10 raw materials and five balanced formulations. It inserts missing codes only and is safe to run repeatedly.

The Customer Order seed adds the locked 10 orders. Its canonical date `T` is stored in `SO-DEMO-001.CreatedAt`, so rerunning the seed neither duplicates orders nor moves their dates.

The Production Plan seed adds the three machine references, eight locked plans, and their computed Material Requirements. It uses the same stored `T` and is safe to run repeatedly.

The schema source of truth is the EF migration under `src/AI.Factory.Infrastructure/Persistence/Migrations`. The generated idempotent fallback is `deploy/database.sql`.

## Scope discipline

The project is locked to one business flow, 11 modules, 11 screens, four roles, four read-only AI tools, 14 application tables, three machines, and 15 required business tests. New scope is prohibited until the locked Definition of Done passes.
