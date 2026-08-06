# Deployment artifacts

`database.sql` is generated from EF Core migrations with the idempotent option. EF migrations remain the schema source of truth.

See [`installation-guide.md`](installation-guide.md) for full step-by-step instructions (prerequisites, both paths below, IIS Installation, Sample Login, Troubleshooting). This file stays a short index.

## Primary path

Run `setup.ps1` from the repo root. It applies EF Core migrations, then seeds the canonical demo dataset. Idempotent - safe to rerun.

## Fallback path (no .NET SDK / EF tooling available)

Run `database.sql` then `seed-data.sql` via `sqlcmd` against an empty database. Both files require `sqlcmd -I` (QUOTED_IDENTIFIER ON) - the app's filtered indexes and its own ADO.NET connections default this on, but `sqlcmd` does not.

`seed-data.sql` is **generated from a freshly-seeded database, not hand-written** - this is the only way its ASP.NET Identity `PasswordHash` values are genuinely valid for `Demo@12345`. Regenerate it whenever the canonical seed data changes (do not hand-edit): run `setup.ps1` against a throwaway database, then dump all rows from `AspNetRoles`, `AspNetUsers`, `AspNetUserRoles`, and the 14 application tables (skipping any `rowversion`/`timestamp` column, which the server generates and can never be explicitly inserted) into `INSERT` statements in FK-safe order.

## IIS Local

`publish-iis.ps1` publishes the app and wires it into IIS (app pool, site, ACLs). It requires the ASP.NET Core Hosting Bundle, IIS with the required role services, and an elevated (Administrator) PowerShell session - none of which this repo's automated tooling can install for you. See the script's header comment for the manual prerequisite-installation steps.
