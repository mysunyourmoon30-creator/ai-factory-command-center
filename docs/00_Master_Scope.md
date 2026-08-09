# Master Scope — Locked Version 4

The authoritative source supplied for this implementation is `00_Master_Scope_Final_Locked_V4.md` (SHA-256 `BC88120747728B56CF681B1ECAA960A7F6744FC803B9426DC0081AE93DAAFE85`). This repository index records the non-negotiable implementation boundaries used by Day 1.

## Locked totals

- 1 portfolio project, business flow, and demo scenario
- 11 system modules and screens **+ 1 post-lock addition** (see "Deliberate departures" below)
- 12 AI engineering topics
- 4 report views, roles, and runtime AI tools
- 3 machines
- 15 required tests
- 14 application tables
- 14-day delivery plan

## Locked architecture

- .NET 10 modular monolith with a single ASP.NET Core host and origin
- Blazor Web App using global Interactive Server rendering
- `AI.Factory.Api` is an endpoint assembly, not an executable host
- UI and endpoints use the same application services; UI never accesses `DbContext` directly
- SQL Server Express, EF Core migrations as schema source of truth, ASP.NET Core Identity/cookies
- C# or SQL owns deterministic calculations; Ollama/Qwen is read-only, grounded, allow-listed, and non-critical

## Day 1 boundary

Day 1 includes solution topology, dependency direction, 14-table entity/schema contracts, constraints and indexes, TimeProvider policy, initial EF migration, empty-database application, build, smoke verification, and status evidence. It excludes Day 2–14 business and UI features.

## Deliberate departures from the locked totals

This section exists so a departure is recorded rather than quietly absorbed into the numbers above.
The authoritative V4 document and its checksum are unchanged — the source did not move; this
repository did.

**2026-08-09 — Executive Overview (`/executive`), a 12th screen.** Added at the project owner's
explicit request after the alternative was recommended and declined: the Dashboard is already the
read-only KPI screen, and `Manager` is already the executive-shaped role. Scope of the addition,
kept as small as the request allows:

- 1 screen, 1 read-only application service (`IExecutiveService`), 1 read endpoint
  (`GET /api/executive/overview`).
- **No** new table, migration, role, named authorization policy, AI tool, or business rule. The 14
  application tables, 4 roles, 3 machines, 4 AI tools, 11 policies, and 15 Required tests are all
  unchanged.
- Every figure on the screen is an existing calculation grouped or sorted for display, and
  `ExecutiveTests` asserts the cross-checks that hold it to the Dashboard's numbers.

## Locked corrections

- Never add `Unique(SourceProductionPlanId)` to purchase requests.
- Never add a `.Client` project or change Blazor to WebAssembly/Auto.
- Never collapse incoming supply evaluation to the minimum completion date across plans.
- Demo and automated tests use time fixed at canonical `T`; production uses `TimeProvider.System`.
- Late production alerts use `LateProduction`.
- Lifecycle and computed risk remain separate.
- Order updates cannot bypass draft/plan/status-transition rules.

When detail is needed, verify it against the authoritative V4 document and its checksum above. No summary may override that source.
