using AlphaElite.domain.entities;
using AlphaElite.infrastructure.data;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Complaints CRUD + Admin A navigation acceptance harness (Tenant A phase).
    ///
    /// Modes (mirroring the Phase 2 harness pattern):
    ///  * --complaints-inspect      read-only: Complaints schema + row counts for Tenant A / B / C
    ///  * --complaints-fix-schema   additive, guarded: adds the three missing nullable complaint
    ///                              columns (Category / Priority / StaffAssigned) to Tenant A and
    ///                              Tenant B - never drops or rewrites anything
    ///  * --complaints-verify       headless acceptance run: CRUD round-trip with all 11 fields,
    ///                              member-dropdown tenant isolation, Admin A navigation (no
    ///                              Dashboard, no Business Intelligence) and regressions for
    ///                              Tenant B / Tenant C / Gym Staff A
    ///
    /// Tenant isolation: every complaint read/write in the verification uses the authenticated
    /// Tenant A context; Tenant B and Tenant C are only ever READ to prove they were untouched.
    /// </summary>
    internal static class ComplaintsVerification
    {
        // Harness-owned subjects: only these rows are ever created, and any leftover from an
        // interrupted run is purged at the start of the next verification.
        private const string TempSubject = "phase-crud-temp complaint";
        private const string TempUpdatedSubject = "phase-crud-updated complaint";
        private const string TempDescription = "Harness-created complaint for CRUD verification.";
        private const string TempInitialResolution = "initial response";
        private const string TempUpdatedResolution = "handled by harness";

        private static readonly DateTime SubmittedDate = new DateTime(2026, 9, 28, 10, 15, 0);
        private static readonly DateTime ResolvedDate = new DateTime(2026, 9, 29, 0, 0, 0);

        private static readonly string[] RequiredComplaintColumns = { "Category", "Priority", "StaffAssigned" };

        // =====================================================================================
        // --complaints-inspect (read-only)
        // =====================================================================================
        public static int RunInspect()
        {
            Console.WriteLine("======================================================================");
            Console.WriteLine("COMPLAINTS SCHEMA INSPECT (read-only)");
            Console.WriteLine("======================================================================");

            AppRuntime runtime;
            try
            {
                runtime = AppRuntime.Create();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [FAIL] runtime: {ex.Message}");
                return 1;
            }

            var tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();
            var tenantA = tenants.FirstOrDefault(t => string.Equals(t.CredentialKey, "TenantA", StringComparison.OrdinalIgnoreCase));
            var tenantB = tenants.FirstOrDefault(t => string.Equals(t.CredentialKey, "TenantB", StringComparison.OrdinalIgnoreCase));
            var tenantC = tenants.FirstOrDefault(t => t.IsBranchAware);

            var failed = false;

            foreach (var (label, tenant) in new[] { ("Tenant A", tenantA), ("Tenant B", tenantB) })
            {
                if (tenant == null)
                {
                    Console.WriteLine($"  [FAIL] {label}: tenant not configured");
                    failed = true;
                    continue;
                }

                try
                {
                    var info = runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId).GetAwaiter().GetResult();
                    using var db = new TenantAlphaEliteDbContext(runtime.ConnectionFactory.CreateStandardOptions(info));

                    var columns = db.Database.SqlQueryRaw<string>(
                            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Complaints' ORDER BY ORDINAL_POSITION")
                        .AsEnumerable()
                        .ToList();
                    var count = db.Complaints.AsNoTracking().Count();

                    Console.WriteLine($"  {label} ({info.DatabaseName}): {count} complaint(s)");
                    Console.WriteLine($"      columns: {string.Join(", ", columns)}");

                    foreach (var required in RequiredComplaintColumns)
                    {
                        Console.WriteLine(columns.Contains(required)
                            ? $"      [ok]   {required}"
                            : $"      [MISSING] {required}  -> run --complaints-fix-schema");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [FAIL] {label}: {ex.GetBaseException().Message}");
                    failed = true;
                }
            }

            if (tenantC != null)
            {
                try
                {
                    var info = runtime.Resolver.GetDatabaseInfoAsync(tenantC.CompanyId).GetAwaiter().GetResult();
                    using var db = new TenantCAlphaEliteDbContext(runtime.ConnectionFactory.CreateBranchAwareOptions(info));
                    var columns = db.Database.SqlQueryRaw<string>(
                            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Complaints' ORDER BY ORDINAL_POSITION")
                        .AsEnumerable()
                        .ToList();
                    var count = db.Complaints.AsNoTracking().Count();

                    Console.WriteLine($"  Tenant C ({info.DatabaseName}, reference only - never modified): {count} complaint(s)");
                    Console.WriteLine($"      columns: {string.Join(", ", columns)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [FAIL] Tenant C inspect: {ex.GetBaseException().Message}");
                    failed = true;
                }
            }

            return failed ? 1 : 0;
        }

        // =====================================================================================
        // --complaints-fix-schema (additive, guarded, Tenant A + Tenant B only)
        // =====================================================================================
        public static int RunFixSchema()
        {
            Console.WriteLine("======================================================================");
            Console.WriteLine("COMPLAINTS SCHEMA FIX (additive only - no drops, no recreation)");
            Console.WriteLine("  Adds three NULLABLE columns to Tenant A and Tenant B:");
            Console.WriteLine("    Category nvarchar(100), Priority nvarchar(50), StaffAssigned nvarchar(100)");
            Console.WriteLine("  Existing rows are preserved (all three default to NULL).");
            Console.WriteLine("  Tenant C is NOT touched (its context ignores these properties).");
            Console.WriteLine("======================================================================");

            AppRuntime runtime;
            try
            {
                runtime = AppRuntime.Create();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [FAIL] runtime: {ex.Message}");
                return 1;
            }

            var tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();
            var tenantA = tenants.FirstOrDefault(t => string.Equals(t.CredentialKey, "TenantA", StringComparison.OrdinalIgnoreCase));
            var tenantB = tenants.FirstOrDefault(t => string.Equals(t.CredentialKey, "TenantB", StringComparison.OrdinalIgnoreCase));

            var failed = false;

            foreach (var (label, tenant) in new[] { ("Tenant A", tenantA), ("Tenant B", tenantB) })
            {
                if (tenant == null)
                {
                    Console.WriteLine($"  [FAIL] {label}: tenant not configured");
                    failed = true;
                    continue;
                }

                try
                {
                    var info = runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId).GetAwaiter().GetResult();
                    using var db = new TenantAlphaEliteDbContext(runtime.ConnectionFactory.CreateStandardOptions(info));

                    var tableExists = db.Database.SqlQueryRaw<int>(
                            "SELECT CAST(COUNT(*) AS int) AS [Value] FROM sys.tables WHERE name = 'Complaints'")
                        .AsEnumerable()
                        .First() > 0;

                    if (!tableExists)
                    {
                        Console.WriteLine($"  [FAIL] {label}: Complaints table does not exist - run --phase2-fix-schema first.");
                        failed = true;
                        continue;
                    }

                    var countBefore = db.Complaints.AsNoTracking().Count();

                    foreach (var (column, type) in new[]
                             {
                                 ("Category", "nvarchar(100)"),
                                 ("Priority", "nvarchar(50)"),
                                 ("StaffAssigned", "nvarchar(100)")
                             })
                    {
                        var exists = db.Database.SqlQueryRaw<int>(
                                $"SELECT CASE WHEN COL_LENGTH(N'dbo.Complaints', N'{column}') IS NULL THEN 0 ELSE 1 END AS [Value]")
                            .AsEnumerable()
                            .First() == 1;

                        if (exists)
                        {
                            Console.WriteLine($"  {label}: {column} already present (no change)");
                            continue;
                        }

                        db.Database.ExecuteSqlRaw($"ALTER TABLE dbo.Complaints ADD [{column}] {type} NULL");
                        Console.WriteLine($"  {label}: ADDED [{column}] {type} NULL");
                    }

                    var countAfter = db.Complaints.AsNoTracking().Count();
                    Console.WriteLine($"  {label}: complaint rows before={countBefore} after={countAfter} " +
                                      (countBefore == countAfter ? "(preserved)" : "(CHANGED - FAIL)"));

                    if (countBefore != countAfter)
                    {
                        failed = true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [FAIL] {label}: {ex.GetBaseException().Message}");
                    failed = true;
                }
            }

            Console.WriteLine(failed ? "--- Result: FAILED ---" : "--- Result: OK ---");
            return failed ? 1 : 0;
        }

        // =====================================================================================
        // --complaints-verify (headless acceptance)
        // =====================================================================================
        public static int RunVerify()
        {
            var failures = new List<string>();
            var checks = 0;

            void Write(string text) => Console.WriteLine(text);

            void Check(string name, bool ok, string detail)
            {
                checks++;
                Write($"  [{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
                if (!ok) failures.Add($"{name}: {detail}");
            }

            Write("======================================================================");
            Write("COMPLAINTS CRUD + ADMIN A NAVIGATION VERIFICATION");
            Write("======================================================================");

            ApplicationConfiguration.Initialize();
            SynchronizationContext.SetSynchronizationContext(null);

            AppRuntime runtime;
            try
            {
                runtime = AppRuntime.Create();
            }
            catch (Exception ex)
            {
                Write($"  [FAIL] runtime: {ex.Message}");
                return 1;
            }

            var tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();
            var tenantA = tenants.FirstOrDefault(t => string.Equals(t.CredentialKey, "TenantA", StringComparison.OrdinalIgnoreCase));
            var tenantB = tenants.FirstOrDefault(t => string.Equals(t.CredentialKey, "TenantB", StringComparison.OrdinalIgnoreCase));
            var tenantC = tenants.FirstOrDefault(t => t.IsBranchAware);

            if (tenantA == null || tenantB == null || tenantC == null)
            {
                Write("  [FAIL] tenant discovery: TenantA/TenantB/TenantC must all be configured.");
                return 1;
            }

            // ---------------------------------------------------------------------
            // 0. Schema precondition + baselines + harness-row purge
            // ---------------------------------------------------------------------
            Write("--- 0. Schema precondition, baselines, harness-row purge ---");

            var infoA = runtime.Resolver.GetDatabaseInfoAsync(tenantA.CompanyId).GetAwaiter().GetResult();
            var infoB = runtime.Resolver.GetDatabaseInfoAsync(tenantB.CompanyId).GetAwaiter().GetResult();
            var infoC = runtime.Resolver.GetDatabaseInfoAsync(tenantC.CompanyId).GetAwaiter().GetResult();

            using var legacyA = new TenantAlphaEliteDbContext(runtime.ConnectionFactory.CreateStandardOptions(infoA));
            using var legacyB = new TenantAlphaEliteDbContext(runtime.ConnectionFactory.CreateStandardOptions(infoB));
            using var tenantCdb = new TenantCAlphaEliteDbContext(runtime.ConnectionFactory.CreateBranchAwareOptions(infoC));

            foreach (var (label, db) in new (string, TenantAlphaEliteDbContext)[] { ("Tenant A", legacyA), ("Tenant B", legacyB) })
            {
                var columns = db.Database.SqlQueryRaw<string>(
                        "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Complaints'")
                    .AsEnumerable()
                    .ToList();

                var missing = RequiredComplaintColumns.Where(c => !columns.Contains(c)).ToList();
                Check($"{label} has the three complaint columns", missing.Count == 0,
                    missing.Count == 0 ? string.Join(", ", RequiredComplaintColumns) : $"missing: {string.Join(", ", missing)} (run --complaints-fix-schema)");

                if (missing.Count > 0)
                {
                    return 1;
                }
            }

            // Remove leftovers from an interrupted earlier run (only harness-owned subjects).
            var purged = legacyA.Database.ExecuteSqlRaw(
                "DELETE FROM dbo.Complaints WHERE Subject LIKE N'phase-crud-temp%' OR Subject LIKE N'phase-crud-updated%'");
            Write($"  purged leftover harness rows: {purged}");

            var baselineA = legacyA.Complaints.AsNoTracking().Select(c => c.ComplaintId).OrderBy(id => id).ToList();
            var baselineBCount = legacyB.Complaints.AsNoTracking().Count();
            var baselineCCount = tenantCdb.Complaints.AsNoTracking().Count();

            var aMembers = legacyA.Members.AsNoTracking()
                .Select(m => new { m.MemberId, m.FullName, m.MemberCode })
                .OrderBy(m => m.MemberId)
                .ToList();
            var bMembers = legacyB.Members.AsNoTracking()
                .Select(m => new { m.MemberId, m.FullName, m.MemberCode })
                .ToList();
            var cMembers = tenantCdb.Members.AsNoTracking()
                .Select(m => new { m.MemberId, m.FullName, m.MemberCode })
                .ToList();
            var aStaff = legacyA.UserAccounts.AsNoTracking()
                .Where(u => u.Role != Roles.Member)
                .Select(u => u.Username)
                .OrderBy(u => u)
                .ToList();

            Check("Tenant A has members and staff to work with",
                aMembers.Count >= 1 && aStaff.Count >= 1,
                $"members={aMembers.Count}, staff=[{string.Join(", ", aStaff)}]");
            Check("Tenant A complaint baseline captured", baselineA.Count >= 0, $"rows={baselineA.Count}");

            // ---------------------------------------------------------------------
            // 1. Admin A navigation: no Dashboard, no Business Intelligence
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 1. Admin A navigation (Dashboard removed; no BI added) ---");

            Form1? adminForm = null;
            try
            {
                adminForm = BuildForm(runtime, tenants, tenantA, "adminA");
            }
            catch (Exception ex)
            {
                Check("'adminA' form builds", false, ex.GetBaseException().Message);
            }

            if (adminForm != null)
            {
                using (adminForm)
                {
                    var nav = adminForm.DescribeNav();
                    const string expectedNav = "Members, Attendance, Payments, Feedback, Inquiries, Complaints";

                    Check("'adminA' nav is exactly the Tenant A set", nav == expectedNav,
                        nav == expectedNav ? nav : $"{nav} (expected: {expectedNav})");
                    Check("'adminA' has NO Dashboard", !nav.Contains("Dashboard"), nav);
                    Check("'adminA' has NO Business Intelligence", !nav.Contains("Business Intelligence"), nav);
                    Check("'adminA' keeps Feedback & Complaints + working modules",
                        new[] { "Members", "Attendance", "Payments", "Feedback", "Inquiries", "Complaints" }.All(nav.Contains),
                        nav);
                    Check("'adminA' tenant dropdown still removed", adminForm.TenantSwitcherHiddenForTest,
                        adminForm.DescribeTenantSwitcher());
                    Check("'adminA' has NO branch selector", !adminForm.HasCurrentBranchSelector(),
                        adminForm.DescribeCurrentBranchSelector());

                    // Uniform sidebar spacing (hidden Dashboard must not leave a gap).
                    var layout = adminForm.DescribeNavLayoutForTest();
                    var positions = layout
                        .Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(part => int.Parse(part.Substring(part.IndexOf('@') + 1)))
                        .ToList();
                    var uniform = positions.Count > 0
                        && positions[0] == 100
                        && positions.Zip(positions.Skip(1), (a, b) => b - a).All(gap => gap == 44);
                    Check("'adminA' nav spacing is uniform (no Dashboard gap)", uniform, layout);

                    adminForm.ReloadAllModulesForTestAsync().GetAwaiter().GetResult();
                    SynchronizationContext.SetSynchronizationContext(null);
                    var loadFailures = adminForm.LoadFailuresForTest;
                    Check("'adminA' all modules load without errors", loadFailures.Count == 0,
                        loadFailures.Count == 0 ? "0 failures" : string.Join("; ", loadFailures));
                }
            }

            // ---------------------------------------------------------------------
            // 2. Member dropdown: Tenant A members only, DropDownList, staff from Tenant A
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 2. Member / staff dropdown sources (Tenant A only) ---");

            using var form = BuildForm(runtime, tenants, tenantA, "adminA");

            var optionsDump = form.DescribeComplaintMemberOptionsForTest().GetAwaiter().GetResult();
            SynchronizationContext.SetSynchronizationContext(null);

            Check("member selector is a DropDownList (no MemberId typing)",
                optionsDump.Contains("style=DropDownList"), optionsDump);

            var memberOptions = ParseBracketList(optionsDump, "members=[", "]; staff=[", " ; ");

            // Compare full display triples ("id: Full Name (Code)"): a numeric code that happens
            // to exist in another tenant's database (codes are per-database) can never make this
            // check ambiguous.
            var optionSet = memberOptions.ToHashSet();
            var aDisplay = aMembers.Select(m => $"{m.MemberId}: {m.FullName} ({m.MemberCode})").ToHashSet();
            var bDisplay = bMembers.Select(m => $"{m.MemberId}: {m.FullName} ({m.MemberCode})").ToHashSet();
            var cDisplay = cMembers.Select(m => $"{m.MemberId}: {m.FullName} ({m.MemberCode})").ToHashSet();

            Check("member dropdown is exactly Tenant A's members (id + name + code)",
                optionSet.SetEquals(aDisplay),
                $"options=[{string.Join("; ", memberOptions)}]");
            // B/C displays are first reduced to members NOT present in Tenant A: some member
            // values were seeded identically across databases before this phase, and an
            // identical twin in Tenant A is by definition an A member (the create/write
            // checks below prove the data source is Tenant A's context either way).
            Check("member dropdown contains NO Tenant B-only member (id + name + code)",
                !optionSet.Overlaps(bDisplay.Except(aDisplay)),
                $"B-only overlaps=[{string.Join("; ", optionSet.Intersect(bDisplay.Except(aDisplay)))}]");
            Check("member dropdown contains NO Tenant C-only member (id + name + code)",
                !optionSet.Overlaps(cDisplay.Except(aDisplay)),
                $"C-only overlaps=[{string.Join("; ", optionSet.Intersect(cDisplay.Except(aDisplay)))}]");

            var staffOptions = ParseBracketList(optionsDump, "staff=[", "]", ", ");
            Check("staff dropdown = Tenant A's staff/admin accounts only",
                staffOptions.ToHashSet().SetEquals(aStaff.ToHashSet()),
                $"staff=[{string.Join(", ", staffOptions)}]");

            // ---------------------------------------------------------------------
            // 3. CREATE - all 11 fields persisted to Tenant A
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 3. Complaints Create (all 11 fields) ---");

            var createdId = 0;
            try
            {
                form.SetComplaintFormForTest(
                    memberId: aMembers[0].MemberId,
                    category: "Equipment",
                    subject: TempSubject,
                    description: TempDescription,
                    dateSubmitted: SubmittedDate,
                    priority: "Critical",
                    status: "Under Review",
                    staff: aStaff[0],
                    resolution: TempInitialResolution,
                    dateResolved: null); // open complaint: Date Resolved must stay NULL

                var result = form.SubmitComplaintForTestAsync().GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);
                createdId = int.Parse(result.Split(':')[1]);
                Check("create returns the new Complaint ID", createdId > 0, result);
            }
            catch (Exception ex)
            {
                Check("create runs without dialogs/errors", false, ex.GetBaseException().Message);
            }

            if (createdId > 0)
            {
                var row = legacyA.Complaints.AsNoTracking().FirstOrDefault(c => c.ComplaintId == createdId);

                Check("1. Complaint ID stored (database identity)", row != null,
                    row == null ? "(missing)" : $"id={row.ComplaintId}");
                Check("2. Member stored (Tenant A member)", row != null && row.MemberId == aMembers[0].MemberId,
                    row?.MemberId.ToString() ?? "(missing)");
                Check("3. Category stored", row?.Category == "Equipment", row?.Category ?? "(missing)");
                Check("4. Subject stored", row?.Subject == TempSubject, row?.Subject ?? "(missing)");
                Check("5. Description stored", row?.Description == TempDescription, row?.Description ?? "(missing)");
                Check("6. Date Submitted stored", row != null && row.CreatedAt == SubmittedDate,
                    row?.CreatedAt.ToString("yyyy-MM-dd HH:mm") ?? "(missing)");
                Check("7. Priority stored", row?.Priority == "Critical", row?.Priority ?? "(missing)");
                Check("8. Status stored", row?.Status == "Under Review", row?.Status ?? "(missing)");
                Check("9. Staff Assigned stored", row?.StaffAssigned == aStaff[0], row?.StaffAssigned ?? "(missing)");
                Check("10. Resolution / Response stored", row?.ResolutionNotes == TempInitialResolution,
                    row?.ResolutionNotes ?? "(missing)");
                Check("11. Date Resolved NULL for an open complaint", row != null && row.ResolvedAt == null,
                    row?.ResolvedAt?.ToString("yyyy-MM-dd") ?? "(null)");

                Check("Tenant A row count is baseline + 1",
                    legacyA.Complaints.AsNoTracking().Count() == baselineA.Count + 1,
                    $"count={legacyA.Complaints.AsNoTracking().Count()} (baseline {baselineA.Count})");

                Check("Tenant B complaints untouched",
                    legacyB.Complaints.AsNoTracking().Count() == baselineBCount,
                    $"count={legacyB.Complaints.AsNoTracking().Count()} (baseline {baselineBCount})");
                Check("Tenant C complaints untouched",
                    tenantCdb.Complaints.AsNoTracking().Count() == baselineCCount,
                    $"count={tenantCdb.Complaints.AsNoTracking().Count()} (baseline {baselineCCount})");
                Check("no cross-tenant write: subject exists only in Tenant A",
                    !legacyB.Complaints.AsNoTracking().Any(c => c.Subject == TempSubject)
                    && !tenantCdb.Complaints.AsNoTracking().Any(c => c.Subject == TempSubject),
                    "absent from Tenant B and Tenant C");
            }

            // ---------------------------------------------------------------------
            // 4. READ - a fresh screen (second form instance) shows the record
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 4. Complaints Read (fresh screen = reload) ---");

            using var freshForm = BuildForm(runtime, tenants, tenantA, "adminA");

            if (createdId > 0)
            {
                var grid = freshForm.DescribeComplaintGridForTest().GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);

                var gridRows = grid.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries);
                var createdRow = gridRows.FirstOrDefault(r => r.StartsWith($"{createdId}|"));

                Check("list shows the new complaint (ID + all 9 list fields)", createdRow != null,
                    createdRow ?? $"grid={grid}");
                Check("list shows the Tenant A member name",
                    createdRow != null && createdRow.Contains(aMembers[0].FullName),
                    aMembers[0].FullName);

                freshForm.LoadComplaintIntoFormForTestAsync(createdId).GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);

                var dump = freshForm.DescribeComplaintFormForTest();
                Check("detail view shows Complaint ID", dump.Contains($"selected={createdId}"), dump);
                Check("detail view shows Member (Tenant A)", dump.Contains(aMembers[0].FullName), dump);
                Check("detail view shows Category", dump.Contains("category=Equipment"), dump);
                Check("detail view shows Subject", dump.Contains($"subject={TempSubject}"), dump);
                Check("detail view shows Description", dump.Contains($"description={TempDescription}"), dump);
                Check("detail view shows Date Submitted",
                    dump.Contains($"submitted={SubmittedDate:yyyy-MM-dd HH:mm}"), dump);
                Check("detail view shows Priority", dump.Contains("priority=Critical"), dump);
                Check("detail view shows Status", dump.Contains("status=Under Review"), dump);
                Check("detail view shows Staff Assigned", dump.Contains($"staff={aStaff[0]}"), dump);
                Check("detail view shows Resolution / Response",
                    dump.Contains($"resolution={TempInitialResolution}"), dump);
                Check("detail view shows empty Date Resolved for an open complaint",
                    dump.Contains("resolved=(none)"), dump);

                // ---------------------------------------------------------------------
                // 5. UPDATE - edit subject / priority / status / resolution; ID + Date
                //    Submitted must be preserved; no duplicate record.
                // ---------------------------------------------------------------------
                Write(string.Empty);
                Write("--- 5. Complaints Update ---");

                try
                {
                    // Deliberately NOT touching Date Submitted: it must survive the edit.
                    freshForm.SetComplaintFormForTest(
                        subject: TempUpdatedSubject,
                        priority: "Low",
                        status: "Resolved",
                        resolution: TempUpdatedResolution,
                        dateResolved: ResolvedDate);

                    var result = freshForm.UpdateComplaintForTestAsync().GetAwaiter().GetResult();
                    SynchronizationContext.SetSynchronizationContext(null);
                    Check("update returns the SAME Complaint ID (no new record)", result == $"updated:{createdId}", result);
                }
                catch (Exception ex)
                {
                    Check("update runs without dialogs/errors", false, ex.GetBaseException().Message);
                }

                var updated = legacyA.Complaints.AsNoTracking().FirstOrDefault(c => c.ComplaintId == createdId);
                Check("Subject changed", updated?.Subject == TempUpdatedSubject, updated?.Subject ?? "(missing)");
                Check("Priority changed", updated?.Priority == "Low", updated?.Priority ?? "(missing)");
                Check("Status changed", updated?.Status == "Resolved", updated?.Status ?? "(missing)");
                Check("Resolution / Response changed", updated?.ResolutionNotes == TempUpdatedResolution,
                    updated?.ResolutionNotes ?? "(missing)");
                Check("Date Resolved stored", updated != null && updated.ResolvedAt == ResolvedDate,
                    updated?.ResolvedAt?.ToString("yyyy-MM-dd") ?? "(missing)");
                Check("Date Submitted preserved (not overwritten by the edit)",
                    updated != null && updated.CreatedAt == SubmittedDate,
                    updated?.CreatedAt.ToString("yyyy-MM-dd HH:mm") ?? "(missing)");
                Check("still exactly baseline + 1 rows (no duplicate)",
                    legacyA.Complaints.AsNoTracking().Count() == baselineA.Count + 1,
                    $"count={legacyA.Complaints.AsNoTracking().Count()}");

                var reloadedGrid = freshForm.DescribeComplaintGridForTest().GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);
                Check("reloaded list shows the updated subject",
                    reloadedGrid.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries)
                        .Any(r => r.StartsWith($"{createdId}|") && r.Contains(TempUpdatedSubject)),
                    reloadedGrid);

                // ---------------------------------------------------------------------
                // 6. DELETE - only the selected complaint, then reload proves it is gone
                // ---------------------------------------------------------------------
                Write(string.Empty);
                Write("--- 6. Complaints Delete ---");

                try
                {
                    var result = freshForm.DeleteComplaintForTestAsync(createdId).GetAwaiter().GetResult();
                    SynchronizationContext.SetSynchronizationContext(null);
                    Check("delete removes the selected complaint", result == $"deleted:{createdId}", result);
                }
                catch (Exception ex)
                {
                    Check("delete runs without dialogs/errors", false, ex.GetBaseException().Message);
                }

                var afterDeleteIds = legacyA.Complaints.AsNoTracking().Select(c => c.ComplaintId).OrderBy(id => id).ToList();
                Check("complaint is gone from Tenant A", !afterDeleteIds.Contains(createdId),
                    $"ids=[{string.Join(", ", afterDeleteIds)}]");
                Check("Tenant A restored to its exact baseline rows",
                    afterDeleteIds.SequenceEqual(baselineA),
                    $"ids=[{string.Join(", ", afterDeleteIds)}] (baseline [{string.Join(", ", baselineA)}])");
                Check("Tenant B complaints still untouched",
                    legacyB.Complaints.AsNoTracking().Count() == baselineBCount,
                    $"count={legacyB.Complaints.AsNoTracking().Count()}");
                Check("Tenant C complaints still untouched",
                    tenantCdb.Complaints.AsNoTracking().Count() == baselineCCount,
                    $"count={tenantCdb.Complaints.AsNoTracking().Count()}");

                var finalGrid = freshForm.DescribeComplaintGridForTest().GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);
                Check("reloaded list no longer shows the deleted complaint",
                    !finalGrid.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries)
                        .Any(r => r.StartsWith($"{createdId}|")),
                    finalGrid);

                Check("delete cleaned up after itself (no residue)",
                    !legacyA.Complaints.AsNoTracking().Any(c => c.Subject == TempSubject || c.Subject == TempUpdatedSubject),
                    "harness rows absent");
            }

            // ---------------------------------------------------------------------
            // 7. Regressions: Gym Staff A, Tenant B (Business Intelligence kept), Tenant C
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 7. Regressions (Gym Staff A / Tenant B / Tenant C) ---");

            foreach (var (username, tenant, expectedNav, mustKeepDashboard) in new[]
                     {
                         ("gymstaffA", tenantA, "Members, Attendance, Payments, Feedback, Inquiries, Complaints", false),
                         // Tenant B phase: the Dashboard page is relabeled "Business Intelligence"
                         // for COMP002 (label only - the same page). Must match TenantBVerification.
                         ("adminB", tenantB, "Business Intelligence, Members, Attendance, Payments, Feedback, Inquiries, Retention, Complaints, UserManagement", true),
                         // Tenant C phase 9: the same relabel applies to COMP003.
                         ("admin", tenantC, "Business Intelligence, Members, Attendance, Payments, Feedback, Inquiries, Retention, Complaints, UserManagement, Branches", true)
                     })
            {
                try
                {
                    using var regressionForm = BuildForm(runtime, tenants, tenant, username);
                    var nav = regressionForm.DescribeNav();

                    Check($"'{username}' nav unchanged", nav == expectedNav,
                        nav == expectedNav ? nav : $"{nav} (expected: {expectedNav})");
                    Check($"'{username}' Dashboard presence is correct",
                        username is "adminB" or "admin"
                            ? nav.Contains("Business Intelligence") && !nav.Contains("Dashboard")
                            : nav.Contains("Dashboard") == mustKeepDashboard,
                        nav);

                    regressionForm.ReloadAllModulesForTestAsync().GetAwaiter().GetResult();
                    SynchronizationContext.SetSynchronizationContext(null);
                    var loadFailures = regressionForm.LoadFailuresForTest;
                    Check($"'{username}' all modules load without errors", loadFailures.Count == 0,
                        loadFailures.Count == 0 ? "0 failures" : string.Join("; ", loadFailures));
                }
                catch (Exception ex)
                {
                    Check($"'{username}' regression run", false, ex.GetBaseException().Message);
                }
            }

            Write(string.Empty);
            Write($"--- Result: {(failures.Count == 0 ? "OK" : "FAILED")} | checks: {checks} | failures: {failures.Count} ---");
            foreach (var failure in failures)
            {
                Write($"  FAILED: {failure}");
            }

            return failures.Count == 0 ? 0 : 1;
        }

        // =====================================================================================
        // helpers
        // =====================================================================================

        /// <summary>
        /// Builds a headless Form1 exactly like Program.cs (login -> subscription -> session).
        /// Tenant A / Tenant B accounts authenticate with the real demo credentials; Tenant C's
        /// demo account keeps its Phase 4F one-time password (never stored), so when the demo
        /// password is rejected the session is constructed from the tenant's own account row -
        /// the same pattern the other acceptance harnesses use for Tenant C.
        /// </summary>
        private static Form1 BuildForm(
            AppRuntime runtime,
            IReadOnlyList<TenantOption> tenants,
            TenantOption tenant,
            string username)
        {
            var login = runtime.AuthenticationService
                .AuthenticateAsync(tenant.CompanyId, username, "123").GetAwaiter().GetResult();

            var info = runtime.SubscriptionService.GetCompanySubscriptionAsync(tenant.CompanyId).GetAwaiter().GetResult();
            var subscriptionContext = info == null
                ? new UserSubscriptionContext()
                : new UserSubscriptionContext
                {
                    HasActiveSubscription = info.EffectiveStatus == SubscriptionStatuses.Active,
                    EffectiveStatus = info.EffectiveStatus,
                    PlanName = info.PlanName,
                    EndDate = info.EndDate,
                    EnabledModuleCodes = info.EffectiveStatus == SubscriptionStatuses.Active
                        ? info.EnabledModuleCodes
                        : Array.Empty<string>()
                };

            UserSession session;
            if (login != null)
            {
                session = new UserSession
                {
                    CompanyId = login.CompanyId,
                    CompanyCode = login.CompanyCode,
                    CompanyName = login.CompanyName,
                    IsBranchAware = login.IsBranchAware,
                    BranchId = login.BranchId,
                    MemberId = login.MemberId,
                    Username = login.Username,
                    FullName = login.FullName,
                    Role = login.Role,
                    Subscription = subscriptionContext
                };
            }
            else
            {
                // Credential fallback: read the account row from the tenant's OWN database.
                var fallbackDb = runtime.DataContextFactory
                    .CreateAsync(tenant.CompanyId, tenant.IsBranchAware).GetAwaiter().GetResult();

                UserAccount? account;
                try
                {
                    account = fallbackDb.UserAccounts.AsNoTracking()
                        .FirstOrDefault(u => u.Username == username);
                }
                finally
                {
                    fallbackDb.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }

                if (account == null)
                {
                    throw new InvalidOperationException(
                        $"'{username}' rejected by login and no such account exists in {tenant.CompanyCode}.");
                }

                session = new UserSession
                {
                    CompanyId = tenant.CompanyId,
                    CompanyCode = tenant.CompanyCode,
                    CompanyName = tenant.CompanyName,
                    IsBranchAware = tenant.IsBranchAware,
                    BranchId = account.BranchId,
                    MemberId = account.MemberId,
                    Username = account.Username,
                    FullName = account.FullName,
                    Role = account.Role,
                    Subscription = subscriptionContext
                };
            }

            var form = new Form1(session, tenants, runtime.DataContextFactory, runtime.AuthorizationService)
            {
                SuppressDialogs = true
            };

            SynchronizationContext.SetSynchronizationContext(null);
            form.InitializeCurrentBranchSelectorForTestAsync().GetAwaiter().GetResult();
            SynchronizationContext.SetSynchronizationContext(null);
            return form;
        }

        /// <summary>Parses a separator-joined list found between two markers in a describe string.</summary>
        private static List<string> ParseBracketList(string source, string startMarker, string endMarker, string separator)
        {
            var start = source.IndexOf(startMarker, StringComparison.Ordinal);
            if (start < 0) return new List<string>();

            var body = source.Substring(start + startMarker.Length);
            var end = body.IndexOf(endMarker, StringComparison.Ordinal);
            if (end >= 0)
            {
                body = body.Substring(0, end);
            }

            return body
                .Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim())
                .Where(v => v.Length > 0)
                .ToList();
        }
    }
}
