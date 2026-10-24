/* =====================================================================================
   ALPHA ELITE - PHASE 3 DATA MIGRATION  (Tenant A + Tenant B  ->  Tenant C)
   -------------------------------------------------------------------------------------
   TARGET DATABASE : Tenant C only  (master CompanyId 3 / COMP003, credential key 'TenantC')
   SOURCES         : Tenant A (COMP001) and Tenant B (COMP002) - READ-ONLY, never touched.

   STATUS          : *** REVIEW ARTIFACT - NOT EXECUTED IN PHASE 3 ***
                     This file is generated from the live Phase 3 read-only inventory.
                     It will only ever be run after explicit written approval.

   WHY THIS SCRIPT TARGETS ONLY TENANT C
   -------------------------------------
   The three tenant databases live on three different SQL Server instances
   (db68259 / db68416 / db68417 .public.databaseasp.net) with per-database logins.
   There is no linked server and no cross-database privilege, so a single script
   cannot join A and B to C. The migration is therefore expressed as a fully
   literal, self-contained script against Tenant C: every source row is carried
   in as an explicit VALUES tuple and every old-id -> new-id mapping is written
   out by hand (see section D of the Phase 3 report).

   SAFETY
   ------
     * Wrapped in an explicit transaction with XACT_ABORT ON and TRY/CATCH ROLLBACK.
     * Guarded by @ApprovePhase3Migration so it cannot run by accident.
     * A preflight block refuses to run unless the destination business tables are
       still empty (guards against a double-run).
     * Branch ids are resolved from BranchCode (never hardcoded).
     * The Tenant B 'admin' PasswordHash is NOT written into this file. It is passed
       in as @AdminPasswordHash at run time and never persisted to disk or logs.
   ===================================================================================== */

SET NOCOUNT ON;
SET XACT_ABORT ON;   -- any error aborts and rolls back the whole transaction

----------------------------------------------------------------------------------------
-- 0. APPROVAL GUARD  (flip to 1 only after the user approves the real migration)
----------------------------------------------------------------------------------------
DECLARE @ApprovePhase3Migration bit = 0;

-- Supplied at run time by the migration host (never stored in this file):
DECLARE @AdminPasswordHash nvarchar(max) = NULL;

IF @ApprovePhase3Migration <> 1
BEGIN
    RAISERROR('Phase 3 migration is NOT approved. Set @ApprovePhase3Migration = 1 (after sign-off) to run.', 16, 1);
    RETURN;
END;

----------------------------------------------------------------------------------------
-- 1. BRANCH RESOLUTION  (by BranchCode, never by hardcoded id)
----------------------------------------------------------------------------------------
DECLARE @BranchA int = (SELECT BranchId FROM dbo.Branches WHERE BranchCode = N'BR-A');
DECLARE @BranchB int = (SELECT BranchId FROM dbo.Branches WHERE BranchCode = N'BR-B');

IF @BranchA IS NULL OR @BranchB IS NULL
BEGIN
    RAISERROR('Branch BR-A and/or BR-B not found in Tenant C. Aborting.', 16, 1);
    RETURN;
END;

----------------------------------------------------------------------------------------
-- 2. PREFLIGHT - destination must still be empty (idempotency / double-run guard)
----------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM dbo.Members)
   OR EXISTS (SELECT 1 FROM dbo.Attendances)
   OR EXISTS (SELECT 1 FROM dbo.Payments)
   OR EXISTS (SELECT 1 FROM dbo.Feedbacks)
   OR EXISTS (SELECT 1 FROM dbo.Inquiries)
   OR EXISTS (SELECT 1 FROM dbo.Subscriptions)
   OR EXISTS (SELECT 1 FROM dbo.UserAccounts)
   OR EXISTS (SELECT 1 FROM dbo.RetentionRecords)
   OR EXISTS (SELECT 1 FROM dbo.Complaints)
BEGIN
    RAISERROR('Tenant C already contains business data. Migration aborted to protect it.', 16, 1);
    RETURN;
END;

----------------------------------------------------------------------------------------
-- 3. MIGRATION  (single atomic transaction)
----------------------------------------------------------------------------------------
BEGIN TRY
    BEGIN TRANSACTION;

    /* -------------------------------------------------------------------------------
       3.1 MEMBERS  (parent first)
           New ids: A member 1 -> 1 ; B members 1..5 -> 2..6
       ------------------------------------------------------------------------------- */
    SET IDENTITY_INSERT dbo.Members ON;

    INSERT INTO dbo.Members (MemberId, MemberCode, FullName, Email, IsActive, CreatedAt, BranchId) VALUES
     (1, N'102', N'axel',             N'axel@gmail.com',    1, '2026-09-18T07:12:20.218', @BranchA),  -- Tenant A member 1
     (2, N'101', N'Gabriel Gongon',   N'Gabriel@gmail.com', 0, '2026-09-24T05:24:02.661', @BranchB),  -- Tenant B member 1
     (3, N'103', N'Alex Escobido',    N'alex@gmail.com',    1, '2026-09-24T09:15:31.668', @BranchB),  -- Tenant B member 2
     (4, N'104', N'meljim joves',     N'meljim@gmail.com',  0, '2026-09-24T09:54:33.116', @BranchB),  -- Tenant B member 3
     (5, N'105', N'richard culanag',  N'richard@gmail.com', 1, '2026-09-24T09:58:03.500', @BranchB),  -- Tenant B member 4
     (6, N'106', N'Angelo Taperla',   N'angelo@gmail.com',  1, '2026-09-24T18:31:55.294', @BranchB);  -- Tenant B member 5

    SET IDENTITY_INSERT dbo.Members OFF;

    /* -------------------------------------------------------------------------------
       3.2 ATTENDANCES   (MemberId remapped: A m1->1 ; B m1..m4 -> 2,3,4,5)
           New ids: A att 1 -> 1 ; B att 1..4 -> 2..5
       ------------------------------------------------------------------------------- */
    SET IDENTITY_INSERT dbo.Attendances ON;

    INSERT INTO dbo.Attendances (AttendanceId, MemberId, CheckInDate, BranchId) VALUES
     (1, 1, '2026-09-18T07:34:14.831', @BranchA),  -- A att 1 -> A member 1
     (2, 2, '2026-09-24T05:24:45.730', @BranchB),  -- B att 1 -> B member 1 -> C member 2
     (3, 3, '2026-09-24T09:17:33.139', @BranchB),  -- B att 2 -> B member 2 -> C member 3
     (4, 4, '2026-09-25T09:58:25.024', @BranchB),  -- B att 3 -> B member 3 -> C member 4
     (5, 5, '2026-09-23T09:58:39.753', @BranchB);  -- B att 4 -> B member 4 -> C member 5

    SET IDENTITY_INSERT dbo.Attendances OFF;

    /* -------------------------------------------------------------------------------
       3.3 PAYMENTS   (MemberId remapped; exact decimal(18,2) values preserved)
           New ids: A pay 1 -> 1 ; B pay 1..4 -> 2..5
       ------------------------------------------------------------------------------- */
    SET IDENTITY_INSERT dbo.Payments ON;

    INSERT INTO dbo.Payments (PaymentId, MemberId, AmountPaid, PaymentDate, NextDueDate, Notes, BranchId) VALUES
     (1, 1, 500.00,  '2026-09-18T07:34:26.202', NULL,                        NULL,  @BranchA), -- A pay 1 -> A member 1
     (2, 2, 1000.00, '2026-09-24T05:26:56.970', NULL,                        N'NA', @BranchB), -- B pay 1 -> B member 1 -> C member 2
     (3, 2, 2000.00, '2026-09-24T09:17:45.613', NULL,                        NULL,  @BranchB), -- B pay 2 -> B member 1 -> C member 2
     (4, 4, 500.00,  '2026-09-24T09:58:51.108', '2026-09-30T09:58:51.112',   N'N/A',@BranchB), -- B pay 3 -> B member 3 -> C member 4
     (5, 5, 150.00,  '2026-09-24T09:59:33.265', '2026-09-25T09:59:33.265',   NULL,  @BranchB); -- B pay 4 -> B member 4 -> C member 5

    SET IDENTITY_INSERT dbo.Payments OFF;

    /* -------------------------------------------------------------------------------
       3.4 FEEDBACKS   (MemberId remapped)
           New ids: A fb 1 -> 1 ; B fb 1..4 -> 2..5
       ------------------------------------------------------------------------------- */
    SET IDENTITY_INSERT dbo.Feedbacks ON;

    INSERT INTO dbo.Feedbacks (FeedbackId, MemberId, Rating, Comments, SubmittedAt, BranchId) VALUES
     (1, 1, 1, N'Good',                                                          '2026-09-18T07:34:48.137', @BranchA),
     (2, 2, 5, N'Very Good and Clean Equipments',                                '2026-09-24T05:27:28.689', @BranchB),
     (3, 3, 4, N'Clean but the musics are too loud',                             '2026-09-24T09:17:10.279', @BranchB),
     (4, 4, 3, N'Ok equipments but the place are a bit mess and the musics are loud', '2026-09-24T09:59:36.669', @BranchB),
     (5, 5, 1, N'floors were dirty and uncleaned musics are loud too',           '2026-09-24T10:00:32.562', @BranchB);

    SET IDENTITY_INSERT dbo.Feedbacks OFF;

    /* -------------------------------------------------------------------------------
       3.5 INQUIRIES   (no parent; BranchId assigned by source tenant)
           Tenant A source ids 1,2,4,5,6  -> new ids 1..5   (ids 5 and 6 BOTH preserved)
           Tenant B source ids 1..5       -> new ids 6..10
       ------------------------------------------------------------------------------- */
    SET IDENTITY_INSERT dbo.Inquiries ON;

    INSERT INTO dbo.Inquiries (InquiryId, FullName, ContactNumber, EmailAddress, InterestedPlan, Message, IsFollowedUp, CreatedAt, BranchId) VALUES
     ( 1, N'Axel Dos Pueblos',   N'09165140479',  N'axeldospueblos@gmail.com', N'Monthly', N'N/A',               1, '2026-09-18T07:34:57.761', @BranchA), -- A id 1
     ( 2, N'Richard Culanag',    N'09123456789',  N'richard@gmail.com',        N'Weekly',  N'N/A',               1, '2026-09-19T08:29:54.414', @BranchA), -- A id 2
     ( 3, N'asas',               N'09123456677',  N'asas@gmail.com',           N'anually', N'N/A',               1, '2026-09-19T08:33:30.249', @BranchA), -- A id 4
     ( 4, N'Ivan Glenn Entrina', N'09123568934',  N'Ivan@gmail.com',           N'Monthly', N'N/A',               1, '2026-09-19T08:33:30.249', @BranchA), -- A id 5  (kept)
     ( 5, N'Ivan Glenn Entrina', N'09123568934',  N'Ivan@gmail.com',           N'Monthly', N'N/A',               1, '2026-09-19T08:33:30.249', @BranchA), -- A id 6  (kept as separate row)
     ( 6, N'Gabriel',            N'09123451231',  N'Gabriel@gmail.com',        N'Monthly', N'N/A',               1, '2026-09-24T05:24:27.831', @BranchB), -- B id 1
     ( 7, N'Richard',            N'0932412332',   N'Richard@gmail.com',        N'Daily',   N'N/A',               1, '2026-09-25T09:16:12.854', @BranchB), -- B id 2
     ( 8, N'Meljim',             N'09821375823',  N'meljim@gmail.com',         N'Weekly',  N'i want to be better',1, '2026-09-24T09:54:38.229', @BranchB), -- B id 3
     ( 9, N'Alex',               N'093241234121', N'Alex@gmail.com',           N'Anually', N'N/A',               1, '2026-09-25T09:16:12.854', @BranchB), -- B id 4
     (10, N'Angelo Taperla',     N'091234523234', N'Angelo@gmail.com',         N'Monthly', N'N/A',               1, '2026-09-24T18:32:55.649', @BranchB); -- B id 5

    SET IDENTITY_INSERT dbo.Inquiries OFF;

    /* -------------------------------------------------------------------------------
       3.6 RETENTIONRECORDS   (Tenant B only; Tenant A has no such table)
           MemberId remapped: B retention member 2 -> C member 3
           New tenant-C columns not present in the source are set to NULL:
             AssignedStaff (does not exist in Tenant B)
       ------------------------------------------------------------------------------- */
    SET IDENTITY_INSERT dbo.RetentionRecords ON;

    INSERT INTO dbo.RetentionRecords
        (RetentionRecordId, MemberId, Reason, RetentionAction, Promotion, PromotionDetails, Notes,
         CreatedAt, CreatedBy, Status, ApprovedBy, ApprovedAt, ApprovalReason, RejectionReason,
         CompletedAt, CompletionNotes, AssignedStaff, BranchId)
    VALUES
        (1, 3, N'Other', N'Offer Discount', NULL, NULL, NULL,
         '2026-09-27T16:10:30.611', N'System Administrator', N'Approved', N'System Administrator',
         '2026-09-27T16:10:56.585', N'Good', NULL, NULL, NULL, NULL, @BranchB);

    SET IDENTITY_INSERT dbo.RetentionRecords OFF;

    /* -------------------------------------------------------------------------------
       3.7 USERACCOUNTS   (Tenant B 'admin' only; Tenant A has no UserAccounts table)
           MemberId NULL (admin is not a member), BranchId = BR-B.
           PasswordHash is supplied at run time, never written to this file.
       ------------------------------------------------------------------------------- */
    IF @AdminPasswordHash IS NULL
    BEGIN
        RAISERROR('@AdminPasswordHash was not supplied. Aborting so no account is created without its hash.', 16, 1);
    END;

    SET IDENTITY_INSERT dbo.UserAccounts ON;

    INSERT INTO dbo.UserAccounts (UserAccountId, Username, PasswordHash, FullName, Role, IsActive, CreatedAt, MemberId, BranchId) VALUES
     (1, N'admin', @AdminPasswordHash, N'System Administrator', N'Admin', 1, '2026-09-23T21:06:23.713', NULL, @BranchB);

    SET IDENTITY_INSERT dbo.UserAccounts OFF;

    /* -------------------------------------------------------------------------------
       3.8 SUBSCRIPTIONS   - both sources contain 0 rows; nothing to insert.
           COMPLAINTS        - no source table exists; remains empty.
       ------------------------------------------------------------------------------- */

    ----------------------------------------------------------------------------------
    -- 4. RESEED IDENTITIES so future app inserts continue past the migrated ids
    ----------------------------------------------------------------------------------
    DBCC CHECKIDENT (N'dbo.Members',          RESEED);
    DBCC CHECKIDENT (N'dbo.Attendances',      RESEED);
    DBCC CHECKIDENT (N'dbo.Payments',         RESEED);
    DBCC CHECKIDENT (N'dbo.Feedbacks',        RESEED);
    DBCC CHECKIDENT (N'dbo.Inquiries',        RESEED);
    DBCC CHECKIDENT (N'dbo.RetentionRecords', RESEED);
    DBCC CHECKIDENT (N'dbo.UserAccounts',     RESEED);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    DECLARE @msg nvarchar(2048) = ERROR_MESSAGE();
    DECLARE @sev int = ERROR_SEVERITY();
    DECLARE @st  int = ERROR_STATE();
    RAISERROR(@msg, @sev, @st);
    THROW;
END CATCH;

/* =====================================================================================
   EXPECTED RESULT AFTER THIS SCRIPT (see report section D/L for the full mapping)
     Branches          2   (BR-A=@BranchA, BR-B=@BranchB - unchanged)
     Members           6   (BR-A 1, BR-B 5)
     Attendances       5   (BR-A 1, BR-B 4)
     Payments          5   (BR-A 1 = 500.00 ; BR-B 4 = 3650.00 ; grand total 4150.00)
     Feedbacks         5   (BR-A 1, BR-B 4)
     Inquiries        10   (BR-A 5, BR-B 5 - Tenant A ids 5 AND 6 both preserved)
     RetentionRecords  1   (BR-B)
     UserAccounts      1   (admin, BR-B, MemberId NULL)
     Subscriptions     0
     Complaints        0
   ===================================================================================== */
