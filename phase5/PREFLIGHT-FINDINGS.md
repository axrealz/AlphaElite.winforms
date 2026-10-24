# Phase 5 — Master / Super Admin: Read-Only Preflight Findings

**Status: PREFLIGHT COMPLETE — NO CHANGES MADE.** No migrations, inserts, updates, deletes, or schema changes were executed. Zero rows were written anywhere. Nothing in the app or in any database was modified by this inspection.

This report states what exists today, what must be added, what can be reused, and the risks — then implementation stops here for your review.

---

## 1. Current Master Identity schema (verified live)

`MasterAlphaEliteDbContext` extends `IdentityDbContext` (the **non-generic** version → default `IdentityUser` / `IdentityRole`). The context maps exactly three domain entities plus the Identity sets: `Companies`, `CompanyDatabase`, `Devices`. Package `Microsoft.AspNetCore.Identity.EntityFrameworkCore` **10.0.11** is already referenced by the infrastructure project, so `UserManager<TUser>`, `RoleManager<TRole>`, and `IPasswordHasher<TUser>` are available **without adding any package**.

Live Master DB (db67598) — all seven standard ASP.NET Identity tables exist with the standard EF Core column shapes:

| Table | Rows | Notes |
|---|---|---|
| AspNetUsers | **0** | 15 standard columns; `PasswordHash nvarchar(max)`, `LockoutEnd datetimeoffset`, `AccessFailedCount int`, etc. |
| AspNetRoles | 0 | `Id, Name, NormalizedName, ConcurrencyStamp` |
| AspNetUserRoles | 0 | `UserId, RoleId` (composite PK, standard) |
| AspNetUserClaims | 0 | Standard shape |
| AspNetRoleClaims | 0 | Standard shape |
| AspNetUserLogins | 0 | Standard shape |
| AspNetUserTokens | 0 | Standard shape |

Seeding the SuperAdmin will be the **first Identity write ever** on this database.

Two cosmetic observations (no action requested):
- `__EFMigrationsHistory` contains 4 migration records, but the Identity tables carry no FK constraints or indexes — consistent with the Identity tables having been created by `EnsureCreated()` rather than by Identity-specific migrations. The columns match the default Identity model exactly, so EF can read/write them normally with no schema change.
- The design-time factory `MasterAlphaEliteDbContextFactory` contains a **placeholder** connection string ("RESTORE_YOUR_ORIGINAL_MASTER_DB_PASSWORD_HERE") — not a real secret; it is unused by the runtime and should not be filled with a real password.

## 2. Current Master data (verified live)

| Table | Rows | Contents |
|---|---|---|
| Companies | 3 | 1=COMP001 "My First Company" (active), 2=COMP002 "My Second Company" (active), 3=COMP003 "Alpha Elite - Tenant C" (active) |
| CompanyDatabase | 3 | 1→TenantA (db68259, CredentialKey `TenantA`, active), 2→TenantB (db68416, `TenantB`, active), 4→TenantC (db68417, `TenantC`, active) |
| Devices | 1 | DEV003 "Super Extension PC" (CompanyId 1) |
| All AspNet* tables | 0 | Empty, as verified in Phase 3 |

Migration history: `20260907101457_InitialMasterErp`, `20260914084122_AddDeviceToMasterErp`, `20260915072911_AlphaElite`, `20260915115009_AddCredentialKeyToCompanyDatabase`.

## 3. Current authentication flow (verified from source)

`Program.Main` → `AppRuntime.Create()` builds `MasterAlphaEliteDbContext` from `ConnectionStrings:MasterErp` → `TenantAuthenticationService.GetAvailableTenantsAsync()` reads `CompanyDatabase ⋈ Companies` (active only) → `LoginForm` shows the tenant dropdown → `AuthenticateAsync(companyId, username, password)`:

1. `TenantDatabaseResolver` resolves CompanyId → `TenantDatabaseInfo` (ServerName / DatabaseName / CredentialKey) from `CompanyDatabase` where `IsActive`.
2. `TenantDbConnectionFactory` builds the connection string from **configuration** (`TenantCredentials:{CredentialKey}:UserId/Password`) — the CredentialKey is a lookup key, never a database login.
3. The correct context is chosen per tenant: `TenantAlphaEliteDbContext` (A/B) vs `TenantCAlphaEliteDbContext` (C, branch-aware).
4. The user row is projected (no full entity materialization), `PasswordHasher.Verify` (PBKDF2 / legacy SHA-256) checks the password, roles normalized via `Roles.Normalize`.
5. If the tenant is branch-aware, `UpgradePasswordHashIfNeededAsync` transparently upgrades legacy hashes to PBKDF2 (Tenant A/B are **never** written).

`Program` then constructs `UserSession { CompanyId, CompanyCode, CompanyName, IsBranchAware, BranchId, MemberId, Username, FullName, Role }` and opens `Form1(session, tenants, dataContextFactory, authorizationService)`. `Form1.PopulateTenantSwitcher` fills `cboTenantSwitcher` from the same tenant list, but for **non-MasterAdmin users the switcher is locked** (`Enabled = false`) to the authenticated tenant — exactly where Phase 5 tenant switching will plug in.

## 4. What must be added (design items — NOT yet implemented)

1. **Identity usage via the existing context.** `MasterAlphaEliteDbContext` already exposes the full Identity sets (default `IdentityUser`/`IdentityRole`). A `UserManager`/`RoleManager`-backed service can be built on it directly; no new tables and no schema change are needed. (A separate small `MasterIdentityDbContext` mapping the *same* tables is possible but adds no value here.)
2. **`MasterAuthenticationService`** in the infrastructure project: verifies credentials through Identity (`CheckPasswordSignInAsync` semantics, lockout-aware) against the Master DB. It never touches tenant `UserAccounts` and never uses the tenant `PasswordHasher`.
3. **`MasterUserSession`** (new, separate from tenant `UserSession`): `IsMasterAdmin`, `MasterUserId`, `Username`, `Role`. Kept a distinct **type** so a master login can never be confused with a tenant role or accidentally flow into `Form1`.
4. **A separate `MasterLoginForm`** (dark charcoal + gold, matching the existing brand). The tenant `LoginForm` stays untouched in behavior.
5. **`MasterDashboardForm`** with the nav: Dashboard / Companies–Tenants / Tenant Databases / Subscriptions & Billing / Users–Administrators / Tenant Overview / Reports–BI / System–Admin Panel / Logout — reusing `CreateNavButton`, the sidebar, the gold/charcoal palette, and the `_visibleNav` / `DescribeNav` headless-diagnostics pattern.
6. **`MasterAdminSeeder`** — one verified SuperAdmin account seeded via Identity APIs with a crypto-random password (printed once, stored nowhere).
7. **`Phase5Verification`** headless harness (`--phase5-verify`) following the `Phase4Verification` pattern: master login, role, each master screen, tenant switching to A/B/C, tenant-unchanged checks, negative authorization checks.

## 5. What can be reused (no changes needed)

- `TenantDatabaseResolver`, `TenantDbConnectionFactory`, `TenantDataContextFactory`, `TenantOption` — for tenant switching and BI aggregation, without hardcoding any CompanyId.
- `Roles` / `Permission` / `RoleAuthorizationService` — `Roles.Normalize` already maps "SuperAdmin" → `MasterAdmin`; `Permission.TenantManagement`, `Permission.AdminPanel`, `Permission.BusinessIntelligence` already exist in the matrix for `Roles.MasterAdmin`.
- The palette and layout conventions from `LoginForm` / `Form1` (dark #121212 background, gold #D4AF37, `CreateNavButton`, sidebar, KPI-card dashboards with LiveCharts).

## 6. Subscription/billing gap (reported, not fixed)

The only subscription model that exists is the **tenant-level, per-member** `Subscription` entity (`SubscriptionId, MemberId, PlanName, Price, BillingCycle, StartDate, EndDate, IsActive`) mapped in the **tenant** contexts only. Tenant A and Tenant B have no `Subscriptions` table; Tenant C has the table with **0 rows**. There is **no Master-level billing entity** (no plan/price per Company), so a SaaS-level "Subscriptions & Billing" admin page has no backing schema today.

Options (your call):
- **(A) Recommended:** read-only view of each tenant's member-level subscriptions inside the master area — no schema change needed.
- **(B)** Add a new Master-level billing table — requires your explicit approval (new Master DB table, Step 14).
- **(C)** Ship the page as a documented gap ("not supported by current schema").

## 7. API authentication state

`AlphaElite.api/Program.cs` maps controllers and endpoints with **no authentication middleware** — the API is fully anonymous today (its tenant endpoints accept a companyId path parameter with no auth). **No API changes are planned in this phase**; this is reported as a remaining security item.

## 8. Conflicts and risks

1. **Company activation is not consistently enforced at login.** `GetAvailableTenantsAsync` checks `CompanyDatabase.IsActive && Company.IsActive`, but `AuthenticateAsync` itself does not re-check `Companies.IsActive`. A deactivated company would still authenticate for users who know the credentials. Phase 5 should add that one-line check to `AuthenticateAsync` (small, targeted change to an existing verified service — flagged for your approval).
2. **Role naming:** `Roles.Normalize` maps "SuperAdmin" → `MasterAdmin`. Plan: the Identity role in the Master DB is named `SuperAdmin`; the application session Role becomes `MasterAdmin` (canonical). This mapping is intentional and must stay consistent.
3. **Master vs tenant separation:** `Form1` must never be reachable with a master session and `MasterDashboardForm` never with a tenant session. Enforced by type separation (distinct session types + distinct forms), not by string checks.
4. Aggregate queries over tenant DBs from the master area must handle unavailable/inactive tenant DBs gracefully (per-tenant try/catch, report failures without crashing the dashboard).
5. **No git safety net** and `dotnet ef` is not installed — all changes must be hand-authored and small; Master DB writes are limited to Identity rows for the seeded SuperAdmin.
6. Tenant A/B are read-only by long-standing policy; master admin tenant switching must respect that (view-only for A/B).
7. `TenantDbConnectionFactory` throws a value-free error when a credential key is missing from configuration — good pattern to follow for master admin operations against tenants.

## 9. Planned write footprint (for your approval — nothing executed)

| Database | Planned writes |
|---|---|
| **Tenant A** (db68259) | None — read-only |
| **Tenant B** (db68416) | None — read-only |
| **Tenant C** (db68417) | None from Phase 5 (existing app behavior unchanged) |
| **Master DB** (db67598) | Exactly 3 Identity rows: 1 AspNetRoles (SuperAdmin), 1 AspNetUsers (demo account), 1 AspNetUserRoles link. Nothing else. |

## 10. Decision points for your approval

1. **Identity role name**: role stored as `SuperAdmin` (Identity side), normalized to `MasterAdmin` (app side). OK?
2. **Login UX**: separate dedicated `MasterLoginForm` (recommended — cleanest separation, tenant form untouched) vs. adding an "Admin" tab to the existing form.
3. **Subscription page**: option A (read-only view of tenant member subscriptions, no schema change), B (new Master billing table — explicit approval required), or C (report as gap only).
4. **Company activation enforcement**: add the missing `Companies.IsActive` check to `TenantAuthenticationService.AuthenticateAsync`. OK?
5. **Seed**: exactly one SuperAdmin demo account (username `superadmin` + generated password, reported once in the final report). OK?

**STOP — awaiting your approval before any design implementation, seeding, or writes.**
