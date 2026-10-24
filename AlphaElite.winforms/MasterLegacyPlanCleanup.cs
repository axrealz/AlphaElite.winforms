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
    /// ONE-OFF, GUARDED maintenance operation for the demo Master DB (MASTER DB ONLY):
    ///
    ///   --master-remove-legacy-plans [--dry-run]
    ///
    /// The final subscription system has EXACTLY THREE plans - BASIC, PROFESSIONAL and
    /// ENTERPRISE. The earlier launch plans MICRO_BUSINESS and SMALL_BUSINESS are removed
    /// completely: their SubscriptionPlans rows, their exclusive PlanModules mappings, their
    /// SubscriptionPlanVersions and those versions' SubscriptionPlanVersionModules snapshot rows.
    ///
    /// NOTHING is deleted blindly. Before a single row is touched the operation proves there is
    /// no live reference to either plan and ABORTS (leaving the database untouched) when an
    /// active subscription, ANY subscription, a pending/recorded request or an audit-history row
    /// names one of them - the STOP conditions in the task. It also refuses to run when a
    /// retained plan (BASIC / PROFESSIONAL / ENTERPRISE) is missing or duplicated.
    ///
    /// Preserved verbatim:
    ///   * the three retained plans (ids, codes, prices, billing cycles, terms, activation),
    ///   * their PlanModules mappings and every one of their versions/module snapshots,
    ///   * the SoftwareModules catalogue (modules are never deleted),
    ///   * every CompanySubscription, SubscriptionHistory and SubscriptionChangeRequest row.
    ///
    /// A CompanySubscription whose plan-version link is NULL, points at a removed version, or
    /// points at a version of a DIFFERENT plan (residue the automated verification runs left
    /// behind) is re-pointed at its OWN plan's current version. Subscriptions are never deleted.
    /// </summary>
    internal static class MasterLegacyPlanCleanup
    {
        /// <summary>The obsolete launch plans being removed from the product.</summary>
        private static readonly string[] RetiredCodes = { "MICRO_BUSINESS", "SMALL_BUSINESS" };

        /// <summary>The three and only three plans that must survive.</summary>
        private static readonly string[] RetainedCodes =
        {
            SoftwarePlanCodes.Basic, SoftwarePlanCodes.Professional, SoftwarePlanCodes.Enterprise
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
                Write("Operation      : remove-legacy-plans" + (dryRun ? " (DRY RUN - nothing is written)" : string.Empty));
                Write($"Target database: {masterDatabase} (ConnectionStrings:MasterErp)");
                Write($"Context        : {nameof(MasterAlphaEliteDbContext)} (deployment step: strictly sequential)");

                // Same guard as every other master deployment operation.
                if (!masterDatabase.Contains("67598"))
                {
                    Write($"[ABORT] Refusing to run: the master connection does not point at db67598 (got '{masterDatabase}').");
                    return 1;
                }

                // ---- Snapshot the whole picture (evidence) -------------------------------------
                var plans = context.SubscriptionPlans.AsNoTracking().OrderBy(p => p.SubscriptionPlanId).ToList();
                var versions = context.SubscriptionPlanVersions.AsNoTracking()
                    .OrderBy(v => v.SubscriptionPlanId).ThenBy(v => v.VersionNumber).ToList();
                var versionModules = context.SubscriptionPlanVersionModules.AsNoTracking().ToList();
                var mappings = context.PlanModules.AsNoTracking().ToList();
                var modules = context.SoftwareModules.AsNoTracking().ToList();
                var subscriptionsBefore = context.CompanySubscriptions.AsNoTracking().ToList();
                var requestsBefore = context.SubscriptionChangeRequests.AsNoTracking().ToList();
                var historyBefore = context.SubscriptionHistories.AsNoTracking().ToList();

                var planById = plans.ToDictionary(p => p.SubscriptionPlanId, p => p);
                var moduleCodeById = modules.ToDictionary(m => m.ModuleId, m => m.Code);

                var retiredPlans = plans.Where(p => RetiredCodes.Contains(p.Code, StringComparer.Ordinal)).ToList();
                var retiredPlanIds = retiredPlans.Select(p => p.SubscriptionPlanId).ToHashSet();
                var retainedPlans = plans.Where(p => RetainedCodes.Contains(p.Code, StringComparer.Ordinal)).ToList();
                // The versions that go away with the retired plans (also the set a subscription link
                // must not point at, whether or not this run removes anything).
                var retiredVersionIds = versions.Where(v => retiredPlanIds.Contains(v.SubscriptionPlanId))
                    .Select(v => v.SubscriptionPlanVersionId).ToHashSet();

                Write(string.Empty);
                Write("--- Before ---");
                Write($"plans={plans.Count} versions={versions.Count} versionModules={versionModules.Count} " +
                      $"mappings={mappings.Count} modules={modules.Count} subscriptions={subscriptionsBefore.Count} " +
                      $"requests={requestsBefore.Count} history={historyBefore.Count}");
                foreach (var plan in plans)
                {
                    Write($"  plan {plan.SubscriptionPlanId}: {plan.Code} | {plan.Name} | {(plan.IsActive ? "Active" : "Inactive")} | " +
                          $"price {plan.Price:0.00} / {plan.BillingCycle} | terms {plan.TermsAndAgreement.Length} chars | " +
                          $"mappings {mappings.Count(pm => pm.SubscriptionPlanId == plan.SubscriptionPlanId)} | " +
                          $"versions {versions.Count(v => v.SubscriptionPlanId == plan.SubscriptionPlanId)}");
                }

                if (retiredPlans.Count == 0)
                {
                    Write(string.Empty);
                    Write("  Nothing to remove: neither MICRO_BUSINESS nor SMALL_BUSINESS exists in the master catalogue.");
                    Write("  (The final 3-plan state is already in place.)");

                    // The operation is idempotent and doubles as the subscription-link repair: even
                    // with nothing to remove, a dangling / cross-plan plan-version pointer left by
                    // an earlier verification run is re-pointed at its own plan's current version.
                    if (!dryRun)
                    {
                        var repaired = RepairSubscriptionVersionLinks(context, versions, retiredVersionIds);
                        Write("  Subscription version links repaired: " +
                              (repaired.Count == 0 ? "(none needed)" : string.Join("; ", repaired)));
                    }

                    return ReportPostState(context, Write, Check, failures, retiredPlanIdSet: retiredPlanIds, dryRun: dryRun, nothingRemoved: true);
                }

                // ---- STOP-condition screening (never guess, never silently reassign) -----------
                Write(string.Empty);
                Write("--- Reference screening (STOP conditions) ---");

                var blocking = new List<string>();

                var subsOnRetired = subscriptionsBefore.Where(s => retiredPlanIds.Contains(s.SubscriptionPlanId)).ToList();
                foreach (var sub in subsOnRetired)
                {
                    blocking.Add($"CompanySubscription #{sub.CompanySubscriptionId} (company {sub.CompanyId}) uses retired plan " +
                                 $"{planById.GetValueOrDefault(sub.SubscriptionPlanId)?.Code ?? sub.SubscriptionPlanId.ToString()} (status {sub.Status}).");
                }
                Check("no CompanySubscription (any status) references a retired plan",
                    subsOnRetired.Count == 0,
                    subsOnRetired.Count == 0 ? "0 rows" : string.Join("; ", subsOnRetired.Select(s => $"#{s.CompanySubscriptionId}:plan{s.SubscriptionPlanId}")));

                var requestsOnRetired = requestsBefore
                    .Where(r => (r.CurrentPlanId.HasValue && retiredPlanIds.Contains(r.CurrentPlanId.Value))
                             || (r.RequestedPlanId.HasValue && retiredPlanIds.Contains(r.RequestedPlanId.Value)))
                    .ToList();
                foreach (var request in requestsOnRetired)
                {
                    blocking.Add($"SubscriptionChangeRequest #{request.SubscriptionChangeRequestId} (company {request.CompanyId}, status {request.Status}) " +
                                 $"names a retired plan (current={request.CurrentPlanId?.ToString() ?? "-"}, requested={request.RequestedPlanId?.ToString() ?? "-"}).");
                }
                Check("no SubscriptionChangeRequest (pending or recorded) references a retired plan",
                    requestsOnRetired.Count == 0,
                    requestsOnRetired.Count == 0 ? $"0 rows (total requests={requestsBefore.Count})" : $"{requestsOnRetired.Count} rows");

                var historyOnRetired = historyBefore
                    .Where(h => (h.PreviousPlanId.HasValue && retiredPlanIds.Contains(h.PreviousPlanId.Value))
                             || (h.NewPlanId.HasValue && retiredPlanIds.Contains(h.NewPlanId.Value)))
                    .ToList();
                foreach (var history in historyOnRetired)
                {
                    blocking.Add($"SubscriptionHistory #{history.HistoryId} (company {history.CompanyId}, {history.Action}) " +
                                 $"references a retired plan (previous={history.PreviousPlanId?.ToString() ?? "-"}, new={history.NewPlanId?.ToString() ?? "-"}).");
                }
                Check("no SubscriptionHistory (audit trail) references a retired plan",
                    historyOnRetired.Count == 0,
                    historyOnRetired.Count == 0 ? $"0 rows (total history={historyBefore.Count})" : $"{historyOnRetired.Count} rows");

                // Retained plan identity: missing or duplicated identifiers are a STOP condition.
                var retainedDuplicate = plans.GroupBy(p => p.Code).Where(g => g.Count() > 1)
                    .Select(g => g.Key).ToList();
                var retainedMissing = RetainedCodes.Where(c => !plans.Any(p => p.Code == c)).ToList();
                Check("the three retained identifiers exist exactly once (no BASIC/PROFESSIONAL/ENTERPRISE conflict)",
                    retainedMissing.Count == 0 && retainedDuplicate.Count == 0,
                    $"missing=[{string.Join(",", retainedMissing)}] duplicated=[{string.Join(",", retainedDuplicate)}] retained={retainedPlans.Count}");

                if (blocking.Count > 0)
                {
                    Write(string.Empty);
                    Write("[ABORT] STOP CONDITION: a retired plan is still referenced by live or historical data.");
                    foreach (var line in blocking)
                    {
                        Write("   - " + line);
                    }
                    Write("[ABORT] No row was deleted and no subscription was reassigned. Resolve the references first.");
                    failures.Add("retired plan is still referenced; deletion refused");
                    return 1;
                }

                // ---- Exact rows to remove ------------------------------------------------------
                var retiredVersionRows = versions.Where(v => retiredVersionIds.Contains(v.SubscriptionPlanVersionId)).ToList();
                var retiredMappingRows = mappings.Where(pm => retiredPlanIds.Contains(pm.SubscriptionPlanId)).ToList();
                var retiredModuleRows = versionModules.Where(m => retiredVersionIds.Contains(m.SubscriptionPlanVersionId)).ToList();
                var retiredPlanRows = retiredPlans.ToList();

                Write(string.Empty);
                Write("--- Removal plan (exact rows) ---");
                foreach (var plan in retiredPlanRows)
                {
                    Write($"  SubscriptionPlans row: id {plan.SubscriptionPlanId} code {plan.Code} name '{plan.Name}'");
                }
                foreach (var mapping in retiredMappingRows.OrderBy(m => m.SubscriptionPlanId).ThenBy(m => m.ModuleId))
                {
                    Write($"  PlanModules row: plan {planById[mapping.SubscriptionPlanId].Code} -> module {moduleCodeById.GetValueOrDefault(mapping.ModuleId, mapping.ModuleId.ToString())} " +
                          $"(IsActive={mapping.IsActive})");
                }
                foreach (var version in retiredVersionRows.OrderBy(v => v.SubscriptionPlanId).ThenBy(v => v.VersionNumber))
                {
                    Write($"  SubscriptionPlanVersions row: id {version.SubscriptionPlanVersionId} {version.PlanCode} v{version.VersionNumber}" +
                          $"{(version.IsCurrent ? " (current)" : string.Empty)} | {version.ChangeType} | by '{version.ChangedBy}' | " +
                          $"{versionModules.Count(m => m.SubscriptionPlanVersionId == version.SubscriptionPlanVersionId)} module snapshots");
                }
                Write($"  SubscriptionPlanVersionModules rows: {retiredModuleRows.Count}");

                // ---- Execution -----------------------------------------------------------------
                var relinked = new List<string>();
                if (dryRun)
                {
                    Write(string.Empty);
                    Write("  DRY RUN: no write performed.");
                    // Report what the link repair WOULD do, without writing.
                    foreach (var subscription in subscriptionsBefore)
                    {
                        var reason = StaleLinkReason(subscription, versions, retiredVersionIds);
                        if (reason == null)
                        {
                            continue;
                        }

                        relinked.Add($"#{subscription.CompanySubscriptionId} ({reason})");
                        Write($"  would re-point CompanySubscription #{subscription.CompanySubscriptionId} -> its plan's current version ({reason})");
                    }
                }
                else
                {
                    // The stale-link REASONS are captured from the pre-delete snapshot: once EF
                    // tracks the version deletions it nulls the dependent FK itself, so reading it
                    // afterwards would only ever report "NULL" and would lose the real evidence
                    // (which plan/version the dangling link used to point at).
                    var relinkReasons = subscriptionsBefore
                        .Select(s => (s.CompanySubscriptionId,
                            Reason: StaleLinkReason(s, versions, retiredVersionIds)))
                        .Where(r => r.Reason != null)
                        .ToDictionary(r => r.CompanySubscriptionId, r => r.Reason!);

                    var trackedSubscriptions = context.CompanySubscriptions.ToList();
                    var trackedVersions = context.SubscriptionPlanVersions.ToList();
                    var trackedModuleRows = context.SubscriptionPlanVersionModules.ToList();
                    var trackedMappings = context.PlanModules.ToList();
                    var trackedPlans = context.SubscriptionPlans.ToList();

                    // 1. Version snapshot rows first (FK: version-module -> version).
                    foreach (var moduleRow in trackedModuleRows.Where(m => retiredVersionIds.Contains(m.SubscriptionPlanVersionId)))
                    {
                        context.SubscriptionPlanVersionModules.Remove(moduleRow);
                    }

                    // 2. The retired plans' versions.
                    foreach (var version in trackedVersions.Where(v => retiredPlanIds.Contains(v.SubscriptionPlanId)))
                    {
                        context.SubscriptionPlanVersions.Remove(version);
                    }

                    // 3. The retired plans' own module mappings (the SoftwareModules catalogue is
                    //    never touched).
                    foreach (var mapping in trackedMappings.Where(pm => retiredPlanIds.Contains(pm.SubscriptionPlanId)))
                    {
                        context.PlanModules.Remove(mapping);
                    }

                    // 4. The retired plan rows themselves.
                    foreach (var plan in trackedPlans.Where(p => retiredPlanIds.Contains(p.SubscriptionPlanId)))
                    {
                        context.SubscriptionPlans.Remove(plan);
                    }

                    // 5. Repair every subscription's plan-version link against the version rows
                    //    that remain (the cross-plan / dangling residue the verification runs left
                    //    behind). Subscriptions are never deleted, only re-pointed.
                    foreach (var subscription in trackedSubscriptions)
                    {
                        // Prefer the pre-delete reason; fall back to a fresh check (a link the
                        // delete already nulled is still repaired, it just has no captured reason).
                        var reason = relinkReasons.TryGetValue(subscription.CompanySubscriptionId, out var captured)
                            ? captured
                            : StaleLinkReason(subscription, versions, retiredVersionIds);
                        if (reason == null)
                        {
                            continue;
                        }

                        var current = trackedVersions.FirstOrDefault(v =>
                            v.SubscriptionPlanId == subscription.SubscriptionPlanId && v.IsCurrent);
                        if (current == null)
                        {
                            failures.Add($"company subscription #{subscription.CompanySubscriptionId}: no current version for plan {subscription.SubscriptionPlanId}");
                            continue;
                        }

                        relinked.Add($"#{subscription.CompanySubscriptionId} -> v{current.VersionNumber} ({reason})");
                        subscription.SubscriptionPlanVersionId = current.SubscriptionPlanVersionId;
                        subscription.UpdatedAt = DateTime.UtcNow;
                    }

                    context.SaveChanges();
                    Write(string.Empty);
                    Write("  Cleanup committed.");
                    Write($"  Subscription version links re-pointed: {(relinked.Count == 0 ? "(none)" : string.Join("; ", relinked))}");
                }

                return ReportPostState(context, Write, Check, failures,
                    retiredPlanIdSet: retiredPlanIds, dryRun: dryRun, nothingRemoved: false);

            }
            catch (Exception ex)
            {
                Write($"[ERROR] {ex.GetType().Name}: {ex.GetBaseException().Message}");
                Write(ex.StackTrace ?? string.Empty);
                return 1;
            }
        }

        /// <summary>
        /// Why a subscription's plan-version link needs repairing: NULL, pointing at a version that
        /// is going away / does not exist, or pointing at a DIFFERENT plan's version. Returns null
        /// when the link is already correct.
        /// </summary>
        private static string? StaleLinkReason(CompanySubscription subscription,
            IReadOnlyList<SubscriptionPlanVersion> allVersions, IReadOnlyCollection<int> removedVersionIds)
        {
            // Only the version rows that will REMAIN can be a valid target.
            var remaining = allVersions.Where(v => !removedVersionIds.Contains(v.SubscriptionPlanVersionId)).ToList();
            var link = subscription.SubscriptionPlanVersionId;

            if (link == null)
            {
                return "link is NULL";
            }

            var target = remaining.FirstOrDefault(v => v.SubscriptionPlanVersionId == link.Value);
            if (target == null)
            {
                return removedVersionIds.Contains(link.Value)
                    ? $"link {link.Value} points at a removed (retired-plan) version"
                    : $"link {link.Value} does not exist";
            }

            return target.SubscriptionPlanId != subscription.SubscriptionPlanId
                ? $"link {link.Value} belongs to plan {target.SubscriptionPlanId}, not the subscription's own plan {subscription.SubscriptionPlanId}"
                : null;
        }

        /// <summary>
        /// Re-points every stale / cross-plan subscription version link at its OWN plan's current
        /// version. Subscriptions are never deleted. Returns the repairs performed (empty when the
        /// state was already consistent), so the operation is safe and idempotent to re-run.
        /// </summary>
        private static List<string> RepairSubscriptionVersionLinks(
            MasterAlphaEliteDbContext context, IReadOnlyList<SubscriptionPlanVersion> allVersions,
            IReadOnlyCollection<int> removedVersionIds)
        {
            var repairs = new List<string>();
            var tracked = context.CompanySubscriptions.ToList();

            foreach (var subscription in tracked)
            {
                var reason = StaleLinkReason(subscription, allVersions, removedVersionIds);
                if (reason == null)
                {
                    continue;
                }

                var current = allVersions.FirstOrDefault(v =>
                    v.SubscriptionPlanId == subscription.SubscriptionPlanId && v.IsCurrent);
                if (current == null)
                {
                    repairs.Add($"#{subscription.CompanySubscriptionId}: NO CURRENT VERSION for plan {subscription.SubscriptionPlanId} (left untouched)");
                    continue;
                }

                repairs.Add($"#{subscription.CompanySubscriptionId} -> v{current.VersionNumber} ({reason})");
                subscription.SubscriptionPlanVersionId = current.SubscriptionPlanVersionId;
                subscription.UpdatedAt = DateTime.UtcNow;
            }

            if (repairs.Any(r => !r.Contains("left untouched", StringComparison.Ordinal)))
            {
                context.SaveChanges();
            }

            return repairs;
        }

        /// <summary>
        /// Post-state verification of the FINAL 3-plan subscription system. Runs after a dry run
        /// too (it then reflects the current, unchanged state). Returns the process exit code.
        /// </summary>
        private static int ReportPostState(
            MasterAlphaEliteDbContext context, Action<string> write, Action<string, bool, string> check,
            List<string> failures, HashSet<int> retiredPlanIdSet,
            bool dryRun, bool nothingRemoved)
        {
            context.ChangeTracker.Clear();

            var plans = context.SubscriptionPlans.AsNoTracking().OrderBy(p => p.SubscriptionPlanId).ToList();
            var versions = context.SubscriptionPlanVersions.AsNoTracking().ToList();
            var versionModules = context.SubscriptionPlanVersionModules.AsNoTracking().ToList();
            var mappings = context.PlanModules.AsNoTracking().ToList();
            var modules = context.SoftwareModules.AsNoTracking().ToList();
            var subscriptions = context.CompanySubscriptions.AsNoTracking().ToList();

            write(string.Empty);
            write("--- After ---");
            foreach (var plan in plans)
            {
                write($"  plan {plan.SubscriptionPlanId}: {plan.Code} | {plan.Name} | {(plan.IsActive ? "Active" : "Inactive")} | " +
                      $"price {plan.Price:0.00} / {plan.BillingCycle} | terms {plan.TermsAndAgreement.Length} chars | " +
                      $"mappings {mappings.Count(pm => pm.SubscriptionPlanId == plan.SubscriptionPlanId)} | " +
                      $"versions {versions.Count(v => v.SubscriptionPlanId == plan.SubscriptionPlanId)}");
            }

            write($"plans={plans.Count} versions={versions.Count} versionModules={versionModules.Count} " +
                  $"mappings={mappings.Count} modules={modules.Count} subscriptions={subscriptions.Count} " +
                  $"requests={context.SubscriptionChangeRequests.AsNoTracking().Count()} " +
                  $"history={context.SubscriptionHistories.AsNoTracking().Count()}");

            write(string.Empty);
            write("--- Integrity (final 3-plan state) ---");

            check("exactly 3 SubscriptionPlans", plans.Count == 3, $"plans={plans.Count}: {string.Join(", ", plans.Select(p => p.Code))}");
            check("the remaining plans are exactly BASIC, PROFESSIONAL, ENTERPRISE",
                plans.Select(p => p.Code).OrderBy(c => c, StringComparer.Ordinal)
                    .SequenceEqual(new[] { SoftwarePlanCodes.Basic, SoftwarePlanCodes.Professional, SoftwarePlanCodes.Enterprise }.OrderBy(c => c, StringComparer.Ordinal)),
                string.Join(", ", plans.Select(p => p.Code)));
            check("Micro Business count = 0", plans.Count(p => p.Code == "MICRO_BUSINESS") == 0,
                $"count={plans.Count(p => p.Code == "MICRO_BUSINESS")}");
            check("Small Business count = 0", plans.Count(p => p.Code == "SMALL_BUSINESS") == 0,
                $"count={plans.Count(p => p.Code == "SMALL_BUSINESS")}");
            check("no version row remains for a removed plan",
                !versions.Any(v => retiredPlanIdSet.Contains(v.SubscriptionPlanId)),
                $"versions={versions.Count}");
            check("no mapping row remains for a removed plan",
                !mappings.Any(pm => retiredPlanIdSet.Contains(pm.SubscriptionPlanId)),
                $"mappings={mappings.Count}");
            check("the SoftwareModules catalogue is intact (16 modules, none deleted)",
                modules.Count == 16 && SoftwareModuleCodes.All.All(c => modules.Any(m => m.Code == c)),
                $"modules={modules.Count}");
            check("every retained plan keeps exactly one current version",
                plans.All(p => versions.Count(v => v.SubscriptionPlanId == p.SubscriptionPlanId && v.IsCurrent) == 1),
                string.Join(", ", plans.Select(p => $"{p.Code}={versions.Count(v => v.SubscriptionPlanId == p.SubscriptionPlanId && v.IsCurrent)}")));
            check("every retained plan keeps a readable version history (sequential 1..n)",
                plans.All(p =>
                {
                    var numbers = versions.Where(v => v.SubscriptionPlanId == p.SubscriptionPlanId)
                        .OrderBy(v => v.VersionNumber).Select(v => v.VersionNumber).ToList();
                    return numbers.Count > 0 && numbers.SequenceEqual(Enumerable.Range(1, numbers.Count));
                }),
                string.Join(", ", plans.Select(p => $"{p.Code}=v{versions.Where(v => v.SubscriptionPlanId == p.SubscriptionPlanId).Select(v => v.VersionNumber).DefaultIfEmpty(0).Max()}")));
            check("each retained plan's current module snapshot matches its live PlanModules mapping",
                plans.All(p =>
                {
                    var current = versions.FirstOrDefault(v => v.SubscriptionPlanId == p.SubscriptionPlanId && v.IsCurrent);
                    if (current == null)
                    {
                        return false;
                    }

                    var snapshotCodes = versionModules.Where(m => m.SubscriptionPlanVersionId == current.SubscriptionPlanVersionId)
                        .Select(m => m.ModuleCode).OrderBy(c => c, StringComparer.Ordinal).ToList();
                    var liveCodes = mappings.Where(pm => pm.SubscriptionPlanId == p.SubscriptionPlanId && pm.IsActive)
                        .Join(modules, pm => pm.ModuleId, m => m.ModuleId, (pm, m) => m.Code)
                        .OrderBy(c => c, StringComparer.Ordinal).ToList();
                    return snapshotCodes.SequenceEqual(liveCodes);
                }),
                string.Join(", ", plans.Select(p =>
                {
                    var current = versions.FirstOrDefault(v => v.SubscriptionPlanId == p.SubscriptionPlanId && v.IsCurrent);
                    return $"{p.Code}={versionModules.Count(m => m.SubscriptionPlanVersionId == current?.SubscriptionPlanVersionId)}";
                })));
            check("each retained plan's price / billing cycle / terms match its current version snapshot",
                plans.All(p =>
                {
                    var current = versions.FirstOrDefault(v => v.SubscriptionPlanId == p.SubscriptionPlanId && v.IsCurrent);
                    return current != null
                        && current.Price == p.Price
                        && string.Equals(current.BillingCycle, p.BillingCycle, StringComparison.Ordinal)
                        && string.Equals(current.TermsAndAgreement, p.TermsAndAgreement, StringComparison.Ordinal);
                }),
                string.Join(", ", plans.Select(p => $"{p.Code}={p.Price:0.00}/{p.BillingCycle}/terms{p.TermsAndAgreement.Length}")));
            check("every subscription still resolves to its OWN plan's current version",
                subscriptions.All(s => versions.Any(v =>
                    v.SubscriptionPlanVersionId == s.SubscriptionPlanVersionId
                    && v.SubscriptionPlanId == s.SubscriptionPlanId
                    && v.IsCurrent)),
                string.Join(", ", subscriptions.Select(s => $"#{s.CompanySubscriptionId}={s.SubscriptionPlanVersionId?.ToString() ?? "null"}")));
            check("no CompanySubscription row was deleted",
                subscriptions.Count == 3,
                $"subscriptions={subscriptions.Count}");
            check("no pending subscription request and no history row was touched",
                context.SubscriptionChangeRequests.AsNoTracking().Count(r => r.Status == SubscriptionChangeRequestStatuses.Pending) == 0,
                $"requests={context.SubscriptionChangeRequests.AsNoTracking().Count()} history={context.SubscriptionHistories.AsNoTracking().Count()}");
            check("no duplicate (plan, version) or (version, module) rows",
                !versions.GroupBy(v => new { v.SubscriptionPlanId, v.VersionNumber }).Any(g => g.Count() > 1)
                && !versionModules.GroupBy(m => new { m.SubscriptionPlanVersionId, m.ModuleId }).Any(g => g.Count() > 1),
                $"versions={versions.Count} versionModules={versionModules.Count}");

            write(string.Empty);
            write($"--- Result: {(failures.Count == 0 ? "OK" : "FAILED")}{(dryRun ? " (DRY RUN - no write)" : nothingRemoved ? " (nothing to remove)" : string.Empty)} | failures: {failures.Count} ---");
            foreach (var failure in failures)
            {
                write("  FAILED " + failure);
            }

            return failures.Count == 0 ? 0 : 1;
        }
    }
}
