# WarehouseGate — Gated Warehouse Operations Platform

A working, end-to-end platform for gated warehouse movement, spanning three apps that share one API:

- **Mobile app** (.NET MAUI, Android + Windows) — Security and Supervisor roles run the gate/dock/load-unload workflow from the yard: **Inward** (unloading): gate-in → PO validation → live push to a supervisor → dock-in → photo evidence → inspection → GRN. **Outward** (loading): office generates a pick list from a dispatch order → live push to a supervisor → claim → dock-in (vehicle + bay) → photo evidence → the 3D "Plan & Load" simulation (place SKUs into six fixed vehicle zones, save option A/B/C arrangements, confirm actual loading per SKU) → complete → dispatch note (flagged `isPartial` if any line loaded short of the order).
- **Web portal** (Blazor Server) — SuperAdmin, Logistics Manager, and Office roles run the back-office side: dashboards, reports, master data, and the Dispatch-Plan-driven outward pipeline.
- **API** (ASP.NET Core) — the one backend both apps talk to, over JWT-authenticated REST + a shared SignalR hub for live updates.

## Solution layout

```
WarehouseGate.slnx
src/
  WarehouseGate.Domain/            entities & enums
  WarehouseGate.Infrastructure/    EF Core DbContext, Identity, photo storage, seed data
  WarehouseGate.LoadPlanning/      3D load-plan rule engine, optimizer, simulation (pure library, no EF)
  WarehouseGate.LoadPlanning.Tests/  xunit tests for the load-planning library (net10.0)
  WarehouseGate.Api/               ASP.NET Core Web API, JWT auth, SignalR hub, Swagger
  WarehouseGate.Api.Tests/         xunit tests for API services, EF Core Sqlite in-memory (net8.0)
web/
  WarehouseGate.Web/                Blazor Server portal (SuperAdmin / Logistics Manager / Office)
mobile/
  WarehouseGate.Mobile/            .NET MAUI app (Android + Windows), Security & Supervisor screens
```

- API/Infrastructure/Domain/Web/Api.Tests target **.NET 8**. `WarehouseGate.LoadPlanning.Tests` targets **.NET 10** (a pure-library test project with no dependency on the rest of the graph, so it was free to move ahead). The mobile app targets **net9.0-android** / **net9.0-windows**. Every app only talks to the others over HTTP/SignalR, so the different TFMs have no practical effect.

## Roles

| Role | Surface | What they do |
|---|---|---|
| Security | Mobile | Gate check-in/exit for both inward and outward vehicles |
| Supervisor | Mobile | Claim jobs, dock-in, inspection/load-lines, the 3D load-plan editor, complete |
| Office | Web portal | Dispatch orders, pick-list/outward job generation, assign supervisors, follow-ups, edit gate-captured fields, dashboards, reports |
| Logistics Manager | Web portal | Vehicle Logistics Records (the Dispatch Plan — cross-warehouse vehicle movement master data), region-scoped dashboards/reports |
| SuperAdmin | Web portal | Everything Office/Logistics see (unscoped, every warehouse) plus all master data: warehouses (incl. per-warehouse SLA/dock-hours/shift-hours settings), dock bays, transporters, products, vehicle masters, users, geography (countries/states/cities/regions/locations), audit trail |

## Prerequisites

- .NET 8 SDK, .NET 9 SDK, and .NET 10 SDK (all needed across the different TFMs above)
- MAUI workloads: `android`, `maui-windows`
- SQL Server LocalDB (`MSSQLLocalDB` instance)

## First-time setup

```bash
# from the solution root
dotnet ef database update --project src/WarehouseGate.Infrastructure --startup-project src/WarehouseGate.Api
```

This creates the `WarehouseGate` database on `(localdb)\mssqllocaldb` and the API seeds it on first run with:

| Purpose | Value |
|---|---|
| SuperAdmin login | `superadmin1` / `Pass123$` |
| Logistics Manager login | `logistics1` / `Pass123$` (scoped to the West region) |
| Office login | `office1` / `Pass123$` (Mumbai DC) |
| Security login | `security1` / `Pass123$` |
| Supervisor logins | `supervisor1`, `supervisor2` / `Pass123$` (two, so claim-lock behavior can be demonstrated) |
| Seeded warehouses | `Mumbai DC`, `Mumbai CPA` (West region), `Bengaluru DC` (South region) |
| Sample PO | `PO-1001` — Acme Steel Works (MS Angle 50x50, MS Sheet 2mm) |
| Sample PO | `PO-1002` — Prime Packaging Ltd (Corrugated Box - Large) |
| Sample PO | `PO-1003` — Bharat Fasteners Co. (Hex Bolt M12, Flat Washer M12) |
| Sample dispatch order | `DO-2001` — Reliance Retail (MS Angle 50x50, MS Sheet 2mm) |
| Sample dispatch order | `DO-2002` — Vishal Mega Mart (Corrugated Box - Large) |
| Sample dispatch order | `DO-2003` — Big Bazaar (Hex Bolt M12, Flat Washer M12) |
| Sample vehicles | `MH04GT3312`, `GJ01AX7790`, `KA05MZ4521` |

Seeding is per-record and idempotent (`SeedData.cs`), so adding new sample rows later won't be skipped just because the dev DB already has earlier ones — each row is checked and inserted individually on every startup.

### Testing the whole flow end-to-end

`scripts/test-full-flow.sh` drives both mobile-side flows against a running API using the seeded data above: 3 Inward scenarios (clean receipt, damaged+short, excess+mismatch) and 3 Outward scenarios (full load, partial load from stock shortage, full load with load sequencing), plus two claim-lock checks (second supervisor rejected after the first claims). Requires `python3` on PATH.

```bash
cd src/WarehouseGate.Api && dotnet run --urls "http://localhost:5080"   # terminal 1
./scripts/test-full-flow.sh                                             # terminal 2
```

It's safe to re-run — dispatch orders/POs can be reused across runs (only the inward transaction number needs to be unique, and the script timestamps it).

## Running the API

```bash
cd src/WarehouseGate.Api
dotnet run --urls "http://localhost:5080"
# or: dotnet run --launch-profile https   (binds https://localhost:7174 - required if you also want
#     the web portal's default appsettings.json ApiBaseUrl to work unmodified)
```

Swagger UI: `http://localhost:5080/swagger`. Paste a JWT from `/api/auth/login` into the Swagger "Authorize" button (as `Bearer <token>`) to call the protected endpoints.

Photos are written to `src/WarehouseGate.Api/App_Data/photos/{flow}-{transactionId}/...` on disk — there's no cloud storage in this slice.

## Running the web portal

```bash
cd web/WarehouseGate.Web
dotnet run
```

Defaults to `http://localhost:5062` (or `https://localhost:7172`). The portal calls the API at the URL in `appsettings.json`'s `ApiBaseUrl` (`https://localhost:7174/` by default) — run the API with `--launch-profile https` to match, or edit `ApiBaseUrl` to point at `http://localhost:5080/` instead.

## Running the mobile app

**Windows** (fastest for local testing):
```bash
cd mobile/WarehouseGate.Mobile
dotnet build -f net9.0-windows10.0.19041.0
# then run WarehouseGate.Mobile.exe from bin/Debug/net9.0-windows10.0.19041.0/win10-x64/
```
Windows talks to the API at `http://localhost:5080` directly.

**Android emulator**: the app automatically points at `http://10.0.2.2:5080`, which is how the Android emulator reaches the host machine's `localhost`. A physical Android device can't use `10.0.2.2` — edit `Services/AppConfig.cs` to use your dev machine's LAN IP instead, and make sure the API is bound to `0.0.0.0` (not just `localhost`) so it accepts connections from the network.

## Web portal features

- **Dashboards** (SuperAdmin sees every warehouse; Logistics Manager is scoped to their region; Office is scoped to their own warehouse) — Supervisor Leaderboard (top performers by weighted boxes/hr), Supervisor Performance table (vehicles today/week/month, avg load/unload time, and a Utilization % column that only appears once a warehouse has its Shift Hours/Day setting configured), daily/weekly/monthly trend charts, and Advanced KPI tiles: Vehicle Turnaround Time, Dock Utilization, Productivity/hr, Exception Rate, and SLA Compliance — all computed against each warehouse's own SLA target / dock operating hours / active dock-bay count where configured, falling back to system defaults otherwise.
- **Reports** — detailed inward/outward reporting with date range filters, search, and CSV export, at `/admin/reports`, `/logistics/reports`, `/office/reports`.
- **Follow-ups** (`/office/follow-ups`) — GRN exceptions and partial-load dispatches automatically open a follow-up task here (instead of just a server log line) the moment the underlying job completes; Office resolves them with optional notes.
- **Master data** (SuperAdmin) — Warehouses (with per-warehouse SLA Target / Dock Operating Hours / Shift Hours settings that feed the dashboards above), Dock Bays (the bay master a warehouse can define so mobile's dock-in screen shows a tap-to-pick chip list instead of free-number entry), Transporters, Products (SKU), Vehicle Types/Categories/Masters, geography (Countries/States/Cities/Regions/Locations), Users, and the Audit Trail.
- **Dispatch Plan** — Logistics Manager's Vehicle Logistics Records screen is the cross-warehouse movement master data: Office's outward pick-list generation and Inward's "Expected (Not Yet Arrived)" list both key off it, and its status (`InTransit` → `InProgress` → `Completed`) tracks the real job lifecycle from claim through inward completion at the destination.
- **Realtime** — every dashboard, the Office job lists/detail pages, and the Follow-ups page hold a live SignalR connection (`hubs/inward`, the same hub the mobile app uses) and refresh automatically on job lifecycle events, follow-up creation/resolution, load-plan edits made from a supervisor's phone, and office field edits — no manual refresh needed for another user's changes to show up.

## Testing

Two test projects:

```bash
# 3D load-planning rule engine / optimizer / simulation (pure library, no DB)
dotnet test src/WarehouseGate.LoadPlanning.Tests/WarehouseGate.LoadPlanning.Tests.csproj

# API services against an EF Core Sqlite in-memory database - warehouse scope resolution,
# dashboard analytics (weighted leaderboard, SLA/dock-utilization per-warehouse settings),
# assign-supervisor completed-job guards, follow-up task creation on both completion paths
dotnet test src/WarehouseGate.Api.Tests/WarehouseGate.Api.Tests.csproj

# both, plus a compile check of every project in the solution
dotnet test WarehouseGate.slnx
```

## What's wired up and verified

- Full inward flow tested end-to-end: gate check-in → PO/duplicate-txn validation → claim → dock-in → photo upload → inspection (including a short/damaged line) → complete → GRN generated with `hasExceptions` correctly flagged, exception raised as a visible Office follow-up task.
- Full outward flow tested end-to-end: office generate-picklist → live push to supervisors → claim → dock-in (vehicle + bay) → photo upload → the 3D load-plan editor (place, move, split, duplicate, compact, validate) → per-SKU actual-loading confirmation → complete → dispatch note generated with `isPartial` correctly flagged, raised as a visible Office follow-up task.
- Role-based JWT auth confirmed across every role: Security, Supervisor, Office, Logistics Manager, and SuperAdmin endpoints correctly reject the wrong role (403) and enforce warehouse/region scoping.
- Photo storage is keyed by `{flow}-{transactionId}` (e.g. `inward-2`, `outward-1`) specifically so inward and outward transactions — which have independent identity sequences and can share numeric IDs — never collide on disk.
- Mobile app build succeeds for both Android and Windows targets; the full Security and Supervisor flows (including the 3D load-plan editor and per-SKU load confirmation) have been exercised against the live API on Windows.
- Web portal build succeeds; every role's dashboard, reports, master data, and follow-ups pages have been exercised end-to-end (curl + Playwright) against the live API, including realtime push verification via a live SignalR client.

## Known gaps

- No RLM (Regional Logistics Manager) mobile app — the Logistics Manager role is web-portal only; there's no PO/dispatch-order authoring UI either, both are seeded or entered via the Office portal's Dispatch Orders screen.
- No message broker / Hangfire / Redis — single-instance only. Fine for a demo, not for multi-warehouse scale-out.
- Camera capture (`MediaPicker.CapturePhotoAsync`) is wired in code and matches the tested API contracts, but hasn't been click-tested on a physical Android device (this dev machine has no camera to test with on Windows).
- Supplier/customer follow-up notifications (email/SMS) are not sent automatically — the Follow-ups page is the visible to-do surface; outbound notification on task creation is future work.
- No automated browser/UI test suite committed to the repo (verification during development used ad hoc Playwright scripts) — `WarehouseGate.Api.Tests` covers service-layer logic, not the Blazor UI or mobile UI directly.
