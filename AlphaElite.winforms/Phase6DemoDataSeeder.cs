using AlphaElite.domain.entities;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 6 demo data (explicitly user-approved): seeds a small, clearly-bounded set of
    /// demo rows into the branch-aware tenant so the exam demo shows live activity:
    ///   - 2 subscriptions (one per branch)
    ///   - exam-week attendance check-ins (idempotent per member/day)
    ///   - 2 complaints (one per branch, varied statuses)
    /// Rules enforced here: migrated historical rows are never modified or deleted;
    /// new rows are tagged "(demo)" wherever the schema has a free-text column;
    /// the run is idempotent (re-running adds nothing twice).
    /// </summary>
    internal static class Phase6DemoDataSeeder
    {
        public static async Task<int> RunAsync()
        {
            var failures = new List<string>();
            void Write(string text) => Console.WriteLine(text);

            try
            {
                var runtime = AppRuntime.Create();
                var tenants = await runtime.AuthenticationService.GetAvailableTenantsAsync();
                var tenantC = tenants.FirstOrDefault(t => t.IsBranchAware);

                if (tenantC == null)
                {
                    Write("Aborted: no branch-aware tenant (Tenant C) is configured.");
                    return 1;
                }

                using var db = await runtime.DataContextFactory.CreateAsync(tenantC.CompanyId, true);

                Write("======================================================================");
                Write("PHASE 6 DEMO DATA SEEDING (subscriptions / attendance / complaints)");
                Write("======================================================================");
                Write($"Target: {tenantC.CompanyCode} / {tenantC.CompanyName} (CompanyId {tenantC.CompanyId})");
                Write(string.Empty);

                var branches = db.Branches.AsNoTracking().OrderBy(b => b.BranchId).ToList();
                if (branches.Count < 2)
                {
                    Write("  [FAIL] expected two branches (BR-A / BR-B) on the branch-aware tenant.");
                    return 1;
                }

                var members = db.Members.AsNoTracking().OrderBy(m => m.MemberId).ToList();
                var branchA = members.FirstOrDefault(m => m.BranchId == branches[0].BranchId);
                var branchB = members.FirstOrDefault(m => m.BranchId == branches[1].BranchId);

                if (branchA == null || branchB == null)
                {
                    Write("  [FAIL] could not find at least one member per branch - aborting.");
                    return 1;
                }

                var created = 0;

                // ------------------------------------------------------------------
                // 1. Subscriptions: one per branch, active, 12/1-month terms.
                //    Member-visible status text comes from EndDate, no schema change.
                // ------------------------------------------------------------------
                if (!db.Subscriptions.Any(s => s.PlanName.Contains(DemoTag)))
                {
                    var today = DateTime.Today;
                    db.Subscriptions.AddRange(new[]
                    {
                        new Subscription
                        {
                            MemberId = branchA.MemberId,
                            PlanName = $"Monthly Gym {DemoTag}",
                            Price = 500m,
                            BillingCycle = "Monthly",
                            StartDate = today,
                            EndDate = today.AddMonths(1),
                            IsActive = true
                        },
                        new Subscription
                        {
                            MemberId = branchB.MemberId,
                            PlanName = $"Annual Gym {DemoTag}",
                            Price = 4800m,
                            BillingCycle = "Annual",
                            StartDate = today,
                            EndDate = today.AddYears(1),
                            IsActive = true
                        }
                    });
                    created += 2;
                    Write("  [OK] 2 demo subscriptions created (one per branch, active).");
                }
                else
                {
                    Write("  [SKIP] demo subscriptions already exist.");
                }

                // ------------------------------------------------------------------
                // 2. Attendance: current-week check-ins for the first member of each
                //    branch, Tuesday/Wednesday/Thursday at 07:30. Idempotent: only
                //    insert where the member has no check-in on that date yet.
                // ------------------------------------------------------------------
                var today2 = DateTime.Today;
                int daysSinceMonday = ((int)today2.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                var monday = today2.AddDays(-daysSinceMonday);
                var attendanceTimes = new[] { 1, 2, 3 }; // Tue, Wed, Thu offsets from Monday
                var attendanceAdded = 0;

                foreach (var member in new[] { branchA, branchB })
                {
                    var existingDays = db.Attendances
                        .Where(a => a.MemberId == member.MemberId)
                        .AsEnumerable()
                        .Select(a => a.CheckInDate.Date)
                        .ToHashSet();

                    foreach (var offset in attendanceTimes)
                    {
                        var day = monday.AddDays(offset);
                        if (day > today2 || existingDays.Contains(day.Date))
                        {
                            continue;
                        }

                        db.Attendances.Add(new Attendance
                        {
                            MemberId = member.MemberId,
                            CheckInDate = day.AddHours(7).AddMinutes(30),
                            BranchId = member.BranchId
                        });
                        attendanceAdded++;
                    }
                }

                if (attendanceAdded > 0)
                {
                    Write($"  [OK] {attendanceAdded} demo check-in(s) added for the current week (Tue-Thu, 07:30).");
                    created += attendanceAdded;
                }
                else
                {
                    Write("  [SKIP] current-week attendance already present for the demo members.");
                }

                // ------------------------------------------------------------------
                // 3. Complaints: one open per branch, submitted by the branch's demo
                //    member so every role has something to work through the workflow.
                // ------------------------------------------------------------------
                if (!db.Complaints.Any(c => c.Subject.Contains(DemoTag)))
                {
                    db.Complaints.AddRange(new[]
                    {
                        new Complaint
                        {
                            MemberId = branchA.MemberId,
                            BranchId = branchA.BranchId,
                            Subject = $"Locker room light {DemoTag}",
                            Description = "One of the locker room lights flickers constantly (demo record for the workflow demo).",
                            Status = "Pending",
                            CreatedAt = DateTime.UtcNow
                        },
                        new Complaint
                        {
                            MemberId = branchB.MemberId,
                            BranchId = branchB.BranchId,
                            Subject = $"Treadmill belt noise {DemoTag}",
                            Description = "Treadmill 3 makes a grinding noise above speed 10 (demo record for the workflow demo).",
                            Status = "Under Review",
                            CreatedAt = DateTime.UtcNow
                        }
                    });
                    created += 2;
                    Write("  [OK] 2 demo complaints created (one open per branch).");
                }
                else
                {
                    Write("  [SKIP] demo complaints already exist.");
                }

                if (created > 0)
                {
                    await db.SaveChangesAsync();
                }

                Write(string.Empty);
                Write($"--- Result: {(failures.Count == 0 ? "OK" : "FAILED")} | new rows: {created} ---");
                Write("Migrated historical rows were not modified.");
                return failures.Count == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Write($"  [FAIL] {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        private const string DemoTag = "(demo)";
    }
}
