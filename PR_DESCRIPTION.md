# PlainSight: Mission Control redesign

Reimagines the admin dashboard as an industrial NOC-style mission control. Dark surfaces, status-at-a-glance, telemetry-first device tiles. Wires real KPIs from `PlainSightDbContext` into the top bar.

## Files changed

| File | Change |
|---|---|
| `src/PlainSight.Server/wwwroot/app.css` | Replaced — new dark token system, restyled Bootstrap primitives (buttons, alerts, modals, tables, forms) |
| `src/PlainSight.Server/Components/Layout/MainLayout.razor` | Replaced — top bar now shows live system LED + active/degraded/offline KPIs (5s refresh) |
| `src/PlainSight.Server/Components/Layout/MainLayout.razor.css` | Replaced — glassmorphism top bar, scoped KPI styles |
| `src/PlainSight.Server/Components/Layout/NavMenu.razor` | Replaced — gradient logo mark, refined section labels, dedicated sign-out button |
| `src/PlainSight.Server/Components/Layout/NavMenu.razor.css` | Replaced — dark nav with cyan active-state rail |
| `src/PlainSight.Server/Components/Pages/Devices.razor` | Replaced — table swapped for status-filtered tile grid; same code-behind, same EF logic, plus `statusFilter` state and online/warn/offline split |
| `src/PlainSight.Server/Components/Pages/Devices.razor.css` | Replaced — tile, telemetry grid, bulk-action bar, status filter chips |

## Design system

- **Surfaces:** `#06090F → #0A111E → #0F1A2E → #14213A` (4-step dark scale)
- **Status:** Emerald `#10B981` · Amber `#F59E0B` · Crimson `#EF4444` (with halo glow + LED pulse)
- **Accents:** Cyan `#22D3EE` for primary, Blue `#3B82F6` for interactive
- **Type:** Inter (UI) + JetBrains Mono (telemetry/IDs)

All tokens declared in `:root` in `app.css` (`--ps-*` namespace).

## Behaviour parity

Everything from the original `Devices.razor` still works:
- 5-second auto-refresh timer (`refreshTimer`)
- Per-device screenshot request + history modal (`ShowScreenshotHistory`)
- Group reassignment (per-device dropdown + bulk move)
- API key reset
- Live mode editor (override / auto-switch / NDI source)
- Bulk screenshot, bulk move, bulk version assignment

New behaviour:
- **Status filter chips** (`All / Online / Degraded / Offline`) — three-tier status replaces the binary online/offline (`< 60s = online`, `< 5min = degraded`, else `offline`)
- **Top bar KPIs** — pulled from a fresh `LoadDevices`-style query in `MainLayout.razor` itself, refreshed every 5s alongside the existing per-page timer
- **Select all visible** — only selects devices matching the current status filter

## Compatibility notes

1. **Bootstrap** is still in use — the redesign restyles Bootstrap classes rather than removing them. No npm/bundler changes needed.
2. **CSS Isolation** — all new selectors in `MainLayout.razor.css`, `NavMenu.razor.css`, and `Devices.razor.css` are unprefixed and rely on Blazor's per-component scoping. The `::deep` selector on `.nav-link` is preserved (needed because `NavLink` renders an `<a>` not under direct CSS-isolation control).
3. **`/api/device/{deviceId}/screenshots/latest`** — the tile's thumbnail uses this endpoint. If your routing only exposes `/screenshots/{id}`, either add a `latest` shortcut or change the `<img src>` in `Devices.razor` to use `device.LatestScreenshotAt` to pick the most recent ID. Falls back gracefully to the "NO SCREENSHOT" empty state if not present.
4. **`Microsoft.AspNetCore.Components.Web` namespace** — `Devices.razor` uses `KeyboardEventArgs`. This was already imported via the existing `_Imports.razor`; no change needed.

## How to apply

```bash
git checkout -b mission-control-redesign
# Copy the seven files from pr/src/PlainSight.Server/... into the matching paths
git add src/PlainSight.Server
git commit -m "Redesign admin UI as Mission Control"
git push -u origin mission-control-redesign
gh pr create --title "Mission Control admin redesign" --body-file PR_DESCRIPTION.md
```

## Screenshots

See the prototype in `Mission Control.html` (React mock at the same fidelity as the final Blazor output).
