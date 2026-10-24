# Phase 7 — Read-Only Pre-Flight Findings (final acceptance audit)

Date: 2026-10-01 · Scope: full solution audit, **no changes during the audit** (one cosmetic fix was applied *after* this report, see §5).

---

## 1. PASS items

| Area | Evidence |
|---|---|
| Build | `dotnet build AlphaElite.winforms.slnx` → **0 errors**, 27 warnings (all pre-existing; documented in §3) |
| Projects & references | 4 projects: `AlphaElite.winforms` (net10.0-windows, WinExe) → `AlphaElite.domain` + `WinFormsLibrary1` (infrastructure); `AlphaElite.api` (net10.0) → same. EF Core 10.0.11, QuestPDF, LiveChartsCore |
| Master DB | Companies=3, CompanyDatabase=3, Devices=1; AspNet* Identity tables with superadmin row; structural checks pass (`--phase5-verify` §9) |
| Tenant A DB | Members=1, Attendances=1, Payments=1 (500.00), Feedbacks=1, Inquiries=5 — byte-stable vs baseline (fresh `--migration-dryrun` inventory) |
| Tenant B DB | Members=5, Attendances=4, Payments=4 (3650.00), Feedbacks=4, Inquiries=5, Retention=1, Subscriptions=0, UserAccounts=1 (legacy SHA-256, untouched) — matches baseline |
| Tenant C DB | Members=6, Attendances=11, Payments=5 (4150.00), Feedbacks=5, Inquiries=10, Complaints=2, Subscriptions=2, Retention=1, UserAccounts=4, Branches=2 — matches expected demo state; migrated member rows unchanged (102:A, 101:I, 103:A, 104:I, 105:A, 106:A) |
| EF migrations | Tenant C `__EFMigrationsHistory`=1 (hand-authored, applied); A/B on their original histories (4/6 rows); `dotnet ef` not required |
| Authentication | Real-password logins verified headless: master superadmin (Identity PBKDF2, inside 57/57 phase 5 run) and Tenant C **admin / manager / gymstaff / member** (21/21 selftest each, sequential logins; archives in `phase7/phase7-selftest-tenantc-*.txt`). Wrong password rejected; company-deactivation login enforcement verified (phase 5 §8) |
| Authorization / RBAC | `--phase4-verify` **36/36**: nav visibility per role, programmatic permission matrix (Member/GymStaff/Manager blocked correctly, Admin/MasterAdmin allowed), `EnsureAuthorized` throws |
| Tenant isolation | A/B/Master untouched by every harness run (counts re-verified after each phase); tenant resolver driven from Master `CompanyDatabase` rows (no hardcoding) |
| Branch isolation | Manager (BR-A) sees members=1/attendance=4/payments=1/feedback=1/inquiries=5/complaints=1; GymStaff (BR-B) sees 5/7/4/4/5/1; both "capped below tenant total" checks pass; write + complaint-workflow boundaries pass inside a rolled-back transaction |
| Member self-only | member (MemberId 1): members=1, attendance=4, payments=1, feedback=1, own complaints only; nav limited to portal items; staff entry forms hidden (`ApplyMemberPortalLayout`) |
| Master / Super Admin | MasterLoginForm → MasterDashboardForm constructs; full master nav present; tenant switching resolves through Master DB |
| Dashboard / BI | 11 KPI cards at 48px height (no clipped captions), 4 charts + branch comparison grid; all dashboard queries run clean in every harness `Form1` construction |
| Modules | Members/Attendance/Payments/Inquiries/Feedback/Subscriptions/Retention/Complaints + searches + renewals + portal summary exercised headless via selftests, phase 4/6 harnesses; duplicate check-in guard, amount>0 validation, status workflow verified |
| Demo data | `--phase6-demo` re-run → **0 new rows** (SKIP on all three blocks); before/after inventory row counts identical |
| Error handling | `ShowDbError`: raw detail only for Admin/MasterAdmin; others get friendly message + exception type; error-signature scans (Invalid column/object, login failure, NRE, BranchId/MemberId) pass in all harness runs |

Harness results this phase: **phase6-verify 37/37 · phase4-verify 36/36 · phase5-verify 57/57 · selftest B 23/23 · selftest C 21/21 · selftest A 22 checks / 5 pre-existing failures · phase6-demo +0 rows**

## 2. FAIL items

- **None open.** The single harness failure observed during the audit (phase 4 "admin login before upgrade: rejected") was operator error — the harness was run without `ALPHA_VERIFY_PASSWORD`, and its config fallback (`TenantCredentials:TenantB:Password` from the WinForms appsettings copy) is not the Tenant C admin password. Re-run with the correct env var → 36/36. **No code or data was affected** (the rehash write path only executes after a successful authentication, and the stored admin hash was already PBKDF2).

## 3. WARNINGS

1. **UI defect (fixed after audit):** dashboard `Export PDF` button was placed at (700,20) by the designer and completely covered by the `Avg Rating` KPI card after the Phase 6 relayout — invisible to users. Fixed in `Form1.Phase6.cs` by moving it to the one free KPI-grid cell (620,78), sized like a card. One-line layout change; build re-verified.
2. **Demo password stored in source tree:** `WinFormsLibrary1/appsettings.json` → `TenantCredentials.TenantB.Password` actually holds the **Tenant C admin demo password** (misleading key name; used by harnesses as the auth candidate). Secret on disk by design for harness convenience.
3. **Demo password in a generated report:** `phase5/PHASE5-REPORT.md` line 64 contains the master superadmin demo password in plaintext. It is the only persistent record of that password (deliberate disclosure policy from Phase 5) — do **not** delete before the exam; do **not** publish the repo without redacting.
4. **DB connection strings with passwords** in `AlphaElite.winforms/appsettings.json`, `WinFormsLibrary1/appsettings.json`, `AlphaElite.api/appsettings.json` (+ their `bin` build-output copies). Accepted for the exam (no user-secrets infrastructure); move to environment variables / user secrets afterwards.
5. **Hardcoded fallback master connection string** in `WinFormsLibrary1/data/MasterAlphaEliteDbContextFactory.cs` (line 20) — currently a `RESTORE_YOUR_ORIGINAL_MASTER_DB_PASSWORD_HERE` placeholder, not a live secret; recommend replacing with configuration-only lookup after the exam.
6. **API `.http` request file** (`AlphaElite.api/AlphaElite.api.http`) contains server host names (no passwords).
7. **Compile warnings (pre-existing, harmless):** NU1701 ×6 lines (OpenTK, OpenTK.GLControl, SkiaSharp.Views.WindowsForms .NET Framework compat shims), CS0108 (`LoginForm.CompanyName` hides `Control.CompanyName`), CS8600/8602/8604/8625 nullability in `Form1.cs`/`StartupVerification.cs`/`TenantCDataMigration.cs`, CS8622 nullable EventHandler signatures on the two Phase 6 PDF handlers. None suppressed, none indicate broken functionality.
8. **Tenant A selftest 5 failures** — pre-existing since before Phase 5: no `UserAccounts`, `Subscriptions`, `RetentionRecords` tables in the legacy Tenant A schema. Unchanged, out of scope per change control (no schema changes allowed).
9. **Harness env-var dependency:** `--phase4-verify`, `--phase5-verify` skip or fail their password-path checks without `ALPHA_VERIFY_PASSWORD` (+ `ALPHA_MASTER_USERNAME`/`ALPHA_MASTER_PASSWORD` for phase 5). Exam-day command lines are documented in the acceptance report.
10. **`rg`/`sqlcmd` unavailable** in this environment — secret scanning was done with the code-search tool, DB verification via the app's read-only `--migration-dryrun` and EF-based harnesses.

## 4. Remaining known limitations (carried forward, accepted)

1. `AlphaElite.api` is **fully anonymous** (no authentication/authorization middleware; open POST endpoints for companies/devices/members/etc.). Documented since Phase 5; explicitly not rewritten this phase per instructions. Do not expose it publicly.
2. Tenant A runs the pre-auth schema (no user accounts / subscriptions / retention) — it can only be shown as a "legacy source tenant".
3. Tenant B admin keeps its legacy unsalted SHA-256 hash (branch-unaware tenants are never rehashed, by design; the UI reports `rehashNeeded=True`).
4. PDF save dialogs and visual layout (portal card, charts) cannot be driven headless — a 5-minute manual click-through is recommended before the exam (script provided).
5. Master-level billing is a UI-level panel only (no master billing schema) — Phase 6 known gap.
6. No git repository in this folder — no revert safety net; changes were kept minimal for that reason.
7. `dotnet ef` is not installed; migrations are hand-authored (none needed this phase).

## 5. Recommended fixes ranked by severity

| # | Severity | Item | Status |
|---|---|---|---|
| 1 | Low (cosmetic UX) | Dashboard Export PDF button occluded by KPI card | **Fixed** (one-line relocation in `Form1.Phase6.cs`; build + warnings unchanged) |
| 2 | Advisory (post-exam) | Redact the superadmin password from `phase5/PHASE5-REPORT.md` and move all credentials to env vars / user secrets | Recommended after exam — not done now (report is the only record of the master password and change control forbids credential churn) |
| 3 | Advisory | Replace `MasterAlphaEliteDbContextFactory` hardcoded fallback with config lookup; rename the misleading `TenantCredentials:TenantB:Password` harness key | Post-exam |
| 4 | Advisory | API authentication (token/JWT) before any deployment | Post-exam (documented limitation) |
| 5 | Info | Tenant A schema upgrade (auth + subscriptions + retention) | Out of scope |

No architectural problems were found that would require stopping under change control.
