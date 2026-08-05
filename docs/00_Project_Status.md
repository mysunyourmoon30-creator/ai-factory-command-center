# Project Status

Updated: 2026-08-05 (Asia/Bangkok)

## Summary

| Item | Status | Evidence | Problem | Next task |
|---|---|---|---|---|
| Day 1 foundation | Done | Build: 0 warnings/errors; migration `20260804125506_InitialCreate` applied; idempotent SQL generated | None | Completed |
| Day 2 authentication | Done | 19/19 automated checks passed; four demo roles seeded twice; cookie, antiforgery, policy, 403, and audit behavior verified | None | Begin Day 3 master data |
| Day 3 master data | Done | 10 raw materials, 5 balanced formulations, secured CRUD services/API/UI, idempotent LocalDB seed | None | Begin Day 4 Customer Order |
| Day 4 Customer Order | Done | Secured list/detail/create/update/transition API and UI; 10-order stable-T seed; RowVersion and lifecycle rules verified | None | Begin Day 5 Production Plan |
| Day 5 Production Plan | Done | Transactional plan/requirement creation, computed batches, unique order plan, secured API/UI, and stable-T seed | None | Begin Day 6 Material Requirement Query |
| Day 6 Material Requirement Query | Done | Cumulative active demand by date, Late PO exclusion, Available By Date; 68/68 automated checks passed | None | Begin Day 7 Material Shortage |
| Day 7 Material Shortage | Done | Shortage 1,250 kg verified on SQL Server; Serializable PR creation returns one success and one 409 under a real race; 86/86 automated checks passed | None | Begin Day 8 Purchase Request approval |
| Day 8 PR Approval, Incoming PO, Idempotent Receipt | Done | Submit/Approve/Reject wired to locked roles; Test 12 numbers (+300, +200, +0) verified on SQL Server; concurrent Approve returns one 200 and one 409 under a real race; 105/105 automated checks passed | None | Begin Day 9 Dashboard and Reports |
| Day 9 Dashboard, Reports, Secure CSV | Done | KPI/Daily Summary match the locked §16.11 figures exactly; CSV formula-injection and comma-escaping verified live on SQL Server; 135/135 automated checks passed | Fixed a pre-existing Thai-locale date bug (see acceptance evidence) | Begin Day 10 AI Copilot |
| Day 10-14 | Not Started | - | Locked roadmap | Follow the locked sequence |

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

- No Day 10-14 business feature has been implemented. Audit Log Report and its Audit and Administration screen are intentionally not part of Day 9 — the roadmap names "Audit Page" as a Day 11 deliverable alongside Alert Deduplication, so building that screen now would ship it four days early with no alert-writing logic to populate it.
- Generated Counter and Weather demo pages were removed.
- No Docker, cloud, microservice, WebAssembly, `.Client`, AI-write, RAG, or additional table scope was added.
- Foundation tests are verification evidence and are not additions to the 15 locked required business tests.

## Handoff

- Current module: Reporting (Dashboard, Reports)
- Current task: Day 9 Dashboard, Report Views, Secure CSV
- Status: Done
- Remaining error: None known
- Last commit: See `git log -1`
- Next task: Day 10 Ollama, structured output, the 4 read-only AI tools, prompt/allow-list protection
- Do not change: locked topology, table count (view entities via `.ToView` never count toward it), `SourceProductionPlanId` uniqueness rule, TimeProvider policy, the unclamped On-hand Available used in calculations, the Serializable isolation used for Purchase Request creation, the receipt endpoint's cumulative-quantity contract, the report views' no-time-relative-math rule, or any `.ToString("yyyy-MM-dd", ...)` call's explicit `CultureInfo.InvariantCulture` (the dev box's system locale is Thai and silently renders Buddhist-era years otherwise)

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

## Day 5 acceptance evidence

- `IProductionPlanService` is shared by Blazor and API endpoints; the Production Plan component has no `DbContext` access.
- Authenticated roles can list and view plans; only Admin and Planner can create or transition them.
- Plan creation requires a Planned Customer Order, a valid machine, and a unique Plan Number and Customer Order.
- Required Batch is computed with `CEILING(Order Quantity / Batch Size)`; the locked cases 10,000/500 = 20, 1,001/500 = 3, and 800/400 = 2 pass.
- Material Requirements are server-computed as `Required Batch * Weight Per Batch`; clients cannot supply batch, requirements, lifecycle, or risk values.
- Production Plan and all Material Requirements are saved in one relational database transaction; duplicate attempts create neither another Plan nor Requirements.
- Plan lifecycle permits only Planned to InProduction to Completed; RowVersion conflicts return HTTP 409.
- The single locked Production Plan screen includes plan list, create form, machine assignment, required batches, lifecycle, computed risk, and requirement detail.
- Canonical LocalDB seed contains three machine references, eight Plans, and 25 Material Requirements. A second run preserves `T`, dates, counts, and uniqueness.
- `PP-DEMO-001` has 20 required batches, completion `T+5`, computed Critical timing risk, and RM-001 requirement 5,000 kg.
- Latest verification: 47 passed, 0 failed; build 0 warnings/errors; no schema change was introduced.

## Day 6 acceptance evidence

- `IMaterialRequirementQueryService` is shared by Blazor and API endpoints; the Material Management component has no `DbContext` access.
- The module is read-only: no table, no migration, and no write path was added, so the model still contains exactly 14 application entities.
- Active demand counts only `Planned` and `InProduction` production plans; `Completed` plans are excluded and contribute no Required Date.
- `Cumulative Required(d)` sums active requirements whose Planned Completion Date is on or before each evaluated date.
- On-hand Available is `Current Stock - Reserved Stock` and stays unclamped in every calculation; only the UI floors the displayed value at zero and flags the negative.
- `Cumulative Incoming(d)` counts outstanding quantity from Open and Partial purchase orders with Expected Date between today and `d`; late and fully received orders are excluded.
- `Available By Date(d)` is `Current Stock - Reserved Stock + Cumulative Incoming(d)`, evaluated at every active Required Date rather than a single collapsed date.
- Locked Case A holds: with RM-001 at 4,200/450 and a late 1,000 kg purchase order at `T-4`, eligible incoming is 0 and Projected Available is 3,750 kg against a 5,000 kg requirement.
- Locked Case B holds: with two active plans (80 at `d1`, 70 at `d2`) and a 50 unit order expected at `d2`, `Cumulative Required(d2) = 150` and `Available By Date(d2) = 150`; `MIN(PlannedCompletionDate)` is never used to cut off later supply.
- The Material Requirements tab is enabled as the third locked Material Management tab and shows Raw Material, Required Quantity, Current Stock, Reserved Stock, Eligible Incoming, and Projected Available.
- All four roles including Viewer can read the query; anonymous access returns HTTP 401.
- Deficit, Shortage Quantity, Material Required Date, and Evaluation Date selection remain unimplemented and belong to Day 7.
- Latest verification: 68 passed, 0 failed; build 0 warnings/errors; formatting passed; no schema change was introduced.

## Day 7 acceptance evidence

- `IMaterialShortageService` is shared by Blazor and API endpoints; the Material Shortage component has no `DbContext` access.
- No table, column, or migration was added; the model still contains exactly 14 application entities and `PurchaseRequests` still has no unique `SourceProductionPlanId` index.
- `Deficit(d)` is `MAX(Cumulative Required(d) - Available By Date(d), 0)`; Shortage Quantity is the largest deficit across the active horizon.
- Material Required Date is the first date whose deficit exceeds zero, and Evaluation Date is the first date reaching the largest deficit; every displayed figure is read at the Evaluation Date so a row reconciles with itself.
- A raw material with no active plan produces no shortage row and cannot raise a purchase request.
- With active plans but no deficit, Shortage Quantity is 0, Material Required Date is null, and the row is evaluated at the last active Required Date.
- Locked demo case verified against SQL Server Express LocalDB: RM-001 reports Total Requirement 5,000 kg, On-hand Available 3,750 kg, Eligible Incoming 0, Projected Available 3,750 kg, and **Shortage 1,250 kg** required at `T+5`, with `SO-DEMO-001`/`PP-DEMO-001` listed as the affected order.
- Late purchase orders are surfaced with their delay days and outstanding quantity for visibility, and are never counted as eligible incoming supply.
- Purchase Request creation recomputes the shortage inside an `IsolationLevel.Serializable` transaction and re-checks for a competing active request immediately before insert.
- Duplicate prevention covers `Draft` and `PendingApproval` for the same `SourceProductionPlanId` and `RawMaterialId`, enforced in the application service by joining `PurchaseRequests` to `PurchaseRequestItems`. An `Approved` request does not block a new one.
- Creation is rejected when the recomputed shortage is 0 and when Requested Quantity exceeds the current shortage.
- Two genuinely concurrent creation requests against LocalDB produced exactly one HTTP 200 and one HTTP 409, leaving a single active request; the verification rows were then removed so the demo database holds no purchase requests.
- All four roles can read shortages; Viewer receives HTTP 403 on creation and anonymous read returns HTTP 401.
- Purchase Request submit, approve, and reject remain unimplemented and belong to Day 8, as does writing `LatePurchaseOrder` rows into `Alerts`, which Day 11 owns together with alert deduplication.
- Latest verification: 86 passed, 0 failed; build 0 warnings/errors; formatting passed; no schema change was introduced.

## Day 8 acceptance evidence

- `IProcurementService` is shared by Blazor and API endpoints; the Procurement component has no `DbContext` access.
- No table, column, or migration was added; the model still contains exactly 14 application entities and the schema constraints already carried the Day 8 requirements (`IncomingPurchaseOrders.PurchaseRequestId` unique, `RowVersion` on both entities, `ReceivedQuantity <= OrderedQuantity`).
- Purchase Request lifecycle is `Draft → PendingApproval → Approved | Rejected`; Approved and Rejected never transition again, rejection requires a reason, and approval sets `ApprovedByUserId`/`ApprovedDate`.
- Submit is available to Admin, Manager, and Planner; Approve/Reject and recording an Incoming PO are Admin/Manager only — all three reuse the eleven policies already registered in Day 1, so the locked policy count is unchanged.
- Incoming Purchase Orders are created only from an Approved request, at most one per request (enforced at the database level and re-checked in the service), with item quantities capped at what the request asked for. `IsLate`/`DelayDays` are computed on read, never stored.
- Locked Test 12 verified against SQL Server Express LocalDB: raising Received from 0 to 300 then to 500 increased `RawMaterials.CurrentStock` by exactly 300 then 200; resending the same target of 500 changed nothing — the endpoint accepts the new cumulative total, not an increment.
- Approve concurrency verified against LocalDB: two genuinely concurrent Approve requests on the same PendingApproval record produced exactly one HTTP 200 and one HTTP 409, using the same optimistic `RowVersion` pattern already proven for Production Plan and Customer Order transitions.
- The Procurement screen's two locked tabs (Purchase Request Approval, Incoming Purchase Order) provide submit/approve/reject actions, incoming-PO creation from an eligible Approved request, and received-quantity entry, each gated by `AuthorizeView` on the same policies enforced server-side.
- All four roles can read Purchase Requests and Incoming Purchase Orders; Viewer is forbidden on every write; anonymous read returns HTTP 401.
- Note: the integration suite's `Received_quantity_cannot_decrease_or_exceed_the_ordered_quantity` and `Submit_with_a_wrong_row_version_returns_conflict`-style tests intentionally send a value that can never match the stored `RowVersion` (matching the existing `Stale_plan_row_version_returns_http_conflict`/`Stale_order_row_version_returns_http_conflict` idiom), because the EF Core InMemory provider does not regenerate `RowVersion` on save the way SQL Server does; the genuine concurrent-race guarantee is verified live, as above.
- Latest verification: 105 passed, 0 failed; build 0 warnings/errors; formatting passed; no schema change was introduced.

## Day 9 acceptance evidence

- `IDashboardService` and `IReportExportService` are shared by Blazor and API endpoints; every KPI is computed live from the database (`Dashboard อ่านจาก Database`), with no hard-coded or AI-computed figures.
- Only 3 of the 4 locked reports are in scope: Production Risk, Material Shortage, Purchase Order Status. Audit Log Report is deferred to Day 11 (see Handoff), matching the roadmap's own "Audit Page" placement.
- Three new SQL views (`vw_ProductionRiskReport`, `vw_PurchaseOrderStatusReport`, `vw_MaterialShortageReport`) were added via one EF migration and mapped as keyless `.ToView` entities; the model still contains exactly 14 application tables (`FoundationContractTests` filters by `GetTableName()`, which is null for view-only entities). `deploy/database.sql` was regenerated.
- The views are pure JOIN projections with no time-relative `CASE`/window-function logic, because the app's `TimeProvider` abstraction (frozen `T` in Demo/Testing) cannot be seen by `GETUTCDATE()`; risk and lateness classification is applied in C# on top of the view rows using the same tested `OrderRiskCalculator`/`PurchaseRequestRules` statics used everywhere else.
- On-screen and JSON report data reuse the already-tested feature services (`IProductionPlanService`, `IMaterialShortageService`, `IProcurementService`) unchanged; only CSV export reads from the SQL views (Material Shortage CSV reuses `IMaterialRequirementQueryService` directly instead, since its cumulative multi-date math cannot safely live in a view either — this guarantees the CSV numbers can never disagree with the screen).
- CSV export is gated by the `CanExportReports` policy (registered Day 1, all four roles) and implements the locked §13.1 defenses: embedded double quotes are doubled, values containing a comma or newline are quote-wrapped, and values starting with `= + - @` are prefixed with a single quote to neutralize spreadsheet formula injection. Output carries a UTF-8 BOM.
- Dashboard KPI and Daily Summary figures were verified live against SQL Server and match the locked §16.11 demo dataset exactly: Customer Orders 10, Production Plans 8, Material Shortages 1, Critical Machines 1, and SO-DEMO-001 listed as a Critical risk with a 2-day delay — Late Purchase Orders is 0 rather than the spec's worked-example 1, because that example describes the state *after* a human performs the Day 7-narrated PR/PO walkthrough, not the untouched canonical seed.
- Both CSV injection defenses were confirmed live on SQL Server with a deliberately seeded `=PLAN-INJECT` plan number and `SO,INJECT` order number: the export rendered `'=PLAN-INJECT` and `"SO,INJECT"` respectively; the seeded rows were then removed.
- **Found and fixed a pre-existing bug, not introduced by Day 9**: every `.ToString("yyyy-MM-dd")` call across 7 Razor pages (Days 3-8) plus this day's own new code rendered the **Thai Buddhist Era year** (e.g. `2569` instead of `2026`) because the development machine's system locale is `th-TH`, and no code specified `CultureInfo.InvariantCulture`. Every such call now does. `@using System.Globalization` was added to `_Imports.razor`. This is a display-only fix — no stored data or business calculation was affected — but it would have made every date on every screen and in every CSV wrong on this machine (and any other Thai-locale deployment) had it shipped.
- Latest verification: 135 passed, 0 failed; build 0 warnings/errors; formatting passed; 14 application tables unchanged.
