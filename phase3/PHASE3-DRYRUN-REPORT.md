# Alpha Elite — Phase 3 Dry-Run Report
## Tenant A + Tenant B → Tenant C data migration (DESIGN ONLY, NOT EXECUTED)

**Status:** read-only design and complete dry run. **Nothing was inserted, updated, deleted, merged, truncated, altered or dropped in any database.** Tenant A, Tenant B, Tenant C and the master database are exactly as they were.

**How this was produced:** a new read-only mode `--migration-dryrun` (see [MigrationDryRun.cs](../AlphaElite.winforms/MigrationDryRun.cs)) re-inventoried the three live databases through the existing credential pipeline. Raw output is stored under [phase3/inventory/](inventory/). No credential value is written anywhere.

**Artifacts produced in this phase**

| Artifact | Purpose |
| --- | --- |
| [phase3/phase3-migration-tenantc.sql](phase3-migration-tenantc.sql) | The exact migration script (guarded, transactional, **not executed**). |
| [phase3/phase3-reconciliation.sql](phase3-reconciliation.sql) | Read-only post-migration reconciliation checks. |
| [phase3/inventory/phase3-inventory-TenantA.txt](inventory/phase3-inventory-TenantA.txt) | Live Tenant A inventory + baselines. |
| [phase3/inventory/phase3-inventory-TenantB.txt](inventory/phase3-inventory-TenantB.txt) | Live Tenant B inventory + baselines. |
| [phase3/inventory/phase3-inventory-TenantC.txt](inventory/phase3-inventory-TenantC.txt) | Live Tenant C inventory + baselines. |

---

## A. Current source inventory (live, re-read this phase)

### Tenant A — COMP001 / "My First Company" (CompanyId 1, key `TenantA`)
Server `db68259.public.databaseasp.net`, database `db68259`, **not** branch-aware.
Migration history: 4 rows (stops at `20260916073845_AddInquiryAndFeedbackToTenantErp`).

| Table | Rows | PK | PK range | Notes |
| --- | --- | --- | --- | --- |
| Members | 1 | MemberId | 1–1 | code `102`, "axel", `axel@gmail.com`, active |
| Attendances | 1 | AttendanceId | 1–1 | → member 1 |
| Payments | 1 | PaymentId | 1–1 | → member 1, **500.00**, NextDueDate NULL |
| Feedbacks | 1 | FeedbackId | 1–1 | → member 1, rating 1 |
| Inquiries | 5 | InquiryId | 1–6 (gap at 3) | ids **1, 2, 4, 5, 6**; 5 & 6 byte-identical |
| **Absent tables** | — | — | — | Subscriptions, UserAccounts, RetentionRecords, Complaints, Branches **do not exist** |

Nullability/FKs (all match the legacy model): only `Payments.NextDueDate?`, `Payments.Notes?`, `Feedbacks.Comments?`, `Inquiries.ContactNumber?/EmailAddress?/InterestedPlan?/Message?` are nullable. FKs: `Attendances.MemberId`, `Payments.MemberId`, `Feedbacks.MemberId` → `Members.MemberId`, all ON DELETE CASCADE. Unique: `IX_Members_MemberCode`.

### Tenant B — COMP002 / "My Second Company" (CompanyId 2, key `TenantB`)
Server `db68416.public.databaseasp.net`, database `db68416`, **not** branch-aware.
Migration history: 6 rows (latest `20260927013438_AddRetentionRecordToTenantErp`).

| Table | Rows | PK | PK range | Notes |
| --- | --- | --- | --- | --- |
| Members | 5 | MemberId | 1–5 | codes `101,103,104,105,106` |
| Attendances | 4 | AttendanceId | 1–4 | → members 1,2,3,4 |
| Payments | 4 | PaymentId | 1–4 | **total 3650.00** |
| Feedbacks | 4 | FeedbackId | 1–4 | ratings 5,4,3,1 → **avg 3.25** |
| Inquiries | 5 | InquiryId | 1–5 | all IsFollowedUp = 1 |
| RetentionRecords | 1 | RetentionRecordId | 1–1 | → member 2, Status **Approved** |
| Subscriptions | **0** | SubscriptionId | — | **confirmed empty** |
| UserAccounts | 1 | UserAccountId | 1–1 | `admin`, Role `Admin`, active |

Nullability/FKs: same legacy shape. FKs: `Attendances/Payments/Feedbacks/RetentionRecords/Subscriptions.MemberId` → `Members.MemberId` CASCADE. Unique: `IX_Members_MemberCode`, `IX_UserAccounts_Username`.

### Key structural fact discovered this phase (affects the whole design)
The three databases live on **three different SQL Server instances** (`db68259` / `db68416` / `db68417` `.public.databaseasp.net`) with **per-database logins**. There is **no linked server and no cross-database privilege**, so a single T-SQL script *cannot* read A and B while writing C. The migration is therefore expressed as a literal, self-contained script that runs **against Tenant C only** (each source row carried in as an explicit `VALUES` tuple).

---

## B. Tenant C inventory (live)

Server `db68417.public.databaseasp.net`, database `db68417`, branch-aware.
Migration history: 1 row (`20260930030000_InitialTenantC`).

| Table | Rows | Notes |
| --- | --- | --- |
| Branches | **2** | BranchId 1 = `BR-A` "Branch A" (active); BranchId 2 = `BR-B` "Branch B" (active) |
| Members | 0 | has nullable `BranchId`; unique `IX_Members_MemberCode` |
| Attendances | 0 | + nullable `BranchId` |
| Payments | 0 | + nullable `BranchId`; `AmountPaid decimal(18,2)` |
| Feedbacks | 0 | + nullable `BranchId` |
| Inquiries | 0 | + nullable `BranchId` |
| Subscriptions | 0 | **no** BranchId (plan-level) |
| RetentionRecords | 0 | + `AssignedStaff`?, + nullable `BranchId` |
| UserAccounts | 0 | + nullable `MemberId`, + nullable `BranchId`; unique `IX_UserAccounts_Username` |
| Complaints | 0 | MemberId required CASCADE, BranchId nullable |

15 foreign keys (6 CASCADE on required Member links; BranchId links and `UserAccounts.MemberId` are NO_ACTION). 3 unique business indexes: `IX_Branches_BranchCode`, `IX_Members_MemberCode`, `IX_UserAccounts_Username`.

---

## C. Table-by-table migration map

| Source Table | Source Tenant | Source Rows | Destination Table | Dest. Rows After | Branch | ID Strategy | FK Strategy | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Members | A | 1 | Members | 1 | BR-A (1) | preserve (1→1) | n/a | |
| Members | B | 5 | Members | 5 | BR-B (2) | renumber 1..5 → 2..6 | n/a | avoid PK collision with A |
| Attendances | A | 1 | Attendances | 1 | BR-A (1) | preserve (1→1) | MemberId 1→1 | |
| Attendances | B | 4 | Attendances | 4 | BR-B (2) | renumber 1..4 → 2..5 | MemberId via member map | |
| Payments | A | 1 | Payments | 1 | BR-A (1) | preserve (1→1) | MemberId 1→1 | 500.00 |
| Payments | B | 4 | Payments | 4 | BR-B (2) | renumber 1..4 → 2..5 | MemberId via member map | 3650.00 |
| Feedbacks | A | 1 | Feedbacks | 1 | BR-A (1) | preserve (1→1) | MemberId 1→1 | |
| Feedbacks | B | 4 | Feedbacks | 4 | BR-B (2) | renumber 1..4 → 2..5 | MemberId via member map | |
| Inquiries | A | 5 | Inquiries | 5 | BR-A (1) | renumber {1,2,4,5,6}→1..5 | no parent | ids 5 & 6 both kept |
| Inquiries | B | 5 | Inquiries | 5 | BR-B (2) | renumber 1..5→6..10 | no parent | |
| RetentionRecords | A | — | RetentionRecords | — | — | — | — | **table does not exist in A** |
| RetentionRecords | B | 1 | RetentionRecords | 1 | BR-B (2) | preserve (1→1) | MemberId 2→3 | AssignedStaff NULL (new) |
| Subscriptions | A | — | Subscriptions | — | — | — | — | **no source data** |
| Subscriptions | B | 0 | Subscriptions | 0 | — | — | **no source data; remains empty** |
| UserAccounts | A | — | UserAccounts | — | — | — | — | **table does not exist in A** |
| UserAccounts | B | 1 | UserAccounts | 1 | BR-B (2) | preserve (1→1) | MemberId NULL | admin |
| — | — | — | Complaints | 0 | — | — | **No source data; remains empty.** |
| Branches | — | — | Branches | 2 | — | already seeded | — | BR-A, BR-B |

---

## D. Old-ID → new-ID mappings

New ids are deterministic because Tenant C's business tables are empty (identity verified at seed 1). Source PKs from A and B **collide** (both start at 1), so B is renumbered after A. No source id is silently reused for a different row.

### D1. Members (the only FK target that is remapped)

| Source Tenant | Source MemberId | → New MemberId | BranchId | MemberCode | FullName |
| --- | --- | --- | --- | --- | --- |
| A | 1 | **1** | 1 (BR-A) | 102 | axel |
| B | 1 | **2** | 2 (BR-B) | 101 | Gabriel Gongon |
| B | 2 | **3** | 2 (BR-B) | 103 | Alex Escobido |
| B | 3 | **4** | 2 (BR-B) | 104 | meljim joves |
| B | 4 | **5** | 2 (BR-B) | 105 | richard culanag |
| B | 5 | **6** | 2 (BR-B) | 106 | Angelo Taperla |

### D2. Attendances

| Source | Source Id | → New Id | MemberId old → new | Branch |
| --- | --- | --- | --- | --- |
| A | 1 | **1** | 1 → 1 | BR-A |
| B | 1 | **2** | 1 → 2 | BR-B |
| B | 2 | **3** | 2 → 3 | BR-B |
| B | 3 | **4** | 3 → 4 | BR-B |
| B | 4 | **5** | 4 → 5 | BR-B |

### D3. Payments

| Source | Source Id | → New Id | MemberId old → new | Amount | Branch |
| --- | --- | --- | --- | --- | --- |
| A | 1 | **1** | 1 → 1 | 500.00 | BR-A |
| B | 1 | **2** | 1 → 2 | 1000.00 | BR-B |
| B | 2 | **3** | 1 → 2 | 2000.00 | BR-B |
| B | 3 | **4** | 3 → 4 | 500.00 | BR-B |
| B | 4 | **5** | 4 → 5 | 150.00 | BR-B |

### D4. Feedbacks

| Source | Source Id | → New Id | MemberId old → new | Rating | Branch |
| --- | --- | --- | --- | --- | --- |
| A | 1 | **1** | 1 → 1 | 1 | BR-A |
| B | 1 | **2** | 1 → 2 | 5 | BR-B |
| B | 2 | **3** | 2 → 3 | 4 | BR-B |
| B | 3 | **4** | 3 → 4 | 3 | BR-B |
| B | 4 | **5** | 4 → 5 | 1 | BR-B |

### D5. Inquiries (no parent — pure renumber; A ids 5 & 6 kept as two rows)

| Source | Source Id | → New Id | Branch | FullName |
| --- | --- | --- | --- | --- |
| A | 1 | **1** | BR-A | Axel Dos Pueblos |
| A | 2 | **2** | BR-A | Richard Culanag |
| A | 4 | **3** | BR-A | asas |
| A | 5 | **4** | BR-A | Ivan Glenn Entrina |
| A | 6 | **5** | BR-A | Ivan Glenn Entrina *(separate row, preserved)* |
| B | 1 | **6** | BR-B | Gabriel |
| B | 2 | **7** | BR-B | Richard |
| B | 3 | **8** | BR-B | Meljim |
| B | 4 | **9** | BR-B | Alex |
| B | 5 | **10** | BR-B | Angelo Taperla |

### D6. RetentionRecords

| Source | Source Id | → New Id | MemberId old → new | Branch |
| --- | --- | --- | --- | --- |
| B | 1 | **1** | 2 → 3 | BR-B |

### D7. Subscriptions
**No rows in either source → no mapping.**

### D8. UserAccounts

| Source | Source Id | → New Id | Username | MemberId | Branch |
| --- | --- | --- | --- | --- | --- |
| B | 1 | **1** | admin | NULL | 2 (BR-B) |

---

## E. Foreign-key remapping plan (proof: zero orphans)

Every child row is written with an explicit remapped FK computed from the member map in section D1.

| Child FK | Rows | Rule | Orphan risk |
| --- | --- | --- | --- |
| `Attendances.MemberId` | 5 | D2 member map | none — each target exists in the insert |
| `Payments.MemberId` | 5 | D3 member map | none |
| `Feedbacks.MemberId` | 5 | D4 member map | none |
| `RetentionRecords.MemberId` | 1 | B m2 → 3 | none |
| `UserAccounts.MemberId` | 1 | NULL (admin is not a member) | n/a |
| `Subscriptions.MemberId` | 0 | no rows | n/a |
| `Complaints.MemberId` | 0 | no rows | n/a |
| `*.BranchId` | all | BR-A→1 for A rows, BR-B→2 for B rows, resolved by BranchCode | none — both branches exist |

Orphan verification is enforced **after** the fact by section L5 of [phase3-reconciliation.sql](phase3-reconciliation.sql) (15 checks, each must return 0). Because the parents are inserted before the children inside one transaction, a real orphan cannot be committed.

---

## F. Branch assignment plan

| Source | Branch | BranchId |
| --- | --- | --- |
| Tenant A (all tables) | BR-A "Branch A" | 1 |
| Tenant B (all tables) | BR-B "Branch B" | 2 |

`BranchId` is resolved at run time as `SELECT BranchId FROM Branches WHERE BranchCode = 'BR-A' / 'BR-B'` — never hardcoded. No source row is left without a branch (section L7 asserts 0 nulls). `Subscriptions` has no BranchId column and has no rows.

---

## G. UserAccount mapping

* **Tenant A has no UserAccounts table** → nothing to migrate, and none is invented.
* **Tenant B has exactly one account:** `admin` / FullName "System Administrator" / Role `Admin` / active, created 2026-09-23 21:06:23.713. Its `PasswordHash` is the legacy 64-char unsalted SHA-256.
* **Proposed Tenant C mapping (not created yet):**

| Column | Value |
| --- | --- |
| UserAccountId | 1 |
| Username | `admin` |
| PasswordHash | copied verbatim from Tenant B (supplied at run time as `@AdminPasswordHash`, **never written to any file**) |
| FullName | System Administrator |
| Role | Admin |
| IsActive | 1 |
| CreatedAt | 2026-09-23 21:06:23.713 |
| MemberId | NULL |
| BranchId | 2 (BR-B) |

**Decision needed:** whether the admin should be branch-scoped to BR-B (recommended, preserves migration fidelity) or tenant-wide (`BranchId = NULL`). No extra accounts are created and no password is changed.

---

## H. Duplicate / collision findings

| Check | Finding | Verdict |
| --- | --- | --- |
| Tenant A duplicate inquiries | ids **5 and 6 are byte-identical** (same name/number/email/plan/message/IsFollowedUp **and the same CreatedAt to the millisecond**) | **Preserved as two separate rows** (→ C ids 4 and 5). No dedup, per instruction. |
| `MemberCode` A vs B | A = {102}; B = {101,103,104,105,106} | **disjoint** → unique index safe |
| `MemberCode` within A | 1 distinct value / 1 row | no duplicate |
| `MemberCode` within B | 5 distinct values / 5 rows | no duplicate |
| `Username` | only B `admin` (one source) | unique index safe |
| `BranchCode` | sources have no Branches table; C already holds BR-A/BR-B | no collision |
| Cross-source PK collision | A and B both use MemberId/AttendanceId/… starting at 1 | **explicitly renumbered** (section D) |
| Email addresses | several similar across A/B (`richard@`, `alex@`) but not unique-constrained | no constraint issue |

No collision blocks the migration.

---

## I. Data compatibility findings

| # | Finding | Impact | Handling |
| --- | --- | --- | --- |
| 1 | New nullable columns in C with no source value: `BranchId` on Members/Attendances/Payments/Feedbacks/Inquiries/RetentionRecords/UserAccounts | none (nullable) | assigned by source tenant (section F) |
| 2 | `RetentionRecords.AssignedStaff` is new in C and **does not exist in Tenant B** | none (nullable) | set to NULL — no source value is lost because it never existed |
| 3 | `UserAccounts.MemberId` is new in C | none (nullable) | admin → NULL |
| 4 | `Payments.AmountPaid` / `Subscriptions.Price` are `decimal(18,2)` in **both** source and destination | none | literal values copied exactly |
| 5 | Date/time columns are `datetime2` on both sides | none | copied to millisecond precision |
| 6 | Tenant A inquiries 5 & 6 share an identical timestamp | accepted | both kept (section H) |
| 7 | Tenant A lacks Subscriptions / UserAccounts / RetentionRecords / Complaints / Branches | none | no source rows; those tables keep only what B provides |
| 8 | Column widths: `MemberCode` 50→50, `FullName` 200→200, `Email` 200→200, `Username` 50→50, inquiry text `nvarchar(max)`→`nvarchar(max)` | no truncation | — |
| 9 | Tenant B `admin` hash is legacy unsalted SHA-256 | security | copied as-is; `PasswordHasher.Verify` reports `NeedsPasswordRehash = true` and transparently upgrades to PBKDF2 on the **next successful login** (a Tenant C write, allowed only after approval) |
| 10 | Every NOT NULL destination column has a source counterpart | none | no defaults required |

No truncation, nullability conflict, datatype conflict, precision loss or date problem was found. Nothing is silently fixed.

---

## J. Exact proposed INSERT order

1. (Branches — already present; not re-inserted)
2. **Members**
3. **Attendances**
4. **Payments**
5. **Feedbacks**
6. **Inquiries** (no parent)
7. **RetentionRecords**
8. **UserAccounts**
9. Subscriptions / Complaints — nothing to insert

Parents precede children; every table is inserted with `IDENTITY_INSERT` and explicit ids inside one transaction.

---

## K. Exact migration SQL / script artifact

* Full script: [phase3/phase3-migration-tenantc.sql](phase3-migration-tenantc.sql) — **not executed**.
* It contains: approval guard (`@ApprovePhase3Migration`), branch resolution by code, an emptiness preflight, all eight inserts with explicit ids, identity reseeding, and `SET XACT_ABORT ON` + explicit `BEGIN TRANSACTION` + `TRY/CATCH ROLLBACK`.
* The Tenant B `admin` hash is **not** in the file; the script aborts unless it is supplied at run time as `@AdminPasswordHash`.
* **Fallback if the login lacks `IDENTITY_INSERT` permission:** drop the `IDENTITY_INSERT`/reseed pairs and the explicit id column from each insert and rely on identity assignment — because the tables are empty and the inserts are ordered, the resulting ids are exactly those in section D.

Sample of the core pattern:

```sql
SET IDENTITY_INSERT dbo.Members ON;
INSERT INTO dbo.Members (MemberId, MemberCode, FullName, Email, IsActive, CreatedAt, BranchId) VALUES
 (1, N'102', N'axel',           N'axel@gmail.com',    1, '2026-09-18T07:12:20.218', @BranchA),
 (2, N'101', N'Gabriel Gongon', N'Gabriel@gmail.com', 0, '2026-09-24T05:24:02.661', @BranchB),
 (3, N'103', N'Alex Escobido',  N'alex@gmail.com',    1, '2026-09-24T09:15:31.668', @BranchB),
 (4, N'104', N'meljim joves',   N'meljim@gmail.com',  0, '2026-09-24T09:54:33.116', @BranchB),
 (5, N'105', N'richard culanag',N'richard@gmail.com', 1, '2026-09-24T09:58:03.500', @BranchB),
 (6, N'106', N'Angelo Taperla', N'angelo@gmail.com',  1, '2026-09-24T18:31:55.294', @BranchB);
SET IDENTITY_INSERT dbo.Members OFF;
```

---

## L. Reconciliation plan (exact checks)

Run [phase3/phase3-reconciliation.sql](phase3-reconciliation.sql) against Tenant C after the migration. Expected values:

| Check | Expected |
| --- | --- |
| L1 Members | 6 (BR-A 1 / BR-B 5) |
| L1 Attendances | 5 (1 / 4) |
| L1 Payments | 5 (1 / 4) |
| L1 Feedbacks | 5 (1 / 4) |
| L1 Inquiries | **10** (5 / 5) |
| L1 RetentionRecords | 1 (0 / 1) |
| L1 UserAccounts | 1 (0 / 1) |
| L1 Subscriptions | 0 |
| L1 Complaints | 0 |
| L1 Branches | 2 |
| **L2 Payments total BR-A** | **500.00** (matches Tenant A) |
| **L2 Payments total BR-B** | **3650.00** (matches Tenant B) |
| L2 Grand total | 4150.00 |
| L3 Feedback avg BR-A | 1.0000 |
| L3 Feedback avg BR-B | **3.2500** (matches Tenant B) |
| L4 "Ivan Glenn Entrina" rows in BR-A | **2** (proves no dedup) |
| L5 orphan FKs (15 checks) | all **0** |
| L6 duplicate MemberCode / Username / BranchCode | **0 rows** |
| L7 rows with NULL BranchId | all **0** |
| L8 identity seeds | Members 6, Attendances 5, Payments 5, Feedbacks 5, Inquiries 10, Retention 1, UserAccounts 1 |

Additionally, after the migration re-run `--migration-dryrun` (or the checksum section) against A and B and compare to the baselines in section M to prove the sources were untouched.

---

## M. Source database baselines (captured this phase, read-only)

`CHECKSUM_AGG(BINARY_CHECKSUM(*))` over all columns, plus row counts — the post-migration proof that A and B never changed.

**Tenant A:** Members 1 / Members `-1578125915`; Attendances 1 / `1965424511`; Payments 1 / `-962425800`; Feedbacks 1 / `-2000721808`; Inquiries 5 / `-683684898`.

**Tenant B:** Members 5 / `310705712`; Attendances 4 / `-2061580134`; Payments 4 / `-626029674`; Feedbacks 4 / `-1095043683`; Inquiries 5 / `-1374855609`; RetentionRecords 1 / `899403639`; Subscriptions 0 / (empty); UserAccounts 1 / `1132749207`.

**Tenant C (pre-migration):** Branches 2 / `50648009`; every business table empty.

---

## N. Risks / blockers

1. **Cross-server topology (structural).** A, B and C are separate instances with separate logins, so the migration is a Tenant C-only literal script, not a cross-database join. It must run on the C connection.
2. **`IDENTITY_INSERT` / `DBCC CHECKIDENT` permissions.** Both require elevated rights on the destination tables. The Tenant C login could create the schema (EF `Migrate()`), so it very likely has them, but this must be confirmed before running; a permission-free fallback is documented in section K.
3. **Admin password hash transport.** It must be handed to the script at run time by the app and must not be written to disk or logs. The script refuses to run without it.
4. **Legacy hash retained.** `admin` keeps the 64-char SHA-256 until the first successful login triggers the transparent PBKDF2 rehash (a Tenant C write).
5. **Admin branch scope is a decision** (BR-B vs NULL) — see section G.
6. **Idempotency.** The script aborts if any Tenant C business table is non-empty, preventing accidental double-runs; it is therefore **one-shot** by design.
7. **No git safety net** (this workspace is not a repository), so the script and this report are the only rollback documentation. Rollback before commit = transaction; rollback after commit = delete the migrated rows by branch.
8. **Order dependency of the mapping.** The predicted new ids assume the tables are empty (verified) and that the script runs once. If the assumption is ever violated, the preflight aborts rather than half-migrating.
9. **No automated tests** in the solution; verification is the reconciliation script plus the read-only inventory.

---

## O. Exact approval required before any migration write

Nothing has been written. To proceed, explicit approval is required for each of:

1. **Execute** [phase3/phase3-migration-tenantc.sql](phase3-migration-tenantc.sql) against **Tenant C (db68417) only**, with `@ApprovePhase3Migration = 1` and `@AdminPasswordHash` supplied by the app — inserted inside the guarded transaction.
2. **Confirm** the `admin` account's `BranchId` = 2 (BR-B) — or choose NULL for tenant-wide.
3. **Confirm** keeping the legacy SHA-256 hash and relying on transparent rehash at first login (vs rehashing during migration).
4. **Acknowledge** that Tenant A, Tenant B and the master database will not be touched, and that the migration is one-shot.

A separate approval will be needed later for the demo accounts (MasterAdmin/Admin/Manager/GymStaff/Member).

**STOP — awaiting explicit approval. No migration has been performed.**
