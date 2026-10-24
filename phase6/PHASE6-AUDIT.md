# Phase 6 — Read-Only Feature Audit (NO changes made yet)

**Scope of this step:** inspection only. No database writes, no schema changes, no code changes.
Tenant C row counts and fingerprints below were re-verified live via the app's read-only `--migration-dryrun` inventory and match the Phase 3/4 baselines exactly.

---

## 1. Verified current Tenant C state (live, read-only)

| Table | Rows | Notes |
|---|---|---|
| Branches | 2 | BR-A (1), BR-B (2), both active |
| Members | 6 | codes 102,101,103,104,105,106 — 4 active, 2 inactive (2 & 4) |
| Attendances | 5 | Sep 18–25, 2026 — **none within last 5 days** |
| Payments | 5 | total 4150.00; only 2 rows carry NextDueDate (1 overdue: Sep 30, 1 expired: Sep 25) |
| Feedbacks | 5 | ratings 1,5,4,3,1 — avg 2.8; BR-A 1 row, BR-B 4 rows |
| Inquiries | 10 | **all 10 IsFollowedUp=True** — zero pending anywhere |
| Subscriptions | 0 | table exists, empty |
| RetentionRecords | 1 | Status=Approved (member 3) |
| Complaints | 0 | table exists, empty |
| UserAccounts | 4 | admin/manager/gymstaff/member — unchanged |

Schema confirmed: every table the Phase 6 features need already exists — **no new tables and no new columns are required for anything in this phase.** RetentionRecords even has `PromotionDetails`, `AssignedStaff` columns that the UI currently ignores (can display without schema change).

---

## 2. Feature matrix

Legend: EXISTS = controls + code path present; WORKS = functionally correct as-is; ROLE = permission matrix covers it; BRANCH = branch/member scoping applied; NEEDS FIX = real defect found.

| FEATURE | EXISTS | WORKS | ROLE RESTRICTION | BRANCH FILTER | NEEDS FIX |
|---|---|---|---|---|---|
| Members | Yes | Mostly | Yes (matrix) | Yes (ApplyScope) | **Yes** — no search; no cross-branch guard on add/edit/delete; reports ignore scope |
| Attendance | Yes | Mostly | Yes | Yes (list + stats) | **Yes** — no duplicate check-in guard; charts/reports unscoped; add/update/delete lack EnsureBranchAccess |
| Payments | Yes | Mostly | Yes | Yes (list) | **Yes** — charts/reports unscoped; no EnsureBranchAccess on write; no renewal workflow |
| Renewals | Partial | No | — | — | **Yes** — none exists (schema supports via Payment.NextDueDate + Subscription) |
| Inquiries | Yes | Mostly | Yes | List only | **Yes** — retention panel + reports unscoped; no search |
| Feedback | Yes | Mostly | Yes (submit vs manage) | List + stats | **Yes** — retention panel + reports unscoped |
| Complaints | Yes | Yes | Yes (Submit vs Manage) | Yes | Minor — no complaint report export |
| Subscriptions | Yes | Partially | Yes (Admin only) | None (none needed: no BranchId on table) | **Yes** — no status/expiry logic, member status column, member-scoped view |
| Retention | Yes | Mostly | Yes | Partial | **Yes** — detection panel (inactive/due/pending) NOT branch-scoped; report unscoped |
| Member Portal | Partial | Partial | Yes (SelfService) | Self-only (verified) | **Yes** — KPI cards hidden for Member (good) but no welcome/membership summary; no complaint-status columns; no renewals view; My Payments/My Attendance tabs still show staff-style forms (add/update/delete buttons hidden but tabs look administrative) |
| Dashboard | Yes | Mostly | Yes | KPIs + 2 charts | **Yes** — attendance-trend & monthly-payments charts unscoped; missing KPIs: Payments Total, Open Complaints, At-Risk, Attendance This Month |
| Reports (QuestPDF) | Yes | Partially | Gated by module permission | **NO** | **Yes** — all 6 exports read whole tenant regardless of role/branch |
| BI | Partial | Partial | Yes | Partial | **Yes** — no branch comparison view; charts unscoped |
| User Management | Yes | Yes | Yes | Yes | Minor — grid lacks branch column; no password reset (create-only); acceptable as-is |

**Overall: the skeleton of every requested feature exists and the RBAC core is verified. The gaps are (a) scope leaks in charts/reports/retention panels, (b) missing search/filter UI, (c) missing renewal + member-portal polish, (d) missing dashboard KPIs.**

---

## 3. Detailed findings

### A. Scope leaks (the important ones — security/consistency)

1. **`LoadAttendanceTrendChartAsync`, `LoadFeedbackDistributionChartAsync`, `LoadMonthlyPaymentsChartAsync`, `LoadRetentionAsync` (3 detection panels), `LoadRetentionRecordsAsync` and all 6 PDF exports** query `tenantDb.Attendances/Feedbacks/Payments/Inquiries/RetentionRecords` **directly, without `ApplyScope`** (Form1.cs ~line 1755, 1800, 1845, 1935–2100, 2380+). A branch-scoped Manager/GymStaff sees tenant-wide numbers in the dashboard charts, the retention detection grids, and can export another branch's data to PDF. The member is protected from these screens by permissions, but branch-scoped staff are not.
   - **Fix:** route every one of these queries through the existing `ApplyScope` helper. No new architecture, no schema change. Member scoping irrelevant (permission-gated screens).
2. **RetentionRecords list** already flows through `LoadRetentionRecordsAsync` **without** scope — Manager (BR-A) sees the BR-B record. Fix with ApplyScope (member 3 is BR-B; Manager BR-A then sees 0 — correct).
3. **Write-path branch guard:** `btnMemberAdd/Update/Delete`, `btnAttendance*`, `btnPayment*`, `btnSubscription*`, `btnRetention*` re-fetch by id only (`FirstOrDefaultAsync(x => x.Id == _selectedX)`) — a crafted UI state could touch another branch's row. (`Complaints` and `UserAccounts` already guard via `EnsureBranchAccess`.)
   - **Fix:** call the existing `EnsureBranchAccess(entity.BranchId)` after fetch in every update/delete path (and `EnsureOwnMember` where member-scoped). This closes cross-branch *editing* while search remains read-only.

### B. Members module gaps

4. No search box; "filter Active/Inactive" only reachable via dashboard KPI click-through.
5. `btnMemberAdd` does not set `BranchId` — a new member defaults to **BranchId = NULL** ("unassigned"), and Admin-created members never appear in Manager/GymStaff grids. For Tenant C demo, Admin should pick a branch when creating (or default to first branch).
6. Members grid shows raw `Member` entities (all columns), no branch column.

### C. Attendance gaps

7. **Duplicate check-in risk:** `btnAttendanceAdd` inserts unconditionally — recording the same member twice on the same day creates two rows. Add a same-day duplicate guard (query-before-insert; warn instead of silent duplicate).

### D. Payments / Renewals

8. No renewal workflow. Schema already supports it (Payment.NextDueDate). **Plan (no schema change):** add "Renew" action on a member's latest payment row → prefills add-payment form with NextDueDate+cycle; plus an "Outstanding/Due/Overdue" summary label on Payments tab computed from latest payment per member (already computed in retention — reuse the same logic, scoped).

### E. Inquiries

9. All 10 migrated inquiries have `IsFollowedUp=True` — "Pending" filter/ KPI shows 0. Migrated data untouched (requirement); dashboard "Pending Inquiries" will read 0 — correct, will state in demo notes. Add name search + stats label (total/pending/followed-up).

### F. Feedback

10. Retention "pending inquiries" grid + feedback report unscoped (covered in A). Stats label (avg rating, count per rating) missing on Feedback tab.

### G. Subscriptions (Step 9)

11. Module works CRUD-wise but: no active/expired display, no member-status column, no branch view needed. Member portal must show own subscription status. **Plan:** add computed `StatusText` (Active/Expired by EndDate & IsActive), expiry info, and member-scoped grid when role=Member. Optionally seed **clearly-labeled demo subscriptions** for the exam — needs your approval as it writes tenant data (2 rows, member 1 & 3, Monthly/Annual, marked "(demo)" in... nothing stored — just reported). Won't touch migrated history.

### H. Member Portal (Step 11)

12. Member currently gets: My Membership (own row only, verified), My Attendance (own), My Payments (own), Feedback (submit + own history — self-scope via `_memberId`), Complaints (submit + own). Missing: welcome/membership summary, membership/subscription expiry, complaint status columns visible, staff-form fields (member picker etc.) visible-but-hidden-in-role — the tabs show empty staff forms which looks broken.
   - **Plan:** role-aware portal layout: for Member, Members tab becomes "My Membership" summary card (status, branch, subscription expiry, payment due), Attendance/Payments tabs read-only self-history with friendly empty states; Feedback/Complaints unchanged behavior. All existing permission gates stay.

### I. Dashboard / BI (Step 12)

13. Missing KPI cards: **Total Payments, Open Complaints, At-Risk Members, Attendance This Month**. Plan: add 3 more cards (Payments Total, Open Complaints, At-Risk) + This Month. KPI grid is a fixed 2-row layout at y=20/70, cards 190x40; a 3rd row fits (content area ~600px tall, charts start y≈160).

14. Branch comparison (Admin/Manager): plan a small "Branch Comparison" panel on Dashboard (per-branch member/attendance/payment/feedback aggregates), **Admin sees both branches; Manager sees own branch only** (GymStaff/Member never see it). Reads tenant DB directly — no data duplication into Master.

### J. Reports (Step 13)

15. All exports currently unscoped (covered in A) — fix = ApplyScope. Complaints export missing — add btnExportComplaints on Complaints tab (branch-scoped). Reports also show no branch name in header — add to `summaryLine`. Subscriptions report missing — add export button too.

### K. UI/UX polish (Step 14)

16. KPI cards are 40px tall with `\n` — two-line text renders clipped ("Attendance This Week" wraps to 3 lines at 190px width — labels are cramped). Plan: resize KPI cards to 190x48 & shorten captions ("This Week Attendance" → keep, but increase height; or two labels). Actually simplest: height 48 and caption+value in one line ("TOTAL MEMBERS: 6") — no, keep \n but 48px + SmallerFont value line.
17. Complaints/User Mgmt tabs: `dgvComplaints` is 960x400 at fixed positions — at window 1100x700 with 210px sidebar, content ≈ 870px wide → **the grids extend ~90px past the right edge** (no anchor) — real clipping bug. Plan: set `Anchor = Top|Right|Bottom|Left`... actually simplest: resize to 830 wide.
18. Phase 4 tabs lack the dark styling of older tabs (DataGridView default colors) — apply same `BackgroundColor` + default cell style (already set BackgroundColor, but default cell background is white with white text? — DataGridView default cell style: white background, black text → readable; leave). Priority: clipping + KPI cards.

### L. Error handling (Step 15)

19. All CRUD catches show raw `ex.Message` ("Database error: …") — can leak SQL errors to users. Plan: friendly message + keep technical detail in title bar? Requirement: "Never display raw SQL exceptions to normal users." Plan: wrap in a helper `ShowDbError(ex)` that shows a friendly line + short exception type; full detail only when role is Admin/MasterAdmin.

### M. Error state: empty data & unavailable DB

20. Empty grids show nothing (fine); `LoadAllModulesAsync` already collects per-module failures into one dialog (good). Member portal empty states: friendly "No attendance recorded yet" via grid empty-text — DataGridView has no empty-text property; skip heavy custom work; empty grid + label already acceptable. Low priority, will note.

### N. No regressions to Phase 4 verification

21. Phase 4 harness (33 checks) asserts exact nav strings per role and scoped counts. My changes must not alter: nav labels, nav visibility logic, scoped counts, complaint workflow, rehash behavior. The polish work touches `ApplyRoleVisibility` only if I change nav — **I will not change nav labels/order**. KPI card size changes don't affect verification. Chart scoping changes affect only what Manager/GymStaff *see* in charts (not asserted by the harness; DescribeScopedCountsAsync unaffected).

---

## 4. What needs your decision

1. **Approve the fix plan above** (all code-only, no schema changes anywhere, no Master DB writes, no Tenant A/B writes).
2. **Demo subscriptions:** may I create 2 clearly-marked demo subscription rows (member 1 Monthly + member 3 Annual, active) in Tenant C for the exam? Migrated data untouched; rows deletable via the app UI. (Yes/No)
3. **Demo attendance rows:** Tenant C has zero attendance within the last week, so "Attendance Today/This Week" KPIs read 0 on exam day. May I add a few clearly-marked demo check-ins dated exam-week for members 1–6? (Yes/No) — otherwise dashboard "This Week" will read 0 live; report will note this as expected.
4. **Demo complaints:** Complaints table is 0 rows. May I create 2 demo complaints (one per branch) so the workflow is demonstrable end-to-end? They'd be created through the app's own complaint workflow (member 1) and/or seeder. (Yes/No)

Everything else is code-only and reversible; tenant data stays untouched except the demo rows you approve above.
