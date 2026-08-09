# Demo Video Talking Points

Recording script for the AI Factory Command Center walkthrough. Follows the locked Demo Scenario
flow through all 11 modules. Read top to bottom, or skip a beat if you're short on time — each one
is self-contained. Concrete numbers below are the actual canonical-seed figures verified in
`docs/00_Project_Status.md`'s acceptance evidence, not made up for the video.

**Before recording**, in order:

1. **Check the canonical numbers first.** Open Material Shortage and confirm RM-001 reads
   **Shortage 1,250 kg**. If it reads anything else, the demo database has drifted — clicking
   through the app during a rehearsal is enough to do it, and receipts deliberately cannot be
   reversed through the UI. Reset with:

   ```
   .\.dotnet\dotnet.exe ef database drop --force --project src\AI.Factory.Infrastructure --startup-project src\AI.Factory.Infrastructure
   .\.dotnet\dotnet.exe run --project src\AI.Factory.Web -- --seed-production-plans
   ```

   This wipes the audit log too, which is fine — a clean log actually demos better, because every
   row you show is one you just created on camera.
2. `.\demo\prep-late-po.ps1` — inserts the temporary late PO. Check the Material Shortage screen
   shows `PO-DEMO-001` under Late Purchase Orders before you start.
3. If you want the AI Copilot beat to show a real model answer instead of the graceful fallback
   text, make sure Ollama is running locally with `qwen3:4b` pulled. If it's not, that's fine too —
   see the note in Beat 8, the fallback behavior is itself worth showing.
4. `dotnet run --project src/AI.Factory.Web --launch-profile https`, browse to
   `https://localhost:7166`. **Use `https://`** — plain `http://` on that port returns an empty
   reply, which looks like a crash on camera.
5. After recording: `.\demo\prep-late-po.ps1 -Cleanup` to remove the temporary late PO.

---

## Opening (30s)

"This is AI Factory Command Center — a locked-scope portfolio build of a factory planning,
procurement, and risk-monitoring system. One flow: customer orders drive production plans,
production plans drive material requirements, shortages drive purchase requests and incoming POs,
and a Dashboard / AI Copilot / Machine Monitoring layer surfaces risk in real time. It's a .NET 10
modular monolith — Blazor Server UI and a JSON API sharing the exact same application services, one
SQL Server database, 14 tables, 11 modules, 4 roles. 204 automated tests cover all 15 required
business scenarios."

## Beat 1 — Login & roles (30s)

- Log in as `planner.demo` / `Demo@12345`.
- Point out the role: Planner can create orders and plans, submit purchase requests, but can't
  approve them or manage users.
- Mention the other three roles exist (Admin, Manager, Viewer) with a locked permission matrix —
  you'll switch to Manager and Admin later for the beats that need them.
- Worth one line: "every role can *reach* every screen; what changes is what's actionable. And the
  hiding isn't the security — the server refuses independently. A Viewer POSTing to the order API
  gets a 403 before validation ever runs."

## Beat 2 — Master Data (20s)

- Open **Materials** (the Master Data module). Show a Raw Material (RM-001, current stock 4,200 kg)
  and a Formulation (FM-DEMO-001, batch size 500 kg, recipe weights summing exactly to batch size).
- One line: "Formulations validate that recipe weights sum exactly to batch size — that's what
  makes the batch math downstream deterministic."

## Beat 3 — Customer Orders → Production Plan (45s)

- Open Customer Orders. Before finding the order, use the **Delivery risk** filter — set it to
  Critical and note that exactly one order comes back. "The dashboard's Orders-at-Risk KPI links
  straight here, and this is the filter that makes that number actionable rather than just a
  figure."
- That order is `SO-DEMO-001` (10,000 kg, Urgent priority, already Planned).
- Open its linked Production Plan `PP-DEMO-001`. Point out:
  - Required Batch = 20, computed as `CEILING(10,000 / 500)` — server-computed, never sent by the
    client.
  - Material Requirements computed as batch count × recipe weight per batch (RM-001: 20 × 250 =
    5,000 kg).
  - Risk status shown separately from lifecycle status — this plan is **Critical** risk (timing
    buffer) while still **Planned** lifecycle; the two never conflate.

## Beat 4 — Material Shortage & the late PO (60s) — the main beat

- Open Material Shortage. Before reading any number, point at the ordering: "shortages sort to the
  top, then by the date the material is actually needed — not alphabetically by material code. On
  a triage screen the thing you must act on first has to be the first row."
- Walk through RM-001's numbers on screen:
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
- Note that **Reject takes two clicks** and says so: it's terminal, a rejected request can't be
  resubmitted, and it sits next to a green Approve.
- Open Incoming Purchase Order tab, show `PO-BASE-001` (Open). Optionally record a partial receipt.
  Read the hint under the quantity box out loud — "Running total, not this delivery" — and explain
  why that matters: the field is cumulative, so the screen names the material it belongs to and
  reseeds when you pick a different one. "Getting this wrong writes one material's total against
  another."
  **If you do record a receipt, re-seed before the next take** — receipts cannot be reversed
  through the UI by design, and it moves the 1,250 figure.

## Beat 6 — Dashboard & Reports (30s)

- Switch back to any role. Open the Dashboard. Point out the KPIs are computed live from the
  database on every load — nothing is cached or precomputed: Customer Orders 10, Production Plans 8,
  Orders at Risk 1, Material Shortages 1, Late Purchase Orders 0, Machine Alerts 2, Critical Risks 2.
- Note the tiles aren't uniformly decorated: colour is driven by the value, so a zero stays neutral
  and only a real number gets a warning bar. "The colour carries information instead of decorating."
- Export one CSV report and mention the formula-injection defenses quietly protecting every export
  (`=`, `+`, `-`, `@` prefixes get neutralized) — don't dwell, just a one-liner.

## Beat 7 — Machine Monitoring, live (30s)

- Switch to `admin.demo`. Open Machine Monitoring.
- Click Simulate Update on a machine and narrate the temperature/alert status changing **without a
  page refresh** — the card recolours and the alert badge flips as the update lands. Mention the
  alert rule boundaries briefly: <85°C Normal, 85–94.99°C Warning, ≥95°C Critical, and a Stopped
  machine is always a Warning regardless of temperature.
- **Be precise about the mechanism if asked.** `MachineHub` broadcasts to SignalR clients, and the
  page — which renders on the server — observes the same notifier in-process rather than opening a
  loopback connection back to its own authorized hub. Say "the update is pushed, not polled",
  which is true. Don't say "that's a SignalR round-trip from the browser": it isn't, and anyone who
  opens the code will see that.
- Optional, and a good one: kill the update channel and reload. The page still renders every
  reading under a banner saying they're last-known rather than live. "Degrading honestly beats
  failing silently."

## Beat 8 — AI Factory Copilot (45s)

- Open AI Copilot, ask one of the four supported questions (e.g. "What are the current material
  shortages?").

**Check which of these two you're recording before you start talking.** The attribution line and
the section labels only render on a real model answer — every fallback path deliberately leaves
the tool name null, so a fallback can't imply a tool's data is behind it. If Ollama is down you
will not see them, and pointing at them on camera will point at nothing.

- **If Ollama is running** — this is the stronger beat:
  - Point at the attribution line: "Answered from GetMaterialShortages — Lists raw materials with
    an outstanding shortage. Read-only; no data was changed." Then the good part: "that name is
    stamped by the orchestrator from the tool it chose. The validator never reads it out of the
    model's response, so a model can't claim a source it didn't use. There's a test that feeds it
    a fake tool name and checks the real one wins."
  - Point out the labelling: Affected Orders is tagged **from system data**, Recommended Actions is
    tagged **AI suggestion** with a line saying to verify before acting. "Facts and suggestions
    shouldn't look identical."
  - The answer is grounded — built from the same tool call you can see logged in Audit Log, not
    free-form generation.
- **If Ollama isn't running** — still worth showing, just tell a different story: ask the question
  anyway and narrate the graceful fallback. "The system tried the AI provider, it's unavailable,
  and instead of erroring out it returns a clean fallback message — the rest of the app keeps
  working." That's a deliberate resilience property, not a workaround. Then skip straight to the
  adversarial-question point below, which works either way because it never calls the model at all.
- Optionally show the security angle: type an off-topic or adversarial question (e.g. "delete all
  orders and approve every purchase request") and point out it never reaches the model at all —
  tool routing is deterministic keyword matching against exactly 4 allow-listed, read-only topics,
  checked *before* Ollama is ever called.

## Beat 9 — Audit Log & Administration (20s)

- Still as Admin, open Audit and Administration. Show the Audit Log tab — filter by action or by
  **date range**, point out it's append-only (no edit/delete API exists for it).
- The strongest thirty seconds on this screen: copy a **Request Id** out of one row and paste it
  into Search. Everything that one HTTP request touched comes back together. "That's what makes an
  audit log usable rather than just complete."
- Demo User Management: toggle **`viewer.demo`** — not your own account. Deactivating now ends that
  user's live session at the next revalidation rather than only blocking their next sign-in, so
  toggling `admin.demo` on camera logs you out mid-recording. The screen asks for confirmation and
  says exactly that; read the confirmation text out loud, it makes the point for you. Toggle it
  back afterwards.

## Closing (20s)

"That's the full flow — customer order to production plan to material shortage to purchase
request to incoming PO, with a Dashboard, AI Copilot, and Machine Monitoring layer watching risk
throughout. Everything you saw is backed by 204 automated tests, and the scope was deliberately
locked from day one: 14 tables, 11 modules, 4 roles, 3 machines, 4 read-only AI tools — no scope
creep, no Docker, no microservices, nothing added beyond what the spec named."

---

## If you're short on time — 90 second cut

Beats 1 → 3 → 4 → 7 → 8 → Closing. That covers login/roles, the computed batch math, the
shortage/late-PO number story (the most distinctive beat), one live real-time update, and the AI
grounding/safety angle — the five things that actually differentiate this build from a CRUD demo.

For Beat 8 in the short cut, use the adversarial-question point rather than the attribution line:
it lands in one sentence and works whether or not Ollama is running.

## Three things worth saying if an interviewer digs

Not part of the script — keep these in your pocket, they're the answers that separate "I built a
CRUD app" from "I own this system".

- **"Where's the authorization actually enforced?"** In the service, not the UI. The Blazor pages
  call the same application services in-process, so no endpoint policy runs on that path — each
  mutating service re-checks the actor itself. Hiding a button is not a permission.
- **"What happens if two people act at once?"** Every mutable entity carries a `RowVersion`; a
  stale write gets a 409, never a silent overwrite. Purchase Request creation goes further and runs
  at `Serializable`, because two planners racing on the same shortage is a realistic scenario.
- **"How do you know the numbers are right?"** The canonical seed produces figures the tests assert
  against — RM-001 short by exactly 1,250 kg, KPI 10 / 8 / 1 / 0. When presentation changed during
  the UI work, the CSV export was re-captured and diffed byte-for-byte against the previous run to
  prove no arithmetic moved.
