# Tenant A — Complaints CRUD + Admin A Navigation (Delivery Report)

**Status:** both requested changes implemented and verified. New harness: `--complaints-verify` **71/71 PASS**.
**No redesign:** Tenant A's existing structure, the Login page, Tenant B, Tenant C and Master were left architecturally untouched. No branching, no BranchId, no branch selectors, no tenant dropdowns were introduced.

## The two changes

### 1. Complaints module — full CRUD (Create / Read / Update / Delete)

The existing Complaints tab was extended in place (no duplicate complaints system):

| Action | Behavior |
|---|---|
| **Create** | "Add complaint" button. All fields saved to the Tenant A database; list refreshes and the new complaint is selected and shown. No restart needed. |
| **Read** | List grid shows: Complaint ID, Member, Category, Subject, Date Submitted, Priority, Status, Staff Assigned, Date Resolved (Description/Resolution are detail-only, hidden from the list). Clicking a row loads the complaint's **full details** into the form (detail view). |
| **Update** | "Update selected" button loads/edits the selected record; the Complaint ID is preserved and **no new record is created**. Date Submitted is loaded from storage and only changes if the user edits the picker — never merely because the complaint was updated. |
| **Delete** | "Delete selected" button requires a selection **and explicit Yes/No confirmation**; deletes only that Tenant A complaint, then refreshes the list. |

All 11 required fields are stored (entity → EF mapping → database → form):

1. Complaint ID — existing `ComplaintId` identity primary key (database-generated; no manual IDs)
2. Member Name / ID — **DropDownList** populated from the authenticated Tenant A context only (`"{id}: {FullName} ({MemberCode})"`, same pattern as the existing member pickers); stores MemberId; no typing possible
3. Complaint Category — ComboBox: Service / Staff / Facility / Equipment / Billing / Membership / Other (no prior definition existed in the project; single new definition)
4. Subject — text input (required)
5. Description — multiline text area
6. Date Submitted — DateTimePicker (`yyyy-MM-dd HH:mm`), defaults to now on create, loaded from the stored value on edit → **reuses the existing `CreatedAt` column** (no new column needed)
7. Priority — ComboBox: Low / Medium / High / Critical (new; no prior definition existed)
8. Status — ComboBox reusing the module's existing values: Pending / Under Review / Resolved / Closed
9. Staff Assigned — ComboBox populated from **Tenant A's own non-member UserAccounts** (adminA, gymstaffA); stores the username as text (no new auth/user architecture, no cross-tenant users)
10. Resolution / Response — multiline, editable → existing `ResolutionNotes` column
11. Date Resolved — DateTimePicker with checkbox; **nullable** — stays empty for Open/Pending complaints, holds the date for Resolved/Closed → existing `ResolvedAt` column

### 2. Admin A — Dashboard removed

- `SetNav(btnNavDashboard, HasPermission(ViewDashboard) && !DashboardHiddenForAdminA, ...)` — hidden for `Roles.Admin` **and** `CompanyCode == COMP001` only.
- The Dashboard **module load is skipped** for that session, and the window now opens on **Members** instead of the Dashboard tab (tab headers are already hidden, so the nav entry is the only way in).
- Business Intelligence was **not** added to Tenant A (it was never there; verified absent).
- Tenant B, Tenant C and Master admins are untouched: `DashboardHiddenForAdminA` is false for every other company/role.

## Files changed

- [AlphaElite.domain/entities/Complaint.cs](AlphaElite.domain/entities/Complaint.cs) — added nullable `Category`, `Priority`, `StaffAssigned` (Date Submitted / Resolved / Resolution reuse existing `CreatedAt` / `ResolvedAt` / `ResolutionNotes`).
- [WinFormsLibrary1/data/TenantAlphaEliteDbContext.cs](WinFormsLibrary1/data/TenantAlphaEliteDbContext.cs) — maps the three new columns (shared by Tenant A **and** Tenant B, both already have the columns).
- [WinFormsLibrary1/data/TenantCAlphaEliteDbContext.cs](WinFormsLibrary1/data/TenantCAlphaEliteDbContext.cs) — **ignores** the three properties: Tenant C's schema and queries are byte-for-byte unchanged.
- [AlphaElite.winforms/Form1.Phase4.cs](AlphaElite.winforms/Form1.Phase4.cs) — complaints tab rebuilt (11-field layout, Create/Update/Delete buttons, detail view on row click, member/staff/category/priority/date pickers, grid headers + hidden detail columns), CRUD cores + UI handlers, `ApplyComplaintFormVisibility()` (member self-service keeps the compact original form), `DashboardHiddenForAdminA` property, headless test hooks.
- [AlphaElite.winforms/Form1.cs](AlphaElite.winforms/Form1.cs) — Dashboard load gate + startup tab fallback for Admin A.
- [AlphaElite.winforms/ComplaintsVerification.cs](AlphaElite.winforms/ComplaintsVerification.cs) — **NEW** harness: `--complaints-inspect` / `--complaints-fix-schema` / `--complaints-verify`.
- [AlphaElite.winforms/Program.cs](AlphaElite.winforms/Program.cs) — dispatches for the three new flags.
- [AlphaElite.winforms/Phase2Verification.cs](AlphaElite.winforms/Phase2Verification.cs) — expected Admin A nav updated (Dashboard removed).
- [AlphaElite.winforms/Phase1LoginVerification.cs](AlphaElite.winforms/Phase1LoginVerification.cs), [Phase5Verification.cs](AlphaElite.winforms/Phase5Verification.cs), [Phase6Verification.cs](AlphaElite.winforms/Phase6Verification.cs), [Phase8FixesVerification.cs](AlphaElite.winforms/Phase8FixesVerification.cs), [Phase8Verification.cs](AlphaElite.winforms/Phase8Verification.cs), [Phase9SubscriptionVerification.cs](AlphaElite.winforms/Phase9SubscriptionVerification.cs) — stale Tenant A baseline realignment (see "Regression suite results"); no app code touched.

## Database changes (additive only — guarded, reported exactly)

Executed via `--complaints-fix-schema` (rows preserved, nothing dropped or recreated):

| Database | Change | Rows before → after |
|---|---|---|
| Tenant A (db68259) | `ADD [Category] nvarchar(100) NULL`, `ADD [Priority] nvarchar(50) NULL`, `ADD [StaffAssigned] nvarchar(100) NULL` | 0 → 0 (preserved) |
| Tenant B (db68416) | same three nullable columns (required because A and B share the legacy context — no functional change for B) | 0 → 0 (preserved) |
| Tenant C (db68417) | **no change** (its context ignores the new properties) | 2 (untouched) |
| Master | **no change** | — |

No EF migration was used; no table was dropped or recreated; existing complaint records remain intact.

## Verification results

### `--complaints-verify` — 71/71 PASS (0 failures)

- **Create:** temporary complaint created with all 11 fields; each field verified **read back from the Tenant A database** (identity id, member, category, subject, description, date submitted, priority, status, staff, resolution, Date Resolved = NULL for the open record).
- **Read:** a **second, fresh form instance** (simulated restart/reload) shows the record in the list and all fields in the detail view.
- **Update:** subject, priority, status and resolution changed (plus Date Resolved set); same Complaint ID returned (no duplicate row — count stayed baseline+1); Date Submitted **preserved**; values re-read from the database confirmed.
- **Delete:** complaint removed; Tenant A restored to its **exact baseline row set**; reload shows it gone; no residue.
- **Member dropdown:** DropDownList style; contents are exactly Tenant A's members (id + name + code); zero Tenant B-only or Tenant C-only members; Staff Assigned = Tenant A's adminA/gymstaffA only.
- **Tenant isolation:** Tenant B and Tenant C complaint counts unchanged throughout; the created subject exists only in Tenant A.
- **Admin A nav:** exactly `Members, Attendance, Payments, Feedback, Inquiries, Complaints` — no Dashboard, no Business Intelligence, no tenant dropdown, no branch selector, uniform 44px spacing (no gap where Dashboard was), all modules load with 0 errors.

### Regressions — see "Regression suite results" below

- `gymstaffA` and `adminA` log in with real credentials in the harness (password `123`).
- Tenant A Members / Attendance / Payments / Inquiries / Feedback / Complaints all load with 0 failures for Admin A and Gym Staff A.
- Admin B nav unchanged (**still has Dashboard**); Tenant C admin nav unchanged; both load with 0 failures.

### Build

- `dotnet build` — **0 errors**. (Warning totals: see below.)

## Regression suite results

| Suite | Result |
|---|---|
| --phase1-verify | 44/44 PASS (exit 0) |
| --phase2-verify | 43/43 PASS (exit 0) |
| --phase4-verify | 40/40 PASS (exit 0) |
| --phase5-verify | 47/47 PASS (exit 0) |
| --phase6-verify | 37/37 PASS (exit 0) |
| --phase8-fixes-verify | 38/38 PASS (exit 0) |
| --phase9-subscription-verify | 57/57 PASS (exit 0) |
| --phase8-verify (full) | 54/54 PASS (exit 0) |
| --complaints-verify | **71/71 PASS** (exit 0) |

### Stale-baseline realignment (pre-existing drift, not caused by this phase)

Your manual testing legitimately changed Tenant A's live data (members 1→2, attendance 1→2, payments 1→2 totalling 1500, feedback 1→2, inquiries 5→1). The older suites still asserted hardcoded 1/1/1=500, so they failed before any realignment. Seven harnesses were realigned without weakening any assertion:

- **"Unchanged" residue guards** (Phase6 §4, Phase8Fixes §D, Phase8 §8, Phase9 check 35) now capture Tenant A's live counts at harness start and compare at the check — semantically exactly what they always claimed ("this run moved nothing"), and immune to legitimate manual-test drift. Tenant B's still-correct hardcoded 5/4/3650 was left untouched.
- **Read-isolation checks** (Phase1 session-vs-direct-read, Phase2 factory-vs-raw-resolver, Phase5 aggregate-vs-direct-read) now compare two independent live reads instead of hardcoded counts.
- **Phase8 §5 read-back hardening:** the ALL-BRANCHES stamp check's independent DB read-back used a `CheckInDate.Date == DateTime.Today…` equality that never matched the committed row (EF translation quirk), reporting `stored=` even though the write itself was correct. Replaced with a range read-back on a single captured date. During diagnosis a raw SQL dump of the temp member's rows proved **both** attendance writes stamped branch 1 correctly in the database (`b=1`), i.e. the app behavior was always right — this was purely a harness read-back artifact. Final suite: 54/54.

## Build (final)

- `dotnet build AlphaElite.winforms/AlphaElite.winforms.csproj` — **0 errors** (`Build succeeded`).
- 33 distinct warning lines, **none in the new complaints code** (Form1.Phase4.cs and ComplaintsVerification emit no CS warnings): 25 unique compiler warnings are nullable-reference advisories (CS8600/CS8602/CS8622/…) concentrated in Form1.cs and older phase files (pre-existing patterns); plus EF1002/EF1003 raw-SQL analyzer advisories in verification harnesses and pre-existing NU1701 package-compatibility notices (SkiaSharp / OpenTK).

## Manual testing checklist

1. Log in `adminA` / `123` → nav starts at **Members**, no Dashboard entry, no BI, no tenant dropdown, no branch selector.
2. Open **Complaints** → click "Add complaint" with a member/category/subject/description → row appears immediately and is selected.
3. Click the row → all fields repopulate (detail view); edit Subject/Status/Resolution → "Update selected" → list reflects it; restart the app → still there.
4. Select a row → "Delete selected" → confirmation prompt → Yes → row gone.
5. Log in `gymstaffA` / `123` → all six modules still work; Complaints CRUD available.
6. Log in `adminB` / `123` → Dashboard still present, modules unchanged.

## Known notes

- Tenant A's members include one whose MemberCode (102) is *also* the code of a Tenant C member — member values were seeded with overlapping codes/names across databases before this phase. The harness therefore compares full (id + name + code) triples and asserts no dropdown entry exists that is not also a Tenant A member; provenance is additionally proven by the write checks (the created complaint landed only in Tenant A).
- Tenant C passwords remain the Phase 4F one-time passwords (per your earlier choice to leave them as-is); the harness builds Tenant C sessions from the account row when the demo password is rejected (same pattern as the existing Phase 4/6/8 harnesses).
- Tenant C's complaints module deliberately shows only its original columns — the shared form hides the three staff-only fields for member self-service accounts, and Tenant C's context ignores the new properties entirely.
