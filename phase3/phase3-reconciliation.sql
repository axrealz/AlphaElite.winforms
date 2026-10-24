/* =====================================================================================
   ALPHA ELITE - PHASE 3 RECONCILIATION  (run against Tenant C AFTER the migration)
   -------------------------------------------------------------------------------------
   READ-ONLY. Every statement is a SELECT. Run with the TenantC login.
   Expected source baselines (Phase 3 live inventory):
       Tenant A : Members 1, Attendances 1, Payments 1 (500.00),  Feedbacks 1, Inquiries 5
       Tenant B : Members 5, Attendances 4, Payments 4 (3650.00), Feedbacks 4, Inquiries 5,
                  RetentionRecords 1, Subscriptions 0, UserAccounts 1
   ===================================================================================== */

SET NOCOUNT ON;

----------------------------------------------------------------------------------------
-- L1. Row counts overall and per branch (branches resolved by code, not id)
----------------------------------------------------------------------------------------
DECLARE @BranchA int = (SELECT BranchId FROM dbo.Branches WHERE BranchCode = N'BR-A');
DECLARE @BranchB int = (SELECT BranchId FROM dbo.Branches WHERE BranchCode = N'BR-B');

SELECT 'Members'          AS TableName, COUNT(*) AS Total, SUM(CASE WHEN BranchId = @BranchA THEN 1 ELSE 0 END) AS BranchA, SUM(CASE WHEN BranchId = @BranchB THEN 1 ELSE 0 END) AS BranchB FROM dbo.Members
UNION ALL SELECT 'Attendances',     COUNT(*), SUM(CASE WHEN BranchId = @BranchA THEN 1 ELSE 0 END), SUM(CASE WHEN BranchId = @BranchB THEN 1 ELSE 0 END) FROM dbo.Attendances
UNION ALL SELECT 'Payments',        COUNT(*), SUM(CASE WHEN BranchId = @BranchA THEN 1 ELSE 0 END), SUM(CASE WHEN BranchId = @BranchB THEN 1 ELSE 0 END) FROM dbo.Payments
UNION ALL SELECT 'Feedbacks',       COUNT(*), SUM(CASE WHEN BranchId = @BranchA THEN 1 ELSE 0 END), SUM(CASE WHEN BranchId = @BranchB THEN 1 ELSE 0 END) FROM dbo.Feedbacks
UNION ALL SELECT 'Inquiries',       COUNT(*), SUM(CASE WHEN BranchId = @BranchA THEN 1 ELSE 0 END), SUM(CASE WHEN BranchId = @BranchB THEN 1 ELSE 0 END) FROM dbo.Inquiries
UNION ALL SELECT 'RetentionRecords',COUNT(*), SUM(CASE WHEN BranchId = @BranchA THEN 1 ELSE 0 END), SUM(CASE WHEN BranchId = @BranchB THEN 1 ELSE 0 END) FROM dbo.RetentionRecords
UNION ALL SELECT 'UserAccounts',    COUNT(*), SUM(CASE WHEN BranchId = @BranchA THEN 1 ELSE 0 END), SUM(CASE WHEN BranchId = @BranchB THEN 1 ELSE 0 END) FROM dbo.UserAccounts
UNION ALL SELECT 'Subscriptions',   COUNT(*), 0, 0 FROM dbo.Subscriptions
UNION ALL SELECT 'Complaints',      COUNT(*), SUM(CASE WHEN BranchId = @BranchA THEN 1 ELSE 0 END), SUM(CASE WHEN BranchId = @BranchB THEN 1 ELSE 0 END) FROM dbo.Complaints
UNION ALL SELECT 'Branches',        COUNT(*), 0, 0 FROM dbo.Branches;

-- Expected: Members 6 (1/5) | Attendances 5 (1/4) | Payments 5 (1/4) | Feedbacks 5 (1/4)
--           Inquiries 10 (5/5) | RetentionRecords 1 (0/1) | UserAccounts 1 (0/1)
--           Subscriptions 0 | Complaints 0 | Branches 2

----------------------------------------------------------------------------------------
-- L2. Monetary reconciliation per branch (must match source totals)
----------------------------------------------------------------------------------------
SELECT
    CASE WHEN BranchId = @BranchA THEN 'BR-A (from Tenant A)' ELSE 'BR-B (from Tenant B)' END AS Branch,
    COUNT(*)        AS PaymentCount,
    SUM(AmountPaid) AS TotalPaid
FROM dbo.Payments
GROUP BY BranchId;
-- Expected: BR-A -> 1 row, 500.00 ; BR-B -> 4 rows, 3650.00 ; grand total 4150.00

SELECT SUM(AmountPaid) AS GrandTotal FROM dbo.Payments;  -- Expected 4150.00

----------------------------------------------------------------------------------------
-- L3. Feedback rating reconciliation (BR-B must reproduce Tenant B average 3.25)
----------------------------------------------------------------------------------------
SELECT
    CASE WHEN BranchId = @BranchA THEN 'BR-A' ELSE 'BR-B' END AS Branch,
    COUNT(*) AS FeedbackCount,
    CAST(AVG(CAST(Rating AS decimal(9,4))) AS decimal(9,4)) AS AverageRating
FROM dbo.Feedbacks
GROUP BY BranchId;
-- Expected: BR-A -> 1 row, avg 1.0000 ; BR-B -> 4 rows, avg 3.2500

----------------------------------------------------------------------------------------
-- L4. Tenant A inquiries ids 5 and 6 must BOTH be present (no dedup)
----------------------------------------------------------------------------------------
SELECT COUNT(*) AS IvanGlennRows
FROM dbo.Inquiries
WHERE FullName = N'Ivan Glenn Entrina' AND BranchId = @BranchA;
-- Expected: 2 (not 1)

----------------------------------------------------------------------------------------
-- L5. Orphan foreign-key checks - every child must resolve to a parent in Tenant C
----------------------------------------------------------------------------------------
SELECT 'Attendances -> Members'       AS Relationship, COUNT(*) AS Orphans FROM dbo.Attendances a       LEFT JOIN dbo.Members m ON m.MemberId = a.MemberId WHERE a.MemberId IS NULL OR m.MemberId IS NULL
UNION ALL SELECT 'Payments -> Members',          COUNT(*) FROM dbo.Payments p          LEFT JOIN dbo.Members m ON m.MemberId = p.MemberId WHERE p.MemberId IS NULL OR m.MemberId IS NULL
UNION ALL SELECT 'Feedbacks -> Members',         COUNT(*) FROM dbo.Feedbacks f         LEFT JOIN dbo.Members m ON m.MemberId = f.MemberId WHERE f.MemberId IS NULL OR m.MemberId IS NULL
UNION ALL SELECT 'Subscriptions -> Members',     COUNT(*) FROM dbo.Subscriptions s     LEFT JOIN dbo.Members m ON m.MemberId = s.MemberId WHERE s.MemberId IS NULL OR m.MemberId IS NULL
UNION ALL SELECT 'RetentionRecords -> Members',  COUNT(*) FROM dbo.RetentionRecords r  LEFT JOIN dbo.Members m ON m.MemberId = r.MemberId WHERE r.MemberId IS NULL OR m.MemberId IS NULL
UNION ALL SELECT 'Complaints -> Members',        COUNT(*) FROM dbo.Complaints c        LEFT JOIN dbo.Members m ON m.MemberId = c.MemberId WHERE c.MemberId IS NULL OR m.MemberId IS NULL
UNION ALL SELECT 'UserAccounts -> Members',      COUNT(*) FROM dbo.UserAccounts u      LEFT JOIN dbo.Members m ON m.MemberId = u.MemberId WHERE u.MemberId IS NOT NULL AND m.MemberId IS NULL
UNION ALL SELECT 'Members -> Branches',          COUNT(*) FROM dbo.Members x           WHERE x.BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.BranchId = x.BranchId)
UNION ALL SELECT 'Attendances -> Branches',      COUNT(*) FROM dbo.Attendances x       WHERE x.BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.BranchId = x.BranchId)
UNION ALL SELECT 'Payments -> Branches',         COUNT(*) FROM dbo.Payments x          WHERE x.BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.BranchId = x.BranchId)
UNION ALL SELECT 'Feedbacks -> Branches',        COUNT(*) FROM dbo.Feedbacks x         WHERE x.BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.BranchId = x.BranchId)
UNION ALL SELECT 'Inquiries -> Branches',        COUNT(*) FROM dbo.Inquiries x         WHERE x.BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.BranchId = x.BranchId)
UNION ALL SELECT 'RetentionRecords -> Branches', COUNT(*) FROM dbo.RetentionRecords x  WHERE x.BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.BranchId = x.BranchId)
UNION ALL SELECT 'UserAccounts -> Branches',     COUNT(*) FROM dbo.UserAccounts x      WHERE x.BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.BranchId = x.BranchId)
UNION ALL SELECT 'Complaints -> Branches',       COUNT(*) FROM dbo.Complaints x        WHERE x.BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.BranchId = x.BranchId);
-- Expected: every row returns 0 orphans.

----------------------------------------------------------------------------------------
-- L6. Unique-constraint safety (must return zero rows)
----------------------------------------------------------------------------------------
SELECT 'Duplicate MemberCode' AS Problem, MemberCode AS Value, COUNT(*) AS Cnt
FROM dbo.Members GROUP BY MemberCode HAVING COUNT(*) > 1
UNION ALL
SELECT 'Duplicate Username', Username, COUNT(*) FROM dbo.UserAccounts GROUP BY Username HAVING COUNT(*) > 1
UNION ALL
SELECT 'Duplicate BranchCode', BranchCode, COUNT(*) FROM dbo.Branches GROUP BY BranchCode HAVING COUNT(*) > 1;

----------------------------------------------------------------------------------------
-- L7. BranchId completeness (no migrated row may be left without a branch)
----------------------------------------------------------------------------------------
SELECT 'Members without BranchId' AS Problem, COUNT(*) AS Cnt FROM dbo.Members WHERE BranchId IS NULL
UNION ALL SELECT 'Attendances without BranchId', COUNT(*) FROM dbo.Attendances WHERE BranchId IS NULL
UNION ALL SELECT 'Payments without BranchId',    COUNT(*) FROM dbo.Payments    WHERE BranchId IS NULL
UNION ALL SELECT 'Feedbacks without BranchId',   COUNT(*) FROM dbo.Feedbacks   WHERE BranchId IS NULL
UNION ALL SELECT 'Inquiries without BranchId',   COUNT(*) FROM dbo.Inquiries   WHERE BranchId IS NULL
UNION ALL SELECT 'RetentionRecords without BranchId', COUNT(*) FROM dbo.RetentionRecords WHERE BranchId IS NULL
UNION ALL SELECT 'UserAccounts without BranchId', COUNT(*) FROM dbo.UserAccounts WHERE BranchId IS NULL;
-- Expected: every row returns 0.

----------------------------------------------------------------------------------------
-- L8. Identity seeds advanced past migrated ids (next insert must not collide)
----------------------------------------------------------------------------------------
SELECT OBJECT_NAME(ic.object_id) AS TableName, IDENT_CURRENT(OBJECT_NAME(ic.object_id)) AS CurrentIdentity
FROM sys.identity_columns ic
WHERE OBJECT_NAME(ic.object_id) IN
      ('Members','Attendances','Payments','Feedbacks','Inquiries','RetentionRecords','UserAccounts','Subscriptions')
ORDER BY TableName;
-- Expected: Members 6, Attendances 5, Payments 5, Feedbacks 5, Inquiries 10,
--           RetentionRecords 1, UserAccounts 1, Subscriptions (unchanged / NULL).
