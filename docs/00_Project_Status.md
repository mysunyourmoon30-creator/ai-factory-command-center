# Project Status

Updated: 2026-08-04 (Asia/Bangkok)

## Summary

| Item | Status | Evidence | Problem | Next task |
|---|---|---|---|---|
| Day 1 foundation | Done | Build: 0 warnings/errors; 4/4 foundation checks passed; migration `20260804125506_InitialCreate` applied; idempotent SQL generated | None | Begin Day 2 authentication only after review |
| Day 2 authentication | Not Started | — | Gate order | Begin only after Day 1 is complete |
| Day 3–14 | Not Started | — | Locked roadmap | Follow the locked sequence |

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
| Gate 1 — Foundation | Doing (Day 2 authentication work remains) |
| Gate 2 — Core Business Flow | Not Started |
| Gate 3 — Risk and AI | Not Started |
| Gate 4 — Release | Not Started |

## Scope audit

- No Day 2–14 business feature has been implemented.
- Generated Counter and Weather demo pages were removed.
- No Docker, cloud, microservice, WebAssembly, `.Client`, AI-write, RAG, or additional table scope was added.
- Foundation tests are verification evidence and are not additions to the 15 locked required business tests.

## Handoff

- Current module: Foundation
- Current task: Day 1 foundation
- Status: Done
- Remaining error: None known
- Last commit: See `git log -1`
- Next task: Day 2 Identity login/logout, roles, policies, antiforgery, and audit foundation
- Do not change: locked topology, table count, `SourceProductionPlanId` uniqueness rule, or TimeProvider policy
