using AlphaElite.domain.entities;
using AlphaElite.infrastructure.data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlphaElite.infrastructure.services
{
    /// <summary>
    /// One chart of the Super Admin Business Intelligence page: an ordered set of category
    /// labels and one numeric value per category. Every value is a REAL master-database
    /// aggregate (never a hardcoded or fabricated number); a category with no data reports 0.
    /// </summary>
    public sealed class MasterBiSeries
    {
        public string Title { get; init; } = string.Empty;

        /// <summary>What the numbers mean (for the axis caption / tooltip): "subscriptions", "revenue", ...</summary>
        public string Unit { get; init; } = string.Empty;

        public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();

        public IReadOnlyList<decimal> Values { get; init; } = Array.Empty<decimal>();

        public decimal Total => Values.Sum();

        public decimal Max => Values.Count == 0 ? 0m : Values.Max();
    }

    /// <summary>
    /// The complete Super Admin Business Intelligence snapshot: six KPI values and four
    /// subscription-focused charts, all computed from the MASTER database. Display-only data -
    /// there is no export surface anywhere in the UI.
    /// </summary>
    public sealed class MasterBiSnapshot
    {
        public DateTime GeneratedAtUtc { get; init; }

        // ---- KPI cards ----
        public int TotalCompanies { get; init; }
        public int ActiveSubscriptions { get; init; }
        public int PendingSubscriptionRequests { get; init; }
        public int ExpiredSubscriptions { get; init; }
        public decimal TotalSubscriptionRevenue { get; init; }
        public decimal MonthlySubscriptionRevenue { get; init; }

        // ---- Charts ----
        public MasterBiSeries PlanOverview { get; init; } = new();
        public MasterBiSeries RevenueOverTime { get; init; } = new();
        public MasterBiSeries SubscriptionStatus { get; init; } = new();
        public MasterBiSeries PaymentOverview { get; init; } = new();
    }

    /// <summary>
    /// Super Admin Business Intelligence over the MASTER database (the business of the
    /// subscription platform itself - never a tenant's gym data).
    /// </summary>
    public interface IMasterBusinessIntelligenceService
    {
        /// <summary>Computes the whole dashboard in one master read (optionally for a fixed "now").</summary>
        Task<MasterBiSnapshot> GetSnapshotAsync(DateTime? nowUtc = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Per-operation implementation: every call creates its OWN
    /// <see cref="MasterAlphaEliteDbContext"/> from the factory and disposes it when the call
    /// completes, exactly like <see cref="ScopedSubscriptionService"/>. Overlapping BI and
    /// subscription operations are therefore legal by construction - there is no shared master
    /// context to race, and no .Result / .Wait / Thread.Sleep / Task.Run anywhere here.
    ///
    /// Revenue definitions (real data only, no fabrication):
    ///   Total Subscription Revenue  = SUM(CompanySubscription.Price) where PaymentStatus = Paid
    ///   Monthly Subscription Revenue= the same, restricted to subscriptions whose StartDate
    ///                                 falls in the current calendar month
    /// The revenue-over-time chart buckets the SAME paid amounts by StartDate month, so a month
    /// with no paid subscription is reported as 0 rather than invented from a trend.
    /// </summary>
    public sealed class MasterBusinessIntelligenceService : IMasterBusinessIntelligenceService
    {
        private readonly IDbContextFactory<MasterAlphaEliteDbContext> _factory;

        public MasterBusinessIntelligenceService(IDbContextFactory<MasterAlphaEliteDbContext> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public async Task<MasterBiSnapshot> GetSnapshotAsync(DateTime? nowUtc = null, CancellationToken cancellationToken = default)
        {
            await using var context = _factory.CreateDbContext();
            var now = (nowUtc ?? DateTime.UtcNow).Date;

            var companies = await context.Companies.AsNoTracking().ToListAsync(cancellationToken);
            var plans = await context.SubscriptionPlans.AsNoTracking()
                .OrderBy(p => p.SubscriptionPlanId).ToListAsync(cancellationToken);
            var subscriptions = await context.CompanySubscriptions.AsNoTracking().ToListAsync(cancellationToken);
            var pendingRequests = await context.SubscriptionChangeRequests.AsNoTracking()
                .CountAsync(r => r.Status == SubscriptionChangeRequestStatuses.Pending, cancellationToken);

            // ---- KPI cards -------------------------------------------------------------------
            var activeSubscriptions = subscriptions.Count(s => s.Status == SubscriptionStatuses.Active);
            var expiresSubscriptions = subscriptions.Count(s => s.Status == SubscriptionStatuses.Expired);
            var paidSubscriptions = subscriptions
                .Where(s => string.Equals(s.PaymentStatus, PaymentStatuses.Paid, StringComparison.Ordinal))
                .ToList();
            var totalRevenue = paidSubscriptions.Sum(s => s.Price);
            var monthlyRevenue = paidSubscriptions
                .Where(s => s.StartDate.Year == now.Year && s.StartDate.Month == now.Month)
                .Sum(s => s.Price);

            // ---- A. Subscription plans overview (count per plan, from CompanySubscriptions
            //         ONLY - the catalogue itself holds exactly the three plans) ----------------
            var planCategories = plans.Select(p => p.Name).ToList();
            var planValues = plans
                .Select(p => (decimal)subscriptions.Count(s => s.SubscriptionPlanId == p.SubscriptionPlanId))
                .ToList();
            var planOverview = new MasterBiSeries
            {
                Title = "Subscription Plans Overview",
                Unit = "subscriptions",
                Categories = planCategories,
                Values = planValues
            };

            // ---- B. Subscription revenue over time (monthly grouping over real StartDates) ----
            var revenueOverTime = BuildRevenueSeries(subscriptions, now);

            // ---- C. Subscription status (the statuses the model supports) --------------------
            var statusSeries = new MasterBiSeries
            {
                Title = "Subscription Status",
                Unit = "subscriptions",
                Categories = SubscriptionStatuses.All.ToList(),
                Values = SubscriptionStatuses.All
                    .Select(status => (decimal)subscriptions.Count(s => s.Status == status))
                    .ToList()
            };

            // ---- D. Payment overview (real PaymentStatus distribution) -----------------------
            var paymentSeries = new MasterBiSeries
            {
                Title = "Payment Overview",
                Unit = "subscriptions",
                Categories = PaymentStatuses.All.ToList(),
                Values = PaymentStatuses.All
                    .Select(status => (decimal)subscriptions.Count(s => s.PaymentStatus == status))
                    .ToList()
            };

            return new MasterBiSnapshot
            {
                GeneratedAtUtc = DateTime.UtcNow,
                TotalCompanies = companies.Count,
                ActiveSubscriptions = activeSubscriptions,
                PendingSubscriptionRequests = pendingRequests,
                ExpiredSubscriptions = expiresSubscriptions,
                TotalSubscriptionRevenue = totalRevenue,
                MonthlySubscriptionRevenue = monthlyRevenue,
                PlanOverview = planOverview,
                RevenueOverTime = revenueOverTime,
                SubscriptionStatus = statusSeries,
                PaymentOverview = paymentSeries
            };
        }

        /// <summary>
        /// Buckets the PAID subscription amounts by the month of their StartDate, spanning every
        /// month that has a subscription (oldest -> newest). Real buckets with real sums: a month
        /// with no paid subscription is 0, and nothing is extrapolated from a trend.
        /// </summary>
        private static MasterBiSeries BuildRevenueSeries(IReadOnlyList<CompanySubscription> subscriptions, DateTime now)
        {
            const int MaxMonths = 24;

            if (subscriptions.Count == 0)
            {
                // No subscription data at all: show the current month at 0 (never NaN / blank).
                return new MasterBiSeries
                {
                    Title = "Subscription Revenue",
                    Unit = "revenue",
                    Categories = new[] { new DateTime(now.Year, now.Month, 1).ToString("MMM yyyy") },
                    Values = new[] { 0m }
                };
            }

            var oldest = subscriptions.Min(s => s.StartDate.Date);
            var first = new DateTime(oldest.Year, oldest.Month, 1);
            var last = new DateTime(now.Year, now.Month, 1);
            if (last < first)
            {
                last = first;
            }

            var months = new List<DateTime>();
            for (var month = first; month <= last && months.Count < MaxMonths; month = month.AddMonths(1))
            {
                months.Add(month);
            }

            var paid = subscriptions
                .Where(s => string.Equals(s.PaymentStatus, PaymentStatuses.Paid, StringComparison.Ordinal))
                .ToList();

            var categories = months.Select(m => m.ToString("MMM yyyy")).ToList();
            var values = months
                .Select(m => paid
                    .Where(s => s.StartDate.Year == m.Year && s.StartDate.Month == m.Month)
                    .Sum(s => s.Price))
                .ToList();

            return new MasterBiSeries
            {
                Title = "Subscription Revenue",
                Unit = "revenue",
                Categories = categories,
                Values = values
            };
        }
    }
}
