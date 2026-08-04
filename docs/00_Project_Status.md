# Project Status

Updated: 2026-08-04 (Asia/Bangkok)

## Summary

| Item | Status | Evidence | Problem | Next task |
|---|---|---|---|---|
| Day 1 foundation | Done | Build: 0 warnings/errors; migration `20260804125506_InitialCreate` applied; idempotent SQL generated | None | Completed |
| Day 2 authentication | Done | 19/19 automated checks passed; four demo roles seeded twice; cookie, antiforgery, policy, 403, and audit behavior verified | None | Begin Day 3 master data |
| Day 3 master data | Done | 10 raw materials, 5 balanced formulations, secured CRUD services/API/UI, idempotent LocalDB seed | None | Begin Day 4 Customer Order |
| Day 4 Customer Order | Done | Secured list/detail/create/update/transition API and UI; 10-order stable-T seed; RowVersion and lifecycle rules verified | None | Begin Day 5 Production Plan |
| Day 5–14 | Not Started | — | Locked roadmap | Follow the locked sequence |

## Day 1 acceptance evidence

- `AI.Factory.Web` is the only executable host.
- Blazor uses global Interactive Server rendering in `Components/App.razor`.
- `AI.Factory.Api` is a class library with no `Program.cs`.
- No `.Client` project exists.
- Project references enforce Web composition, API/Core separation, and Infrastructure/Core dependency direction.
- The EF model contains exactly 14 application entities plus standard Identity tables.
- `PurchaseRequests` has a non-unique `(SourceProductionPlanId, Status)` index and no unique `SourceProductionPlanId` constraint.
- Required foreign keys, checks, unique indexes, filtered active-alert index, and row-version columns are in the initial migration.
- Production defaults to `TimeProvider.System`; demo requires a fixed canonical `T`; automated tests exercise `FixedTimeProvider`.
- Initial migration applied successfully to `AI_Factory_CommandCenter` on SQL Server Express LocalDB.
- Latest build result: succeeded, 0 warnings, 0 errors.
- Foundation verification: 4 passed, 0 failed (3 unit contract checks and 1 single-host integration smoke check).
- `dotnet format --verify-no-changes` passed.
- `deploy/database.sql` contains all 14 application tables, seven Identity tables, and migration-history guards.
- Scope audit found zero `.Client` projects, zero `DbContext` references in Blazor components, and zero `Program.cs` files in `AI.Factory.Api`.

## Gates

| Gate | Status |
|---|---|
| Gate 1 — Foundation | Done |
| Gate 2 — Core Business Flow | Not Started |
| Gate 3 — Risk and AI | Not Started |
| Gate 4 — Release | Not Started |

## Scope audit

- No Day 5–14 business feature has been implemented.
- Generated Counter and Weather demo pages were removed.
- No Docker, cloud, microservice, WebAssembly, `.Client`, AI-write, RAG, or additional table scope was added.
- Foundation tests are verification evidence and are not additions to the 15 locked required business tests.

## Handoff

- Current module: Customer Order
- Current task: Day 4 Customer Order
- Status: Done
- Remaining error: None known
- Last commit: See `git log -1`
- Next task: Day 5 Production Plan, required batch, unique plan, and material-requirement transaction
- Do not change: locked topology, table count, `SourceProductionPlanId` uniqueness rule, or TimeProvider policy

## Day 2 acceptance evidence

- Four idempotent demo identities exist with exactly one locked role each: Admin, Manager, Planner, and Viewer.
- Login and logout use the single-host ASP.NET Core Identity cookie.
- Authentication cookie is `HttpOnly`, `Secure`, `SameSite=Lax`, and limited to an eight-hour sliding session.
- Antiforgery is mandatory for login, logout, and admin write endpoints; a missing token returns HTTP 400 without exposing a stack trace.
- Eleven named authorization policies implement the locked permission matrix.
- API authentication and authorization failures return HTTP 401/403 instead of HTML redirects.
- Viewer access to the Admin activation endpoint returns HTTP 403, makes no data change, and writes an `Unauthorized Access` audit.
- Login success, login failure, logout, unauthorized access, and Admin activation actions write append-only audit entries.
- Login is rate-limited and request bodies are limited to 1 MiB.
- The four demo users were seeded into SQL Server Express LocalDB twice without duplicates.
- Latest verification: 19 passed, 0 failed; build 0 warnings/errors; formatting passed; no pending EF model changes.
- Gate 1 — Foundation is complete.

## Day 3 acceptance evidence

- `IMasterDataService` is the shared application service used by Blazor and API endpoints; UI has no `DbContext` access.
- Authenticated users can read Raw Materials and Formulations; only Admin and Planner can create or update them.
- Raw Material validation enforces required lengths, non-negative stock/reserved/lead-time values, unique codes, and RowVersion conflicts.
- Formulation validation enforces positive unique material weights and exact `SUM(WeightPerBatch) = BatchSize`.
- The single Material Management screen contains the locked Raw Materials, Formulations, and future Material Requirements tabs.
- Canonical seed contains exactly 10 locked raw materials and five formulations with balanced recipes.
- Master seed was executed twice against SQL Server Express LocalDB without duplicate canonical codes.
- API write endpoints explicitly validate antiforgery tokens; API status-code responses are no longer re-executed through UI error pages.
- Latest verification: 24 passed, 0 failed; build 0 warnings/errors before final formatting; no schema change was introduced.

## Day 4 acceptance evidence

- `ICustomerOrderService` is shared by Blazor and API endpoints; Customer Order components have no `DbContext` access.
- Authenticated roles can list and view orders; only Admin and Planner can create, update, or transition them.
- General update accepts no Lifecycle or Risk field and succeeds only while the order is Draft with no Production Plan.
- Once a Production Plan exists, general update is rejected, permanently locking Quantity and FormulationId through that route.
- Lifecycle transitions permit only Draft to Planned to InProduction to Completed, with no skip or backward transition.
- RowVersion is required for update and transition; stale API writes return HTTP 409.
- Computed Risk is server-calculated from the locked production-timing buffer boundaries and is never accepted from the client.
- The two locked screens provide filter/table/lifecycle/risk/pagination and create/edit/detail/validation behavior.
- Canonical LocalDB seed contains exactly 10 orders, preserves `T = 2026-08-04` from `SO-DEMO-001.CreatedAt`, and preserves `SO-DEMO-001 Delivery = T+3` after a second run.
- Latest verification: 36 passed, 0 failed; build 0 warnings/errors; no schema change was introduced.
