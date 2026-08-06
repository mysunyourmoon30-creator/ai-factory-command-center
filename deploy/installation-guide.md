# Installation Guide

Covers both installation paths named in the locked spec: the primary path (EF Core tooling) and
the fallback path (raw SQL scripts, for a machine without the .NET SDK). Also covers IIS Local
hosting, sample login, and troubleshooting.

## Prerequisites

- .NET SDK 10.0.302 or a compatible later 10.0 SDK — the repo also pins its own copy at
  `.dotnet\dotnet.exe` (gitignored), which every command below uses instead of a system-wide
  `dotnet` that may be a different major version.
- SQL Server Express or SQL Server Express LocalDB. The default connection targets
  `(localdb)\MSSQLLocalDB`.
- For the fallback SQL-script path: `sqlcmd` (ships with SQL Server / the SQL Server command-line
  tools, or install separately from Microsoft).
- For IIS Local hosting only: see [IIS Installation](#iis-installation) below.

## Primary path: EF Core tooling

```powershell
.\deploy\setup.ps1
```

This applies EF Core migrations, then seeds the canonical demo dataset (4 users, 3 machines, 10
raw materials, 5 formulations, 10 customer orders, 8 production plans, 25 material requirements).
It's idempotent — safe to run again against the same database.

Override the target database:

```powershell
.\deploy\setup.ps1 -ConnectionString "Server=.;Database=AI_Factory_CommandCenter;Trusted_Connection=True;TrustServerCertificate=True"
```

Then run the app:

```powershell
.\.dotnet\dotnet.exe run --project src\AI.Factory.Web
```

### Manual EF Migration steps (if you don't want the wrapper script)

```powershell
.\.dotnet\dotnet.exe ef database update --project src\AI.Factory.Infrastructure --startup-project src\AI.Factory.Infrastructure
.\.dotnet\dotnet.exe run --project src\AI.Factory.Web -- --seed-production-plans
```

The `--seed-production-plans` flag implies the three earlier seed flags (`--seed-identity`,
`--seed-master-data`, `--seed-customer-orders`) in dependency order — one invocation seeds
everything. Passing any `--seed-*` flag runs migrations + seeds, then **exits without serving**.

## Fallback path: raw SQL scripts (no .NET SDK required)

Run against an empty database, in order:

```
sqlcmd -S <server> -d <database> -I -i deploy\database.sql
sqlcmd -S <server> -d <database> -I -i deploy\seed-data.sql
```

**The `-I` flag (capital I, sets `SET QUOTED_IDENTIFIER ON` for the session) is required on both
files** — the app's own ADO.NET connections default this on automatically, but `sqlcmd` does not,
and several objects (a filtered unique index for alert deduplication, computed report views)
need it. Omitting `-I` fails with `CREATE INDEX failed because... QUOTED_IDENTIFIER` or similar.

`database.sql` is regenerated from EF Core migrations (`dotnet ef migrations script
--idempotent`) — EF migrations remain the schema source of truth. `seed-data.sql` is **generated
from a real seeded database, not hand-written**, specifically so its ASP.NET Identity
`PasswordHash` values are genuinely valid for `Demo@12345`; regenerate it the same way if the
canonical seed data ever changes (see `deploy/README.md`).

## SQL Setup

Both paths above assume the target database already exists (`CREATE DATABASE ...`) but is empty
— neither script creates the database itself. LocalDB creates a database automatically on first
connection if it doesn't exist; a full SQL Server instance needs an explicit `CREATE DATABASE`
first.

Connection string resolution (in this priority order): `ConnectionStrings:AiFactory` in
`appsettings.json` → the `AI_FACTORY_CONNECTION_STRING` environment variable → the app refuses to
start. `setup.ps1` sets both `AI_FACTORY_CONNECTION_STRING` (for the `dotnet ef` step) and
`ConnectionStrings__AiFactory` (the ASP.NET Core env-var-to-config-key convention, for the
`dotnet run` seeding step, since that step resolves the config key before the env var and would
otherwise silently ignore an override — see the script's own comment for why both are needed).

## EF Migration

`src/AI.Factory.Infrastructure/Persistence/Migrations` is the canonical migration history. To add
a new migration after a schema change:

```powershell
.\.dotnet\dotnet.exe ef migrations add <Name> --project src\AI.Factory.Infrastructure --startup-project src\AI.Factory.Infrastructure
.\.dotnet\dotnet.exe ef database update --project src\AI.Factory.Infrastructure --startup-project src\AI.Factory.Infrastructure
.\.dotnet\dotnet.exe ef migrations script --idempotent --project src\AI.Factory.Infrastructure --startup-project src\AI.Factory.Infrastructure -o deploy\database.sql
```

Regenerating `database.sql` after every migration change keeps the fallback path in sync with
the EF Core source of truth.

## Seed Data

Seeders live in `src/AI.Factory.Infrastructure/*/Canonical*Seeder.cs` and are driven by a stable
canonical date `T`, derived from `SO-DEMO-001.CreatedAt` so reruns never move dates. All seeders
insert missing rows only (matched by unique code/number) — safe to run repeatedly without
duplicating data.

| Flag | Seeds |
|---|---|
| `--seed-identity` | 4 demo users, one per role |
| `--seed-master-data` | + 10 raw materials, 5 formulations |
| `--seed-customer-orders` | + 10 customer orders |
| `--seed-production-plans` | + 3 machines, 8 production plans, 25 material requirements |

## PowerShell Setup

`deploy/setup.ps1` (primary path, above) and `deploy/publish-iis.ps1` (IIS hosting, below) are
both plain PowerShell — no module install required beyond `WebAdministration` for the IIS script
(ships with IIS itself). Run PowerShell as Administrator only for `publish-iis.ps1`; `setup.ps1`
needs no elevation.

## IIS Installation

Needed only for IIS Local hosting (`deploy/publish-iis.ps1`) — not needed to just run the app
with `dotnet run` for local development or demoing.

1. **Install the IIS Windows feature** (as Administrator):
   ```powershell
   Install-WindowsFeature -Name Web-Server, Web-Asp-Net45, Web-Net-Ext45, Web-ISAPI-Ext, Web-ISAPI-Filter
   ```
   (Or Control Panel → Programs → Turn Windows features on or off → Internet Information
   Services, with the same role services checked.)
2. **Install the ASP.NET Core Hosting Bundle** (not just the runtime) from
   [dotnet.microsoft.com/download/dotnet](https://dotnet.microsoft.com/download/dotnet) — this
   installs the ASP.NET Core Module (ANCM) v2 into IIS. Run `iisreset` (or reboot) afterward.
3. **Run `deploy\publish-iis.ps1` as Administrator**:
   ```powershell
   .\deploy\publish-iis.ps1
   ```
   Publishes the app, creates a "No Managed Code" app pool (ANCM hosts the .NET runtime
   out-of-process — a Managed Code pool would conflict with it), creates the IIS site, and ACLs
   the publish folder for the app pool identity. See the script's own header comment for
   parameters (site name, port, connection string).

Verify: browse to the configured port; if the site doesn't respond, check the Windows
Application event log — ANCM logs startup failures there (a missing Hosting Bundle is the most
common cause).

> **Not verified in this repository's own development environment**: the machine this guide was
> written on has no IIS, no Hosting Bundle, and no Administrator session, so `publish-iis.ps1`'s
> IIS-specific steps are written to Microsoft's documented process but have not been executed
> end-to-end. The `dotnet publish` step and its web.config editing were tested in isolation. See
> `docs/00_Project_Status.md`'s Day 13 acceptance evidence for the exact scope of what is and
> isn't verified.

## Sample Login

| Username | Password | Role | Can do |
|---|---|---|---|
| `admin.demo` | `Demo@12345` | Admin | Everything, including Simulate Update and Manage Demo Users |
| `manager.demo` | `Demo@12345` | Manager | Approve/Reject Purchase Requests, view Audit Log |
| `planner.demo` | `Demo@12345` | Planner | Create Customer Orders/Production Plans, submit Purchase Requests |
| `viewer.demo` | `Demo@12345` | Viewer | Read-only access to every screen |

Demo-only credentials — never reuse `Demo@12345` outside this demo environment.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `dotnet` commands fail or use the wrong SDK | System-wide `dotnet` is a different major version | Use `.\.dotnet\dotnet.exe` (repo-pinned SDK), not a bare `dotnet` on PATH |
| `CREATE INDEX failed because... QUOTED_IDENTIFIER` running `database.sql`/`seed-data.sql` via `sqlcmd` | `sqlcmd` doesn't default `SET QUOTED_IDENTIFIER ON` | Add `-I` to the `sqlcmd` command |
| `Incorrect syntax near the keyword 'VIEW'` | Only affects `database.sql` generated before this fix — already resolved in the current migrations (`CREATE VIEW` is wrapped in `EXEC(N'...')`) | Regenerate `database.sql` from current migrations if you see this |
| Seeding runs but the target database still looks empty | The seed step resolves `ConnectionStrings:AiFactory` from `appsettings.json` *before* an environment variable override | Use `setup.ps1` (sets both required env vars) rather than `AI_FACTORY_CONNECTION_STRING` alone |
| Login succeeds but every subsequent write returns 400 "Invalid antiforgery token" | Antiforgery cookie requires HTTPS (`SecurePolicy = Always`) | Use the `https` launch profile / an HTTPS URL, not plain HTTP |
| `/health/ready` returns 401/403 | It's Admin-only by design | Log in as `admin.demo` first, or use `/health/live` (anonymous, status-only) for a basic liveness check |
| IIS site returns 502.5 or doesn't start | ASP.NET Core Hosting Bundle missing, or app pool isn't "No Managed Code" | Install the Hosting Bundle (not just the runtime) and `iisreset`; confirm `managedRuntimeVersion` is empty on the app pool |
| Production error responses look different from what you expect | By design — `ASPNETCORE_ENVIRONMENT=Production` returns a minimal `ProblemDetails` (title/status only, no stack trace); `Development` shows the full Developer Exception Page | Check `Hosting environment:` in the startup log to confirm which mode is actually running — `dotnet run --launch-profile https` forces `Development` via `launchSettings.json` regardless of a shell-level `ASPNETCORE_ENVIRONMENT` override |
