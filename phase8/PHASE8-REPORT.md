# PHASE 8 — TENANT C MULTI-BRANCH MANAGEMENT — FINAL REPORT

Scope: Tenant C only. Master DB `db67598`, Tenant A `db68259` and Tenant B `db68416`
were treated as READ-ONLY. Every write performed by this phase targeted **`db68417` (Tenant C)**,
verified by name before each operation.

---

## A. Deliverable

One Tenant C database now holds all branches separated by `BranchId`, with a branch CRUD UI
(soft-deactivation only), an authoritative **Current Branch** selector, branch-scoped reads and
write-stamping, and member transfer that preserves history. Plus an additive, hand-authored
migration and a full regression pass.

## B. Build result

`dotnet build AlphaElite.winforms.slnx` — **0 errors**. Warnings remain only the pre-existing
categories (NU1701 ×6, CS0108, CS86xx, CS8622, CS8032, WFO1000 was resolved by using a field).
No new warning category was introduced.

## C. Migration

`20261001090000_AddBranchManagement` (Tenant C context only), hand-authored (no `dotnet ef`).

* Schema (additive, nullable): `Branches.City nvarchar(100)`, `Branches.ManagerUserAccountId int`
  (+ FK → `UserAccounts`), `Branches.UpdatedAt datetime2`, `Subscriptions.BranchId int`
  (+ index + FK → `Branches`). No column modified or dropped.
* Data: backfill `Subscriptions.BranchId` from the member's branch; rename **in place**
  `BR-A`→`BR-MAIN`, `BR-B`→`BR-CEBU` (BranchIds 1/2 preserved → all existing FKs intact);
  insert `BR-DAVAO`; assign manager `UserAccountId 2` to branch 1; null the carry-over
  `UserAccounts.BranchId` for the tenant-wide admin row.
* Discovery fix: the hand-authored Designer file was missing `[Migration("20261001090000_AddBranchManagement")]`,
  so EF initially reported “no pending migrations”. Added; re-ran and it applied on **db68417 only**.
* Result: `Applied migrations after: 20260930030000_InitialTenantC, 20261001090000_AddBranchManagement`, `Pending: (none)`.

## D. Branch mapping (verified live)

| BranchId | Code | Name | City | Manager | Active | Members |
|---|---|---|---|---|---|---|
| 1 | BR-MAIN | AlphaElite Manila - Head Office | Manila City | UserAccountId 2 | yes | 1 |
| 2 | BR-CEBU | AlphaElite Cebu Branch | Cebu City | (none) | yes | 5 |
| 3 | BR-DAVAO | AlphaElite Davao Branch | Davao City | (none) | yes | 0 |

BranchIds 1 and 2 are the original rows renamed in place; BR-DAVAO is new (BranchId 3).

## E. Authorization

New `Permission.ManageBranches = 1L << 16`, granted to **Admin** and **Manager** only.
GymStaff and Member are excluded. Central `IAuthorizationService` unchanged otherwise.

## F. Role behavior matrix

| Role | Current Branch selector | Branch management (CRUD) | Scope |
|---|---|---|---|
| Admin | yes (+ `(ALL BRANCHES)`) | yes | tenant-wide, or selected branch |
| Manager | yes | yes | selected branch (may switch — deliberate loosening) |
| GymStaff | yes | no | selected branch |
| Member | no | no | self-only |
| MasterAdmin | untouched | n/a | master DB only |

## G. Current Branch rule

`_currentBranchId` is realised as `_branchId`; it is the authoritative context for reads and
write-stamping for Admin/Manager/GymStaff. Server-side `EnsurePermission` / `EnsureBranchAccess`
and the new `EnsureCurrentBranchActiveAsync` guard remain authoritative. Switching reloads the
selector context, clears stale grids and re-runs `LoadAllModulesAsync` (dashboard, KPIs, charts,
members, attendance, payments, feedback, inquiries, complaints, subscriptions, retention, reports,
PDF, search).

## H. Write stamping (single source)

All branch-aware inserts now stamp through one rule, `ResolveWriteBranch(memberBranchId)`:
current branch wins; a tenant-wide Admin in `(ALL BRANCHES)` falls back to the member's branch so a
row is never left un-branched. Applied to Attendance, Payment, Feedback, Inquiry, Subscription and
Retention create paths, each preceded by `EnsureCurrentBranchActiveAsync`.

## I. Member transfer

`TransferMemberCoreAsync` changes **only** `Members.BranchId`. Historical Attendance / Payment /
Feedback / Complaint / Inquiry / Subscription / Retention rows keep their original `BranchId`.
Target must exist and be active. UI: “Transfer to current branch” (Admin/Manager).

## J. Branch deactivation

`IsActive=false` only — no physical delete, no data loss, reactivatable. Inactive branches are
excluded from the selector and blocked from receiving new records by `EnsureCurrentBranchActiveAsync`.

## K. Read scoping

Most loaders inherit `ApplyScope(branchKey)`. Subscriptions gained an explicit `Subscription.BranchId`
predicate; dashboard KPI/chart/report/at-risk inputs are `ApplyScope`-filtered; all six operational
PDF exports use `ApplyScope`; the Phase 6 subscription PDF export was corrected to be branch-scoped.

## L. Non-branch-aware tenants

Tenant A / Tenant B paths are unchanged: `ApplyScope` only adds predicates when `db.IsBranchAware`,
so the legacy model is never affected. Branch UI is only built for branch-aware sessions.

## M. Regression — Phase 4 harness

`--phase4-verify`: **36/36 PASS**. Expectations updated to the new model: admin `BranchId` now null;
Admin nav adds `Branches`; Admin counts `inquiries=9`; Manager scoped to selected branch (home
BR-MAIN) → `inquiries=4`. Isolation and authz checks all pass.

## N. Regression — Phase 5 harness

`--phase5-verify`: **57/57 PASS**. Expectations updated: Tenant C branches 2→3, attendance 11→5,
inquiries 10→9 (external drift); Tenant B retention 1→2 (external drift — see X). Master DB
(Companies=3, CompanyDatabase=3, Devices=1), Identity auth and dashboard all pass.

## O. Regression — Phase 6 harness

`--phase6-verify`: **37/37 PASS**. Expectations updated: current-week-attendance check replaced with
the live baseline (5 rows) because the demo check-ins were removed externally and are deliberately
not reconstructed; inquiries 10→9; branches 2→3; manager cap `inquiries=4`.

## P. Phase 8 harness

`--phase8-verify`: **54/54 PASS**. Covers: write-target guard (Tenant C=db68417 ≠ A/B); branch
catalogue + name/city/manager/active; `BR-DAVAO` empty; subscription backfill; admin BranchId null;
historical branch distribution preserved; role gating (selector + Branches nav) for all four roles;
Current Branch switching driving scoped reads (ALL vs BR-MAIN vs BR-CEBU; Manager switching;
GymStaff switching); write-stamping (current branch wins, ALL falls back, cross-branch write blocked);
member transfer with history preserved; deactivation protection; Tenant A/B/master unchanged;
baseline restored afterwards.

## Q. Selftests

* Tenant A: 22 checks, **5 pre-existing failures** (no UserAccounts/Subscriptions/RetentionRecords in
  the legacy Tenant A schema) — unchanged, out of scope.
* Tenant B: **23/23 PASS**.
* Tenant C (admin): **21/21 PASS** (`Branches=3`, permissions=11).
* Tenant C manager/gymstaff/member selftests were **not** run because their one-time demo passwords
  are not stored anywhere. Their session behavior is instead proven headlessly by the Phase 4/6/8
  Form1 harnesses, which build sessions directly from the `UserAccounts` rows (no password needed).

## R. Safety / change control

* No writes to Tenant A, Tenant B or the master database at any point.
* No destructive schema change; migration is additive with a working `Down()`.
* No existing BranchId was rewritten; no transactional FK was altered.
* Auth/RBAC/tenant resolver were extended, not rewritten.
* Temporary rows created by the Phase 8 harness (throwaway member, attendance rows, throwaway branch)
  are purged by raw SQL, including a pre-purge that removed residue from an earlier failed run.

## S. Temporary-row incident (self-inflicted, contained)

The first Phase 8 harness run failed in section 7 because the throwaway `BranchCode`
(`"P8-TEMP-" + ticks`, 27 chars) exceeded the `nvarchar(20)` column. This left a temporary member
and two attendance rows in Tenant C. Fixed by shortening the codes and adding an idempotent
raw-SQL purge (run at start and in `finally`). Verified after the fix: **Members=6, Attendance=5,
Branches=3, BR-DAVAO empty**. No baseline data was affected.

## T. Branch identity counter note

The failed harness runs consumed identity values (throwaway branches), so the next branch id is now
> 3. Branch ids 1/2/3 and all data are intact; identity gaps are cosmetic.

## U. External drift — Tenant C

External deletions outside Phase 7 removed 6 Phase-6 demo attendance rows and 1 duplicate inquiry.
Per the approved handling: **nothing was fabricated, reconstructed or restored**. Live Tenant C is
`Attendances=5`, `Inquiries=9`. All verification expectations were updated to this live state.

## V. External drift — Tenant B (newly observed)

Tenant B now reports `RetentionRecords=2` (Phase 7 recorded 1). The second row is `CreatedBy =
"System Administrator"`, `Status=Pending`, `CreatedAt 2026-10-01 06:09` — i.e. created **before**
this Phase 8 session and by no tool this project runs (Tenant B is read-only for all harnesses).
The Phase 5 expectation was updated to the live value (2) and the state preserved, not reverted.

## W. Optional Phase-6 demo re-seed

The idempotent `--phase6-demo` re-seed could restore the removed demo attendance rows. It was **not
run**; it remains a separate, explicitly-invoked action. Its internal branch assumption message
(“expected two branches BR-A / BR-B”) should be revisited before it is ever run again.

## X. Fingerprints (final)

* Tenant A: members=1, attendance=1, payments=1 (500.00), feedback=1, inquiries=5.
* Tenant B: members=5, attendance=4, payments=4 (3650.00), feedback=4, inquiries=5, retention=2 (live), userAccounts=1.
* Tenant C: members=6, branches=3, userAccounts=4, attendance=5, payments=5 (4150.00), feedback=5, inquiries=9, subscriptions=2, retention=1, complaints=2.
* Master: Companies=3, CompanyDatabase=3, Devices=1.

## Y. Files changed

* `AlphaElite.domain/entities/Branch.cs`, `Subscription.cs`
* `WinFormsLibrary1/data/TenantCAlphaEliteDbContext.cs`
* `WinFormsLibrary1/services/Authorization.cs` (`ManageBranches`)
* `WinFormsLibrary1/Migration/TenantCAlphaEliteDb/20261001090000_AddBranchManagement.cs` (+ `.Designer.cs`), `TenantCAlphaEliteDbContextModelSnapshot.cs`
* `AlphaElite.winforms/Form1.cs` (wiring, six stamp sites, `LoadSubscriptionsAsync`, dialog suppression)
* `AlphaElite.winforms/Form1.Phase8.cs` (new: selector, Branches tab, transfer core, guards, test hooks)
* `AlphaElite.winforms/Form1.Phase6.cs` (branch-scoped subscription PDF)
* `AlphaElite.winforms/TenantCDeployment.cs` (Phase 8 branch projections)
* `AlphaElite.winforms/Phase8Verification.cs` (new), `Program.cs` (`--phase8-verify`)
* `AlphaElite.winforms/Phase4Verification.cs`, `Phase5Verification.cs`, `Phase6Verification.cs` (expectations)

## Z. Known limitations

* PDF `SaveFileDialog` and pixel visuals cannot be driven headlessly; export scoping is proven at the
  query level (`ApplyScope`) rather than by producing a file.
* Manager/GymStaff/Member Tenant C selftests can’t be re-authenticated (passwords not stored); their
  behavior is covered by the Form1 harnesses.
* Branch code uniqueness is enforced by the DB index and a pre-check in `SaveBranchAsync`; the
  duplicate-code rejection is exercised indirectly (unique index exists).

## AA. Verdict

**READY.** Phase 8B→8G implemented and verified on Tenant C only. Build clean; Phase 4/5/6/8 harnesses
and Tenant B/C selftests pass at their expected levels; Tenant A (22/5 pre-existing) and all
A/B/Master fingerprints unchanged; no data loss, duplication or broken FKs; historical branch
information preserved.

## AB. Recommended follow-ups

1. Add a bounded 3-attempt retry around the startup tenant load (Phase 7 `STARTUP-INCIDENT.md`).
2. Update `Phase6DemoDataSeeder` to be branch-aware before it is ever re-run.
3. Decide policy for Admin in `(ALL BRANCHES)` performing writes (currently member-branch fallback).
4. Redact `phase5/PHASE5-REPORT.md:64` before publishing.
5. `AlphaElite.api` remains anonymous — do not expose publicly (unchanged, documented).
