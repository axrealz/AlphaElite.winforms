using AlphaElite.infrastructure.services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>
    /// PHASE 3 execution: migrates Tenant A and Tenant B data into Tenant C.
    ///
    /// The three databases live on different servers with different logins, so the migration
    /// is app-driven: Tenant A and Tenant B are only ever read (SELECT), and the rows are
    /// written to Tenant C inside ONE transaction with XACT_ABORT + TRY/CATCH + ROLLBACK.
    ///
    /// The Tenant B 'admin' PasswordHash is read from Tenant B into memory at run time and is
    /// never printed, logged or written to any file.
    ///
    /// Modes:
    ///   --tenantc-preflight          read-only preflight (prints exactly what would run)
    ///   --tenantc-migrate-data       preflight, then the transactional migration
    ///   --tenantc-verify-migration   read-only post-migration verification (A..P)
    /// </summary>
    internal static class TenantCDataMigration
    {
        private sealed record SourceMember(int Id, string Code, string FullName, string Email, bool IsActive, DateTime CreatedAt);
        private sealed record SourceAttendance(int Id, int MemberId, DateTime CheckInDate);
        private sealed record SourcePayment(int Id, int MemberId, decimal Amount, DateTime PaymentDate, DateTime? NextDueDate, string? Notes);
        private sealed record SourceFeedback(int Id, int MemberId, int Rating, string? Comments, DateTime SubmittedAt);
        private sealed record SourceInquiry(int Id, string FullName, string? ContactNumber, string? EmailAddress, string? InterestedPlan, string? Message, bool IsFollowedUp, DateTime CreatedAt);
        private sealed record SourceRetention(int Id, int MemberId, string Reason, string RetentionAction, string? Promotion, string? PromotionDetails, string? Notes, DateTime CreatedAt, string CreatedBy, string Status, string? ApprovedBy, DateTime? ApprovedAt, string? ApprovalReason, string? RejectionReason, DateTime? CompletedAt, string? CompletionNotes);
        private sealed record SourceAccount(int Id, string Username, string PasswordHash, string FullName, string Role, bool IsActive, DateTime CreatedAt);

        private sealed record Resolved(AppRuntime Runtime, TenantOption A, TenantOption B, TenantOption C, TenantDatabaseInfo InfoA, TenantDatabaseInfo InfoB, TenantDatabaseInfo InfoC, IReadOnlyList<TenantOption> Tenants);

        // Phase 3 read-only baselines (row count, CHECKSUM_AGG(BINARY_CHECKSUM(*)) -> nullable when empty).
        private static readonly (string Table, long Count, long? Checksum)[] BaselineA =
        {
            ("Members", 1, -1578125915), ("Attendances", 1, 1965424511), ("Payments", 1, -962425800),
            ("Feedbacks", 1, -2000721808), ("Inquiries", 5, -683684898)
        };

        private static readonly (string Table, long Count, long? Checksum)[] BaselineB =
        {
            ("Members", 5, 310705712), ("Attendances", 4, -2061580134), ("Payments", 4, -626029674),
            ("Feedbacks", 4, -1095043683), ("Inquiries", 5, -1374855609), ("RetentionRecords", 1, 899403639),
            ("Subscriptions", 0, null), ("UserAccounts", 1, 1132749207)
        };

        private static readonly (string Table, long Count)[] BaselineMaster =
        {
            ("Companies", 3), ("CompanyDatabase", 3), ("Devices", 1),
            ("AspNetUsers", 0), ("AspNetRoles", 0), ("AspNetUserRoles", 0)
        };

        public static int Preflight() => Execute(migrate: false, verify: false);
        public static int MigrateData() => Execute(migrate: true, verify: false);
        public static int VerifyMigration() => Execute(migrate: false, verify: true);

        // =====================================================================================
        // Entry point
        // =====================================================================================
        private static int Execute(bool migrate, bool verify)
        {
            var failures = new List<string>();

            void Write(string text) => Console.WriteLine(text);
            void Fail(string text) { failures.Add(text); Write($"  [FAIL] {text}"); }

            var runtime = AppRuntime.Create();
            var resolved = Resolve(runtime);

            Write("======================================================================");
            Write(migrate ? "TENANT C DATA MIGRATION (Phase 3 - executing)" : verify ? "TENANT C DATA MIGRATION VERIFICATION (read-only)" : "TENANT C DATA MIGRATION PREFLIGHT (read-only)");
            Write("======================================================================");
            Write($"Target tenant  : {resolved.C.CompanyCode} / {resolved.C.CompanyName} (CompanyId {resolved.C.CompanyId})");
            Write($"Target server  : {resolved.InfoC.ServerName}");
            Write($"Target database: {resolved.InfoC.DatabaseName} (credential key '{resolved.InfoC.CredentialKey}')");
            Write($"Sources        : A = {resolved.A.CompanyCode}/{resolved.InfoA.DatabaseName}, B = {resolved.B.CompanyCode}/{resolved.InfoB.DatabaseName} (read-only)");
            Write($"Context        : {nameof(AlphaElite.infrastructure.data.TenantCAlphaEliteDbContext)}");

            if (verify)
            {
                RunVerification(resolved, Write, Fail, failures);
            }
            else
            {
                RunPreflight(resolved, Write, Fail, failures);

                if (migrate && failures.Count == 0)
                {
                    Write(string.Empty);
                    Write("--- Executing transactional migration (Tenant C only) ---");
                    RunMigration(resolved, Write, Fail, failures);
                }
                else if (migrate)
                {
                    Write(string.Empty);
                    Write("--- Migration NOT executed: preflight did not pass. ---");
                }
            }

            Write(string.Empty);
            var exit = failures.Count == 0 ? 0 : 1;
            Write($"--- Result: {(exit == 0 ? "OK" : "PROBLEMS")} | failures: {failures.Count} ---");
            foreach (var f in failures)
            {
                Write($"        - {f}");
            }

            return exit;
        }

        private static Resolved Resolve(AppRuntime runtime)
        {
            var tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();
            var branchAware = tenants.FirstOrDefault(t => t.IsBranchAware)
                ?? throw new InvalidOperationException("No branch-aware tenant (Tenant C) is configured.");
            var sources = tenants.Where(t => !t.IsBranchAware).OrderBy(t => t.CompanyId).ToList();

            if (sources.Count != 2)
            {
                throw new InvalidOperationException($"Expected exactly 2 source tenants but found {sources.Count}.");
            }

            var a = sources[0];
            var b = sources[1];

            return new Resolved(
                runtime, a, b, branchAware,
                runtime.Resolver.GetDatabaseInfoAsync(a.CompanyId).GetAwaiter().GetResult(),
                runtime.Resolver.GetDatabaseInfoAsync(b.CompanyId).GetAwaiter().GetResult(),
                runtime.Resolver.GetDatabaseInfoAsync(branchAware.CompanyId).GetAwaiter().GetResult(),
                tenants);
        }

        // =====================================================================================
        // Preflight (read-only)
        // =====================================================================================
        private static void RunPreflight(Resolved r, Action<string> write, Action<string> fail, List<string> failures)
        {
            using var c = Open(r, r.InfoC);
            var dbName = ScalarString(c, "SELECT DB_NAME()");

            write(string.Empty);
            write("--- Preflight checks (all read-only) ---");
            write($"  1. Destination database is db68417: {(dbName == "db68417" ? "PASS" : "FAIL")} (actual '{dbName}')");
            if (dbName != "db68417")
            {
                fail($"target database is '{dbName}', expected 'db68417'.");
            }

            // Branches
            var branches = Read(c, "SELECT BranchId, BranchCode FROM dbo.Branches ORDER BY BranchId",
                rd => (Id: rd.GetInt32(0), Code: rd.GetString(1)));
            var branchOk = branches.Count == 2
                           && branches[0].Id == 1 && branches[0].Code == "BR-A"
                           && branches[1].Id == 2 && branches[1].Code == "BR-B";
            write($"  2. Branches are exactly BR-A(1) / BR-B(2): {(branchOk ? "PASS" : "FAIL")} ({string.Join(", ", branches.Select(b => $"{b.Id}:{b.Code}"))})");
            if (!branchOk)
            {
                fail("destination branches are not the expected seeded BR-A/BR-B.");
            }

            // Emptiness
            var businessTables = new[] { "Members", "Attendances", "Payments", "Feedbacks", "Inquiries", "Subscriptions", "UserAccounts", "RetentionRecords", "Complaints" };
            var nonEmpty = businessTables.Select(t => (t, n: ScalarLong(c, $"SELECT COUNT(*) FROM dbo.[{t}]"))).Where(x => x.n > 0).ToList();
            write($"  3. No migrated application data present (all 9 business tables empty): {(nonEmpty.Count == 0 ? "PASS" : "FAIL")}");
            if (nonEmpty.Count > 0)
            {
                fail($"destination is not empty: {string.Join(", ", nonEmpty.Select(x => $"{x.t}={x.n}"))}.");
            }

            // Migration history
            var migrations = Read(c, "SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId", rd => rd.GetString(0));
            var historyOk = migrations.Count == 1 && migrations[0] == "20260930030000_InitialTenantC";
            write($"  4. Migration history is exactly [20260930030000_InitialTenantC]: {(historyOk ? "PASS" : "FAIL")} ({string.Join(", ", migrations)})");
            if (!historyOk)
            {
                fail("destination migration history is not the expected single Tenant C migration.");
            }

            // Source baselines still match Phase 3
            using var a = Open(r, r.InfoA);
            using var b = Open(r, r.InfoB);
            var aDiff = CompareBaseline(a, BaselineA);
            var bDiff = CompareBaseline(b, BaselineB);
            write($"  5. Tenant A matches the Phase 3 baseline: {(aDiff.Count == 0 ? "PASS" : "FAIL")}{(aDiff.Count == 0 ? string.Empty : " -> " + string.Join("; ", aDiff))}");
            write($"  6. Tenant B matches the Phase 3 baseline: {(bDiff.Count == 0 ? "PASS" : "FAIL")}{(bDiff.Count == 0 ? string.Empty : " -> " + string.Join("; ", bDiff))}");
            foreach (var d in aDiff) fail($"Tenant A baseline drift: {d}");
            foreach (var d in bDiff) fail($"Tenant B baseline drift: {d}");

            // Show the exact source rows that will be read (counts only; no hash printed)
            write(string.Empty);
            write("  Source rows to read (read-only SELECT):");
            write($"        Tenant A: Members {ScalarLong(a, "SELECT COUNT(*) FROM dbo.Members")}, Attendances {ScalarLong(a, "SELECT COUNT(*) FROM dbo.Attendances")}, Payments {ScalarLong(a, "SELECT COUNT(*) FROM dbo.Payments")}, Feedbacks {ScalarLong(a, "SELECT COUNT(*) FROM dbo.Feedbacks")}, Inquiries {ScalarLong(a, "SELECT COUNT(*) FROM dbo.Inquiries")}");
            write($"        Tenant B: Members {ScalarLong(b, "SELECT COUNT(*) FROM dbo.Members")}, Attendances {ScalarLong(b, "SELECT COUNT(*) FROM dbo.Attendances")}, Payments {ScalarLong(b, "SELECT COUNT(*) FROM dbo.Payments")}, Feedbacks {ScalarLong(b, "SELECT COUNT(*) FROM dbo.Feedbacks")}, Inquiries {ScalarLong(b, "SELECT COUNT(*) FROM dbo.Inquiries")}, RetentionRecords {ScalarLong(b, "SELECT COUNT(*) FROM dbo.RetentionRecords")}, Subscriptions {ScalarLong(b, "SELECT COUNT(*) FROM dbo.Subscriptions")}, UserAccounts {ScalarLong(b, "SELECT COUNT(*) FROM dbo.UserAccounts")}");
            write("        Tenant B UserAccounts read will include the PasswordHash column; its value is never printed or persisted.");
            write(string.Empty);
            write($"  Preflight: {(failures.Count == 0 ? "PASS - safe to migrate" : "FAIL - migration would be aborted")}.");
        }

        // =====================================================================================
        // Migration (single transaction)
        // =====================================================================================
        private static void RunMigration(Resolved r, Action<string> write, Action<string> fail, List<string> failures)
        {
            // 1. Read sources (read-only)
            using var a = Open(r, r.InfoA);
            using var b = Open(r, r.InfoB);

            var aMembers = ReadMembers(a);
            var bMembers = ReadMembers(b);
            var aAtt = Read(a, "SELECT AttendanceId, MemberId, CheckInDate FROM dbo.Attendances ORDER BY AttendanceId", rd => new SourceAttendance(rd.GetInt32(0), rd.GetInt32(1), rd.GetDateTime(2)));
            var bAtt = Read(b, "SELECT AttendanceId, MemberId, CheckInDate FROM dbo.Attendances ORDER BY AttendanceId", rd => new SourceAttendance(rd.GetInt32(0), rd.GetInt32(1), rd.GetDateTime(2)));
            var aPay = Read(a, "SELECT PaymentId, MemberId, AmountPaid, PaymentDate, NextDueDate, Notes FROM dbo.Payments ORDER BY PaymentId", rd => new SourcePayment(rd.GetInt32(0), rd.GetInt32(1), rd.GetDecimal(2), rd.GetDateTime(3), rd.IsDBNull(4) ? null : rd.GetDateTime(4), rd.IsDBNull(5) ? null : rd.GetString(5)));
            var bPay = Read(b, "SELECT PaymentId, MemberId, AmountPaid, PaymentDate, NextDueDate, Notes FROM dbo.Payments ORDER BY PaymentId", rd => new SourcePayment(rd.GetInt32(0), rd.GetInt32(1), rd.GetDecimal(2), rd.GetDateTime(3), rd.IsDBNull(4) ? null : rd.GetDateTime(4), rd.IsDBNull(5) ? null : rd.GetString(5)));
            var aFb = Read(a, "SELECT FeedbackId, MemberId, Rating, Comments, SubmittedAt FROM dbo.Feedbacks ORDER BY FeedbackId", rd => new SourceFeedback(rd.GetInt32(0), rd.GetInt32(1), rd.GetInt32(2), rd.IsDBNull(3) ? null : rd.GetString(3), rd.GetDateTime(4)));
            var bFb = Read(b, "SELECT FeedbackId, MemberId, Rating, Comments, SubmittedAt FROM dbo.Feedbacks ORDER BY FeedbackId", rd => new SourceFeedback(rd.GetInt32(0), rd.GetInt32(1), rd.GetInt32(2), rd.IsDBNull(3) ? null : rd.GetString(3), rd.GetDateTime(4)));
            var aInq = Read(a, "SELECT InquiryId, FullName, ContactNumber, EmailAddress, InterestedPlan, Message, IsFollowedUp, CreatedAt FROM dbo.Inquiries ORDER BY InquiryId", MapInquiry);
            var bInq = Read(b, "SELECT InquiryId, FullName, ContactNumber, EmailAddress, InterestedPlan, Message, IsFollowedUp, CreatedAt FROM dbo.Inquiries ORDER BY InquiryId", MapInquiry);
            var bRet = Read(b, "SELECT RetentionRecordId, MemberId, Reason, RetentionAction, Promotion, PromotionDetails, Notes, CreatedAt, CreatedBy, Status, ApprovedBy, ApprovedAt, ApprovalReason, RejectionReason, CompletedAt, CompletionNotes FROM dbo.RetentionRecords ORDER BY RetentionRecordId",
                rd => new SourceRetention(rd.GetInt32(0), rd.GetInt32(1), rd.GetString(2), rd.GetString(3), S(rd, 4), S(rd, 5), S(rd, 6), rd.GetDateTime(7), rd.GetString(8), rd.GetString(9), S(rd, 10), rd.IsDBNull(11) ? null : rd.GetDateTime(11), S(rd, 12), S(rd, 13), rd.IsDBNull(14) ? null : rd.GetDateTime(14), S(rd, 15)));
            var bAcct = Read(b, "SELECT UserAccountId, Username, PasswordHash, FullName, Role, IsActive, CreatedAt FROM dbo.UserAccounts ORDER BY UserAccountId",
                rd => new SourceAccount(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetString(4), rd.GetBoolean(5), rd.GetDateTime(6)));

            // 2. Build the id mappings (section D of the report)
            var memberMap = new Dictionary<(char Tenant, int Id), int>();
            var nextMember = 1;
            foreach (var m in aMembers) memberMap[('A', m.Id)] = nextMember++;
            foreach (var m in bMembers) memberMap[('B', m.Id)] = nextMember++;

            // 3. Write to Tenant C inside one transaction
            using var c = Open(r, r.InfoC);
            using var tx = c.BeginTransaction(IsolationLevel.Serializable);

            try
            {
                Exec(c, tx, "SET XACT_ABORT ON");

                var branchA = ScalarLong(c, "SELECT BranchId FROM dbo.Branches WHERE BranchCode = 'BR-A'", tx);
                var branchB = ScalarLong(c, "SELECT BranchId FROM dbo.Branches WHERE BranchCode = 'BR-B'", tx);

                // Members
                IdentityInsert(c, tx, "Members", true);
                var newMember = 1;
                foreach (var m in aMembers) { InsertMember(c, tx, newMember++, m, branchA); }
                foreach (var m in bMembers) { InsertMember(c, tx, newMember++, m, branchB); }
                IdentityInsert(c, tx, "Members", false);

                // Attendances
                IdentityInsert(c, tx, "Attendances", true);
                var newAtt = 1;
                foreach (var x in aAtt) { InsertAttendance(c, tx, newAtt++, memberMap[('A', x.MemberId)], x.CheckInDate, branchA); }
                foreach (var x in bAtt) { InsertAttendance(c, tx, newAtt++, memberMap[('B', x.MemberId)], x.CheckInDate, branchB); }
                IdentityInsert(c, tx, "Attendances", false);

                // Payments
                IdentityInsert(c, tx, "Payments", true);
                var newPay = 1;
                foreach (var x in aPay) { InsertPayment(c, tx, newPay++, memberMap[('A', x.MemberId)], x, branchA); }
                foreach (var x in bPay) { InsertPayment(c, tx, newPay++, memberMap[('B', x.MemberId)], x, branchB); }
                IdentityInsert(c, tx, "Payments", false);

                // Feedbacks
                IdentityInsert(c, tx, "Feedbacks", true);
                var newFb = 1;
                foreach (var x in aFb) { InsertFeedback(c, tx, newFb++, memberMap[('A', x.MemberId)], x, branchA); }
                foreach (var x in bFb) { InsertFeedback(c, tx, newFb++, memberMap[('B', x.MemberId)], x, branchB); }
                IdentityInsert(c, tx, "Feedbacks", false);

                // Inquiries (no parent). Tenant A ids 5 and 6 are both kept, in source-id order.
                IdentityInsert(c, tx, "Inquiries", true);
                var newInq = 1;
                foreach (var x in aInq) { InsertInquiry(c, tx, newInq++, x, branchA); }
                foreach (var x in bInq) { InsertInquiry(c, tx, newInq++, x, branchB); }
                IdentityInsert(c, tx, "Inquiries", false);

                // RetentionRecords (Tenant B only)
                IdentityInsert(c, tx, "RetentionRecords", true);
                var newRet = 1;
                foreach (var x in bRet) { InsertRetention(c, tx, newRet++, memberMap[('B', x.MemberId)], x, branchB); }
                IdentityInsert(c, tx, "RetentionRecords", false);

                // UserAccounts (Tenant B 'admin' only). Hash stays in memory.
                IdentityInsert(c, tx, "UserAccounts", true);
                var newAcct = 1;
                foreach (var x in bAcct) { InsertAccount(c, tx, newAcct++, x, branchB); }
                IdentityInsert(c, tx, "UserAccounts", false);

                // Advance identity seeds
                foreach (var t in new[] { "Members", "Attendances", "Payments", "Feedbacks", "Inquiries", "RetentionRecords", "UserAccounts" })
                {
                    Exec(c, tx, $"DBCC CHECKIDENT (N'dbo.{t}', RESEED)");
                }

                tx.Commit();
                write("  Migration committed.");
                write($"        members inserted: {aMembers.Count + bMembers.Count} (A {aMembers.Count}, B {bMembers.Count})");
                write($"        attendances: {aAtt.Count + bAtt.Count}, payments: {aPay.Count + bPay.Count}, feedbacks: {aFb.Count + bFb.Count}, inquiries: {aInq.Count + bInq.Count}, retention: {bRet.Count}, user accounts: {bAcct.Count}");
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); write("  Migration FAILED - transaction ROLLED BACK."); } catch { /* already rolled back by XACT_ABORT */ }
                fail($"migration: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // =====================================================================================
        // Verification (read-only, sections A..P)
        // =====================================================================================
        private static void RunVerification(Resolved r, Action<string> write, Action<string> fail, List<string> failures)
        {
            using var c = Open(r, r.InfoC);
            using var a = Open(r, r.InfoA);
            using var b = Open(r, r.InfoB);

            var branchA = ScalarLong(c, "SELECT BranchId FROM dbo.Branches WHERE BranchCode = 'BR-A'");
            var branchB = ScalarLong(c, "SELECT BranchId FROM dbo.Branches WHERE BranchCode = 'BR-B'");

            void Expect(string label, object actual, object expected)
            {
                var ok = string.Equals(actual?.ToString(), expected?.ToString(), StringComparison.Ordinal);
                write($"  [{(ok ? "PASS" : "FAIL")}] {label}: {actual} (expected {expected})");
                if (!ok) fail($"{label}: got {actual}, expected {expected}");
            }

            write(string.Empty);
            write("--- A/B. Row counts (total and per branch) ---");
            var tables = new[] { "Members", "Attendances", "Payments", "Feedbacks", "Inquiries", "Subscriptions", "RetentionRecords", "UserAccounts", "Complaints" };
            foreach (var t in tables)
            {
                var total = ScalarLong(c, $"SELECT COUNT(*) FROM dbo.[{t}]");
                var ct = HasBranchColumn(c, t)
                    ? $"BR-A {(int)ScalarLong(c, $"SELECT COUNT(*) FROM dbo.[{t}] WHERE BranchId = {branchA}")} / BR-B {(int)ScalarLong(c, $"SELECT COUNT(*) FROM dbo.[{t}] WHERE BranchId = {branchB}")}"
                    : "(no BranchId column)";
                write($"        {t,-18} total = {total,-4} {ct}");
            }

            write(string.Empty);
            write("--- C. Payment totals ---");
            var pa = ScalarDecimal(c, $"SELECT SUM(AmountPaid) FROM dbo.Payments WHERE BranchId = {branchA}");
            var pb = ScalarDecimal(c, $"SELECT SUM(AmountPaid) FROM dbo.Payments WHERE BranchId = {branchB}");
            var pt = ScalarDecimal(c, "SELECT SUM(AmountPaid) FROM dbo.Payments");
            Expect("BR-A payments total", pa.ToString("0.00"), "500.00");
            Expect("BR-B payments total", pb.ToString("0.00"), "3650.00");
            Expect("Payments grand total", pt.ToString("0.00"), "4150.00");

            write(string.Empty);
            write("--- D. Feedback average ---");
            var avgB = ScalarDecimal(c, $"SELECT AVG(CAST(Rating AS decimal(9,4))) FROM dbo.Feedbacks WHERE BranchId = {branchB}");
            Expect("BR-B feedback average", avgB.ToString("0.0000"), "3.2500");

            write(string.Empty);
            write("--- E. Tenant A duplicate inquiries preserved ---");
            var ivan = ScalarLong(c, $"SELECT COUNT(*) FROM dbo.Inquiries WHERE FullName = N'Ivan Glenn Entrina' AND BranchId = {branchA}");
            Expect("'Ivan Glenn Entrina' rows in BR-A", ivan, 2);
            var maxInq = ScalarLong(c, "SELECT COUNT(*) FROM dbo.Inquiries");
            Expect("Inquiries total", maxInq, 10);

            write(string.Empty);
            write("--- F. FK / orphan checks (all must be 0) ---");
            var orphanChecks = new (string Label, string Sql)[]
            {
                ("Attendances -> Members", "SELECT COUNT(*) FROM dbo.Attendances x LEFT JOIN dbo.Members m ON m.MemberId = x.MemberId WHERE m.MemberId IS NULL"),
                ("Payments -> Members", "SELECT COUNT(*) FROM dbo.Payments x LEFT JOIN dbo.Members m ON m.MemberId = x.MemberId WHERE m.MemberId IS NULL"),
                ("Feedbacks -> Members", "SELECT COUNT(*) FROM dbo.Feedbacks x LEFT JOIN dbo.Members m ON m.MemberId = x.MemberId WHERE m.MemberId IS NULL"),
                ("Subscriptions -> Members", "SELECT COUNT(*) FROM dbo.Subscriptions x LEFT JOIN dbo.Members m ON m.MemberId = x.MemberId WHERE m.MemberId IS NULL"),
                ("RetentionRecords -> Members", "SELECT COUNT(*) FROM dbo.RetentionRecords x LEFT JOIN dbo.Members m ON m.MemberId = x.MemberId WHERE m.MemberId IS NULL"),
                ("Complaints -> Members", "SELECT COUNT(*) FROM dbo.Complaints x LEFT JOIN dbo.Members m ON m.MemberId = x.MemberId WHERE m.MemberId IS NULL"),
                ("UserAccounts -> Members", "SELECT COUNT(*) FROM dbo.UserAccounts x LEFT JOIN dbo.Members m ON m.MemberId = x.MemberId WHERE x.MemberId IS NOT NULL AND m.MemberId IS NULL"),
                ("Members -> Branches", "SELECT COUNT(*) FROM dbo.Members x WHERE x.BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.BranchId = x.BranchId)"),
                ("Attendances -> Branches", "SELECT COUNT(*) FROM dbo.Attendances x WHERE x.BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.BranchId = x.BranchId)"),
                ("Payments -> Branches", "SELECT COUNT(*) FROM dbo.Payments x WHERE x.BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.BranchId = x.BranchId)"),
                ("Feedbacks -> Branches", "SELECT COUNT(*) FROM dbo.Feedbacks x WHERE x.BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.BranchId = x.BranchId)"),
                ("Inquiries -> Branches", "SELECT COUNT(*) FROM dbo.Inquiries x WHERE x.BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.BranchId = x.BranchId)"),
                ("RetentionRecords -> Branches", "SELECT COUNT(*) FROM dbo.RetentionRecords x WHERE x.BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.BranchId = x.BranchId)"),
                ("UserAccounts -> Branches", "SELECT COUNT(*) FROM dbo.UserAccounts x WHERE x.BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.BranchId = x.BranchId)"),
                ("Complaints -> Branches", "SELECT COUNT(*) FROM dbo.Complaints x WHERE x.BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.BranchId = x.BranchId)")
            };

            foreach (var (label, sql) in orphanChecks)
            {
                var n = ScalarLong(c, sql);
                write($"  [{(n == 0 ? "PASS" : "FAIL")}] {label}: {n} orphan(s)");
                if (n != 0) fail($"orphan FKs: {label} = {n}");
            }

            write(string.Empty);
            write("--- G. UserAccount 'admin' ---");
            var admin = Read(c, "SELECT UserAccountId, Username, Role, IsActive, MemberId, BranchId, PasswordHash FROM dbo.UserAccounts",
                rd => (Id: rd.GetInt32(0), User: rd.GetString(1), Role: rd.GetString(2), Active: rd.GetBoolean(3), MemberId: rd.IsDBNull(4) ? (int?)null : rd.GetInt32(4), BranchId: rd.IsDBNull(5) ? (int?)null : rd.GetInt32(5), Hash: rd.GetString(6)));
            Expect("UserAccounts count", admin.Count, 1);
            if (admin.Count == 1)
            {
                var u = admin[0];
                Expect("admin username", u.User, "admin");
                Expect("admin role", u.Role, "Admin");
                Expect("admin active", u.Active, true);
                Expect("admin MemberId is NULL", u.MemberId == null, true);
                Expect("admin BranchId", u.BranchId, (int)branchB);

                var sourceHash = ScalarString(b, "SELECT PasswordHash FROM dbo.UserAccounts WHERE Username = 'admin'");
                var hashOk = string.Equals(sourceHash, u.Hash, StringComparison.Ordinal);
                write($"  [{(hashOk ? "PASS" : "FAIL")}] admin password hash migrated verbatim (64-char legacy SHA-256, value not printed): {hashOk}");
                if (!hashOk) fail("admin password hash does not match Tenant B (value not printed).");
            }

            write(string.Empty);
            write("--- H/I. Subscriptions, Complaints and branches ---");
            Expect("Subscriptions count", ScalarLong(c, "SELECT COUNT(*) FROM dbo.Subscriptions"), 0);
            Expect("Complaints count", ScalarLong(c, "SELECT COUNT(*) FROM dbo.Complaints"), 0);
            Expect("Branches count", ScalarLong(c, "SELECT COUNT(*) FROM dbo.Branches"), 2);

            write(string.Empty);
            write("--- J. Tenant A and Tenant B unchanged (vs Phase 3 baselines) ---");
            var aDiff = CompareBaseline(a, BaselineA);
            var bDiff = CompareBaseline(b, BaselineB);
            write($"  [{(aDiff.Count == 0 ? "PASS" : "FAIL")}] Tenant A unchanged (counts + checksums)");
            write($"  [{(bDiff.Count == 0 ? "PASS" : "FAIL")}] Tenant B unchanged (counts + checksums)");
            foreach (var d in aDiff) fail($"Tenant A changed: {d}");
            foreach (var d in bDiff) fail($"Tenant B changed: {d}");

            write(string.Empty);
            write("--- K. Master database unchanged ---");
            using (var master = new SqlConnection(r.Runtime.Configuration.GetConnectionString("MasterErp")))
            {
                master.Open();
                foreach (var (table, expected) in BaselineMaster)
                {
                    var actual = ScalarLong(master, $"SELECT COUNT(*) FROM dbo.[{table}]");
                    write($"  [{(actual == expected ? "PASS" : "FAIL")}] {table}: {actual} (expected {expected})");
                    if (actual != expected) fail($"master {table} changed: {actual} (expected {expected})");
                }
            }

            write(string.Empty);
            write("--- L. Tenant C migration history ---");
            var migrations = Read(c, "SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId", rd => rd.GetString(0));
            var historyOk = migrations.Count == 1 && migrations[0] == "20260930030000_InitialTenantC";
            write($"  [{(historyOk ? "PASS" : "FAIL")}] history = {string.Join(", ", migrations)}");
            if (!historyOk) fail("Tenant C migration history is not the expected single InitialTenantC migration.");

            write(string.Empty);
            write("--- N. Tenant C admin authentication flow ---");
            var candidate = Environment.GetEnvironmentVariable("ALPHA_VERIFY_PASSWORD");
            if (string.IsNullOrEmpty(candidate))
            {
                candidate = r.Runtime.Configuration["TenantCredentials:TenantB:Password"];
            }

            if (string.IsNullOrEmpty(candidate))
            {
                write("  [SKIP] no candidate password available (ALPHA_VERIFY_PASSWORD / TenantCredentials:TenantB:Password).");
            }
            else
            {
                try
                {
                    var login = r.Runtime.AuthenticationService.AuthenticateAsync(r.C.CompanyId, "admin", candidate).GetAwaiter().GetResult();
                    var ok = login != null && login.Role == "Admin" && login.IsBranchAware;
                    write($"  [{(ok ? "PASS" : "FAIL")}] Tenant C admin authenticated (password not printed): {(login == null ? "rejected" : $"role={login.Role}, branch-aware={login.IsBranchAware}")}");
                    if (login != null)
                    {
                        write($"        password rehash needed (legacy hash): {login.NeedsPasswordRehash}");
                    }
                    if (!ok) fail("Tenant C admin authentication did not succeed.");
                }
                catch (Exception ex)
                {
                    fail($"Tenant C admin authentication: {ex.GetType().Name}: {ex.Message}");
                }
            }

            write(string.Empty);
            write("--- O. Tenant C dashboard load (module queries + main window construction) ---");
            try
            {
                var info = r.InfoC;
                var options = r.Runtime.ConnectionFactory.CreateBranchAwareOptions(info);
                using var db = new AlphaElite.infrastructure.data.TenantCAlphaEliteDbContext(options);
                var checks = new (string Name, Func<object> Query)[]
                {
                    ("Branches (count)", () => db.Branches.Count()),
                    ("Members (count)", () => db.Members.Count()),
                    ("Members active (count)", () => db.Members.Count(x => x.IsActive)),
                    ("Members per branch (groups)", () => db.Members.Where(x => x.BranchId != null).GroupBy(x => x.BranchId).Select(g => g.Key).ToList().Count),
                    ("Attendance (count)", () => db.Attendances.Count()),
                    ("Payments (count)", () => db.Payments.Count()),
                    ("Payments total (sum)", () => db.Payments.Sum(x => (decimal?)x.AmountPaid) ?? 0m),
                    ("Feedback (count)", () => db.Feedbacks.Count()),
                    ("Feedback average rating", () => db.Feedbacks.Any() ? Math.Round(db.Feedbacks.Average(x => (double)x.Rating), 2) : 0d),
                    ("Inquiries (count)", () => db.Inquiries.Count()),
                    ("Subscriptions (count)", () => db.Subscriptions.Count()),
                    ("Retention records (count)", () => db.RetentionRecords.Count()),
                    ("User accounts (count)", () => db.UserAccounts.Count()),
                    ("Complaints (count)", () => db.Complaints.Count())
                };

                foreach (var (name, query) in checks)
                {
                    try
                    {
                        write($"  [PASS] dashboard {name}: {query()}");
                    }
                    catch (Exception ex)
                    {
                        fail($"dashboard {name}: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                ApplicationConfiguration.Initialize();

                var session = new UserSession
                {
                    CompanyId = r.C.CompanyId,
                    CompanyCode = r.C.CompanyCode,
                    CompanyName = r.C.CompanyName,
                    IsBranchAware = true,
                    BranchId = admin.Count == 1 ? admin[0].BranchId : null,
                    MemberId = null,
                    Username = "admin",
                    FullName = "System Administrator",
                    Role = Roles.Admin
                };

                using var window = new Form1(session, r.Tenants, r.Runtime.DataContextFactory, r.Runtime.AuthorizationService);
                write($"  [PASS] Form1 constructed; tenant switcher: {window.DescribeTenantSwitcher()}; session: {window.DescribeSession()}");
            }
            catch (Exception ex)
            {
                fail($"dashboard/main window: {ex.GetType().Name}: {ex.Message}");
            }

            write(string.Empty);
            write("--- P. Error signature scan ---");
            var signatures = new[] { "Invalid column name", "Invalid object name", "Login failed for user", "NullReferenceException", "FK violation", "orphan" };
            var hits = failures.Where(f => signatures.Any(s => f.Contains(s, StringComparison.OrdinalIgnoreCase))).ToList();
            if (hits.Count == 0)
            {
                write("  [PASS] no Invalid column/object, connection-login, null-reference, FK or orphan errors.");
            }
            else
            {
                foreach (var h in hits) write($"  [FAIL] {h}");
            }
        }

        // =====================================================================================
        // Helpers
        // =====================================================================================

        private static SourceInquiry MapInquiry(SqlDataReader rd) =>
            new(rd.GetInt32(0), rd.GetString(1), S(rd, 2), S(rd, 3), S(rd, 4), S(rd, 5), rd.GetBoolean(6), rd.GetDateTime(7));

        private static string? S(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

        private static SqlConnection Open(Resolved r, TenantDatabaseInfo info)
        {
            var conn = new SqlConnection(r.Runtime.ConnectionFactory.CreateConnectionString(info));
            conn.Open();
            return conn;
        }

        private static List<SourceMember> ReadMembers(SqlConnection conn) =>
            Read(conn, "SELECT MemberId, MemberCode, FullName, Email, IsActive, CreatedAt FROM dbo.Members ORDER BY MemberId",
                rd => new SourceMember(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetBoolean(4), rd.GetDateTime(5)));

        private static List<T> Read<T>(SqlConnection conn, string sql, Func<SqlDataReader, T> map, SqlTransaction? tx = null)
        {
            var list = new List<T>();
            using var cmd = new SqlCommand(sql, conn, tx) { CommandTimeout = 60 };
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add(map(reader));
            return list;
        }

        private static void Exec(SqlConnection conn, SqlTransaction tx, string sql)
        {
            using var cmd = new SqlCommand(sql, conn, tx) { CommandTimeout = 60 };
            cmd.ExecuteNonQuery();
        }

        private static void IdentityInsert(SqlConnection conn, SqlTransaction tx, string table, bool on) =>
            Exec(conn, tx, $"SET IDENTITY_INSERT dbo.[{table}] {(on ? "ON" : "OFF")}");

        private static void Insert(SqlConnection conn, SqlTransaction tx, string table, string columns, params SqlParameter[] parameters)
        {
            var names = string.Join(", ", parameters.Select(p => p.ParameterName));
            using var cmd = new SqlCommand($"INSERT INTO dbo.[{table}] ({columns}) VALUES ({names})", conn, tx);
            cmd.Parameters.AddRange(parameters);
            cmd.ExecuteNonQuery();
        }

        private static SqlParameter PInt(string n, int v) => new(n, SqlDbType.Int) { Value = v };
        private static SqlParameter PNInt(string n, int? v) => new(n, SqlDbType.Int) { Value = (object?)v ?? DBNull.Value };
        private static SqlParameter PStr(string n, string v) => new(n, SqlDbType.NVarChar, -1) { Value = v };
        private static SqlParameter PNStr(string n, string? v) => new(n, SqlDbType.NVarChar, -1) { Value = (object?)v ?? DBNull.Value };
        private static SqlParameter PBit(string n, bool v) => new(n, SqlDbType.Bit) { Value = v };
        private static SqlParameter PDec(string n, decimal v) => new(n, SqlDbType.Decimal) { Value = v, Precision = 18, Scale = 2 };
        private static SqlParameter PDt(string n, DateTime v) => new(n, SqlDbType.DateTime2) { Value = v };
        private static SqlParameter PNDt(string n, DateTime? v) => new(n, SqlDbType.DateTime2) { Value = (object?)v ?? DBNull.Value };

        private static void InsertMember(SqlConnection c, SqlTransaction tx, int id, SourceMember m, long branchId) =>
            Insert(c, tx, "Members", "MemberId, MemberCode, FullName, Email, IsActive, CreatedAt, BranchId",
                PInt("@MemberId", id), PStr("@MemberCode", m.Code), PStr("@FullName", m.FullName), PStr("@Email", m.Email),
                PBit("@IsActive", m.IsActive), PDt("@CreatedAt", m.CreatedAt), PInt("@BranchId", (int)branchId));

        private static void InsertAttendance(SqlConnection c, SqlTransaction tx, int id, int memberId, DateTime when, long branchId) =>
            Insert(c, tx, "Attendances", "AttendanceId, MemberId, CheckInDate, BranchId",
                PInt("@AttendanceId", id), PInt("@MemberId", memberId), PDt("@CheckInDate", when), PInt("@BranchId", (int)branchId));

        private static void InsertPayment(SqlConnection c, SqlTransaction tx, int id, int memberId, SourcePayment p, long branchId) =>
            Insert(c, tx, "Payments", "PaymentId, MemberId, AmountPaid, PaymentDate, NextDueDate, Notes, BranchId",
                PInt("@PaymentId", id), PInt("@MemberId", memberId), PDec("@AmountPaid", p.Amount), PDt("@PaymentDate", p.PaymentDate),
                PNDt("@NextDueDate", p.NextDueDate), PNStr("@Notes", p.Notes), PInt("@BranchId", (int)branchId));

        private static void InsertFeedback(SqlConnection c, SqlTransaction tx, int id, int memberId, SourceFeedback f, long branchId) =>
            Insert(c, tx, "Feedbacks", "FeedbackId, MemberId, Rating, Comments, SubmittedAt, BranchId",
                PInt("@FeedbackId", id), PInt("@MemberId", memberId), PInt("@Rating", f.Rating), PNStr("@Comments", f.Comments),
                PDt("@SubmittedAt", f.SubmittedAt), PInt("@BranchId", (int)branchId));

        private static void InsertInquiry(SqlConnection c, SqlTransaction tx, int id, SourceInquiry i, long branchId) =>
            Insert(c, tx, "Inquiries", "InquiryId, FullName, ContactNumber, EmailAddress, InterestedPlan, Message, IsFollowedUp, CreatedAt, BranchId",
                PInt("@InquiryId", id), PStr("@FullName", i.FullName), PNStr("@ContactNumber", i.ContactNumber), PNStr("@EmailAddress", i.EmailAddress),
                PNStr("@InterestedPlan", i.InterestedPlan), PNStr("@Message", i.Message), PBit("@IsFollowedUp", i.IsFollowedUp),
                PDt("@CreatedAt", i.CreatedAt), PInt("@BranchId", (int)branchId));

        private static void InsertRetention(SqlConnection c, SqlTransaction tx, int id, int memberId, SourceRetention x, long branchId) =>
            Insert(c, tx, "RetentionRecords",
                "RetentionRecordId, MemberId, Reason, RetentionAction, Promotion, PromotionDetails, Notes, CreatedAt, CreatedBy, Status, ApprovedBy, ApprovedAt, ApprovalReason, RejectionReason, CompletedAt, CompletionNotes, AssignedStaff, BranchId",
                PInt("@RetentionRecordId", id), PInt("@MemberId", memberId), PStr("@Reason", x.Reason), PStr("@RetentionAction", x.RetentionAction),
                PNStr("@Promotion", x.Promotion), PNStr("@PromotionDetails", x.PromotionDetails), PNStr("@Notes", x.Notes), PDt("@CreatedAt", x.CreatedAt),
                PStr("@CreatedBy", x.CreatedBy), PStr("@Status", x.Status), PNStr("@ApprovedBy", x.ApprovedBy), PNDt("@ApprovedAt", x.ApprovedAt),
                PNStr("@ApprovalReason", x.ApprovalReason), PNStr("@RejectionReason", x.RejectionReason), PNDt("@CompletedAt", x.CompletedAt),
                PNStr("@CompletionNotes", x.CompletionNotes), PNStr("@AssignedStaff", null), PInt("@BranchId", (int)branchId));

        private static void InsertAccount(SqlConnection c, SqlTransaction tx, int id, SourceAccount x, long branchId) =>
            Insert(c, tx, "UserAccounts", "UserAccountId, Username, PasswordHash, FullName, Role, IsActive, CreatedAt, MemberId, BranchId",
                PInt("@UserAccountId", id), PStr("@Username", x.Username), PStr("@PasswordHash", x.PasswordHash), PStr("@FullName", x.FullName),
                PStr("@Role", x.Role), PBit("@IsActive", x.IsActive), PDt("@CreatedAt", x.CreatedAt), PNInt("@MemberId", null), PInt("@BranchId", (int)branchId));

        private static bool HasBranchColumn(SqlConnection c, string table) =>
            ScalarLong(c, $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'BranchId'") > 0;

        private static string ScalarString(SqlConnection c, string sql, SqlTransaction? tx = null)
        {
            using var cmd = new SqlCommand(sql, c, tx);
            var value = cmd.ExecuteScalar();
            return value == null || value == DBNull.Value ? string.Empty : value.ToString()!;
        }

        private static long ScalarLong(SqlConnection c, string sql, SqlTransaction? tx = null)
        {
            using var cmd = new SqlCommand(sql, c, tx) { CommandTimeout = 60 };
            var value = cmd.ExecuteScalar();
            return value == null || value == DBNull.Value ? 0L : Convert.ToInt64(value);
        }

        private static decimal ScalarDecimal(SqlConnection c, string sql, SqlTransaction? tx = null)
        {
            using var cmd = new SqlCommand(sql, c, tx) { CommandTimeout = 60 };
            var value = cmd.ExecuteScalar();
            return value == null || value == DBNull.Value ? 0m : Convert.ToDecimal(value);
        }

        private static List<string> CompareBaseline(SqlConnection conn, (string Table, long Count, long? Checksum)[] baseline)
        {
            var diffs = new List<string>();

            foreach (var (table, count, checksum) in baseline)
            {
                var actualCount = ScalarLong(conn, $"SELECT COUNT(*) FROM dbo.[{table}]");
                if (actualCount != count)
                {
                    diffs.Add($"{table} count {actualCount} != {count}");
                    continue;
                }

                if (checksum == null)
                {
                    continue;
                }

                var actualChecksum = ScalarLong(conn, $"SELECT CAST(CHECKSUM_AGG(BINARY_CHECKSUM(*)) AS bigint) FROM dbo.[{table}]");
                if (actualChecksum != checksum)
                {
                    diffs.Add($"{table} checksum {actualChecksum} != {checksum}");
                }
            }

            return diffs;
        }
    }
}
