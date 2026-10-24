using AlphaElite.domain.entities;
using AlphaElite.infrastructure.data;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlphaElite.winforms
{
    /// <summary>
    /// ONE-OFF, GUARDED maintenance operation for the demo Master DB (MASTER DB ONLY).
    ///
    ///   --master-clean-plan-version-artifacts [--dry-run]
    ///
    /// Removes plan-configuration versions that were created ONLY by automated verification runs
    /// (never by an authenticated Super Admin), so the demo database starts from the legitimate
    /// original configuration. Nothing is deleted blindly: every decision is printed with the row
    /// values it was based on, and a plan is left COMPLETELY untouched if ANY of its versions above
    /// Version 1 is not recognisable as an artifact.
    ///
    /// Artifact definition (deliberately narrow):
    ///   * VersionNumber &gt; 1 (Version 1 - the original configuration - is NEVER removed), AND
    ///   * ChangedBy is an automated-verification identity ("system", "phase4-verify" or
    ///     "phase4-verify-cleanup"). A real Super Admin change is attributed to an authenticated
    ///     identity, so it can never match, AND
    ///   * ChangeType is a material plan-configuration change (price / billing cycle / terms /
    ///     configuration / module add / module remove).
    ///
    /// Afterwards the affected plan's live row is restored from its own Version 1 snapshot (price,
    /// terms, billing cycle, name, description, activation) and Version 1 becomes that plan's
    /// single current version. Live plan-to-module mappings are never touched. Company
    /// subscriptions, requests and history are never deleted. A subscription version link that is
    /// missing, pointed at a removed version, or pointed at a version of a plan the subscription
    /// no longer uses (a stale pointer the verification runs left behind) is re-pointed at the
    /// subscription's own plan's Version 1.
    /// </summary>
    internal static class PlanVersionArtifactCleanup
    {
        /// <summary>
        /// Automated-verification identities. A real Super Admin change carries an authenticated
        /// identity (e.g. "superadmin"), so it can never appear here.
        /// </summary>
        private static readonly string[] VerificationActors = { "system", string.Empty, "phase4-verify", "phase4-verify-cleanup" };

        /// <summary>Material plan-configuration change types. OriginalVersion/PlanCreated are excluded on purpose.</summary>
        private static readonly string[] ConfigurationChangeTypes =
        {
            PlanVersionChangeTypes.PriceChanged,
            PlanVersionChangeTypes.BillingCycleChanged,
            PlanVersionChangeTypes.TermsChanged,
            PlanVersionChangeTypes.ConfigurationChanged,
            PlanVersionChangeTypes.ModuleAdded,
            PlanVersionChangeTypes.ModuleRemoved
        };

        public static int Run(bool dryRun)
        {
            var failures = new List<string>();

            void Write(string text) => Console.WriteLine(text);

            void Check(string name, bool ok, string detail)
            {
                Write($"  [{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
                if (!ok)
                {
                    failures.Add($"{name}: {detail}");
                }
            }

            try
            {
                var runtime = AppRuntime.Create();
                var context = runtime.MasterDb;

                var masterDatabase = context.Database.GetDbConnection().Database;
                Write("Operation      : clean-plan-version-artifacts" + (dryRun ? " (DRY RUN - nothing is written)" : string.Empty));
                Write($"Target database: {masterDatabase} (ConnectionStrings:MasterErp)");

                // Same guard as every other master deployment operation.
                if (!masterDatabase.Contains("67598"))
                {
                    Write($"[ABORT] Refusing to run: the master connection does not point at db67598 (got '{masterDatabase}').");
                    return 1;
                }

                var plans = context.SubscriptionPlans.AsNoTracking().OrderBy(p => p.SubscriptionPlanId).ToList();
                var versions = context.SubscriptionPlanVersions.AsNoTracking()
                    .OrderBy(v => v.SubscriptionPlanId).ThenBy(v => v.VersionNumber).ToList();
                var versionModules = context.SubscriptionPlanVersionModules.AsNoTracking().ToList();
                var planById = plans.ToDictionary(p => p.SubscriptionPlanId, p => p);
                var planCodeById = plans.ToDictionary(p => p.SubscriptionPlanId, p => p.Code);
                var subscriptionsBefore = context.CompanySubscriptions.AsNoTracking().Count();
                var requestsBefore = context.SubscriptionChangeRequests.AsNoTracking().Count();
                var historyBefore = context.SubscriptionHistories.AsNoTracking().Count();

                Write(string.Empty);
                Write("--- Current plan versions (evidence) ---");
                foreach (var version in versions)
                {
                    Write($"  {version.PlanCode} v{version.VersionNumber}{(version.IsCurrent ? " (current)" : string.Empty)} | " +
                          $"{version.ChangeType} | changed {version.ChangedAt:yyyy-MM-dd HH:mm:ss} by '{version.ChangedBy}' | " +
                          $"price {version.Price:0.00} / {version.BillingCycle} | terms {version.TermsAndAgreement.Length} chars | " +
                          $"effective {version.EffectiveDate:yyyy-MM-dd} | " +
                          $"{versionModules.Count(m => m.SubscriptionPlanVersionId == version.SubscriptionPlanVersionId)} modules | {version.ChangeSummary}");
                }

                // ---- Classification ----
                var artifacts = new List<SubscriptionPlanVersion>();
                var protectedPlans = new List<string>();

                Write(string.Empty);
                Write("--- Classification (artifact = VersionNumber > 1 + automated-verification actor + material config change) ---");
                foreach (var group in versions.GroupBy(v => v.SubscriptionPlanId))
                {
                    var plan = planById[group.Key];
                    var aboveVersionOne = group.Where(v => v.VersionNumber > 1).ToList();
                    if (aboveVersionOne.Count == 0)
                    {
                        Write($"  {plan.Code}: only Version 1 - nothing to clean.");
                        continue;
                    }

                    var legitimate = aboveVersionOne.Where(v => !IsArtifact(v)).ToList();
                    if (legitimate.Count > 0)
                    {
                        protectedPlans.Add(plan.Code);
                        Write($"  {plan.Code}: LEFT UNTOUCHED - {legitimate.Count} version(s) above Version 1 are not verification artifacts " +
                              $"({string.Join(", ", legitimate.Select(v => $"v{v.VersionNumber} by '{v.ChangedBy}' ({v.ChangeType})"))}). " +
                              "Legitimate historical versions are never removed.");
                        continue;
                    }

                    foreach (var version in aboveVersionOne)
                    {
                        artifacts.Add(version);
                        Write($"  {plan.Code}: ARTIFACT v{version.VersionNumber} | {version.ChangeType} | by '{version.ChangedBy}' | " +
                              $"price {version.Price:0.00} | {version.ChangedAt:yyyy-MM-dd HH:mm:ss}");
                    }
                }

                if (artifacts.Count == 0)
                {
                    Write("  (none - the master database holds no verification-generated plan versions)");
                }

                // ---- Execution ----
                var affectedPlanIds = artifacts.Select(v => v.SubscriptionPlanId).Distinct().ToList();
                var artifactIds = artifacts.Select(v => v.SubscriptionPlanVersionId).ToHashSet();
                var versionOneByPlan = versions.Where(v => v.VersionNumber == 1)
                    .GroupBy(v => v.SubscriptionPlanId)
                    .ToDictionary(g => g.Key, g => g.OrderBy(v => v.SubscriptionPlanVersionId).First());

                foreach (var planId in affectedPlanIds.Where(id => !versionOneByPlan.ContainsKey(id)))
                {
                    Write($"  [ABORT] Plan {planCodeById[planId]} has artifact versions but no Version 1 - refusing to remove anything for it.");
                    failures.Add($"plan {planCodeById[planId]}: no Version 1 to restore from");
                }

                var removable = artifacts.Where(v => versionOneByPlan.ContainsKey(v.SubscriptionPlanId)).ToList();
                var removableIds = removable.Select(v => v.SubscriptionPlanVersionId).ToHashSet();
                // Plans that can actually be cleaned (they still have their Version 1 to restore from).
                var cleanedPlanIds = removable.Select(v => v.SubscriptionPlanId).Distinct().ToList();
                var removedModuleRows = versionModules.Count(m => removableIds.Contains(m.SubscriptionPlanVersionId));

                Write(string.Empty);
                Write("--- Cleanup plan ---");
                Write($"Verification-only versions to remove : {removable.Count}");
                Write($"Version-module snapshot rows to remove: {removedModuleRows}");
                Write($"Plans restored from their Version 1  : {(affectedPlanIds.Count == 0 ? "(none)" : string.Join(", ", affectedPlanIds.Select(id => planCodeById[id])))}");
                if (protectedPlans.Count > 0)
                {
                    Write($"Plans left untouched (legitimate history): {string.Join(", ", protectedPlans)}");
                }

                if (dryRun)
                {
                    Write("  DRY RUN: no write performed.");
                }
                else if (removable.Count == 0)
                {
                    Write("  Nothing to do.");
                }
                else
                {
                    // Track everything up front so EF can order the FK updates without a conflict.
                    var trackedVersions = context.SubscriptionPlanVersions.ToList();
                    var trackedModuleRows = context.SubscriptionPlanVersionModules.ToList();
                    var trackedSubscriptions = context.CompanySubscriptions.ToList();
                    var utcNow = DateTime.UtcNow;

                    foreach (var moduleRow in trackedModuleRows.Where(m => removableIds.Contains(m.SubscriptionPlanVersionId)))
                    {
                        context.SubscriptionPlanVersionModules.Remove(moduleRow);
                    }

                    foreach (var version in trackedVersions.Where(v => removableIds.Contains(v.SubscriptionPlanVersionId)))
                    {
                        context.SubscriptionPlanVersions.Remove(version);
                    }

                    foreach (var planId in removable.Select(v => v.SubscriptionPlanId).Distinct())
                    {
                        var versionOneId = versionOneByPlan[planId].SubscriptionPlanVersionId;
                        var versionOne = trackedVersions.First(v => v.SubscriptionPlanVersionId == versionOneId);

                        foreach (var other in trackedVersions.Where(v => v.SubscriptionPlanId == planId
                                     && v.SubscriptionPlanVersionId != versionOneId && v.IsCurrent))
                        {
                            other.IsCurrent = false;
                            other.UpdatedAt = utcNow;
                        }

                        versionOne.IsCurrent = true;
                        versionOne.UpdatedAt = utcNow;

                        // The live plan row returns to exactly what Version 1 recorded (a DIRECT
                        // write - no new version is published for a rollback of fake history).
                        var plan = context.SubscriptionPlans.First(p => p.SubscriptionPlanId == planId);
                        plan.Name = versionOne.PlanName;
                        plan.Description = versionOne.Description;
                        plan.Price = versionOne.Price;
                        plan.BillingCycle = versionOne.BillingCycle;
                        plan.TermsAndAgreement = versionOne.TermsAndAgreement;
                        plan.IsActive = versionOne.PlanIsActive;
                        plan.UpdatedAt = utcNow;
                    }

                    // Subscriptions are never deleted. A link that is missing, pointed at a removed
                    // version, or pointed at a version of a DIFFERENT plan (the verification runs
                    // downgraded a tenant and restored only its plan id, leaving a stale pointer)
                    // is re-pointed at the subscription's own plan's Version 1.
                    var relinked = new List<string>();
                    foreach (var subscription in trackedSubscriptions)
                    {
                        if (!versionOneByPlan.TryGetValue(subscription.SubscriptionPlanId, out var versionOne))
                        {
                            continue;
                        }

                        var link = subscription.SubscriptionPlanVersionId;
                        var stale = link == null
                            || removableIds.Contains(link.Value)
                            || trackedVersions.Any(v => v.SubscriptionPlanVersionId == link.Value
                                                        && v.SubscriptionPlanId != subscription.SubscriptionPlanId);

                        if (!stale || link == versionOne.SubscriptionPlanVersionId)
                        {
                            continue;
                        }

                        relinked.Add($"#{subscription.CompanySubscriptionId}: {link?.ToString() ?? "NULL"} -> {versionOne.SubscriptionPlanVersionId} (V1)");
                        subscription.SubscriptionPlanVersionId = versionOne.SubscriptionPlanVersionId;
                    }

                    context.SaveChanges();
                    Write("  Cleanup committed.");
                    Write($"  Subscription version links re-pointed: {(relinked.Count == 0 ? "(none)" : string.Join("; ", relinked))}");
                }

                // ---- Post-state verification (always runs, including after a dry run) ----
                context.ChangeTracker.Clear();
                var afterPlans = context.SubscriptionPlans.AsNoTracking().OrderBy(p => p.SubscriptionPlanId).ToList();
                var afterVersions = context.SubscriptionPlanVersions.AsNoTracking().ToList();
                var afterModules = context.SubscriptionPlanVersionModules.AsNoTracking().ToList();

                Write(string.Empty);
                Write("--- After ---");
                foreach (var version in afterVersions.OrderBy(v => v.SubscriptionPlanId).ThenBy(v => v.VersionNumber))
                {
                    Write($"  {version.PlanCode} v{version.VersionNumber}{(version.IsCurrent ? " (current)" : string.Empty)} | {version.ChangeType} | " +
                          $"by '{version.ChangedBy}' | price {version.Price:0.00} / {version.BillingCycle} | terms {version.TermsAndAgreement.Length} chars | " +
                          $"{afterModules.Count(m => m.SubscriptionPlanVersionId == version.SubscriptionPlanVersionId)} modules");
                }

                var subscriptionRows = context.CompanySubscriptions.AsNoTracking().ToList();
                Write($"plans={afterPlans.Count} versions={afterVersions.Count} versionModules={afterModules.Count} " +
                      $"subscriptions={subscriptionRows.Count} linkedSubscriptions={subscriptionRows.Count(s => s.SubscriptionPlanVersionId != null)} " +
                      $"requests={context.SubscriptionChangeRequests.AsNoTracking().Count()} " +
                      $"history={context.SubscriptionHistories.AsNoTracking().Count()}");

                Write(string.Empty);
                Write("--- Integrity ---");
                Check("exactly 3 plans (BASIC, PROFESSIONAL, ENTERPRISE)", afterPlans.Count == 3, $"plans={afterPlans.Count}");
                Check("every plan has EXACTLY ONE current version",
                    afterPlans.All(p => afterVersions.Count(v => v.SubscriptionPlanId == p.SubscriptionPlanId && v.IsCurrent) == 1),
                    string.Join(", ", afterPlans.Select(p => $"{p.Code}={afterVersions.Count(v => v.SubscriptionPlanId == p.SubscriptionPlanId && v.IsCurrent)}")));
                Check("every plan has a Version 1 (original configuration preserved)",
                    afterPlans.All(p => afterVersions.Any(v => v.SubscriptionPlanId == p.SubscriptionPlanId && v.VersionNumber == 1)),
                    string.Join(", ", afterPlans.Select(p => $"{p.Code}={(afterVersions.Any(v => v.SubscriptionPlanId == p.SubscriptionPlanId && v.VersionNumber == 1) ? "V1 ok" : "MISSING")}")));
                Check("version numbers are sequential per plan (1..n)",
                    afterPlans.All(p =>
                    {
                        var numbers = afterVersions.Where(v => v.SubscriptionPlanId == p.SubscriptionPlanId)
                            .OrderBy(v => v.VersionNumber).Select(v => v.VersionNumber).ToList();
                        return numbers.SequenceEqual(Enumerable.Range(1, numbers.Count));
                    }),
                    "1..n per plan");
                Check("no verification version remains for a cleaned plan",
                    cleanedPlanIds.All(id => !afterVersions.Any(v => v.SubscriptionPlanId == id && v.VersionNumber > 1)),
                    cleanedPlanIds.Count == 0
                        ? "(no plan was affected)"
                        : string.Join(", ", cleanedPlanIds.Select(id =>
                            $"{planCodeById[id]}=v{(afterVersions.Any(v => v.SubscriptionPlanId == id) ? afterVersions.Where(v => v.SubscriptionPlanId == id).Max(v => v.VersionNumber) : 0)}")));
                Check("no duplicate (plan, version) pairs",
                    !afterVersions.GroupBy(v => new { v.SubscriptionPlanId, v.VersionNumber }).Any(g => g.Count() > 1),
                    $"rows={afterVersions.Count}");
                Check("no duplicate (version, module) snapshot rows",
                    !afterModules.GroupBy(m => new { m.SubscriptionPlanVersionId, m.ModuleId }).Any(g => g.Count() > 1),
                    $"rows={afterModules.Count}");
                Check("no orphan version-module snapshot rows",
                    !afterModules.Any(m => !afterVersions.Any(v => v.SubscriptionPlanVersionId == m.SubscriptionPlanVersionId)),
                    $"rows={afterModules.Count}");
                Check("the current version of every cleaned plan matches its live plan row (price/billing/terms)",
                    cleanedPlanIds.All(id =>
                    {
                        var plan = afterPlans.First(p => p.SubscriptionPlanId == id);
                        var current = afterVersions.First(v => v.SubscriptionPlanId == id && v.IsCurrent);
                        return current.Price == plan.Price
                            && string.Equals(current.BillingCycle, plan.BillingCycle, StringComparison.Ordinal)
                            && string.Equals(current.TermsAndAgreement, plan.TermsAndAgreement, StringComparison.Ordinal);
                    }),
                    "current snapshot == live plan row");
                Check("every cleaned plan's current module set matches Version 1's snapshot (mappings untouched)",
                    cleanedPlanIds.All(id =>
                    {
                        var versionOneCodes = afterModules.Where(m => m.SubscriptionPlanVersionId == versionOneByPlan[id].SubscriptionPlanVersionId)
                            .Select(m => m.ModuleCode).OrderBy(c => c).ToList();
                        var liveCodes = context.PlanModules.AsNoTracking()
                            .Where(pm => pm.SubscriptionPlanId == id && pm.IsActive && pm.Module.IsActive)
                            .Join(context.SoftwareModules.AsNoTracking().Where(m => m.IsActive),
                                pm => pm.ModuleId, m => m.ModuleId, (pm, m) => m.Code)
                            .ToList();
                        return versionOneCodes.Count == liveCodes.Count && versionOneCodes.All(c => liveCodes.Contains(c, StringComparer.Ordinal));
                    }),
                    cleanedPlanIds.Count == 0
                        ? "(no plan was affected)"
                        : string.Join(", ", cleanedPlanIds.Select(id =>
                            $"{planCodeById[id]}={afterModules.Count(m => m.SubscriptionPlanVersionId == versionOneByPlan[id].SubscriptionPlanVersionId)} modules")));
                Check("no CompanySubscription row was deleted",
                    subscriptionRows.Count == subscriptionsBefore,
                    $"subscriptions={subscriptionRows.Count}/{subscriptionsBefore}");
                Check("every linked subscription points at its OWN plan's CURRENT version",
                    subscriptionRows.Where(s => s.SubscriptionPlanVersionId != null).All(s => afterVersions.Any(v =>
                        v.SubscriptionPlanVersionId == s.SubscriptionPlanVersionId
                        && v.SubscriptionPlanId == s.SubscriptionPlanId
                        && v.IsCurrent)),
                    "links=" + string.Join(", ", subscriptionRows.Select(s => $"#{s.CompanySubscriptionId}={s.SubscriptionPlanVersionId?.ToString() ?? "null"}")));
                Check("no pending subscription requests",
                    context.SubscriptionChangeRequests.AsNoTracking().Count(r => r.Status == SubscriptionChangeRequestStatuses.Pending) == 0,
                    $"pending={context.SubscriptionChangeRequests.AsNoTracking().Count(r => r.Status == SubscriptionChangeRequestStatuses.Pending)}");
                Check("requests and history rows untouched",
                    context.SubscriptionChangeRequests.AsNoTracking().Count() == requestsBefore
                    && context.SubscriptionHistories.AsNoTracking().Count() == historyBefore,
                    $"requests={context.SubscriptionChangeRequests.AsNoTracking().Count()}/{requestsBefore} history={context.SubscriptionHistories.AsNoTracking().Count()}/{historyBefore}");

                Write(string.Empty);
                Write($"--- Result: {(failures.Count == 0 ? "OK" : "FAILED")} | failures: {failures.Count} ---");
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

        /// <summary>True when a version can only have been produced by an automated verification run.</summary>
        private static bool IsArtifact(SubscriptionPlanVersion version) =>
            version.VersionNumber > 1
            && VerificationActors.Contains(version.ChangedBy ?? string.Empty, StringComparer.Ordinal)
            && ConfigurationChangeTypes.Contains(version.ChangeType ?? string.Empty, StringComparer.Ordinal);
    }
}
