# Elite Bio Radar

A standalone portable Windows companion app for Elite Dangerous Odyssey.

Think of it as a cross between SRV Survey and EDDiscovery, but built to live on a second screen or monitor as a set of in-ship instruments and monitoring screens, instead of a separate tool or overlay.

The app is designed to run on a second monitor or touchscreen alongside the game.

**VR ready** — can be pinned in Meta Quest, Virtual Desktop, or any VR environment that supports pinning Windows applications into your playspace.

Latest Release can be found here: https://github.com/macrossmerrell/EliteBioRadar/releases

---

## Application Overview

![image](https://github.com/macrossmerrell/EliteBioRadar/blob/669e4cc2e8c27867c3fa8925104955077e02f87f/screenshots/elitebioradar.gif)

There are multiple windows you can use: the **main window**, which follows whatever you're doing (flying, landing, sitting in a system, mid-jump), and three pop-out windows you open on demand — **System Scan**, a dashboard of everything you've found in the current system, **Scan Log**, a lifetime survey of everything you've ever found, anywhere, and **Star Finder**, a lookup tool for the nearest star of a chosen type (neutron star, black hole, white dwarf, and more) relative to your current system.

### Main window — six modes, one tab bar

The tab bar switches automatically between six modes based on what you're actually doing. Click a tab to override it manually — it stays put until something real changes (a new target, a jump, landing).

- **🛰️ RADAR** — your boots-on-the-ground and low-altitude instrument. Colony-exclusion range rings around organisms you've scanned, scan dots that change color through the Log → Sample → Sample → Analyse sequence, geological site markers, and a dashed ship-departure-range ring so you know how far you can wander before losing the ability to call your ship back.
- **☀️ STAR** — class, solar mass, age, temperature, radius, rings, habitable zone.
- **🪐 PLANET** — a rendered scene instead of a flat icon: terrain worlds get procedural craters, fissures, material-tinted surface, rings when present, and an atmosphere glow that scales with actual surface pressure. Class, gravity, atmosphere, bio/geo/mining signal counts, scan-state tag, DSS composition once mapped. A "D" badge marks a body someone else discovered first; an "M" badge marks one someone else already mapped — both read from your own journal history, so they never credit another commander for something you did yourself. An optional **Gravity Warning** flags a landable body's gravity in a caution colour once it meets or exceeds a threshold you set.
- **⛛ DEORBIT / ⤒ LAUNCH** — takes over while you're descending or launching: a glide-path scene using the same rendered planet as the Planet tab, with a ship that follows your telemetry-derived altitude and vertical speed instead of a fixed-duration animation. Holds on a neutral state until it actually knows which direction you're going.
- **📡 FSS SCANNER** — takes over while the FSS scanner is open: an orbital overview with the star at center and every resolved planet placed on a distance-ranked ring, zooming into a planet's own moons once you resolve one.
- **🧭 DESTINATION** — live jump range from your ship's actual stats and current fuel, the full plotted route, a hop counter and progress bar, and route progress that survives a restart.

A left-side **Biological & Geological Sites** panel and a right-side **Bio Survey** sidebar stay available throughout, with per-organism payout figures, wiki links, and a First Footfall indicator.

### 🛰️ System Scan window

Everything scanned in the current system, opened from the top bar. Every body gets a card: a real render, class, bio/geo/mining signal badges with hover tooltips, a habitable-zone chip, a "high value" chip, and material chips you can hover for a full breakdown.

- **Colored outlines** on the cards mark terraformable, water world, earthlike, and ammonia-world bodies, and habitable-zone placement, at a glance.
- **Current value updates live** as you scan and map bodies — the figure on each card is the body's actual current worth, not a one-time estimate frozen at first scan.
- **Discovery and mapping notifications** — the "D" and "M" badges flag a body already discovered or mapped by another commander, worked out from your own journal history so it only fires when it genuinely wasn't you.
- **Binary/trinary grouping** — bodies that actually orbit each other around a shared barycenter get bracketed together with a labeled line, instead of scattering across the layout. Stars themselves get the same treatment: two or three stars genuinely orbiting each other are grouped under a shared bracket, and a star that orbits another one but has no bodies of its own is folded into that star's own section as a compact card instead of getting a whole separate one.
- **Barycenter Orbit / Parent Not Yet Scanned sections** — bodies with no clean single parent star still show up, grouped and labeled, instead of vanishing.
- **Ring class chips** — a "rocky rings" / "icy rings" / "metallic rings" / "metal rich rings" chip on any card (planet, moon, or star) that actually carries them, including the rare dim star with a real ring of its own.
- **Gravity Warning chip** — matches the Planet tab's own gravity warning threshold, shown on any landable body that meets or exceeds it.
- **FSD injection material notification** — a chip that tells you whether the system's scanned bodies carry every raw material for a Basic, Standard, or Premium FSD synthesis recipe. Color-coded per tier (orange/blue/green) and shows only the best one you qualify for.
- **Material inventory aware** — reads your actual raw-material stock from the journal, and can hide chips for anything you're already capped out on ("Hide Full Mats"), so what's shown is what you'd actually benefit from picking up.
- **Trip value tracking** — a running total since your last reset, survivable across restarts, with an **Import Trip Data** option to backfill it from journal history without double-counting.
- **Mining signal targeting** — selecting a Planetary Mining Location Signal in the nav panel resolves to its real parent planet and shows a dedicated illustration, instead of the radar jumping to the primary star.
- Toggle between "Notable Only" and every scanned body, hide asteroid belts, and click the EDSM status chip to jump straight to EDSM settings.

### 🗂️ Scan Log — Galactic Survey

Everything you've ever scanned, across every session — Biology, Geology, Stellar, Worlds, Phenomena, and Journals tabs, grouped by real in-game galactic region, with lifetime totals, first-discovery breakdowns, and a date-range filter. **Scan All** rebuilds the whole library from your journal history in well under a minute.

### EDSM Integration

Live journal-event upload to EDSM as you play (opt-in, off by default), with your ship name/type attributed correctly. A background sync catches up recent history automatically; **Sync Journals to EDSM** runs it on demand.

### Screenshot Conversion

Opt-in: watches for new Elite Dangerous screenshots and converts them from its own huge, uncompressed `.bmp` to `.png`, deleting the original once the `.png` is confirmed saved — with a brief toast notification when one finishes, and a manual button to convert an existing backlog all at once.

### Star Finder

A pop-out window, opened from the top bar, for finding the nearest star of a chosen type — neutron star, black hole, white dwarf, Wolf-Rayet, supergiant classes, and more — relative to your current system. Results show system name, distance, and region, with a one-click **Copy** per result (and a **Copy All**) so you can paste the system name straight into the in-game galaxy map to plot your own route. The app has no way to plot or push a route into Elite Dangerous itself, so this is a lookup-and-copy tool rather than automation.

### Session persistence

Scan locations, First Footfall status, earnings, route progress, trip value, and window positions all survive an app restart, and most survive a game restart too. Writes are atomic, so a hard kill mid-write can't corrupt your data.

**Second-monitor and VR ready** — works pinned into any VR environment that supports overlaying Windows applications (Meta Quest, Virtual Desktop, SteamVR, Windows Mixed Reality).

---

## Application Features

### Radar
- **North-up display** — ship always centred, North always at the top
- **Heading arrow** — shows your current facing direction
- **Colony range rings** — solid border with diagonal-hatched fill per organism showing the species exclusion zone (minimum distance between scans of the same genus), colour-matched to the scan dot
- **Scan animation** — optional expanding pulse effect that sweeps outward from the centre, lighting up each range ring as it passes
- **Mouse-wheel zoom** — 100m to 10km range
- **Auto-scale mode** — automatically zooms to keep all active scan sites in view (ignores completed grey dots)
- **Scan dot colours:**
  - 🔵 Blue — first scan (Log)
  - 🟢 Green — second scan (Sample)
  - 🟠 Orange — third scan (Sample) — turns grey when Analyse completes
  - ⚫ Grey — fully logged, shown as a faint reference marker
- **Off-screen indicators** — dots outside the current zoom range are clamped to the radar edge
- **Geological scan markers** — discovered geological sites are shown on the radar as distinct markers separate from biological scan dots
- Automatically takes over the info panel the instant Latitude/Longitude becomes available (on foot, ship on the surface, SRV, etc.)

### Star / Planet / Destination / Deorbit / FSS Scanner Info Panel
The RADAR tab only means something while you have Lat/Long. The rest of the time — supercruise, sitting in a system, cruising between jumps, descending or launching, or scanning with the FSS — five other modes fill the same space instead of leaving it blank.

- **Full-width tab bar** — RADAR / STAR / PLANET / DESTINATION, spanning the entire app width. The RADAR and STAR tabs relabel themselves to DEORBIT/LAUNCH and FSS SCANNER while those modes are active, and clicking them always takes you back to whichever of the pair is actually current.
- **Automatic switching**, based on live game state:
  - **DEORBIT/LAUNCH** wins while you're descending toward or launching from a body but don't yet have (or have just lost) a real ground position
  - **RADAR** wins the instant Lat/Long is available (landing always takes priority)
  - **FSS SCANNER** wins while the FSS scanner is open
  - **DESTINATION** wins the instant the FSD starts charging for a real hyperspace jump — read directly from `Status.json`, so it's immediate even from within a landable atmosphere, and never triggers for a plain or SCO-boosted supercruise charge
  - **STAR** or **PLANET** shows whichever body is currently targeted (including a secondary/tertiary star, or a nav-panel signal resolved to its real parent body)
  - Falls back to **STAR** (the primary star) when nothing else applies
  - **Click any tab manually** to override the automatic choice — it stays there until a genuine new event clears the override, so you can check something on another tab and it won't snap back on its own
- **STAR tab** — class (with common name, e.g. "M (Red Dwarf)"), solar mass, age, surface temperature, radius, rings (correctly excludes asteroid belts, which report as a "ring" on the star's own scan but aren't one), and habitable zone. A dim star (a brown dwarf) that genuinely carries a real ring gets it rendered the same way a ringed gas giant does, not just named in the text
- **PLANET tab** — a rendered scene for gas giants and every terrain-family class (High Metal Content, Icy, Rocky, Rocky Ice, Water World, Metal Rich, Earthlike, Ammonia World): procedural craters and fissures, material-tinted surface, real ring geometry when present, and an atmosphere glow scaled to actual surface pressure. Also planet class (short-form for gas giants, e.g. "Water Giant"), gravity, atmosphere, surface temperature, bio/geo/mining signal counts, landable flag, and a scan-state tag (`AUTOSCAN` / `DETAILED` / `MAPPED`)
  - **Gravity Warning** — optional, off by default: set a G threshold in Settings and a landable body's gravity value turns a caution colour with a "HIGH GRAVITY" chip once it's met or exceeded
  - **Composition callout** — once DSS-mapped, a line above the planet reveals Ice/Rock/Metal percentages
  - **Discovery/mapped indicators** — shows whether the body was discovered or DSS-mapped by another commander before you
  - Shows whichever body is targeted, or the one you're standing on if nothing is targeted
  - **Asteroid belt clusters** get one of five pieces of dedicated belt artwork, randomly assigned per belt and kept stable
  - **Nav-panel signals** (e.g. a Planetary Mining Location Signal) resolve to their real parent body instead of jumping to the primary star
- **DEORBIT / LAUNCH tab** — a glide-path scene using the same rendered planet as the Planet tab, fixed gates the ship flies through, and a ship position driven by telemetry-derived altitude and vertical speed rather than a fixed timer. Holds on a neutral "resolving" state until it knows the direction and the body's scan detail has resolved, so it doesn't flash the wrong scene
- **FSS SCANNER tab** — an orbital overview: star at centre, every resolved planet on a distance-ranked ring (more rings as the system has more planets to spread across), with a manifest list of every body and its signal counts. Resolving a moon zooms into that planet's own local moon view
- **DESTINATION tab**:
  - **Jump range** calculated live from your FSD's actual stats, current fuel, and ship mass — not the stale figure from the last Loadout event
  - Fuel level, next-jump distance, remaining/total route distance, a jump counter (e.g. `HOP 6 / 33`), progress bar
  - **Full route history** — the entire route, not just what's left, already-passed hops dimmed; scoopable star classes get a small icon
  - Auto-scrolls to your current position, scroll up freely to review earlier systems
  - **Persists across restarts** — cached to disk (`EliteBioRadar.route.json`) and matched by final destination

### System Scan window
A dashboard for the current system, opened from the top bar, with a card for every scanned body.

- **Real render per card**, matching the Planet tab's own rendering
- **Colored outlines** for terraformable, water world, earthlike, and ammonia-world bodies, plus habitable-zone placement
- **Bio/geo/mining signal badges** with hover tooltips
- **Material chips** — hover for a full surface-material breakdown, with materials you're already capped on optionally hidden ("Hide Full Mats")
- **Current value updates live** as bodies get scanned and mapped
- **Discovery and mapping notifications** — "D" and "M" badges for bodies already discovered or mapped by another commander
- **Binary/trinary grouping** — genuinely co-orbiting bodies bracketed together with a labeled connector line, including the stars themselves; an orbiting star with no bodies of its own is folded into its parent's section as a compact card instead of getting a whole separate one
- **Barycenter Orbit / Parent Not Yet Scanned** sections for bodies with no clean single scanned parent
- **Ring class chips** — "rocky rings" / "icy rings" / "metallic rings" / "metal rich rings" on any card that actually carries them
- **Gravity Warning chip** — same threshold as the Planet tab, on any landable body that meets or exceeds it
- **FSD injection material notification** — a color-coded chip (orange/blue/green for Basic/Standard/Premium) showing whether the system carries every material for that synthesis recipe; shows only the best one you qualify for
- **Trip value tracking** — a running credit total since the last reset, with **Import Trip Data** to backfill it from journal history without double-counting
- **EDSM status chip** — click to jump straight to EDSM settings on the main window
- Toggle between "Notable Only" and every scanned body, hide/show asteroid belts

### Biological & Geological Sites Panel (left toggle — "Bio Sites Sidebar")
- Lists every planet in the current system with biological signals
- Populated from FSS and DSS scans, backfilled from journal history — including planets scanned in previous sessions
- Shows short body name and bio signal count — e.g. `▶ A 4 (3)`
- Current body highlighted with a `▶` indicator
- Planet names go grey once all biology on that body is fully logged
- **Click any planet** to preview its bio data in the Bio Survey sidebar
- **Geological sites section** — when **Show Geological Sites Info** is enabled, a second list appears showing short body name, geo signal count, and sites already discovered — e.g. `A 4 a (3) — 1 found`
- **Mining sites section** — a third list for bodies with mining signals
- All three lists sort by real planet/moon designation order (including secondary-star-prefixed bodies like "B 6"), not alphabetically
- Toggle via **Show Bio Sites Sidebar** in settings — stays mounted across all six modes, not just RADAR

### Bio Survey Sidebar (right)
- Lists all biology types on the current planet
- Populated from DSS (`SAASignalsFound`) with genus names
- Shows while orbiting a targeted planet (before landing) — unknowns shown if genus names not yet available
- Unknown slots shown as `? Unknown` until DSS is completed
- Each entry shows:
  - **Genus name (underlined and clickable)** — opens a browser to the Elite Dangerous Wiki page for that organism
  - Species name once identified
  - `Payout:` or `FF Payout:` with the expected credit value
  - Pip indicators showing scan progress (blue → green → orange)
- Completed organisms remain listed with all pips filled
- **First Footfall indicator** at the top — `✓ First Footfall` (gold) or `○ First Footfall` (dim)
- **Total Payout** shown at the bottom — only appears once all organisms on the planet are fully scanned
- **GEO SURVEY section** — when **Show Geological Sites Info** is enabled, lists each known geological site with a clickable wiki link
- Scrollable with a slim 6px scrollbar

### Top Bar
- Current system name
- Current body name (or targeted body when in orbit)
- BIO counter — completed/total (e.g. `2/3`)
- Current zoom scale with scroll hint
- **POTENTIAL:** — total possible payout for the current planet
- **System Scan icon** — opens the [System Scan window](#system-scan-window)
- **Scan Log icon** — opens the [Scan Log — Galactic Survey](#scan-log--galactic-survey) window
- **Star Finder icon** — opens the [Star Finder](#star-finder) window
- **⟳ Refresh button** — forces a full journal re-read and state rebuild without restarting the app, useful if the app picks up incorrect data after a journal file switch. Saves a timestamped log snapshot before refreshing (see [Log Snapshots](#log-snapshots))

### Bottom Bar
- Live latitude, longitude, heading, and altitude
- Nearest tracked organism name and distance
- Scan progress pips for the active genus
- **EARNED:** — cumulative total earnings

### Earnings Tracking
- Records credit value of every completed bio scan
- Accounts for **First Footfall** (5× payout multiplier) — confirmed at the moment you Disembark
- Persists across sessions in `EliteBioRadar.earnings.json`
- Settings panel options:
  - **Scan All Journals** — replaces the current total with a full recalculation from all journal history
  - **Scan Date Range** — same as above but limited to journals between two dates
  - **Clear Earnings** — reset to zero (can be restored by scanning journals again)

> **Important:** Each journal scan **replaces** the stored total — it does not add to it. Running the same scan twice will not double the amount. If you want to add a new date range on top of an existing total, use Clear first, then scan the combined range.

### Trip Value Tracking
A separate, resettable "how much has this exploration trip earned so far" total, shown on the System Scan window's Trip Scans bar — independent of the lifetime Earnings total above (which tracks bio-organism sales specifically).

- Updates live as you scan and map bodies, using the same value estimate the System Scan cards show
- **Start New Trip** resets it to zero without touching lifetime Earnings
- **Import Trip Data** rebuilds/backfills it from journal history — all journals, or a chosen date range — merging in by body so nothing already counted gets doubled
- Elapsed time since the trip started is shown in months/days/hours/minutes, whichever two units apply
- Persists across restarts in its own file, unaffected by Earnings' Scan All / Clear actions

### Scan Log — Galactic Survey
A separate browsable window (opened from the **Scan Log icon** in the top bar) covering everything you've ever scanned — not just the current body — organized by real in-game galactic region using an offline boundary map, so results are complete even for systems where nothing happened to get logged as a personal first discovery.

- **Six tabs**: Biology, Geology, Stellar, Worlds, Phenomena, and Journals
- **Group By toggle** on every tab — flip between grouping by Region or by the tab's own type (Genus, Site Type, Star Class, Planet Type, Category)
- **Biology tab** — lifetime organism counts by genus and species, with each species' last scanned location
- **Geology tab** — every geological site ever found, grouped by feature type, showing the first-discovery credit bonus earned for each site
- **Stellar tab** — every star you've scanned, grouped by class, split into First Discovery and Already Catalogued; neutron stars and black holes get a ✦ marker
- **Worlds tab** — every planet you've scanned, grouped by class, with Earthlikes shown in bold; Terraformable-only and Footfalled-only filters, plus a First Footfall breakdown
- **Phenomena tab** — Notable Stellar Phenomena finds (Anomalies, Mineral Formations, Molluscs, Plants, Seed Pods), matching the game's own Codex breakdown
- **Lifetime / Date Range / Since-date filter** — a persistent range picker above every tab
- **Journals tab** — **Scan All** rebuilds the entire library from your full journal history; **Clear** wipes it for a fresh rebuild. Neither touches your actual Elite Dangerous journal files
- Opens as a separate window without disturbing the live radar/info panel underneath

### EDSM Integration
Opt-in, off by default — no data leaves your PC until you enable it in Settings.

- **Live upload** — journal events are submitted to EDSM as you play, with your current ship correctly attributed
- **Background sync** — runs quietly on every app start, catching up roughly the last 2 days of journal history
- **Sync Journals to EDSM button** — runs the same sync on demand
- **Commander Name / API Key** fields — found at edsm.net → Commander Settings → API
- Reachable directly from the System Scan window's own EDSM status chip

### Screenshot Conversion
Opt-in, off by default — needs a real screenshots folder configured (the journal's own screenshot event only ever reports a relative in-game path, never where the game actually saves them on disk).

- **Live conversion** — watches for a new screenshot as you play, waits for the game to finish writing it, converts `.bmp` to `.png`, then deletes the original once the `.png` is confirmed saved
- **Toast notification** — a brief "Screenshot converted" popup on the main window when one finishes
- **Source / destination folders** — pick where Elite saves screenshots and (optionally) a separate folder for the converted PNGs; leave the destination blank to convert in place
- **Convert Existing Screenshots button** — sweeps the source folder for any leftover `.bmp` files right now, not just new ones

### Star Finder
A pop-out window (opened from its own top-bar icon) for finding the nearest star of a chosen type relative to your current system.

- **Star type dropdown** — curated from the canonical Elite Dangerous journal star-type list: Neutron Star, Black Hole, Supermassive Black Hole, White Dwarf, Wolf-Rayet, Herbig Ae/Be, T Tauri, Carbon, Brown Dwarf, every main-sequence class (O through M), and each class's supergiant/giant variant as its own separate entry
- **Closest count** — how many results to return (up to 50)
- Resolves your current system's galactic coordinates via EDSM (works even for a procedurally-named system nobody has visited yet), then queries [Spansh](https://spansh.co.uk)'s galaxy database, sorted by distance
- Each result shows system name, distance, and region, with a **Copy** button per row and a **Copy All** for the whole list
- Copy the system name and paste it into the in-game galaxy map's search box to plot your own route — the app has no way to plot or push a route into Elite Dangerous directly, so this is a lookup-and-copy tool, not automation
- Read-only and user-triggered only — no background network activity, and not tied to the EDSM Integration opt-in above (this is a one-off coordinate lookup, not exploration-data upload/download)

### Settings (⚙ gear icon)
- Show Bio Survey Sidebar (right)
- Show Bio Sites Sidebar (left)
- Radar scan animation (expanding pulse effect)
- Show Geological Sites Info (adds the geological section to both sidebars)
- Auto Scale
- Default Scale (200m – 10km)
- Gravity Warning (enable toggle, threshold in G)
- Screenshot Conversion (enable toggle, source/destination folders, convert-existing button)
- Earnings section with journal scan and clear options
- EDSM Integration (enable toggle, commander name, API key, manual sync)
- About (version, credits, links)

### Session Persistence
- Completed scan locations saved to `EliteBioRadar.cache.json`
- Incomplete scans always rebuilt from journal on startup — never stale
- Bio signal counts and genus names cached per body
- First Footfall status cached per body
- Biological Sites panel backfills from journal history across all sessions
- Planet bio and geo lists cleared and rebuilt cleanly on FSD jump to new system
- Returning to a previously scanned planet reloads completed scan history
- Window position and size restored on launch, with off-screen safety fallback
- All writes are atomic (written to a temp file, then swapped in), so a hard kill of the app or the game mid-write can't corrupt a data file

---

## Usage

1. Launch **EliteBioRadar.exe** — works before or after launching Elite Dangerous
2. Fly to a planet with biology signals
3. Point the **FSS scanner** at the planet to register signal counts
4. Perform a **Detailed Surface Scan (DSS)** to populate genus names in the sidebar
5. Land and begin scanning — dots appear on the radar as you scan each organism
6. The radar shows your position relative to all scan sites, with colony range rings to help plan your route between them
7. After the third scan, wait for the **Analyse** prompt — all dots go grey and the organism is logged

Along the way, open **System Scan** from the top bar for a dashboard of the whole system, or **Scan Log** for your lifetime survey. Both work whether or not you're currently in the system they cover.

### VR Usage
EliteBioRadar is a standard Windows application and works in any VR environment that supports pinning Windows apps into the playspace, including:
- **Meta Quest** (via Air Link, Virtual Desktop, or Meta PC app)
- **SteamVR** with desktop overlay tools
- Any headset using Windows Mixed Reality

Launch the app before entering VR, then pin or overlay it in your preferred position. The app's dark theme and high-contrast colour scheme are optimised for readability at typical VR overlay distances.

---

## Scan Sequence

The game writes four journal events per organism:

| ScanType | Dot Colour | Description |
|---|---|---|
| `Log` | 🔵 Blue | First interaction — fires on first-ever encounter of a species |
| `Sample` | 🟢 Green | Second location |
| `Sample` | 🟠 Orange | Third location |
| `Analyse` | ⚫ All grey | Completion — genetic sampler resets for next organism |

> **Note:** On planets where you've previously encountered a species, the `Log` event is skipped and the sequence starts at `Sample`.

If you abandon a scan mid-sequence and switch to a different organism, the incomplete dots turn grey as reference markers. When you return to scan that genus again, the grey dots clear and the sequence restarts from the beginning.

---

## Building from Source

Requires **.NET 10.0 SDK** (Windows only). See [BUILDING.md](BUILDING.md) for full instructions.

```
dotnet restore
dotnet publish -c Release
```

Output: `bin\Release\net10.0-windows\BioRadar-App\`

---

## How It Works

### Status.json polling
Elite Dangerous writes `Status.json` every ~250ms. The app polls every 300ms with a 500ms startup offset to avoid conflicts with other tools (e.g. Stream Deck plugins). Provides live lat/lon, heading, altitude, body name, fuel level, and the FSD's `Flags`/`Flags2` bits.

FSD hyperdrive charging (which drives the automatic switch to the DESTINATION tab) is read directly from `Flags2` bit 19 (`FsdHyperdriveCharging`) rather than the journal's `StartJump` event — that journal write can lag or occasionally not land promptly, while `Status.json` reflects the charge the instant it starts and clears the instant it stops. `Flags` bit 17 (`FsdCharging`) is set for both a real hyperspace charge and a plain supercruise charge and so isn't specific enough on its own; `Flags2` bit 19 only fires for an actual jump.

### NavRoute.json / route persistence
`NavRoute.json` only ever shows the route from your current position onward — it shrinks every jump and never lists hops already passed. Its **last** entry (the final destination) is the one thing that stays constant for a route's entire lifetime, so it's used as the identity key for the persisted route cache (`EliteBioRadar.route.json`): as long as the final destination still matches, the cached full route/hop position/total distance are reused; the moment it changes, that's treated as a new route and the cache resets.

### Nav-panel signal resolution
A signal picked in the nav panel (e.g. a Planetary Mining Location Signal) reports a `Destination.Name` like `$SAA_Unknown_Signal:#type=$PlanetaryMiningLocation_Name;:#index=13;` — not a real body name. The app matches this pattern, reads `Destination.Body` (the signal's real parent planet's BodyID), and resolves it against known scanned bodies so the Planet tab shows the correct target instead of falling back to the primary star.

### Deorbit/Launch telemetry
Altitude and vertical speed are derived from `Status.json` updates that only actually change a few times a second, dead-reckoned forward between updates using the last known sink rate, bounded so one noisy reading can't fling the estimate off course. Progress along the glide arc is monotonic — it can only move forward, never backward, however the underlying estimate jitters. Direction (ascending vs descending) requires a sustained reading before committing to either the launch or descent scene, and the animation state is fully reset on every entry to and exit from the mode, so a stale leftover from a previous glide can't bleed into the next one.

### Journal events used

| Event | Purpose |
|---|---|
| `ScanOrganic` | Each scan interaction — drives dot placement and colour |
| `SAASignalsFound` | DSS completion — provides genus names, bio count, and geological signal counts |
| `SAAScanComplete` | DSS mapping complete — flips a body's scan-state tag to `MAPPED` |
| `FSSBodySignals` | FSS scan — registers biology and geology signal counts per body |
| `Scan` | Star/planet/asteroid-belt scan — provides `WasFootfalled`/`WasDiscovered`/`WasMapped` for payout and discovery-credit calculation, and the physical detail shown on the STAR/PLANET/System Scan views |
| `FSDTarget` | Nav-panel/galaxy-map target set — drives the DESTINATION tab's next-system info |
| `Loadout` / `LoadGame` | Ship jump range/fuel capacity, FSD module stats, and current ship type/ID — used for live jump range and EDSM attribution |
| `StartJump` | Backup signal for FSD charging (see Status.json polling above) |
| `Disembark` | Confirms First Footfall when player steps off ship on an unvisited planet |
| `Touchdown` | Body detection — loads cached scan history |
| `LeaveBody` | Clears radar display, preserves cache |
| `FSDJump` / `CarrierJump` | Clears display, wipes cache for old body, clears and rebuilds planet lists for new system |
| `Materials` / `MaterialCollected` / `MaterialDiscarded` / `MaterialTrade` / `EngineerCraft` / `Synthesis` / `TechnologyBroker` | Keeps a live tally of your raw-material stock for System Scan's "Hide Full Mats" filter |

### Journal backfill
On startup, the app scans up to 60 recent journal files (in batches, starting at 20) to rebuild state for the current body — scan dot positions, genus names, bio counts, first footfall status. The Biological Sites and Geological Sites panels scan all journal files to find every relevant planet in the current system. The STAR/PLANET/DESTINATION info panel state (current star, targeted body, route) is rebuilt the same way from the last 20 journal files. This works whether or not the game is running.

The app also reads recent journals during startup to establish the current system before backfill begins, so cached data from a previous system is never shown by mistake.

A live scan's actual ship position is only used for a scan happening live, in the moment — never as a substitute during backfill for a historical scan whose position can't otherwise be determined. That way a relaunch after a hard close can't misplace a dot at wherever the ship happens to be sitting right now.

Journal files are sorted by their real embedded date, not by filename text — Elite Dangerous changed its journal filename format in 2022, and a plain text sort silently misorders files for anyone whose journal history spans that change, which could make the app treat a stale file as the current one.

### First Footfall detection
The `Scan` event (fired during DSS from orbit) carries a `WasFootfalled` flag. If false, the app sets a pending first footfall state. This is confirmed — and the `✓ First Footfall` indicator activated — only when a `Disembark` event fires, meaning you physically stepped off your ship on the planet surface.

### Discovery / mapped-by-other detection
A `Scan`'s `WasDiscovered`/`WasMapped` flags only mean "someone had already done this before this particular scan" — and that someone can be you, revisiting a body from an earlier trip. To tell your own history apart from another commander's, the app separately tracks every body you've personally discovered (a `Scan` you made with `WasDiscovered:false`) or mapped (a `SAAScanComplete` you made) across your full journal history. The "D"/"M" badges only fire when the flag says someone got there first AND your own history confirms it wasn't you.

### Colony ranges
Each genus has a community-documented minimum distance between scan sites. Active scan rings use a solid border with a colour-matched diagonal hatch fill. Completed rings use a faint dashed border only. Examples: Bacterium = 500m, Osseus = 800m, Tubus = 800m, Aleoida = 150m.

### No interference design
Uses polling instead of `FileSystemWatcher` for `Status.json` to avoid conflicts with Stream Deck plugins or other tools. All file reads use `FileShare.ReadWrite | FileShare.Delete`.

A `FileSystemWatcher` is used exclusively to detect when Elite Dangerous creates a new `Journal.*.log` file. When a new journal is detected, the app waits 1.5 seconds for the game to finish writing the file header, then automatically triggers a full state refresh — the same operation as clicking the ⟳ Refresh button manually.

### Log Snapshots
Each time the ⟳ Refresh button is clicked, the app saves a timestamped copy of the current diagnostic log to the app folder before wiping and rebuilding state. The snapshot filename follows the format `EliteBioRadar_YYYY-MM-DD_HH-MM-SS.log`. Snapshots accumulate in the app folder and can be safely deleted at any time.

### Single instance
A named system mutex prevents more than one instance of the app from running simultaneously, protecting the cache file from concurrent write conflicts.

---

## Runtime Files

These files are created next to the exe and are excluded from the repository:

| File | Purpose |
|---|---|
| `EliteBioRadar.cache.json` | Scan location and body metadata cache |
| `EliteBioRadar.settings.json` | Saved app settings, including window position and size |
| `EliteBioRadar.earnings.json` | Persistent lifetime earnings history |
| `EliteBioRadar.trip.json` | Persistent trip-value total (System Scan's Trip Scans bar), independent of lifetime Earnings |
| `EliteBioRadar.regions.json` | System-to-galactic-region lookup cache, used by the Scan Log's Region grouping |
| `EliteBioRadar.route.json` | Persisted full plotted route (DESTINATION tab), so hop count/progress survive an app or game restart mid-route |
| `EliteBioRadar.log` | Diagnostic log (overwritten each launch) |
| `EliteBioRadar_YYYY-MM-DD_HH-MM-SS.log` | Timestamped log snapshot — created automatically each time the ⟳ Refresh button is clicked |

---

## Credits
Radar icon by [Good Ware](https://www.flaticon.com/free-icons/radar) via Flaticon

## License
MIT — see [LICENSE](LICENSE)
