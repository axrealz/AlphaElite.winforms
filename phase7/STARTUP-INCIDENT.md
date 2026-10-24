# Startup Incident — "could not load the tenant list from the master database"

Date: 2026-10-01 · Status: **DIAGNOSED — transient cloud-DB connectivity failure. No code, config, or data changed.**

## Symptoms (as reported)
- Startup dialog: "Alpha Elite could not load the tenant list from the master database."
- UI text: "An exception has been raised that is likely due to a transient failure. Consider enabling transient error resiliency by adding 'EnableRetryOnFailure' to the 'UseSqlServer' call."

## Evidence gathered (all read-only)
1. **Exact startup connection path** — `Program.cs` → `AppRuntime.Create()` builds config as `appsettings.json` → optional `appsettings.Local.json` → env vars, then `GetAvailableTenantsAsync()` on `TenantAuthenticationService`, which runs one EF Core query on the Master DB: `CompanyDatabase JOIN Companies WHERE IsActive` (`[TenantAuthenticationService.cs:42-56](WinFormsLibrary1/services/TenantAuthenticationService.cs#L42-L56)`). Target: `db67598.public.databaseasp.net / db67598` (credentials not printed).
2. **The exact same query path succeeded during diagnosis** — `--selftest` (production composition, same connection string, same context) discovered **3 tenants** seconds after the reported failure.
3. **Config comparison vs Phase 7** — bin copy byte-identical to source (`cmp` IDENTICAL); no `appsettings.Local.json` exists; appsettings mtime unchanged from Phase 7 day (2026-09-30); env-var override path empty at diagnosis. **No configuration mismatch.**
4. **Network** — DNS resolves to 5.9.179.197; TCP 1433 succeeded on 3/3 attempts.
5. **SQL** — 5/5 direct connections opened and `SELECT COUNT(*) FROM dbo.Companies` returned **3** each time.

## Root cause
**Genuine transient connection failure to the shared cloud SQL Server host** (databaseasp.net free tier: connection throttling/idle-drop/short server-side hiccups are common). The message shown by EF Core is its standard `RelationalEventId`-based transient-failure hint. Not a credential issue (subsequent logins succeed with the same string), not a missing database (Companies=3), not a firewall (TCP steady), not a configuration drift (config byte-identical to the Phase 7 run). The startup path runs this query **exactly once with no retry**, so a single dropped connection aborts app start with that dialog.

## Recommended next step (smallest safe fix — NOT applied)
Wrap the two startup calls in `Program.cs` (`GetAvailableTenantsAsync`, and the `MasterIdentityService` path inside the login form if desired) with a small bounded retry loop: **3 attempts, ~2 s delay between attempts**, catching `Exception` and retrying only when the innermost exception is a `SqlException`/`Win32Exception` socket-level failure. Roughly 15 lines in one file, no connection-string change, no `UseSqlServer` option change, no schema/migration/auth change, no data touched. Long-term post-exam option: `EnableRetryOnFailure` on the Master options — intentionally not done now per instructions.

**No restart action needed:** simply launching the application again succeeded at diagnosis time.

---

## RESOLVED — 2026-10-07 (Master DB transient-failure resilience)

The incident recurred during manual Super Admin testing. The underlying diagnosis above was correct
(a *genuine* transient connection failure, not credentials/config/schema), and the recommended
long-term fix was implemented:

- One shared `MasterDbContextOptions.Configure` now enables `EnableRetryOnFailure`
  (**5 retries / 10 s max delay** — the same bounded policy the tenant contexts already used) and
  adds `errorNumbersToAdd` covering the network errors actually observed on this link
  (53, 64, 258, 10061, plus EF Core's own transient list).
- **Every** master context path uses it: `AppRuntime`, `MasterDbContextFactory`,
  `MasterIdentityService.CloneContextAsync`, the design-time factory and the API host. The
  per-operation master contexts (concurrency fix) are unchanged.
- The operator now sees "The Master Database is temporarily unavailable. Please check the database
  connection and try again." instead of the raw EF hint; the original exception and its inner chain
  are appended to `master-db-diagnostics.log`. Permanent errors (auth/config/schema) are still
  reported as themselves.
- Targeted verification: `--master-connectivity-verify`.

Evidence from a live outage: `--phase3-subscription-verify` now fails cleanly with
`RetryLimitExceededException: The specified network name is no longer available.` — the original
diagnostic is preserved and the misleading "consider enabling EnableRetryOnFailure" text is gone.
