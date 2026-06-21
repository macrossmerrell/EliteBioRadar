# Elite Bio Radar

A standalone portable Windows application for Elite Dangerous Odyssey. Displays a real-time radar of bio-organism scan locations on planetary surfaces, tracks biological and geological sites across an entire star system, tracks payout values, and helps you navigate between biology sites efficiently.

Designed to run on a second monitor or touchscreen alongside the game. 

**VR ready** — can be pinned in Meta Quest, Virtual Desktop, or any VR environment that supports pinning Windows applications into your playspace.

Latest Release can be found here: https://github.com/macrossmerrell/EliteBioRadar/releases

---

## Application Overview

![image](https://github.com/macrossmerrell/EliteBioRadar/blob/e6a68f403e648b42e44c283b7357baebfe9a29af/screenshots/fullscan2.png)

- **Top menu** shows:
   - The name of the current system.
   - How many bio sites on selected / current planet.
   - The current radar scaling.
   - Potential earnings (once a DSS has been completed on the planet).
- **Bottom menu** shows:
  - Current Latitude and Longitude (if on planet).
  - Active heading (on foot, ship, SRV, or fighter).
  - Altitude.
  - The distance to nearest previous scan location.
  - Colour PIPs to identify how many scans on the current biological sample have been completed.
  - How much you have earned since using the app / since log file importation.

![image](https://github.com/macrossmerrell/EliteBioRadar/blob/e6a68f403e648b42e44c283b7357baebfe9a29af/screenshots/SettingsMenu2.png)
- **Biological Sites**, **Geological Sites**, and **Bio Survey** side-panels can be activated and deactivated for your desired look and information.
- **Biological Sites** side-panel:
   - Shows planets with biological sites as identified by FSS.
   - Shows an arrow on your current planet.
   - Greys out planet name once all biological scans have been completed.
   - Clicking a planet in the list previews that planet's bio data in the Bio Survey sidebar.
- **Geological Sites** side-panel:
   - Shows planets with geological signals as revealed by DSS.
   - Displays the number of geological sites per planet and how many have been discovered.
   - Clicking a geological site entry opens a browser to its Elite Dangerous Wiki page.
- **Bio Survey** side-panel:
   - First Footfall notification (activates the moment you step off your ship on an unvisited planet).
   - Biological types from DSS scan, shown as unknowns until DSS is completed.
   - Biological genus name is clickable, opening a browser to its elite-dangerous.fandom.com/wiki page.
   - Shows payout for each completed biological scan (adjusted for regular and first footfall values).
   - Shows total scanned payout for the current planet once all scans are complete.

---

### What's New in Version 2.5.0

**Bug Fixes & Improvments**
- Abandoned scan detection — switching to a different organism mid-scan without completing the previous one now correctly clears the Bio Survey pips for the abandoned genus instead of leaving it stuck at partial completion.
- Corrupt cache recovery — the app now automatically detects and cleans up stale incomplete-but-marked-complete cache entries left behind by earlier versions, so affected genera display correctly without manual cache deletion.
- Cross-session abandonment logic — fixed an issue where an older session's in-progress scan could incorrectly wipe a newer session's scan progress for a different genus during journal backfill.
- False "First Footfall" on flyby — fixed an issue where simply passing near a planet in supercruise (without landing) could incorrectly trigger First Footfall and load stale scan data for that planet.
- Planet name truncation after journal switch — fixed a system-name detection bug that occasionally caused the currently-displayed planet's short name to be cut too short (e.g. "A" instead of "1 A") after a journal rollover.

## What's New in Version 2.4.0

### Geological Site Markers
**First scanned** geological site is now marked directly on the radar display. 

- When you scan a **new** geological type on a body, an X appears at that location in the same amber color used by the GEO sidebar panel. 
- As you zoom out, the marker automatically transitions to a compact dot to keep the display clean at wider scales. 
- Off-screen sites are clamped to the radar edge so you always know which direction to head. Markers persist across sessions via the scan cache and are restored on body revisit.

### Refresh & Auto-Recovery

⟳ **Refresh button** — added to the top bar next to the settings gear. Clicking it performs a full journal re-read and state rebuild without needing to restart the app. Useful when the app picks up incorrect data after Elite Dangerous rolls over to a new journal file mid-session.

**Automatic log snapshot** — before any refresh runs, the app automatically saves a timestamped copy of the diagnostic log to the app folder (EliteBioRadar_YYYY-MM-DD_HH-MM-SS.log), capturing exactly what the app saw at the moment the problem occurred. No dialog, no extra steps.

**Automatic journal file detection** — the app now watches for new Journal.*.log files being created by the game and triggers a refresh automatically, so most journal rollover issues resolve themselves without any manual intervention.

### Total Payout Display

**Total Payout** in the Bio Survey sidebar now only appears once biological scans are complete — it no longer shows prematurely while scanning is in progress.
- The total increments organism by organism as each one is fully logged, rather than showing a speculative total.
- Total Payout now correctly persists across app restarts — completed scans are restored from cache and reflected in the total immediately on launch.

### Geological Sites
**Show Geological Sites Info** toggle in settings — displays a dedicated Geological Sites panel listing planets with geological signals in the current system.
- Toggling the geological sites option now instantly refreshes the Bio Survey sidebar without requiring the sidebar to be manually toggled off and on.
- Geological sites are populated from `FSSBodySignals` and `SAASignalsFound` events, consistent with how the game reveals them (geo sites on a body only appear after that body has been DSS scanned).

### Session & Journal Improvements
- **Correct system detection on startup** — the app now scans recent journal files during startup to establish the current system before any backfill runs, preventing stale data from a previous system appearing in the sidebars
- **Cross-system cached body rejection** — if the last cached body is from a different system than the one you're currently in, the app now correctly skips backfilling that body's scan data rather than repopulating the Bio Survey sidebar with irrelevant old information
- **Journal rollover handling** — when Elite Dangerous creates a new journal file mid-session, the app now correctly preserves the known current system rather than reverting to an old cached body's system
- **Planet list rebuilt cleanly on system change** — the Biological Sites and Geological Sites panels are now fully cleared and rebuilt when switching systems, preventing leftover entries from a previous system appearing alongside the current one
- **Progressive journal search** — on startup the app searches up to 60 journal files in batches of 10 (starting from 20) when looking for scan history, rather than stopping at a hard limit of 20 files. Expansion only occurs if scan data has not yet been found, so typical sessions are unaffected
- **FSDJump system tracking fix** — corrected an issue where `StarSystem` was read from the wrong JSON field name in journal events, causing system identification to silently fall back to body-name prefix matching and occasionally match bodies from other systems
- **Completed Genera restored from cache on startup** — completed organisms loaded from cache at launch are now correctly registered as completed, so the Total Payout and sidebar completion state are accurate from the moment the app opens
- **Abandoned scan detection** — when switching to a different organism mid-scan without completing the previous one (no Analyse event), the Bio Survey now correctly clears the pips for the abandoned genus. Previously, switching genera left both showing partial pips. The abandoned genus resets to zero and will rebuild correctly if re-scanned later.
- **Corrupt cache recovery** — a previous version of the abandoned scan logic saved greyed (incomplete) organisms to the cache instead of removing them, causing them to appear fully complete on the next app launch. The app now detects and clears these corrupt entries automatically during startup backfill, so no manual cache deletion is required going forward.
- **Cross-session abandonment logic** — the backfill now correctly handles the case where an in-progress scan from an older session exists alongside a newer session's scan of a different genus. Previously, the older session's data could incorrectly remove the newer session's scan dots on app restart.

### Window Management
- **Window position and size persistence** — the app now remembers its position and size between sessions, including which monitor it was on. Position is saved automatically as you move or resize the window
- **Off-screen safety check** — if the saved position is no longer on a connected screen (e.g. a monitor was unplugged), the app falls back to the default centered position rather than opening off-screen

### Single Instance Enforcement
- Launching a second instance of the app now shows a friendly message and exits immediately, preventing cache corruption that could occur from two instances running simultaneously

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
- **Geological scan markers** — discovered geological sites are shown on the radar as distinct markers separate from biological scan dots, so you can see geo site locations alongside your bio scan history

### Biological Sites Panel (left toggle)
- Lists every planet in the current system that has biological signals
- Populated from FSS and DSS scans, backfilled from journal history on startup — including planets scanned in previous sessions
- Shows short body name and bio signal count — e.g. `▶ A 4 (3)`
- Current body highlighted with a `▶` indicator
- Planet names go grey once all biology on that body is fully logged
- **Click any planet** to preview its bio data in the Bio Survey sidebar
- Toggle button (planet icon) at the top-left of the radar opens/closes the panel
- **Settings options:**
  - Panel can shrink the radar or overlay on top of it
  - Keep panel open between sessions

### Geological Sites Panel
- Lists every planet in the current system with geological signals, populated from DSS scans
- Shows short body name, geo signal count, and number of sites already discovered — e.g. `A 4 a (3) — 1 found`
- Only appears for bodies that have been DSS scanned, consistent with how the game reveals geological signals
- **Geological site names are clickable** — opens a browser directly to the relevant Elite Dangerous Wiki page for that site type
- Toggle via **Show Geological Sites Info** in settings

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
- **First Footfall indicator** at the top — `✓ First Footfall` (gold, confirms the moment you Disembark) or `○ First Footfall` (dim)
- **Total Payout** shown at the bottom — only appears once all organisms on the planet are fully scanned, incrementing as each one completes
- Scrollable with a slim 6px scrollbar

### Top Bar
- Current body name (or targeted body when in orbit)
- BIO counter — completed/total (e.g. `2/3`)
- Current zoom scale with scroll hint
- **POTENTIAL:** — total possible payout for the current planet
- **⟳ Refresh button** — forces a full journal re-read and state rebuild without restarting the app. Useful if the app picks up incorrect data after a journal file switch. Automatically saves a timestamped log snapshot to the app folder before refreshing (see [Log Snapshots](#log-snapshots) below)

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
- Show Geological Sites Info
- Biological Sites panel overlays radar (vs shrinking it)
- Keep Bio Sites panel open between sessions
- Radar scan animation (expanding pulse effect)
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
- Planet bio and geo lists cleared and rebuilt cleanly on FSD jump to new system
- Returning to a previously scanned planet reloads completed scan history
- Window position and size restored on launch, with off-screen safety fallback

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

Requires **.NET 8.0 SDK** (Windows only). See [BUILDING.md](BUILDING.md) for full instructions.

```
dotnet restore
dotnet publish -c Release
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
| `SAASignalsFound` | DSS completion — provides genus names, bio count, and geological signal counts |
| `FSSBodySignals` | FSS scan — registers biology and geology signal counts per body |
| `Scan` | Planet scan — provides `WasFootfalled` for payout calculation |
| `Disembark` | Confirms First Footfall when player steps off ship on an unvisited planet |
| `Touchdown` | Body detection — loads cached scan history |
| `LeaveBody` | Clears radar display, preserves cache |
| `FSDJump` / `CarrierJump` | Clears display, wipes cache for old body, clears and rebuilds planet lists for new system |

### Journal backfill
On startup, the app scans up to 60 recent journal files (in batches, starting at 20) to rebuild state for the current body — including scan dot positions, genus names, bio counts, and first footfall status. The Biological Sites and Geological Sites panels scan all journal files to find every relevant planet in the current system. This works whether or not the game is running.

The app also reads recent journals during startup to establish the current system before backfill begins, ensuring that cached data from a previous system is never incorrectly shown.

### First Footfall detection
The `Scan` event (fired during DSS from orbit) carries a `WasFootfalled` flag. If false, the app sets a pending first footfall state. This is confirmed — and the `✓ First Footfall` indicator activated — only when a `Disembark` event fires, meaning you physically stepped off your ship on the planet surface.

### Colony ranges
Each genus has a community-documented minimum distance between scan sites. Active scan rings use a solid border with a colour-matched diagonal hatch fill. Completed rings use a faint dashed border only. Examples: Bacterium = 500m, Osseus = 800m, Tubus = 800m, Aleoida = 150m.

### No interference design
Uses polling instead of `FileSystemWatcher` for `Status.json` to avoid conflicts with Stream Deck plugins or other tools. All file reads use `FileShare.ReadWrite | FileShare.Delete`.

A `FileSystemWatcher` is used exclusively to detect when Elite Dangerous creates a new `Journal.*.log` file. When a new journal is detected, the app waits 1.5 seconds for the game to finish writing the file header, then automatically triggers a full state refresh — the same operation as clicking the ⟳ Refresh button manually.

### Log Snapshots
Each time the ⟳ Refresh button is clicked, the app saves a timestamped copy of the current diagnostic log to the app folder before wiping and rebuilding state. The snapshot filename follows the format `EliteBioRadar_YYYY-MM-DD_HH-MM-SS.log`. This preserves a point-in-time record of what the app saw before the refresh, which is useful for diagnosing journal-switch issues. Snapshots accumulate in the app folder and can be safely deleted at any time.

### Single instance
A named system mutex prevents more than one instance of the app from running simultaneously, protecting the cache file from concurrent write conflicts.

---

## Runtime Files

These files are created next to the exe and are excluded from the repository:

| File | Purpose |
|---|---|
| `EliteBioRadar.cache.json` | Scan location and body metadata cache |
| `EliteBioRadar.settings.json` | Saved app settings, including window position and size |
| `EliteBioRadar.earnings.json` | Persistent earnings history |
| `EliteBioRadar.log` | Diagnostic log (overwritten each launch) |
| `EliteBioRadar_YYYY-MM-DD_HH-MM-SS.log` | Timestamped log snapshot — created automatically each time the ⟳ Refresh button is clicked, capturing state at the moment of refresh |

---

## Credits
Radar icon by [Good Ware](https://www.flaticon.com/free-icons/radar) via Flaticon

## License
MIT — see [LICENSE](LICENSE)
