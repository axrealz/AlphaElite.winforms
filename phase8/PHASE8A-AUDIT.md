# Phase 8A — Read-Only Audit: Tenant C Multi-Branch Redesign

Date: 2026-10-01 · **No code or data was changed.** All live counts from a fresh `--migration-dryrun` inventory of **db68417 (Tenant C)** — identity verified from the inventory header (Server `db68417.public.databaseasp.net`, credential key `TenantC`), not from variable names.

---

## 1. Current architecture

- **Single operational DB already**: Tenant C (`db68417`) is the only branch-aware database. `TenantCAlphaEliteDbContext` maps `Branches` + `Complaints` and nullable `BranchId` on Members, Attendances, Payments, Feedbacks, Inquiries, Complaints, RetentionRecords, UserAccounts. Legacy `TenantAlphaEliteDbContext` (A/B) ignores branch entities entirely — branch predicates are only added when `IsBranchAware` is true (master-registry driven), so **A/B structurally cannot receive branch data**.
- **Scope engine** (`Form1.Phase4.cs`): `ApplyScope(query, db, branchKey, memberKey)` appends `BranchId == _branchId` / `MemberId == _memberId` predicates; `EnsureBranchAccess(recordBranchId)` and `EnsureOwnMember(memberId)` guard every update/delete. `_branchId` is set once at construction: `Roles.IsBranchScopedByDefault(role) ? session.BranchId : null` → **Manager/GymStaff are locked to their home branch; Admin/Member are tenant-wide** (`[Form1.cs:156](AlphaElite.winforms/Form1.cs#L156)`).
- **RBAC**: central matrix in `Authorization.cs` (`Roles`, `Permission` flags, `RoleAuthorizationService`, `EnsureAuthorized`). Every action calls `EnsurePermission`; hiding controls is only the second layer.
- **Write stamping today** (`Form1.cs`): member add uses `_branchId ?? branch-picker`; attendance (`:884`), payments (`:1117`), feedback (`:1353`) stamp **the member's home branch**; inquiries (`:1549`) and retention (`:2365`) stamp `_branchId ?? existing`; complaints stamp `member.BranchId`. Subscriptions stamp **no branch** (table has no BranchId column).
- **Migrations**: hand-authored EF migration sets; Tenant C set has exactly one applied migration (`20260930030000_InitialTenantC`, `__EFMigrationsHistory` = 1 row). `dotnet ef` is not installed; a new migration must be hand-authored like the existing ones.
- **Tenant switcher** (header combo) switches *tenant* (locked to Tenant C for branch-aware users); it is separate from the requested *branch* selector.

## 2. What already supports the requested design

| Requirement | Status |
|---|---|
| One DB for all branches, BranchId separation | ✅ already true |
| BranchCode unique, required | ✅ unique index exists |
| Soft deactivation (IsActive), no deletes | ✅ IsActive exists; nothing deletes branches |
| Branch-scoped reads (grids/dashboard/KPI/charts/search/PDF/retention) | ✅ via `ApplyScope` |
| Write guard ("never trust UI BranchId") | ✅ `EnsureBranchAccess` + server-side permission gates |
| Member self-only, RBAC, auth | ✅ intact; unchanged by this phase |
| Tenant A/B read-only safety | ✅ branch code paths unreachable for legacy contexts |

## 3. Gaps — what must change

1. **Branch columns missing**: `Branch` has Code/Name/Address/ContactNumber/IsActive/CreatedAt only. Required: **City**, **Manager (UserAccountId FK)**, **Updated** → schema change (additive, nullable — no data loss).
2. **Subscriptions not branch-scoped**: only branch-scoped operational table without `BranchId` → add nullable `Branches.BranchId` FK + one-time backfill from the member's home branch.
3. **No Branch CRUD UI** (create/edit/assign manager/activate/deactivate) → new `Form1.Phase8.cs` partial + `Permission.ManageBranches` for Admin + Manager; GymStaff/Member get none.
4. **No Current Branch selector** → header combo for Admin/Manager/GymStaff over **active** branches; all reads/writes switch to `_currentBranchId`; reload-on-switch; "ALL BRANCHES" optional view for Admin.
5. **Manager/GymStaff are currently locked to one branch** — the new requirement lets them switch. This deliberately **loosens Phase 4 isolation** (see Risks #2).
6. **Stamping source must change**: attendance/payments/feedback currently stamp the *member's home branch*; per §5 they must stamp the *selected* branch. Complaints (member-created) keep `member.BranchId`; member transfer changes only `Members.BranchId`.
7. **Member transfer** (Admin/Manager) — small UI action; historical rows already carry their own BranchId, so no historical rewrite is needed by design.
8. **Inactive-branch safety** — selector lists active only; inserts validate the current branch is active; if the selected branch is deactivated mid-session, clear the selector.
9. **Harness expected values** — Phase 4/5/6 verification expectations must be updated to the new branch model (documented change, not test-gaming), plus a new Phase 8 harness covering A–Y.

## 4. Exact live Tenant C data (verified this hour)

Branches: `1 = BR-A "Branch A"` (Active), `2 = BR-B "Branch B"` (Active). No other rows.

| Table | BR-A (id 1) | BR-B (id 2) | Total |
|---|---|---|---|
| Members | 1 (member 1 "axel") | 5 (members 2–6) | 6 |
| Attendances | 1 | 4 | 5 |
| Payments | 1 (₱500) | 4 (₱3,650) | 5 |
| Feedbacks | 1 | 4 | 5 |
| Inquiries | 4 (ids 1–4) | 5 (ids 6–10) | 9 |
| Complaints | 1 (id 4, Pending, demo) | 1 (id 5, Under Review, demo) | 2 |
| Subscriptions | 2 via member (member 1 / member 2) | — | 2 (no BranchId column) |
| RetentionRecords | 0 | 1 (member 3) | 1 |
| UserAccounts | 2 (manager id2→BR1, member id4→BR1) | 2 (admin id1→BR2, gymstaff id3→BR2) | 4 |

⚠️ **External data drift detected** (not caused by Phase 7 — every Phase 7 write was a no-op or rolled back): the **6 demo current-week attendance rows (ids 6–11) were deleted** (11 → 5; fingerprint changed) and **Inquiry id 5 was deleted** (10 → 9). Consequence: `--phase5-verify` (expects attendance=11) and `--phase6-verify` (expects current-week demo attendance) **fail today, before any Phase 8 work**. The attendance rows can be restored by re-running the idempotent `--phase6-demo`; the deleted inquiry cannot be auto-restored (it was an exact duplicate of id 4 — recommend leaving it deleted and updating the harness expectation).

## 5. Proposed new branch mapping (Option B) — **for your approval, not applied**

Recommended strategy — **in-place rename** (safest, zero transactional rewrites):

| OLD branch | → NEW branch (rename in place, BranchId preserved) | Rows following automatically |
|---|---|---|
| BR-A (id 1) | **BR-MAIN** — AlphaElite Manila - Head Office — Manila City | 1 member, 1 attendance, 1 payment, 1 feedback, 4 inquiries, 1 complaint, 1 subscription, 2 user accounts |
| BR-B (id 2) | **BR-CEBU** — AlphaElite Cebu Branch — Cebu City *(or BR-DAVAO — your choice)* | 5 members, 4 attendance, 4 payments, 4 feedback, 5 inquiries, 1 complaint, 1 subscription (backfill), 1 retention, 2 user accounts |
| (new) | **BR-DAVAO** — AlphaElite Davao Branch — Davao City | starts empty, Active |

- Only **2 UPDATE statements on Branches** (+1 INSERT, or 2 INSERTs if BR-B→BR-DAVAO). **Zero rewrites of transactional BranchIds** — history keeps ids 1/2; new rows use the new codes. Old codes are not preserved (they exist only as history in reports); alternative "insert-new + reassign" strategy available on request (touches ~34 transactional rows).
- **Manager assignment**: the one existing Manager account (id 2, "Branch A Manager" — rename suggested to "Alpha Elite Manager") is assigned as Manager of BR-MAIN; other branches start "Unassigned" (allowed). No new logins created (§3).
- **Demo data** ("(demo)" tagged) follows its branch automatically.

## 6. Proposed implementation plan (after approval)

- **8B** `Form1.Phase8.cs` partial: Branches tab (grid + form: Code/Name/City/Manager dropdown of active Manager accounts/Status/Updated; Save/Cancel/Activate/Deactivate — no Delete), duplicate-BranchCode rejection, `Permission.ManageBranches` added for Admin+Manager.
- **8C** Header "Current Branch" combo (Admin/Manager/GymStaff; active branches; Admin gets "ALL BRANCHES"); `SwitchBranchAsync` clears grids + reloads dashboard/KPIs/charts; `_currentBranchId` becomes the scope/stamp source.
- **8D** Rewire reads/writes to `_currentBranchId` (stamp attendance/payments/feedback/inquiries/retention/subscriptions; complaints from member's current branch; inactive-branch insert guard).
- **8E** Member transfer action (Members tab; Admin/Manager; updates `Members.BranchId` only).
- **8F** Approved migration: hand-authored EF migration `AddBranchManagement` (Branches: +City nvarchar(100) null, +ManagerUserAccountId int null FK→UserAccounts(NO ACTION), +UpdatedAt datetime2 null; Subscriptions: +BranchId int null FK→Branches) + backfill + the approved branch rename/insert script; optional `--phase6-demo` re-run to restore the deleted demo attendance.
- **8G** New `Phase8Verification` harness (branch CRUD, duplicate-code rejection, switching, stamp propagation, transfer + historical preservation, inactive protection, RBAC, tenant isolation via fingerprints) + documented expected-value updates to Phase 4/5/6 harnesses; full regression re-run.

## 7. Database write footprint (all inside db68417 only)

1. Schema: additive columns above (1 new migration, applied via the existing `--tenantc-migrate` path that prints its target first).
2. Data: 2 branch UPDATEs (+1–2 INSERTs), subscriptions backfill (2 rows), optional 6 demo attendance re-seeds.
3. **None** on db67598 / db68259 / db68416 — verified before/after by BINARY_CHECKSUM fingerprints.

## 8. Risks

1. **Pre-existing harness breakage** from the external deletion drift (attendance 11→5, inquiry 10→9) — must be addressed (restore + expectation update) or Phase 5/6 baselines are already stale before Phase 8 starts.
2. **Deliberate RBAC loosening**: letting Manager/GymStaff switch branches removes the Phase 4 "manager sees only BR-A" guarantee; Phase 4 harness isolation assertions must be redefined to "manager sees only the *selected* branch". Cross-branch reads by a manager become possible by design — confirm this is acceptable (requirement §6 says yes).
3. **Schema change on live cloud DB** — additive/nullable only, applied by the proven migration runner; still requires explicit approval per your rules.
4. BranchId-preserving rename keeps history intact but changes the demo-era codes (BR-A/BR-B disappear); the alternative reassign strategy keeps codes but rewrites ~34 rows.

## 9. Approval required before any change

1. Confirm mapping (BR-A→BR-MAIN; BR-B→BR-CEBU **or** BR-DAVAO) and strategy (in-place rename **[recommended]** vs insert-new+reassign).
2. Confirm Manager/GymStaff branch switching (loosens Phase 4 isolation; harness expectations updated accordingly).
3. Confirm schema migration approval (additive columns on Branches + Subscriptions).
4. Confirm demo attendance restore via `--phase6-demo` and leave the deleted duplicate inquiry deleted.

**Stopping here — awaiting explicit approval.**

---

## ADDENDUM — decisions received (2026-10-01) and final mapping

Approved decisions:
1. **Strategy: rename in place** (BranchIds 1/2 preserved; zero transactional rewrites).
2. **BR-B → BR-CEBU**; therefore BR-A → BR-MAIN; BR-DAVAO created empty.
3. **Manager and GymStaff may switch branches** (Current Branch selector for Admin/Manager/GymStaff; Phase 4 harness isolation expectations updated to "selected branch" semantics).
4. **External drift: no restore now.** Do not fabricate history; preserve current DB state through the rename; do not recreate the deleted duplicate inquiry (id 5) unless explicitly approved later.

### Demo-seeder option (reported separately, NOT executed)
The 6 deleted attendance rows (ids 6–11) were Phase 6 demo records. The existing **idempotent** `--phase6-demo` seeder can safely recreate equivalent current-week demo rows (member/day check-in guard prevents duplicates). **Option:** run it during Phase 8G to restore demo attendance for the exam. Awaiting separate approval — will not run otherwise.

### Final migration mapping (CURRENT live data: attendance 5, inquiries 9)

| | BR-A (id 1) → **BR-MAIN** "AlphaElite Manila - Head Office", Manila City | BR-B (id 2) → **BR-CEBU** "AlphaElite Cebu Branch", Cebu City | **BR-DAVAO** (new, empty) |
|---|---|---|---|
| Members | 1 (member 1) | 5 (members 2–6) | 0 |
| Attendances | 1 | 4 | 0 |
| Payments | 1 (500.00) | 4 (3,650.00) | 0 |
| Feedbacks | 1 | 4 | 0 |
| Inquiries | 4 (ids 1–4) | 5 (ids 6–10) | 0 |
| Complaints | 1 (id 4) | 1 (id 5) | 0 |
| Subscriptions | 1 (member 1) | 1 (member 2) | 0 |
| RetentionRecords | 0 | 1 (member 3) | 0 |
| UserAccounts | 2 (manager→1, member→1) | 2 (admin→2, gymstaff→2) | 0 |

Write footprint for the approved migration: 2 branch UPDATEs (rename + city + manager: BR-MAIN manager = existing Manager account id 2; BR-CEBU unassigned) + 1 branch INSERT (BR-DAVAO, Active) + schema columns (Branches: City/ManagerUserAccountId/UpdatedAt; Subscriptions: BranchId) + subscriptions backfill (2 rows stamped from member home branch). Tenant A/B/Master: untouched.

**Status: audit closed; implementation (8B–8G) awaits explicit approval.**

---

## ADDENDUM 2 — Admin BranchId=2 audit point (resolved, read-only)

**Finding: the Admin account's stored BranchId=2 is informational only — it cannot restrict Admin.** The single consumer of `session.BranchId` is [Form1.cs:156](../AlphaElite.winforms/Form1.cs#L156): `_branchId = Roles.IsBranchScopedByDefault(_role) ? session.BranchId : null` — and `IsBranchScopedByDefault` is true only for Manager/GymStaff. With `_branchId = null`, `ApplyScope` adds no branch predicate, `EnsureBranchAccess` returns immediately, User Management lists all accounts, and master-entered admin sessions carry `BranchId = null` outright. Runtime evidence: every harness run shows admin `branchId=all` with full-tenant counts.

Origin of the value: the admin row predates the Branches table (CreatedAt 2026-09-23, branches created 2026-09-30); the BranchId was stamped later (Phase 3→4 era backfill or a User Management edit during testing). It has no effect on authorization.

Phase 8 disposition (no change needed for correctness): (a) during the approved migration, null out `UserAccounts.BranchId` for the Admin row purely for data hygiene; (b) in 8C, the Current Branch selector (`_currentBranchId`) — never `session.BranchId` — becomes the authoritative branch context for Admin/Manager/GymStaff reads and write-stamping, while `EnsurePermission`/`EnsureBranchAccess` continue to validate server-side.
