# RESOLVED — plan-version artifact cleanup (superseded)

> **Status: RESOLVED on 2026-10-08.** The deferred cleanup below was deliberately *not* run on its
> own: the final subscription system mandated **exactly three plans** (BASIC / PROFESSIONAL /
> ENTERPRISE), so Micro Business (and Small Business) were removed outright, which also removed every
> verification-generated version artifact that this note was about.

## What replaced it

```
AlphaElite.winforms.exe --master-remove-legacy-plans [--dry-run]
```

Master-DB-only, guarded, idempotent. It

* screens STOP conditions first (a subscription, request or history row naming a retired plan; a
  BASIC/PROFESSIONAL/ENTERPRISE identifier conflict) and **aborts without deleting anything** if one
  is found,
* deletes only the retired plans' own rows: `SubscriptionPlans`, their `PlanModules` mappings, their
  `SubscriptionPlanVersions` and those versions' `SubscriptionPlanVersionModules` snapshots,
* never touches `SoftwareModules`, the three retained plans (ids, codes, prices, billing cycles,
  terms, activation, mappings, versions), or any `CompanySubscriptions` / `SubscriptionChangeRequest`
  / `SubscriptionHistory` row,
* **repairs subscription plan-version links**: a link that is NULL, points at a removed version or
  points at a *different plan's* version is re-pointed at the subscription's own plan's current
  version (this repair also runs when there is nothing left to remove).

## Outcome (see `legacy_remove.log`, `legacy_link_repair.log`, `FINAL_SUBSCRIPTION_BI_REPORT.md`)

| | before | after |
|---|---|---|
| `SubscriptionPlans` | 5 | **3** |
| `PlanModules` | 56 | **36** |
| `SubscriptionPlanVersions` | 11 | **3** |
| `SubscriptionPlanVersionModules` | 104 | **36** |
| `SoftwareModules` | 16 | 16 (untouched) |
| `CompanySubscriptions` | 3 | 3 (untouched, links repaired) |

The narrower artifact cleanup (`--master-clean-plan-version-artifacts`) is retained as a
general-purpose, still-guarded maintenance utility (its plan-count check now expects the final three
plans); it currently reports nothing to clean, because the demo master database holds only the
original Version 1 of each retained plan.
