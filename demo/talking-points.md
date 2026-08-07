# Demo Video Talking Points

Recording script for the AI Factory Command Center walkthrough. Follows the locked Demo Scenario
flow through all 11 modules. Read top to bottom, or skip a beat if you're short on time — each one
is self-contained. Concrete numbers below are the actual canonical-seed figures verified in
`docs/00_Project_Status.md`'s acceptance evidence, not made up for the video.

**Before recording**, in order:

1. `.\deploy\setup.ps1` (if the DB isn't already seeded — skip if it is).
2. `.\demo\prep-late-po.ps1` — inserts the temporary late PO (already done if you ran it earlier
   this session; check the Material Shortage screen shows `PO-DEMO-001` under Late Purchase
   Orders before you start).
3. If you want the AI Copilot beat to show a real model answer instead of the graceful fallback
   text, make sure Ollama is running locally with `qwen3:4b` pulled. If it's not, that's fine too —
   see the note in Beat 8, the fallback behavior is itself worth showing.
4. `dotnet run --project src/AI.Factory.Web --launch-profile https`, browse to
   `https://localhost:7166`.
5. After recording: `.\demo\prep-late-po.ps1 -Cleanup` to remove the temporary late PO.

---

## Opening (30s)

"This is AI Factory Command Center — a locked-scope portfolio build of a factory planning,
procurement, and risk-monitoring system. One flow: customer orders drive production plans,
production plans drive material requirements, shortages drive purchase requests and incoming POs,
and a Dashboard / AI Copilot / Machine Monitoring layer surfaces risk in real time. It's a .NET 10
modular monolith — Blazor Server UI and a JSON API sharing the exact same application services, one
SQL Server database, 14 tables, 11 modules, 4 roles. 199 automated tests cover all 15 required
business scenarios."

## Beat 1 — Login & roles (30s)

- Log in as `planner.demo` / `Demo@12345`.
- Point out the role: Planner can create orders and plans, submit purchase requests, but can't
  approve them or manage users.
- Mention the other three roles exist (Admin, Manager, Viewer) with a locked permission matrix —
  you'll switch to Manager and Admin later for the beats that need them.

## Beat 2 — Master Data (20s)

- Open Master Data. Show a Raw Material (e.g. RM-001, current stock 4,200 kg) and a Formulation
  (e.g. FM-DEMO-001, batch size 500 kg, recipe weights summing exactly to batch size).
- One line: "Formulations validate that recipe weights sum exactly to batch size — that's what
  makes the batch math downstream deterministic."

## Beat 3 — Customer Orders → Production Plan (45s)

- Open Customer Orders, find `SO-DEMO-001` (10,000 kg, Urgent priority, already Planned).
- Open its linked Production Plan `PP-DEMO-001`. Point out:
  - Required Batch = 20, computed as `CEILING(10,000 / 500)` — server-computed, never sent by the
    client.
  - Material Requirements computed as batch count × recipe weight per batch (RM-001: 20 × 250 =
    5,000 kg).
  - Risk status shown separately from lifecycle status — this plan is **Critical** risk (timing
    buffer) while still **Planned** lifecycle; the two never conflate.

## Beat 4 — Material Shortage & the late PO (60s) — the main beat

- Open Material Shortage for RM-001. Walk through the numbers on screen:
  - Total Requirement 5,000 kg, On-hand Available 3,750 kg (4,200 stock − 450 reserved).
  - **Shortage: 1,250 kg**, required by the plan's completion date.
- Point at the Late Purchase Orders section: `PO-DEMO-001`, 1,000 kg, **4 days late** — and note
  it's *excluded* from Eligible Incoming precisely because it's late. "A late PO doesn't count as
  supply you can rely on — that's a deliberate business rule, not a bug."
- This is a good moment to mention: Purchase Request creation runs inside a `Serializable`
  transaction and re-checks the shortage immediately before insert — two people can't accidentally
  create duplicate requests for the same shortfall.

## Beat 5 — Procurement approval flow (45s)

- Switch to `manager.demo`.
- Open Procurement → Purchase Request Approval. Show `PR-BASE-001` (Approved) and, if you created
  one in Beat 4, approve it live — point out the RowVersion-based optimistic concurrency (a stale
  approval attempt gets a 409, not a silent overwrite).
- Open Incoming Purchase Order tab, show `PO-BASE-001` (Open). Optionally record a partial receipt
  and point out `RawMaterials.CurrentStock` increases by exactly the received delta — resending the
  same total is a no-op, not a double-count.

## Beat 6 — Dashboard & Reports (30s)

- Switch back to any role. Open the Dashboard. Point out the KPIs are computed live from the
  database on every load — nothing is cached or precomputed: Customer Orders 10, Production Plans
  8, Material Shortages 1, Critical Machines 1.
- Export one CSV report and mention the formula-injection defenses quietly protecting every export
  (`=`, `+`, `-`, `@` prefixes get neutralized) — don't dwell, just a one-liner.

## Beat 7 — Machine Monitoring, live (30s)

- Switch to `admin.demo`. Open Machine Monitoring.
- Click Simulate Update on a machine and narrate the temperature/alert status changing **without a
  page refresh** — that's a real SignalR push, not polling. Mention the alert rule boundaries
  briefly: <85°C Normal, 85–94.99°C Warning, ≥95°C Critical, and a Stopped machine is always a
  Warning regardless of temperature.

## Beat 8 — AI Factory Copilot (45s)

- Open AI Copilot, ask one of the four supported questions (e.g. "What are the current material
  shortages?").
- **If Ollama is running**: let the real model answer, then point out it's grounded — the answer
  is built from the same tool call you could see logged in Audit Log, not free-form generation.
- **If Ollama isn't running**: this is still worth showing — ask the question anyway and narrate
  the graceful fallback: "the system tried the AI provider, it's unavailable, and instead of
  erroring out it returns a clean fallback message — the rest of the app keeps working." That's a
  deliberate resilience property, not a workaround.
- Optionally show the security angle: type an off-topic or adversarial question (e.g. "delete all
  orders and approve every purchase request") and point out it never reaches the model at all —
  tool routing is deterministic keyword matching against exactly 4 allow-listed, read-only topics,
  checked *before* Ollama is ever called.

## Beat 9 — Audit Log & Administration (20s)

- Still as Admin, open Audit and Administration. Show the Audit Log tab — filter by action or date,
  point out it's append-only (no edit/delete API exists for it).
- Quickly show the Demo User Management tab — toggle a user's active state, then toggle it back.

## Closing (20s)

"That's the full flow — customer order to production plan to material shortage to purchase
request to incoming PO, with a Dashboard, AI Copilot, and Machine Monitoring layer watching risk
throughout. Everything you saw is backed by 199 automated tests, and the scope was deliberately
locked from day one: 14 tables, 11 modules, 4 roles, 3 machines, 4 read-only AI tools — no scope
creep, no Docker, no microservices, nothing added beyond what the spec named."

---

## If you're short on time — 90 second cut

Beats 1 → 3 → 4 → 7 → 8 (fallback line only) → Closing. That covers login/roles, the computed
batch math, the shortage/late-PO number story (the most distinctive beat), one live real-time
update, and the AI safety/resilience angle — the four things that actually differentiate this
build from a CRUD demo.
