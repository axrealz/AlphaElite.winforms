using System;
using System.ComponentModel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AlphaElite.infrastructure.data
{
    /// <summary>
    /// THE single place where MASTER (Super Admin) DbContext options are built.
    ///
    /// WHY THIS EXISTS: the master database is a shared cloud SQL Server, and the Super Admin
    /// window talking to it over a long session legitimately sees dropped connections / short host
    /// hiccups. Without a retrying execution strategy EF Core surfaces the drop as
    ///
    ///     System.InvalidOperationException: An exception has been raised that is likely due to a
    ///     transient failure. Consider enabling transient error resiliency by adding
    ///     'EnableRetryOnFailure' to the 'UseSqlServer' call.
    ///
    /// and the tenant/company list cannot finish loading. Every master context in the application
    /// (composition root, per-operation factory, Identity clone, design-time factory, API host)
    /// builds its options HERE, so no path can silently run with a different, non-resilient
    /// configuration.
    ///
    /// Retry values deliberately mirror the tenant contexts
    /// (<c>TenantDbConnectionFactory.ConfigureSqlServer</c>: 5 retries / 10 s maximum delay) so the
    /// master and tenant sides of the application behave the same way.
    ///
    /// BOUNDED, NEVER INFINITE: retrying cannot make an unreachable host reachable. After the
    /// configured attempts the original exception is propagated unchanged - it is never swallowed,
    /// never turned into a silent success, and never looped forever. Tenant contexts are NOT
    /// affected by this class.
    /// </summary>
    public static class MasterDbContextOptions
    {
        /// <summary>Retries AFTER the first attempt (6 attempts in total). Deliberately bounded.</summary>
        public const int MaxRetryCount = 5;

        /// <summary>Upper bound on a single retry delay; EF Core applies bounded exponential backoff.</summary>
        public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(10);

        /// <summary>
        /// SQL Server / Win32 error numbers that mean "the server could not be reached or the
        /// connection broke". These are ADDED to EF Core's own transient list (the two are used as a
        /// union), so a retry really does happen for the network errors this application actually
        /// sees on a flaky link:
        ///
        ///   53    the network path was not found          (NOT in EF's default list)
        ///   64    the network name is no longer available (in EF's default list - this is the error
        ///         that produced the reported "likely due to a transient failure, consider enabling
        ///         EnableRetryOnFailure" dialog)
        ///   258   the wait operation timed out            (NOT in EF's default list)
        ///   10061 connection refused                     (NOT in EF's default list)
        ///
        /// Authentication and configuration errors are deliberately absent: retrying those is
        /// pointless, so they are reported to the user as the real problem instead of being
        /// mislabelled as "temporarily unavailable".
        /// </summary>
        public static readonly int[] AdditionalTransientErrorNumbers =
        {
            20,     // The instance does not support encryption / connection broken
            53,     // ERROR_BAD_NETPATH - the network path was not found
            64,     // ERROR_NETNAME_DELETED - the specified network name is no longer available
            121,    // ERROR_SEM_TIMEOUT - the semaphore timeout period has expired
            233,    // Connection forcibly closed by the remote host
            258,    // Wait operation timed out
            -2,     // Execution Timeout Expired
            10053,  // WSAECONNABORTED
            10054,  // WSAECONNRESET
            10060,  // WSAETIMEDOUT
            10061,  // WSAECONNREFUSED
            11001,  // WSAHOST_NOT_FOUND
            10928,  // Azure: resource limit reached
            10929,  // Azure: resource limit reached
            40197,  // The service has encountered an error processing your request
            40501,  // The service is currently busy
            40613,  // Database is not currently available
            49918,  // Not enough resources to process the request
            49919,  // Too many operations in progress
            49920   // Too many operations in progress
        };

        /// <summary>
        /// Applies the ONE master resilience policy to a SQL Server options builder. Pass this
        /// method (never a bare <c>UseSqlServer(connectionString)</c>) whenever master options are
        /// constructed.
        /// </summary>
        public static void Configure(SqlServerDbContextOptionsBuilder sqlServer)
        {
            // EF Core's own transient list stays in force and the network errors this application
            // actually observes are added to it (union), so the retry covers both.
            sqlServer.EnableRetryOnFailure(MaxRetryCount, MaxRetryDelay, AdditionalTransientErrorNumbers);
        }

        /// <summary>Master context options for one context instance (the resilient configuration).</summary>
        public static DbContextOptions<MasterAlphaEliteDbContext> Create(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("A master connection string is required.", nameof(connectionString));
            }

            return new DbContextOptionsBuilder<MasterAlphaEliteDbContext>()
                .UseSqlServer(connectionString, Configure)
                .Options;
        }

        /// <summary>True when the failure means the master database could not be reached / the connection broke.</summary>
        public static bool IsAvailabilityFailure(Exception? exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                switch (current)
                {
                    case SqlException sql:
                        return IsTransientNumber(sql.Number);
                    case Win32Exception win32:
                        // SocketException derives from Win32Exception, so its socket error code
                        // (10054 / 10060 / 10061 / 11001) is classified by the same list.
                        return IsTransientNumber(win32.NativeErrorCode);
                    case TimeoutException:
                        return true;
                }
            }

            return false;
        }

        private static bool IsTransientNumber(int number) => Array.IndexOf(AdditionalTransientErrorNumbers, number) >= 0;
    }
}
