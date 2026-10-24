using AlphaElite.infrastructure.data;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Plan-configuration versioning deployment step (MASTER DB ONLY).
    ///
    ///   --master-init-plan-versions : idempotently creates Version 1 for every plan that has no
    ///                                 version yet, from its CURRENT configuration (name, price,
    ///                                 billing cycle, Terms &amp; Agreement, activation flag) and its
    ///                                 CURRENT module set, then links existing company
    ///                                 subscriptions to their plan's current version.
    ///
    /// Guarantees:
    ///   - MASTER DB ONLY: refuses to run against anything but the configured master (db67598).
    ///   - IDEMPOTENT: running it twice creates NO Version 2/3/4, no duplicate module snapshots
    ///     and no duplicate subscriptions/companies/plans. The second run reports
    ///     "versions created: 0".
    ///   - ADDITIVE ONLY: no plan, module, mapping, subscription, company or tenant row is
    ///     modified except the new nullable subscription -> version link (filled only where it
    ///     is still NULL).
    ///   - The ACTIVE plan configuration is never altered just to create Version 1.
    /// </summary>
    internal static class PlanVersionInitializer
    {
        public static int Run()
        {
            var failures = new List<string>();

            void Write(string text) => Console.WriteLine(text);

            try
            {
                var runtime = AppRuntime.Create();
                var context = runtime.MasterDb;

                var masterDatabase = context.Database.GetDbConnection().Database;
                Write("Operation      : init-plan-versions");
                Write($"Target database: {masterDatabase} (ConnectionStrings:MasterErp)");
                Write($"Context        : per-operation {nameof(MasterAlphaEliteDbContext)} (via ISubscriptionService)");

                // Same guard as every other master deployment operation.
                if (!masterDatabase.Contains("67598"))
                {
                    Write($"[ABORT] Refusing to run: the master connection does not point at db67598 (got '{masterDatabase}').");
                    return 1;
                }

                Write(string.Empty);
                Write("--- Before ---");
                Write($"plans={context.SubscriptionPlans.AsNoTracking().Count()} " +
                      $"modules={context.SoftwareModules.AsNoTracking().Count()} " +
                      $"mappings={context.PlanModules.AsNoTracking().Count()} " +
                      $"versions={context.SubscriptionPlanVersions.AsNoTracking().Count()} " +
                      $"versionModules={context.SubscriptionPlanVersionModules.AsNoTracking().Count()} " +
                      $"subscriptions={context.CompanySubscriptions.AsNoTracking().Count()} " +
                      $"linkedSubscriptions={context.CompanySubscriptions.AsNoTracking().Count(s => s.SubscriptionPlanVersionId != null)} " +
                      $"companies={context.Companies.AsNoTracking().Count()} " +
                      $"history={context.SubscriptionHistories.AsNoTracking().Count()} " +
                      $"requests={context.SubscriptionChangeRequests.AsNoTracking().Count()}");

                // Identity for the first version: the introduction of versioning itself, not a
                // subscription change by a person.
                var result = runtime.SubscriptionService
                    .EnsureInitialPlanVersionsAsync("versioning-initializer")
                    .GetAwaiter().GetResult();

                Write(string.Empty);
                Write("--- Initialization ---");
                Write($"Success            : {result.Success}" + (result.Success ? string.Empty : " (" + result.Error + ")"));
                Write($"Plans scanned      : {result.PlansScanned}");
                Write($"Versions created   : {result.VersionsCreated}");
                Write($"Already versioned  : {result.PlansAlreadyVersioned}");
                Write($"Subscriptions linked: {result.SubscriptionsLinked}");
                foreach (var line in result.Detail)
                {
                    Write("  " + line);
                }

                if (!result.Success)
                {
                    failures.Add("initialization reported failure: " + result.Error);
                }

                var versions = context.SubscriptionPlanVersions.AsNoTracking().OrderBy(v => v.SubscriptionPlanId).ThenBy(v => v.VersionNumber).ToList();
                var versionModuleRows = context.SubscriptionPlanVersionModules.AsNoTracking().ToList();

                Write(string.Empty);
                Write("--- Versions after ---");
                foreach (var version in versions)
                {
                    var moduleCodes = versionModuleRows.Where(m => m.SubscriptionPlanVersionId == version.SubscriptionPlanVersionId)
                        .Select(m => m.ModuleCode).OrderBy(c => c).ToList();
                    Write($"  {version.PlanCode} v{version.VersionNumber}{(version.IsCurrent ? " (current)" : string.Empty)} | " +
                          $"{version.ChangeType} | effective {version.EffectiveDate:yyyy-MM-dd} | changed {version.ChangedAt:yyyy-MM-dd HH:mm} by {version.ChangedBy} | " +
                          $"price {version.Price:0.00} / {version.BillingCycle} | terms {version.TermsAndAgreement.Length} chars | " +
                          $"{moduleCodes.Count} modules: {string.Join(",", moduleCodes)}");
                }

                // ---- Integrity checks on the result ----
                void Check(string name, bool ok, string detail)
                {
                    Write($"  [{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
                    if (!ok)
                    {
                        failures.Add($"{name}: {detail}");
                    }
                }

                var planCount = context.SubscriptionPlans.AsNoTracking().Count();
                var planIds = context.SubscriptionPlans.AsNoTracking().Select(p => p.SubscriptionPlanId).ToList();

                Write(string.Empty);
                Write("--- Integrity ---");
                Check("every plan has at least one version",
                    planIds.All(id => versions.Any(v => v.SubscriptionPlanId == id)),
                    $"plans={planCount} plansWithVersions={versions.Select(v => v.SubscriptionPlanId).Distinct().Count()}");
                Check("every plan has EXACTLY ONE current version",
                    planIds.All(id => versions.Count(v => v.SubscriptionPlanId == id && v.IsCurrent) == 1),
                    string.Join(", ", planIds.Select(id => versions.Count(v => v.SubscriptionPlanId == id && v.IsCurrent))));
                Check("version numbers are sequential per plan",
                    planIds.All(id => versions.Where(v => v.SubscriptionPlanId == id)
                        .OrderBy(v => v.VersionNumber).Select(v => v.VersionNumber)
                        .SequenceEqual(Enumerable.Range(1, versions.Count(v => v.SubscriptionPlanId == id)))),
                    "1..n per plan");
                Check("no duplicate (plan, version) pairs",
                    !versions.GroupBy(v => new { v.SubscriptionPlanId, v.VersionNumber }).Any(g => g.Count() > 1),
                    $"rows={versions.Count}");
                Check("no duplicate (version, module) snapshot rows",
                    !versionModuleRows.GroupBy(m => new { m.SubscriptionPlanVersionId, m.ModuleId }).Any(g => g.Count() > 1),
                    $"rows={versionModuleRows.Count}");

                Write(string.Empty);
                Write("--- After ---");
                Write($"plans={context.SubscriptionPlans.AsNoTracking().Count()} " +
                      $"modules={context.SoftwareModules.AsNoTracking().Count()} " +
                      $"mappings={context.PlanModules.AsNoTracking().Count()} " +
                      $"versions={context.SubscriptionPlanVersions.AsNoTracking().Count()} " +
                      $"versionModules={context.SubscriptionPlanVersionModules.AsNoTracking().Count()} " +
                      $"subscriptions={context.CompanySubscriptions.AsNoTracking().Count()} " +
                      $"linkedSubscriptions={context.CompanySubscriptions.AsNoTracking().Count(s => s.SubscriptionPlanVersionId != null)} " +
                      $"companies={context.Companies.AsNoTracking().Count()} " +
                      $"history={context.SubscriptionHistories.AsNoTracking().Count()} " +
                      $"requests={context.SubscriptionChangeRequests.AsNoTracking().Count()}");

                Write(string.Empty);
                Write($"--- Result: {(failures.Count == 0 ? "OK" : "FAILED")} | checks: {versions.Count + 5} | failures: {failures.Count} ---");
                foreach (var failure in failures)
                {
                    Write("  FAILED " + failure);
                }

                return failures.Count == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Write($"[ERROR] {ex.GetType().Name}: {ex.GetBaseException().Message}");
                Write(ex.StackTrace ?? string.Empty);
                return 1;
            }
        }
    }
}
