# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## SDK: use the repo-local dotnet

`global.json` pins SDK `10.0.302`. The machine-wide `dotnet` on PATH is 9.0.200 and **fails** on every command in this repo. Use the gitignored local SDK at `.dotnet\dotnet.exe`:

```bash
./.dotnet/dotnet.exe --version
```

All commands below assume `dotnet` means `.\.dotnet\dotnet.exe` (PowerShell: `& "$PWD\.dotnet\dotnet.exe" ...`).

## Commands

```bash
./.dotnet/dotnet.exe build AI.Factory.CommandCenter.sln
```

```bash
./.dotnet/dotnet.exe test AI.Factory.CommandCenter.sln
```

Single test / single class (xunit):

```bash
./.dotnet/dotnet.exe test tests/AI.Factory.IntegrationTests --filter "FullyQualifiedName~ProductionPlanTests.Planner_creates_plan"
```

Formatting is a release gate — it must pass with no changes:

```bash
./.dotnet/dotnet.exe format --verify-no-changes
```

Run the app (only `AI.Factory.Web` is executable):

```bash
./.dotnet/dotnet.exe run --project src/AI.Factory.Web
```

### Database

EF migrations are the schema source of truth. The `dotnet-ef` 10.0.10 local tool is declared in the root `dotnet-tools.json` (not `.config/`) and is already restored. `AI.Factory.Infrastructure` is both project and startup project — `AppDbContextFactory` supplies the design-time connection string.

```bash
./.dotnet/dotnet.exe ef database update --project src/AI.Factory.Infrastructure --startup-project src/AI.Factory.Infrastructure
```

After any migration change, regenerate the idempotent script into `deploy/database.sql` (`ef migrations script --idempotent`).

Connection string resolution: `ConnectionStrings:AiFactory` → `AI_FACTORY_CONNECTION_STRING` env var → throw. Default dev target is `(localdb)\MSSQLLocalDB` / `AI_Factory_CommandCenter`.

### Full setup

`deploy/setup.ps1` is the primary bootstrap (SDK check + migrations + build) for a fresh clone. `deploy/installation-guide.md` covers the no-SDK fallback (`sqlcmd` against `deploy/database.sql`) and IIS publish/troubleshooting via `deploy/publish-iis.ps1`.

### Seeding

The web host doubles as the seeder: passing a `--seed-*` flag runs migrations + seeds, then **returns without serving**. Seeds are idempotent and ordered — a later flag implies the earlier ones.

```bash
./.dotnet/dotnet.exe run --project src/AI.Factory.Web -- --seed-production-plans
```

Flags, in dependency order: `--seed-identity`, `--seed-master-data`, `--seed-customer-orders`, `--seed-production-plans`. Demo users are `admin.demo` / `manager.demo` / `planner.demo` / `viewer.demo`, password `Demo@12345` (demo-only).

## Architecture

Modular monolith, one host, one origin. Dependency direction is enforced by project references:

- `AI.Factory.Core` — no dependencies. Entities, enums, DTOs, command records, service **interfaces**, pure calculation rules (`ProductionPlanRules`, `OrderRiskCalculator`), exception types, `FixedTimeProvider`.
- `AI.Factory.Infrastructure` — references Core. `AppDbContext`, migrations, Identity, service implementations, canonical seeders, `AddInfrastructure` DI registration.
- `AI.Factory.Api` — references **Core only**. Minimal-API endpoint mapping extensions, the `MachineHub` SignalR hub (live machine-monitoring push), and authorization policy registration. A class library with no `Program.cs`; it resolves services through Core interfaces.
- `AI.Factory.Web` — the only executable. References Api + Infrastructure and composes them in `Program.cs`. Blazor Web App, global Interactive Server.

Hard rules: no `.Client` project, no WebAssembly/Auto render mode, and Blazor components must never touch `AppDbContext`.

### The shared-service pattern

Every feature is one vertical slice with a single application service consumed by *both* the UI and the API:

1. `Core/<Area>/<Area>Contracts.cs` — DTOs, command records, `I<Area>Service`, and any pure rule statics.
2. `Infrastructure/<Area>/<Area>Service.cs` — implementation; primary-constructor injection of `AppDbContext`, `IAuditWriter`, `IOrderRiskCalculator`, `TimeProvider`.
3. `Api/<Area>EndpointExtensions.cs` — `Map<Area>Endpoints()` under `/api/...`, called from `EndpointRegistrationExtensions.MapAiFactoryEndpoints`.
4. `Web/Components/Pages/<Screen>.razor` — `@inject I<Area>Service`, calls the same methods directly.
5. Register the scoped service in `Infrastructure/DependencyInjection.cs`.

Because the UI calls services in-process, services that mutate business data take a `ClaimsPrincipal actor` and re-check roles themselves (`EnsureCanManage`) rather than trusting the endpoint policy. `IMasterDataService` is the one exception with no actor parameter, relying on endpoint/UI policy only.

### Write-path conventions

Every API write goes through a local `ExecuteWriteAsync` helper that explicitly calls `IAntiforgery.ValidateRequestAsync` (Blazor's `UseAntiforgery` does not cover these JSON endpoints) and maps exceptions:

| Exception | Response |
|---|---|
| `DomainValidationException` | 400 validation problem |
| `BusinessConflictException` | 409 |
| `ConcurrencyConflictException` | 409 |

`UnauthorizedAccessException` from a service is deliberately *not* caught — authorization is expected to have already returned 403 via the named policy on the endpoint.

Concurrency: every mutable entity has a `RowVersion`. Update/transition commands carry the client's `RowVersion`; services compare it, then set `Entry(x).Property(RowVersion).OriginalValue` before saving.

Computed fields are server-only and never accepted from a client: `RequiredBatch` (`CEILING(Quantity / BatchSize)`), material `RequiredQuantity` (`RequiredBatch * WeightPerBatch`), and `RiskStatus` (from delivery-vs-completion buffer days: `< 0` Critical, `<= 1` Warning, else Normal). Lifecycle status moves one step forward only (Draft→Planned→InProduction→Completed; plans start at Planned) and is separate from computed risk.

### Time

`TimeProvider` is injected everywhere; never call `DateTime.UtcNow`. Production/Development get `TimeProvider.System`; the `Demo` environment requires `Demo:FixedUtc` and throws at startup without it; tests register `FixedTimeProvider` at canonical `T = 2026-08-04`. Seeders derive `T` from the stored `SO-DEMO-001.CreatedAt` so reruns never move dates.

### Security wiring (`Program.cs`)

Identity cookie `AI.Factory.Auth` (HttpOnly, Secure, SameSite=Lax, 8h sliding). Cookie events convert redirects to 401/403 for paths under `/api`. `UseAuthorizationAudit` middleware writes an `Unauthorized Access` audit row for every 401/403. Login is rate-limited; request bodies capped at 1 MiB. Eleven named policies in `AuthorizationRegistrationExtensions` map the four roles (`Admin`, `Manager`, `Planner`, `Viewer`); reference them via `PolicyNames.*`, and use the same constants for `<AuthorizeView Policy="...">` in Razor.

## Tests

- `AI.Factory.UnitTests` — pure rules plus **invariant guards**: `FoundationContractTests` asserts exactly 14 non-Identity tables and that `PurchaseRequests.SourceProductionPlanId` has no single-column unique index. Adding a table or that index breaks the build gate by design.
- `AI.Factory.IntegrationTests` — `AiFactoryWebApplicationFactory` boots the real host with an EF InMemory database, a fixed `TimeProvider`, ephemeral data protection, and all five seeders (`DemoIdentitySeeder` plus the four `Canonical*Seeder`s: MasterData, CustomerOrder, ProductionPlan, Procurement). Tests drive real HTTP: fetch a token from `/api/auth/antiforgery`, send it as the `X-XSRF-TOKEN` header, log in via form POST to `/api/auth/login`. Client must use `AllowAutoRedirect = false` and an `https://` base address.

Note the InMemory provider is non-relational, so transaction code guards on `Database.IsRelational()`.

## Locked scope

`docs/00_Master_Scope.md` records non-negotiable boundaries derived from an external checksummed spec: 14 application tables, 11 modules/screens, 4 roles, 3 machines, 4 read-only AI tools, 15 required business tests. Do not add tables, projects, or features outside it; AI (Ollama/Qwen) is read-only, grounded, and allow-listed — never a write path.

`docs/00_Project_Status.md` is the live roadmap and handoff record (day-by-day gates, acceptance evidence, "next task", "do not change" list). Read it before starting work and update it when a day's work completes. Days 1–13 are done — all 11 modules/screens, 4 roles, 3 machines, 4 AI tools, and all 15 required business tests are complete and verified. Day 14 is portfolio/deployment wrap-up (demo video, CV, applications), not further coding.

Its "do not change" list is longer than the locked-scope summary above — e.g. Serializable isolation on purchase-request creation, `InvariantCulture` on every date-format call, the filtered unique index backing active alerts, `IMachineUpdateNotifier` registration order. Check it before touching computed fields, isolation levels, culture-sensitive formatting, or indexes.
