# Alpha Elite — Exam Demonstration Script (~20 minutes)

All passwords stay with you; nothing is typed on screen until prompted. If the demo data looks wrong at any point, stop and re-run the idempotent seeder (step 0) — it never duplicates rows.

---

## 0. Before the examiner arrives (5 min)

```bat
:: 1. Build check (expect: 0 errors)
dotnet build AlphaElite.winforms.slnx -v q --nologo

:: 2. Refresh demo data (idempotent — re-running is safe, adds 0 rows)
dotnet run --project AlphaElite.winforms -- --phase6-demo

:: 3. Quick sanity: read-only inventory of all three tenants
dotnet run --project AlphaElite.winforms -- --migration-dryrun
```

Keep these credentials on paper only (never on screen or in files shown to the examiner):

| Account | Where used | Demo purpose |
|---|---|---|
| Master superadmin | Master login | Company/tenant administration |
| Tenant C `admin` | Tenant C | Tenant-wide admin, both branches |
| Tenant C `manager` | Tenant C | Branch A restriction |
| Tenant C `gymstaff` | Tenant C | Branch B restriction |
| Tenant C `member` | Tenant C | Member portal / self-only |

---

## 1. Master / Super Admin (steps 1–3)

1. Start the app. On the login screen choose the **Master Admin** login, sign in as `superadmin`.
2. **Master dashboard:** show the Companies/Tenants panel (COMP001/COMP002/COMP003), Tenant Databases mapping, Users/Administrators.
3. **Enter Tenant C:** switch into `COMP003 – Alpha Elite` from the master dashboard — this demonstrates the master-resolved tenant switch (nothing hardcoded).

## 2. Tenant C Admin — tenant-wide (steps 4–12)

4. **Admin dashboard:** 11 KPI cards (Total/Active/Inactive Members, Avg Rating, Attendance Today/Week/Month, Total Payments, Pending Inquiries, Open Complaints, At-Risk). Show the Export PDF button (now correctly visible beside the KPI grid).
5. **KPI/BI:** click-through cards (e.g. At-Risk → filtered list) and the four charts + the **Branch Comparison** grid (Admin sees both branches).
6. **Members:** 6 records; point out Branch A/B assignment and the branch picker + search box.
7. **Attendance:** 11 check-ins (this week's demo rows included); search box; duplicate check-in guard on the same member/day.
8. **Payments / Renewals:** outstanding summary, "Renew selected" prefill flow.
9. **Subscriptions:** the two "(demo)" plans with computed status; Export PDF.
10. **Complaints:** two demo complaints in different branches (Pending / Under Review); advance one status; Export PDF.
11. **Retention / At-risk:** inactivity + due-soon panels, retention workflow statuses.
12. **Reports/PDF:** run one staff-facing export (members or dashboard) — SaveFileDialog opens, sensible filename; cancel the dialog.

**Logout** → back to the login screen.

## 3. Tenant C Manager — Branch A restriction (steps 13–15)

13. Sign in as `manager` (Branch A Manager).
14. **Scope proof:** header shows the role/branch context; member list shows only the 1 Branch A member; attendance/payments/feedback/inquiries counts are Branch A only; Branch Comparison is limited to the manager's own branch.
15. **Read/write boundary:** try opening a Branch B member by any means — blocked or absent; complaint workflow works inside Branch A. Logout.

## 4. Tenant C Gym Staff — Branch B restriction (steps 16–17)

16. Sign in as `gymstaff` (Branch B Gym Staff).
17. **Scope proof:** only Branch B data (5 members, 7 attendances); **no** Dashboard nav item, **no** Complaints/Feedback/UserManagement; branch picker locked; delete/update outside Branch B is blocked. Logout.

## 5. Tenant C Member — self-only portal (steps 18–19)

18. Sign in as `member` (linked to member 1, Branch A).
19. **Member portal:** personal summary card (membership, expiry/status, payments, visits, complaints) and **no staff forms** — entry fields and Add/Update/Delete buttons are hidden; nav shows only My Membership / My Attendance / My Payments / Feedback / Complaints. Submit a feedback row or open a complaint live, then logout.

---

## If something misbehaves during the demo

- Data looks off → step 0 seeder (idempotent).
- A DB hiccup → staff roles see a friendly "Something went wrong while contacting the database" message; as `admin` you also get the technical detail — explain that as deliberate error-handling design.
- Never expose passwords on screen; type them directly into the login fields.
