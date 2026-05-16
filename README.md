# Elite Bio Radar

A standalone portable Windows application for Elite Dangerous Odyssey. Displays a real-time radar of bio-organism scan locations on planetary surfaces, tracks payout values, and helps you navigate between biology sites across an entire star system.

Designed to run on a second monitor or touchscreen alongside the game. **VR ready** — can be pinned in Meta Quest, Virtual Desktop, or any VR environment that supports pinning Windows applications into your playspace.

---

## Interface Overview

![image](screenshots/fullscan.png)
- **Top menu** shows: 
   - The name of the current system.
   - How many bio sites on selected / current planet.
   - The current radar scaling.
   - Potential earnings (once a DSS has been completed on the planet).
- **Bottom menu** shows:
  - Current Latitude and Longitude (if on planet).
  - Active heading (on Foot, Ship, SRV, or Fighter).
  - Altitude.
  - The distance to nearest previous scan location.
  - Color PIPs to identify how many scans on the current biological sample have been completed.
  - How much you have earned since using the app / since log file importation.
- **Biological** and **Bio Survey** side-panels can be activated and deactivated for your desired look and information.
- **Biological Sites** side-panel:
   - Shows planets with Biological Sites as identified by FSS.
   - Shows an arrow on your current planet.
   - Greys out planet name once biological scans have been completed.
- **Bio Survey** side-panel:
   - First Footfall notification (will not highlight on non first footfall planets).
   - Biological types from DSS scan.
   - Biological Genus is clickable, opening a browser to its elite-dangerous.fandom.com/wiki page.
   - Shows payout for the completed biological scan (adjust for regular and first footfall values).
   - Shows total scanned payout for the current planet.
 
--- 
## Settings Overview

Checkboxes to: 
- Enable Bio Survey Sidebar.
- Allow Planet information panel overlay radar (not typically recommended).
- Keep Biological Sites panel open.
- Auto Scale radar (highly recommended).
- Default Scale for radar (when not using auto scale).
   - In this mode, the scaling can adjusted using the scroll wheel of mouse when hovering over the radar.
- Journal Scanning:
   - You can choose to scan all journal files for lifetime earnings.
   - You can select start / end dates of journal files to scan.  Helpful to know current exploration outing profits.
   - You can clear your earning if you want to start a fresh count. 
  
---
## Features

### Radar
- **North-up display** — ship always centred, North always at the top
- **Heading arrow** — shows your current facing direction
- **Colony range rings** — diagonal-hatched fill per organism showing the species exclusion zone (minimum distance between scans of the same genus), colour-matched to the scan dot
- **Mouse-wheel zoom** — 100m to 10km range
- **Auto-scale mode** — automatically zooms to keep all active scan sites in view (ignores completed grey dots)
- **Scan dot colours:**
  - 🔵 Blue — first scan (Log)
  - 🟢 Green — second scan (Sample)
  - 🟠 Orange — third scan (Sample) — turns grey when Analyse completes
  - ⚫ Grey — fully logged, shown as a faint reference marker
- **Off-screen indicators** — dots outside the current zoom range are clamped to the radar edge

### Biological Sites Panel (left toggle)
- Lists every planet in the current system that has biological signals
- Populated from FSS and DSS scans, backfilled from journal history on startup
- Shows short body name and bio signal count — e.g. `▶ 5 C (2)`
- Current body highlighted with a `▶` indicator
- Planet names go grey once all biology on that body is fully logged
- Toggle button (planet icon) at the top-left of the radar opens/closes the panel
- **Settings options:**
  - Panel can shrink the radar or overlay on top of it
  - Keep panel open between sessions

### Bio Survey Sidebar (right)
- Lists all biology types on the current planet
- Populated from DSS (`SAASignalsFound`) with genus names
- Shows while orbiting a targeted planet (before landing)
- Unknown slots shown as `? Unknown` until scanned
- Each entry shows:
  - Genus name (underlined, links to the Elite Dangerous Wiki)
  - Species name
  - `Payout:` or `FF Payout:` with the expected credit value
  - Pip indicators showing scan progress (blue → green → orange)
- Completed organisms remain listed with all pips filled
- **First Footfall indicator** at the top — `✓ First Footfall` (gold, confirms when you Disembark) or `○ First Footfall` (dim)
- **Total Payout** shown at the bottom of the sidebar
- Scrollable with a slim 6px scrollbar

### Top Bar
- Current body name (or targeted body when in orbit)
- BIO counter — completed/total (e.g. `2/3`)
- Current zoom scale with scroll hint
- **POTENTIAL:** — total possible payout for the current planet

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

### Settings (⚙ gear icon)
- Show Bio Survey Sidebar
- Biological Sites panel overlays radar (vs shrinking it)
- Keep Bio Sites panel open between sessions
- Auto Scale
- Default Scale (200m – 10km)
- Earnings section with journal scan and clear options
- About (version, credits, links)

### Session Persistence
- Completed scan locations saved to `EliteBioRadar.cache.json`
- Incomplete scans always rebuilt from journal on startup — never stale
- Bio signal counts and genus names cached per body
- First Footfall status cached per body
- Biological Sites panel backfills from journal history across all sessions
- Planet bio list cleared on FSD jump to new system
- Returning to a previously scanned planet reloads completed scan history

---

## Usage

1. Launch **EliteBioRadar.exe** — works before or after launching Elite Dangerous
2. Fly to a planet with biology signals
3. Point the **FSS scanner** at the planet to register signal counts
4. Perform a **Detailed Surface Scan (DSS)** to populate genus names in the sidebar
5. Land and begin scanning — dots appear on the radar as you scan each organism
6. The radar shows your position relative to all scan sites, with colony range rings to help plan your route between them
7. After the third scan, wait for the **Analyse** prompt — all dots go grey and the organism is logged

### VR Usage
EliteBioRadar is a standard Windows application and works in any VR environment that supports pinning Windows apps into the playspace, including:
- **Meta Quest** (via Air Link, Virtual Desktop, or Meta PC app)
- **SteamVR** with desktop overlay tools
- Any headset using Windows Mixed Reality

Launch the app before entering VR, then pin or overlay it in your preferred position. The app's dark theme and high-contrast cyan colour scheme are optimised for readability at typical VR overlay distances.

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

Requires **.NET 8.0 SDK** (Windows only). See [BUILDING.md](BUILDING.md) for full instructions.

```
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output: `bin\Release\net8.0-windows\BioRadar-App\`

---

## How It Works

### Status.json polling
Elite Dangerous writes `Status.json` every ~250ms. The app polls every 300ms with a 500ms startup offset to avoid conflicts with other tools (e.g. Stream Deck plugins). Provides live lat/lon, heading, altitude, and body name.

### Journal events used

| Event | Purpose |
|---|---|
| `ScanOrganic` | Each scan interaction — drives dot placement and colour |
| `SAASignalsFound` | DSS completion — provides genus names and bio count |
| `FSSBodySignals` | FSS scan — registers biology signal counts per body |
| `Scan` | Planet scan — provides `WasFootfalled` for payout calculation |
| `Disembark` | Confirms First Footfall when player steps off ship on an unvisited planet |
| `ApproachBody` / `Touchdown` | Body detection — loads cached scan history |
| `LeaveBody` | Clears radar display, preserves cache |
| `FSDJump` | Clears display, wipes cache for old body, clears planet list |

### Journal backfill
On startup, the app scans up to 20 recent journal files to rebuild state for the current body — including scan dot positions, genus names, bio counts, and first footfall status. The Biological Sites panel scans all journal files to find every planet with bio signals in the current system. This works whether or not the game is running.

### Colony ranges
Each genus has a community-documented minimum distance between scan sites. The radar draws this as a dashed ring with a colour-matched diagonal hatch fill. Examples: Bacterium = 500m, Osseus = 800m, Tubus = 800m, Aleoida = 150m.

### No interference design
Uses polling instead of `FileSystemWatcher` for `Status.json` to avoid conflicts with Stream Deck plugins or other tools. All file reads use `FileShare.ReadWrite | FileShare.Delete`.

---

## Runtime Files

These files are created next to the exe and are excluded from the repository:

| File | Purpose |
|---|---|
| `EliteBioRadar.cache.json` | Scan location and body metadata cache |
| `EliteBioRadar.settings.json` | Saved app settings |
| `EliteBioRadar.earnings.json` | Persistent earnings history |
| `EliteBioRadar.log` | Diagnostic log (overwritten each launch) |

---

## Credits
Radar icon by [Good Ware](https://www.flaticon.com/free-icons/radar) via Flaticon

## License
MIT — see [LICENSE](LICENSE)
