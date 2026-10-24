using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AlphaElite.infrastructure.data
{
    // Design-time factory used by EF tools to create the TenantC context for migrations.
    // The factory looks for a connection string named "TenantC" in appsettings.json
    // (or an environment variable TENANTC_CONNECTIONSTRING). If not found it throws
    // with guidance so you can provide a proper connection string for migrations.
    public class TenantCDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TenantCAlphaEliteDbContext>
    {
        public TenantCAlphaEliteDbContext CreateDbContext(string[] args)
        {
            // Probe for appsettings.json in several likely locations (current dir, base dir, assembly location)
            string? connectionString = null;
            string? appsettingsPath = null;

            var probes = new[]
            {
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory,
                // assembly location for this type
                Path.GetDirectoryName(typeof(TenantCDesignTimeDbContextFactory).Assembly.Location)
            };

            foreach (var probe in probes)
            {
                if (string.IsNullOrWhiteSpace(probe))
                    continue;

                var dir = probe;
                for (int depth = 0; depth < 10 && !string.IsNullOrEmpty(dir); depth++)
                {
                    var candidate = Path.Combine(dir, "appsettings.json");
                    if (File.Exists(candidate))
                    {
                        appsettingsPath = candidate;
                        break;
                    }

                    try
                    {
                        var parent = Directory.GetParent(dir);
                        dir = parent?.FullName;
                    }
                    catch
                    {
                        dir = null;
                    }
                }

                if (appsettingsPath != null)
                    break;
            }

            if (appsettingsPath != null)
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(appsettingsPath));
                    if (doc.RootElement.TryGetProperty("ConnectionStrings", out var cs) &&
                        cs.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        cs.TryGetProperty("TenantC", out var tenantC) &&
                        tenantC.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        connectionString = tenantC.GetString();
                    }
                }
                catch
                {
                    // ignore parse errors and fallback to environment variable
                }
            }

            // Fallback to environment variable if not found in file
            connectionString ??= Environment.GetEnvironmentVariable("TENANTC_CONNECTIONSTRING");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Could not find a connection string for TenantC. Add a 'ConnectionStrings:TenantC' entry to appsettings.json in the project directory, or set the TENANTC_CONNECTIONSTRING environment variable."
                );
            }

            var options = new DbContextOptionsBuilder<TenantCAlphaEliteDbContext>()
                .UseSqlServer(connectionString)
                .Options;

            return new TenantCAlphaEliteDbContext(options);
        }
    }
}
