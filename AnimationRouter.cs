using System;
using System.Collections.Generic;

namespace EliteBioRadar
{
    // Where an animation may be shown: the Elite Bio Radar window, the System Notifications window, both, or neither (off).
    [Flags]
    public enum AnimationSurfaces { None = 0, Main = 1, Notifications = 2, Both = Main | Notifications }

    // One entry per animation the app can play. Group is the heading in the settings list; BackdropOpacity is how much of whatever lies
    // underneath is hidden while it plays (1 = fully covered).
    // PreviewSubject/PreviewDetail are the sample text the Notification Settings window's Play button uses.
    // PreviewCycle = the details Play steps through, one per press, for animations with several modes (gear down / up); null = always PreviewDetail.
    // Live = a screen driven continuously by the ship's state (the atmosphere scenes) rather than a one-shot played through the router:
    // MainWindow shows it in the windows ticked here for as long as the phase lasts, so it has no Play preview.
    public sealed record AnimationDef(string Id, string Name, string Group, AnimationSurfaces DefaultSurfaces, double BackdropOpacity,
                                      string PreviewSubject, string PreviewDetail, bool Live = false, string[]? PreviewCycle = null);

    public static class AnimationCatalog
    {
        public static readonly AnimationDef FirstDiscovery =
            new("first-discovery", "First discovery", "Discovery", AnimationSurfaces.Main, 1.0, "Sample System", "M-Type");

        // Detail = "LABEL=value|LABEL=value": the stats line each of these shows under its title.
        public static readonly AnimationDef Landed =
            new("landed", "Landed on a planet", "Surface", AnimationSurfaces.Main, 1.0, "Qiedea EI-O c8-1 A 1", "GRAVITY=0.89 G|SURFACE=214 K|ATMOSPHERE=None|~STAR=3800|~FLUX=0.8|~GROUND=8A7A6A");
        public static readonly AnimationDef Liftoff =
            new("liftoff", "Lift-off from a planet", "Surface", AnimationSurfaces.Main, 1.0, "Qiedea EI-O c8-1 A 1", "GRAVITY=0.89 G|ATMOSPHERE=None|~STAR=3800|~FLUX=0.8|~GROUND=8A7A6A");
        public static readonly AnimationDef FirstFootfall =
            new("first-footfall", "First footfall on a world", "Surface", AnimationSurfaces.Main, 1.0, "Qiedea EI-O c8-1 A 1", "~STAR=3800|~FLUX=0.8|~GROUND=8A7A6A");
        public static readonly AnimationDef HyperspaceJump =
            new("hyperspace-jump", "Hyperspace jump (departure)", "Jump", AnimationSurfaces.Main, 0.92, "HIP 66074", "PREVIEW|K|18.4|3|12|1");
        public static readonly AnimationDef Supercruise =
            new("supercruise", "Entering supercruise", "Jump", AnimationSurfaces.Both, 0.85, "", "PREVIEW");
        public static readonly AnimationDef SupercruiseOverdrive =
            new("supercruise-overdrive", "Supercruise overdrive engaged / disengaged", "Jump", AnimationSurfaces.Both, 0.85, "", "ON", PreviewCycle: new[] { "ON", "OFF" });
        public static readonly AnimationDef SupercruiseAssist =
            new("supercruise-assist", "Supercruise assist engaged / disengaged", "Jump", AnimationSurfaces.Both, 0.85, "", "ON", PreviewCycle: new[] { "ON", "OFF" });
        public static readonly AnimationDef SupercruiseExit =
            new("supercruise-exit", "Exiting supercruise", "Jump", AnimationSurfaces.Both, 0.85, "", "PREVIEW");
        public static readonly AnimationDef Supercharge =
            new("supercharge", "FSD supercharge (neutron star / white dwarf)", "Jump", AnimationSurfaces.Both, 0.85, "", "NEUTRON|4", PreviewCycle: new[] { "NEUTRON|4", "WHITE DWARF|1.5" });
        public static readonly AnimationDef Route =
            new("route", "Route plotted / cancelled / complete", "Jump", AnimationSurfaces.Both, 0.85, "", "PLOTTED|12|184|KMFKMAFK", PreviewCycle: new[] { "PLOTTED|12|184|KMFKMAFK", "CANCELLED|12|184|KMFKMAFK", "COMPLETE|12|184|KMFKMAFK" });
        public static readonly AnimationDef FssComplete =
            new("fss-complete", "FSS complete (all bodies found)", "Exploration", AnimationSurfaces.Both, 0.85, "Sample System", "14");
        public static readonly AnimationDef CodexEntry =
            new("codex-entry", "New codex entry", "Exploration", AnimationSurfaces.Both, 0.85, "Fonticulua Digitos", "BIOLOGICAL AND GEOLOGICAL|FLORA");
        public static readonly AnimationDef Mapped =
            new("planet-mapped", "Planet mapped", "Exploration", AnimationSurfaces.Both, 0.85, "A 6 a", "4|5", PreviewCycle: new[] { "4|5", "7|5", "2|3" });
        public static readonly AnimationDef Material =
            new("material", "Material collected", "Exploration", AnimationSurfaces.Both, 0.85, "", "Iron|Raw|iron|3|190|300", PreviewCycle: new[] { "Iron|Raw|iron|3|190|300", "Carbon|Raw|carbon|3|300|300", "Basic Conductors|Manufactured|basicconductors|2|-1|-1" });
        public static readonly AnimationDef CombatAlert =
            new("combat-alert", "Under attack / target locked / scanned", "Combat", AnimationSurfaces.Both, 0.85, "", "ATTACK|You", PreviewCycle: new[] { "ATTACK|You", "LOCK|Federal Gunship|Cmdr Vance|Deadly|Federal Navy|Wanted|100|100", "SCAN|Cargo" });
        public static readonly AnimationDef ExobiologySold =
            new("exobiology-sold", "Exobiology data sold", "Exploration", AnimationSurfaces.Both, 0.85, "", "3|Bacterium Aurasus|48200000|96400000", PreviewCycle: new[] { "3|Bacterium Aurasus|48200000|96400000", "1|Osseus Fractus|4800000|0" });
        public static readonly AnimationDef TradeProfit =
            new("trade-profit", "Cargo sold (profit / loss)", "Cargo", AnimationSurfaces.Both, 0.85, "", "Gold|64|40000|58000", PreviewCycle: new[] { "Gold|64|40000|58000", "Tritium|32|48000|41000" });
        public static readonly AnimationDef Crime =
            new("crime", "Crime / bounty on you / fines paid", "Combat", AnimationSurfaces.Both, 0.85, "", "FINE|Assault|Federation|400", PreviewCycle: new[] { "FINE|Assault|Federation|400", "WANTED|Murder|Empire|25000", "PAID|Fines cleared|Empire|25400|Fines paid" });
        public static readonly AnimationDef DiscoveryScan =
            new("discovery-scan", "Discovery scan (honk)", "Exploration", AnimationSurfaces.Both, 0.85, "", "6|Sample System", PreviewCycle: new[] { "6|Sample System", "19|Col 285 Sector EB-O D6-3" });
        public static readonly AnimationDef Mining =
            new("mining", "Mining (prospected / cracked / refined)", "Cargo", AnimationSurfaces.Both, 0.85, "", "PROSPECT|HIGH|Painite:31;Platinum:12;Void Opal:6", PreviewCycle: new[] { "PROSPECT|HIGH|Painite:31;Platinum:12;Void Opal:6", "CRACKED|", "REFINED|Painite|3" });
        public static readonly AnimationDef Module =
            new("module", "Module fitted / stored / retrieved / sold", "Docking", AnimationSurfaces.Both, 0.85, "", "BUY|Guardian FSD Booster|SLOT 02 SIZE 5 - -6,321,023 CR", PreviewCycle: new[] { "BUY|Guardian FSD Booster|SLOT 02 SIZE 5 - -6,321,023 CR", "STORE|Sensors|ENGINEERED - GRADE 3", "GET|Fuel Scoop|SLOT 01 SIZE 7", "SELL|Cargo Rack|+84,000 CR" });
        public static readonly AnimationDef Services =
            new("services", "Station services (refuel / repair / restock)", "Docking", AnimationSurfaces.Both, 0.85, "", "f|REFUEL|0.8 T|39;r|REPAIRS|HULL|10;a|REARM|AMMO|25", PreviewCycle: new[] { "f|REFUEL|0.8 T|39;r|REPAIRS|HULL|10;a|REARM|AMMO|25", "f|REFUEL|14.2 T|710", "a|RESTOCK|SCARAB|1030", "r|REPAIRS|ALL|120;a|REARM|AMMO|25" });
        public static readonly AnimationDef Synthesis =
            new("synthesis", "Synthesis (fuel / repair / ammo / limpets)", "Exploration", AnimationSurfaces.Both, 0.85, "", "Fuel Premium|3|Sulphur:1;Phosphorus:1", PreviewCycle: new[] { "Fuel Premium|3|Sulphur:1;Phosphorus:1", "Limpet Basic|1|Iron:1;Nickel:1", "Module Repair Standard|2|Zinc:2;Chromium:4;Vanadium:6" });
        public static readonly AnimationDef CommunityGoal =
            new("community-goal", "Community goal (joined / tier / reward)", "Missions", AnimationSurfaces.Both, 0.85, "", "JOIN|HIP 87621 Exobiology Initiative|HIP 87621", PreviewCycle: new[] { "JOIN|HIP 87621 Exobiology Initiative|HIP 87621", "TIER|HIP 87621 Exobiology Initiative|2|13371", "REWARD|HIP 87621 Exobiology Initiative|235000000" });
        public static readonly AnimationDef Settlement =
            new("approach-settlement", "Approaching a settlement", "Surface", AnimationSurfaces.Both, 0.85, "", "Rah Engineering Enterprise|Abi Progressive Party|Democracy|Industrial|Abi 2 e|MEFRAP", PreviewCycle: new[] { "Rah Engineering Enterprise|Abi Progressive Party|Democracy|Industrial|Abi 2 e|MEFRAP", "Qwent Research Base|Marco Qwent|Workshop|Colony|Lucifer|OEARFM", "Windri & Co|Windri & Co|Corporate|Colony|HIP 87621 2 a|MFRP" });
        public static readonly AnimationDef Screenshot =
            new("screenshot", "Screenshot taken (instant photo)", "Exploration", AnimationSurfaces.Both, 0.85, "", "0000|QIEDEA ZW-R D5-22 A 2|3440 X 1440  SCREENSHOT 0000|0", PreviewCycle: new[] { "0000|QIEDEA ZW-R D5-22 A 2|3440 X 1440  SCREENSHOT 0000|0", "0011|SCHEAU PHOE HW-T D4-39 BCD 1 B|LAT 2.05  LON -34.65  HDG 285|1", "0002|OUTOTT SI-V B57-0|3440 X 1440  SCREENSHOT 0002|0" });
        public static readonly AnimationDef MaterialDiscovered =
            new("material-discovered", "First time a material is found (codex card)", "Exploration", AnimationSurfaces.Both, 0.85, "", "Basic Conductors|Manufactured|basicconductors|40", PreviewCycle: new[] { "Basic Conductors|Manufactured|basicconductors|40", "Guardian Module Blueprint Fragment|Encoded|guardian_moduleblueprint|31", "Tellurium|Raw|tellurium|12" });
        public static readonly AnimationDef Milestone =
            new("milestone", "Lifetime milestones (jumps, light-years, hours, bounties, wealth)", "Commander", AnimationSurfaces.Both, 0.85, "", "LIVE|JUMPS~HYPERSPACE JUMPS~26000~1000", PreviewCycle: new[] { "LIVE|JUMPS~HYPERSPACE JUMPS~26000~1000", "LIVE|LIGHT-YEARS~LIGHT-YEARS~810000~10000", "LIVE|HOURS~HOURS PLAYED~4300~100", "RECAP|JUMPS~HYPERSPACE JUMPS~26000~1000;BOUNTIES~BOUNTIES CLAIMED~5700~500;WEALTH~BILLION CREDITS~20~1" });
        public static readonly AnimationDef Limpet =
            new("limpet", "Limpet drones (launched, hull repair result)", "Cargo and trade", AnimationSurfaces.Both, 0.85, "", "L|REPAIR|REPAIR LIMPET|HULL REPAIR UNDER WAY|0", PreviewCycle: new[] { "L|REPAIR|REPAIR LIMPET|HULL REPAIR UNDER WAY|0", "R|REPAIR|REPAIR LIMPET|HULL +62  COCKPIT +0.1|62", "L|COLLECTION|COLLECTION LIMPET|COLLECTING CARGO|0", "L|PROSPECTOR|PROSPECTOR LIMPET|SCANNING THE ROCK|0", "L|HATCH BREAKER|HATCH BREAKER LIMPET|CARGO HATCH TARGETED|0" });
        public static readonly AnimationDef Afmu =
            new("afmu-repair", "Module repairs by the AFMU (repair receipt)", "Ship alerts", AnimationSurfaces.Both, 0.85, "", "5|0|THRUSTERS~100;FSD~100;POWER DISTRIBUTOR~100;SENSORS~100;PULSE LASER~100", PreviewCycle: new[] { "1|0|SHIELD GENERATOR~100", "5|0|THRUSTERS~100;FSD~100;POWER DISTRIBUTOR~100;SENSORS~100;PULSE LASER~100", "1|1|FSD~82", "12|1|PULSE LASER~100;HEATSINK~100;FUEL SCOOP~100;CARGO HATCH~100;SENSORS~90" });
        public static readonly AnimationDef Target =
            new("target-scan", "Ship scan complete (target dossier)", "Combat", AnimationSurfaces.Both, 0.85, "", "WANTED|IMPERIAL COURIER|TOBY WIDDOWS|EXPERT|ABI PROGRESSIVE PARTY|100|100|116160", PreviewCycle: new[] { "CLEAN|ANACONDA|SYSTEM DEFENCE FORCE|EXPERT|ABI PROGRESSIVE PARTY|100|100|0", "LAWLESS|PYTHON|FEDERAL NAVY SHIP|COMPETENT|WINDRI & CO|100|100|0", "WANTED|IMPERIAL COURIER|TOBY WIDDOWS|EXPERT|ABI PROGRESSIVE PARTY|100|100|116160", "LAWLESS|PYTHON|FEDERAL NAVY SHIP|COMPETENT|INDEPENDENT DECIAT GREEN PARTY|14|30|0" });
        public static readonly AnimationDef DataLink =
            new("data-link", "Data link scan (data log, data point, wreck, uplink, intel package)", "Exploration", AnimationSurfaces.Both, 0.85, "", "LOG|ABANDONED DATA LOG|DOWNLOAD COMPLETE|", PreviewCycle: new[] { "LOG|ABANDONED DATA LOG|DOWNLOAD COMPLETE|", "POINT|DATA POINT|TELEMETRY LINK ESTABLISHED|", "WRECK|WRECKED SHIP|DATA RECOVERED|", "UPLINK|SHIP UPLINK|UPLINK ACCESSED|", "INTEL|FEDERATION TO EMPIRE|+5,567 CR VOUCHER|5567" });
        public static readonly AnimationDef Session =
            new("session", "Game start and exit (pilot licence)", "Commander", AnimationSurfaces.Both, 0.85, "", "START|CMDR NOVA REYES|MANDALAY HG-08M  SOLO|SOLO|4250118000|7|8|13|8|", PreviewCycle: new[] { "START|CMDR NOVA REYES|MANDALAY HG-08M  SOLO|SOLO|4250118000|7|8|13|8|", "START|CMDR NOVA REYES|MANDALAY HG-08M  OPEN|OPEN|4250118000|7|8|13|8|", "START|CMDR NOVA REYES|MANDALAY HG-08M  HULL BREAKERS|GROUP|4250118000|7|8|13|8|HULL BREAKERS", "END|CMDR NOVA REYES|SESSION 2H 14M  38 JUMPS||0|7|8|13|8|" });
        public static readonly AnimationDef CarrierPanel =
            new("carrier-panel", "Fleet carrier management (tritium, services, bank, orders, access, decommission)", "Jump", AnimationSurfaces.Both, 0.85, "", "FUEL|KOLONIA HUB K7X-01Z|CARRIER REFUELLED|+120 T TRITIUM  TOTAL 640 T|+|120| T|0.52|0.64|", PreviewCycle: new[] { "FUEL|KOLONIA HUB K7X-01Z|CARRIER REFUELLED|+120 T TRITIUM  TOTAL 640 T|+|120| T|0.52|0.64|", "SVC|KOLONIA HUB K7X-01Z|CARRIER SERVICE ON|REFUEL||||||1", "BANK|KOLONIA HUB K7X-01Z|CARRIER BANK|DEPOSIT 5,000,000 CR  BALANCE 42,000,000|+|5000000| CR||||", "TRADE|KOLONIA HUB K7X-01Z|CARRIER TRADE ORDER|BUYING TRITIUM 200 T|BUYING TRITIUM |200| T|||", "DOCK|KOLONIA HUB K7X-01Z|DOCKING ACCESS|FRIENDS + SQUADRON|FRIENDS + SQUADRON||||||", "DECOM|KOLONIA HUB K7X-01Z|CARRIER DECOMMISSION|SCRAP IN 7 DAYS|SCRAP IN 7 DAYS||||||", "CANCEL|KOLONIA HUB K7X-01Z|DECOMMISSION CANCELLED|CARRIER KEPT||||||||" });
        public static readonly AnimationDef Contact =
            new("contact", "Friends and messages (online, offline, request, direct, wing, squadron)", "Commander", AnimationSurfaces.Both, 0.85, "", "ON|CMDR AURORA VANCE||", PreviewCycle: new[] { "ON|CMDR AURORA VANCE||", "OFF|CMDR AURORA VANCE||", "REQ|CMDR AURORA VANCE|AWAITING YOUR ANSWER|", "DM|CMDR AURORA VANCE|DIRECT|Found a Brain Tree cluster, meet at Fujin?", "WING|CMDR AURORA VANCE|WING|Breaking off, I am low on fuel.", "SQ|CMDR AURORA VANCE|SQUADRON|Carrier jumps at 20:00 tonight." });
        public static readonly AnimationDef Powerplay =
            new("powerplay", "Powerplay (pledged, merits, rank up, defected, left, salary)", "Commander", AnimationSurfaces.Both, 0.85, "", "JOIN|ZEMINA TORVAL|ALLEGIANCE SWORN|-1|-1||", PreviewCycle: new[] { "JOIN|ZEMINA TORVAL|ALLEGIANCE SWORN|-1|-1||", "MERIT|ZEMINA TORVAL|+340 MERITS  TOTAL 12,450|3|3|340|12,450", "RANK|ZEMINA TORVAL|RANK 3 TO 4|3|4||", "DEFECT|ZEMINA TORVAL TO LI YONG-RUI||3|3||", "LEAVE|ZEMINA TORVAL||3|3||", "PAY|ZEMINA TORVAL|+50,000 CR WEEKLY PAY|3|3|50,000|" });
        public static readonly AnimationDef Squadron =
            new("squadron", "Squadron (invite, joined, promoted, left, trophy)", "Commander", AnimationSurfaces.Both, 0.85, "", "JOIN|VOID WALKERS|||", PreviewCycle: new[] { "INVITE|VOID WALKERS|AWAITING YOUR ANSWER||", "JOIN|VOID WALKERS|||", "PROMOTE|VOID WALKERS|RANK 2 TO 3|2|3", "LEAVE|VOID WALKERS|||", "TROPHY|VOID WALKERS|FIRST LIGHT TROPHY||" });
        public static readonly AnimationDef Taxi =
            new("taxi", "Taxi / dropship (booked, cancelled, deployed)", "Docking", AnimationSurfaces.Both, 0.85, "", "TAXI|Sol|Galileo, Sol|-4,800 CR|-4,800 CR|TAXI", PreviewCycle: new[] { "TAXI|Sol|Galileo, Sol|-4,800 CR|-4,800 CR|TAXI", "DROP|HIP 87621|HIP 87621 - Combat Zone|-12,000 CR|-12,000 CR|DROPSHIP", "CANCEL|Sol|Galileo, Sol|REFUND +4,800 CR|REFUND +4,800 CR|TAXI", "DEPLOY|HIP 87621|HIP 87621 A 2|ON PLANET|FARE PAID|DROPSHIP" });
        public static readonly AnimationDef Gear =
            new("gear", "Suit / weapon / micro-resources (bought, sold, upgraded, traded)", "Docking", AnimationSurfaces.Both, 0.85, "", "BUY|suit|Artemis Suit|-150,000 CR||", PreviewCycle: new[] { "BUY|suit|Artemis Suit|-150,000 CR||", "UP|suit|Artemis Suit|GRADE 4 TO 5|4|5", "SELL|gun|Karma AR-50|+38,000 CR||", "SELL|box|14 ITEMS|+2,300 CR||", "TRADE|box|6 ITEMS FOR 4 ITEMS|DATA TO CHEMICALS||" });
        public static readonly AnimationDef Shipyard =
            new("shipyard", "Ship bought / delivered / swapped / transferred", "Docking", AnimationSurfaces.Both, 0.85, "", "BUY|Mandalay|19600000", PreviewCycle: new[] { "BUY|Mandalay|19600000", "NEW|Mandalay", "SWAP|Mandalay|Cobra Mk III", "XFER|Vulture|Ceos|473.7|481882|5036" });
        public static readonly AnimationDef SelfDestruct =
            new("self-destruct", "Self destruct countdown", "Combat", AnimationSurfaces.Both, 0.85, "", "");
        public static readonly AnimationDef Voucher =
            new("voucher", "Vouchers redeemed", "Cargo", AnimationSurfaces.Both, 0.85, "", "bounty|811423|Federation", PreviewCycle: new[] { "bounty|811423|Federation", "codex|375000|", "CombatBond|1250000|Empire;Federation" });
        public static readonly AnimationDef SearchRescue =
            new("search-rescue", "Search and rescue handed in", "Cargo", AnimationSurfaces.Both, 0.85, "", "RESCUE|3|Occupied Escape Pod|240000", PreviewCycle: new[] { "RESCUE|3|Occupied Escape Pod|240000", "RECOVER|1|Black Box|48000", "RECOVER|12|Wreckage Components|96000" });
        public static readonly AnimationDef ShipCall =
            new("ship-call", "Ship dismissed / recalled", "Surface", AnimationSurfaces.Both, 0.85, "", "DISMISS", PreviewCycle: new[] { "DISMISS", "RECALL" });
        public static readonly AnimationDef NavBeacon =
            new("nav-beacon", "Nav beacon scanned", "Exploration", AnimationSurfaces.Both, 0.85, "", "14|Sample System", PreviewCycle: new[] { "14|Sample System", "3|Col 285 Sector EB-O D6-3" });
        public static readonly AnimationDef SignalSource =
            new("signal-source", "Signal source found (resource site / conflict zone / USS)", "Exploration", AnimationSurfaces.Both, 0.85, "", "RES|Resource Extraction Site|Low|2|0", PreviewCycle: new[] { "RES|Resource Extraction Site|Low|2|0", "CZ|Conflict Zone|High Intensity|1|0", "USS|Combat aftermath|Threat level 2|0|2" });
        public static readonly AnimationDef BodySignals =
            new("body-signals", "Signals found on a body", "Exploration", AnimationSurfaces.Both, 0.85, "", "A 5 c|Biological:3;Geological:2", PreviewCycle: new[] { "A 5 c|Biological:3;Geological:2", "D 2|Geological:3;Planetary Mining Location:17", "C 1 a|Human:1", "B 2|Guardian:2;Thargoid:1" });
        public static readonly AnimationDef CockpitBreach =
            new("cockpit-breach", "Cockpit breached", "Combat", AnimationSurfaces.Both, 0.85, "", "");
        public static readonly AnimationDef WingCrew =
            new("wing-crew", "Wing invite / joined / left / crew member", "HUD", AnimationSurfaces.Both, 0.85, "", "INVITE|Cmdr Vance", PreviewCycle: new[] { "INVITE|Cmdr Vance", "ADD|Cmdr Vance|Cmdr Aria", "LEAVE||Cmdr Aria;Cmdr Vance", "CREW|Cmdr Vance" });
        public static readonly AnimationDef Engineer =
            new("engineer", "Module engineered / engineer unlocked", "HUD", AnimationSurfaces.Both, 0.85, "", "CRAFT|FSD Long Range|Felicity Farseer|4|Increased Range|Range:+18.2:0.36;Mass:-6.0:0.12;Power:+3.1:0.06", PreviewCycle: new[] { "CRAFT|FSD Long Range|Felicity Farseer|4|Increased Range|Range:+18.2:0.36;Mass:-6.0:0.12;Power:+3.1:0.06", "UNLOCK|Felicity Farseer|1|0", "RANKUP|Felicity Farseer|2|25" });
        public static readonly AnimationDef TechBroker =
            new("tech-broker", "Tech broker unlock", "HUD", AnimationSurfaces.Both, 0.85, "", "GUARDIAN|14|Guardian Gauss Cannon;Guardian Shard Cannon", PreviewCycle: new[] { "HUMAN|9|Remote Release Flak Launcher", "GUARDIAN|14|Guardian Gauss Cannon;Guardian Shard Cannon" });
        public static readonly AnimationDef Bounty =
            new("bounty", "Bounty claimed / combat bond", "Combat", AnimationSurfaces.Both, 0.85, "", "BOUNTY|1|Federal Dropship|Marquis Hauler Syndicate|184000|", PreviewCycle: new[] { "BOUNTY|1|Federal Dropship|Marquis Hauler Syndicate|184000|", "BOND|1||Pirate Lord Syndicate|95000|Lugh Mining Cartel", "BOUNTY|5|Federal Dropship|Marquis Hauler Syndicate|690000|" });
        public static readonly AnimationDef Cargo =
            new("cargo", "Cargo collected / jettisoned", "Cargo", AnimationSurfaces.Both, 0.85, "", "COLLECT|Gold|12|40|64", PreviewCycle: new[] { "COLLECT|Gold|12|40|64", "DUMP|Gold|12|28|64", "COLLECT|Biowaste|3|-1|-1" });
        public static readonly AnimationDef Payout =
            new("payout", "Exploration data sold", "Exploration", AnimationSurfaces.Both, 0.85, "", "14|1840000|2310000|4150000", PreviewCycle: new[] { "14|1840000|2310000|4150000", "3|420000|0|420000", "1|96000|310000|406000" });
        public static readonly AnimationDef Mission =
            new("mission", "Mission accepted / completed / failed / abandoned / redirected", "Missions", AnimationSurfaces.Both, 0.85, "", "ACCEPTED|1|Deliver 24t Biowaste|Lugh Mining Cartel|1250000", PreviewCycle: new[] { "ACCEPTED|1|Deliver 24t Biowaste|Lugh Mining Cartel|1250000", "COMPLETED|1|Deliver 24t Biowaste|Lugh Mining Cartel|1250000", "FAILED|1|Deliver 24t Biowaste||12000", "ABANDONED|1|Deliver 24t Biowaste||4000", "REDIRECTED|1|Deliver 24t Biowaste|Jameson Memorial  (Shinrarta Dezhra)|0", "ACCEPTED|4|Deliver 24t Biowaste|Lugh Mining Cartel|3800000" });
        public static readonly AnimationDef OrganismScan =
            new("organism-scan", "Organism sample 1 / 2 / 3 / genus complete", "Biology", AnimationSurfaces.Both, 0.85, "Bacterium Aurasus", "1", PreviewCycle: new[] { "1", "2", "3", "4" });
        public static readonly AnimationDef ColonyRange =
            new("colony-range", "Colony range left / entered", "Biology", AnimationSurfaces.Both, 0.85, "", "LEFT|Bacterium Aurasus|150|2", PreviewCycle: new[] { "LEFT|Bacterium Aurasus|150|2", "ENTERED|Bacterium Aurasus|150|2" });
        public static readonly AnimationDef FuelScoop =
            new("fuel-scoop", "Fuel scooping", "Jump", AnimationSurfaces.Main, 0.92, "Sample Star", "PREVIEW|TYPE=K-Star|~STAR=4500");
        public static readonly AnimationDef CombatMode =
            new("combat-mode", "Combat mode", "HUD", AnimationSurfaces.Main, 0.85, "", "");
        public static readonly AnimationDef LandingGear =
            new("landing-gear", "Landing gear deployed / retracted", "HUD", AnimationSurfaces.Both, 0.85, "", "DOWN", PreviewCycle: new[] { "DOWN", "UP" });
        public static readonly AnimationDef Hardpoints =
            new("hardpoints", "Hardpoints deployed / retracted", "HUD", AnimationSurfaces.Both, 0.85, "", "OUT", PreviewCycle: new[] { "OUT", "IN" });
        public static readonly AnimationDef CargoScoop =
            new("cargo-scoop", "Cargo scoop deployed / retracted", "HUD", AnimationSurfaces.Both, 0.85, "", "OUT", PreviewCycle: new[] { "OUT", "IN" });
        public static readonly AnimationDef Headlights =
            new("headlights", "Headlights on / high beam / off", "HUD", AnimationSurfaces.Both, 0.85, "", "HIGH", PreviewCycle: new[] { "ON", "HIGH", "OFF" });
        public static readonly AnimationDef NightVision =
            new("night-vision", "Night vision activated / deactivated", "HUD", AnimationSurfaces.Both, 0.85, "", "ON", PreviewCycle: new[] { "ON", "OFF" });
        public static readonly AnimationDef Shields =
            new("shields", "Shields up / down", "HUD", AnimationSurfaces.Both, 0.85, "", "UP", PreviewCycle: new[] { "UP", "DOWN" });
        public static readonly AnimationDef LowFuel =
            new("low-fuel", "Low fuel / fuel critical", "HUD", AnimationSurfaces.Both, 0.85, "", "LOW", PreviewCycle: new[] { "LOW", "CRITICAL" });
        public static readonly AnimationDef Overheat =
            new("overheat", "Overheating", "HUD", AnimationSurfaces.Both, 0.85, "", "");
        public static readonly AnimationDef Vitals =
            new("vitals", "Low oxygen / low health", "HUD", AnimationSurfaces.Both, 0.85, "", "OXYGEN|0.18", PreviewCycle: new[] { "OXYGEN|0.18", "HEALTH|0.24" });
        public static readonly AnimationDef Thermal =
            new("thermal", "Extreme heat / cold", "HUD", AnimationSurfaces.Both, 0.85, "", "HOT|2|341", PreviewCycle: new[] { "HOT|2|341", "COLD|2|118", "HOT|1|318", "COLD|1|240" });
        public static readonly AnimationDef RankUp =
            new("rank-up", "Rank up", "HUD", AnimationSurfaces.Both, 0.85, "", "Exobiologist|5", PreviewCycle: new[] { "Exobiologist|5", "Combat|8", "Trade|3", "Explore|9" });
        public static readonly AnimationDef HullDamage =
            new("hull-damage", "Hull damaged / critical", "HUD", AnimationSurfaces.Both, 0.85, "", "DAMAGED", PreviewCycle: new[] { "DAMAGED", "CRITICAL" });
        public static readonly AnimationDef ShipLost =
            new("ship-lost", "Ship destroyed / respawn", "HUD", AnimationSurfaces.Both, 0.9, "", "DESTROYED|Cobra Mk III|KILLED BY  Thargoid Interceptor", PreviewCycle: new[] { "DESTROYED|Cobra Mk III|KILLED BY  Thargoid Interceptor", "RESTORED|Cobra Mk III|REBUY PAID  3,420,000 CR" });
        public static readonly AnimationDef Fighter =
            new("fighter", "Fighter launched / recovered / destroyed / rebuilt", "HUD", AnimationSurfaces.Both, 0.85, "", "LAUNCH|YOU", PreviewCycle: new[] { "LAUNCH|YOU", "RECOVER", "DESTROYED", "REBUILT" });
        public static readonly AnimationDef MassLock =
            new("mass-lock", "Mass locked / mass lock cleared", "HUD", AnimationSurfaces.Both, 0.85, "", "LOCKED", PreviewCycle: new[] { "LOCKED", "CLEARED" });
        public static readonly AnimationDef FsdCooldown =
            new("fsd-cooldown", "FSD cooling down / ready", "HUD", AnimationSurfaces.Both, 0.85, "", "COOLING", PreviewCycle: new[] { "COOLING", "READY" });
        public static readonly AnimationDef Interdiction =
            new("interdiction", "Being interdicted", "HUD", AnimationSurfaces.Both, 0.85, "", "");
        public static readonly AnimationDef InterdictionOutcome =
            new("interdiction-outcome", "Interdiction evaded / submitted / pulled out", "HUD", AnimationSurfaces.Both, 0.85, "", "EVADED|Pirate Cobra Mk III", PreviewCycle: new[] { "EVADED|Pirate Cobra Mk III", "SUBMITTED|Pirate Cobra Mk III", "PULLED|Pirate Cobra Mk III" });
        public static readonly AnimationDef SilentRunning =
            new("silent-running", "Silent running on / off", "HUD", AnimationSurfaces.Both, 0.85, "", "ON", PreviewCycle: new[] { "ON", "OFF" });
        public static readonly AnimationDef DriveAssist =
            new("drive-assist", "SRV drive assist on / off", "HUD", AnimationSurfaces.Both, 0.85, "", "ON", PreviewCycle: new[] { "ON", "OFF" });
        public static readonly AnimationDef Handbrake =
            new("handbrake", "SRV handbrake on / off", "HUD", AnimationSurfaces.Both, 0.85, "", "ON", PreviewCycle: new[] { "ON", "OFF" });
        public static readonly AnimationDef Turret =
            new("turret", "SRV turret deployed / retracted", "HUD", AnimationSurfaces.Both, 0.85, "", "ON", PreviewCycle: new[] { "ON", "OFF" });
        public static readonly AnimationDef FlightAssist =
            new("flight-assist", "Flight assist on / off", "HUD", AnimationSurfaces.Both, 0.85, "", "OFF", PreviewCycle: new[] { "OFF", "ON" });
        public static readonly AnimationDef Disembark =
            new("disembark", "Disembark from a vehicle", "Surface", AnimationSurfaces.Both, 0.85, "", "SHIP");
        public static readonly AnimationDef Srv =
            new("srv", "SRV deployed / recovered", "Surface", AnimationSurfaces.Both, 0.85, "SRV Scarab", "DEPLOYED", PreviewCycle: new[] { "DEPLOYED", "RECOVERED" });
        public static readonly AnimationDef Embark =
            new("embark", "Board a vehicle", "Surface", AnimationSurfaces.Both, 0.85, "", "SHIP");
        public static readonly AnimationDef AnalysisMode =
            new("analysis-mode", "Analysis mode", "HUD", AnimationSurfaces.Main, 0.85, "", "");
        public static readonly AnimationDef Arrival =
            new("arrival", "Arrival in a system", "Jump", AnimationSurfaces.Main, 1.0, "Sample System", "TYPE=K-Star|~STAR=4500");
        public static readonly AnimationDef DockingRequest =
            new("docking-request", "Docking requested / granted / denied / undocked", "Docking", AnimationSurfaces.Both, 0.85, "Jameson Memorial", "REQUESTED", PreviewCycle: new[] { "REQUESTED", "GRANTED|17", "DENIED|NO SPACE", "UNDOCKED" });
        public static readonly AnimationDef CarrierJump =
            new("carrier-jump", "Fleet carrier jump scheduled / cancelled / arrived", "Jump", AnimationSurfaces.Both, 0.85, "", "SCHEDULED|Colonia|Colonia Hub|PREVIEW", PreviewCycle: new[] { "SCHEDULED|Colonia|Colonia Hub|PREVIEW", "CANCELLED|Colonia||622", "ARRIVED|Colonia" });
        public static readonly AnimationDef Docked =
            new("docked", "Docked at a station or carrier", "Docking", AnimationSurfaces.Main, 1.0, "Sample Station", "TYPE=Coriolis|ECONOMY=Refinery|DISTANCE=432 ls");

        public static readonly AnimationDef Descent =
            new("descent", "Descent to a planet", "Atmosphere", AnimationSurfaces.Main, 1.0, "", "", Live: true);
        public static readonly AnimationDef Climb =
            new("climb", "Climb out (launch / ascent)", "Atmosphere", AnimationSurfaces.Main, 1.0, "", "", Live: true);
        public static readonly AnimationDef AtmosphericJump =
            new("atmospheric-jump", "Hyperspace jump from an atmosphere", "Atmosphere", AnimationSurfaces.Main, 1.0, "", "", Live: true);

        public static readonly IReadOnlyList<AnimationDef> All = new[] { FirstDiscovery, HyperspaceJump, Arrival, CarrierJump, Supercruise, SupercruiseOverdrive, SupercruiseAssist, SupercruiseExit, Supercharge, Route, FuelScoop, CombatMode, AnalysisMode, LandingGear, Hardpoints, CargoScoop, Headlights, NightVision, Shields, LowFuel, Overheat, Vitals, Thermal, RankUp, HullDamage, ShipLost, Fighter, MassLock, FsdCooldown, Interdiction, InterdictionOutcome, Engineer, TechBroker, WingCrew, SilentRunning, DriveAssist, Handbrake, Turret, FlightAssist, Disembark, Embark, Srv, Landed, Liftoff, FirstFootfall, DockingRequest, Docked, OrganismScan, ColonyRange, FssComplete, CodexEntry, Mapped, Material, Payout, ExobiologySold, DiscoveryScan, SignalSource, NavBeacon, ShipCall, SearchRescue, Voucher, SelfDestruct, Shipyard, Services, Synthesis, CommunityGoal, Settlement, Taxi, Gear, Squadron, Powerplay, Contact, CarrierPanel, Session, DataLink, Target, Afmu, Limpet, Milestone, MaterialDiscovered, Screenshot, Module, BodySignals, Mission, Bounty, CombatAlert, Crime, CockpitBreach, Cargo, TradeProfit, Mining, Descent, Climb, AtmosphericJump };

        // The user's choice from the settings file, else the animation's own default.
        public static AnimationSurfaces SurfacesFor(AnimationDef def, AppSettingsData settings) =>
            settings.AnimationTargets.TryGetValue(def.Id, out var v) ? (AnimationSurfaces)v : def.DefaultSurfaces;
    }

    // What to play and what to say. Subject/Detail are the animation's own text (First discovery: system name, star class).
    public sealed record AnimationRequest(AnimationDef Def, string Subject, string Detail)
    {
        public DateTime At { get; } = DateTime.UtcNow;
    }

    // A place that can play animations. Play must call done exactly once when the animation has finished and the surface is clear again.
    public interface IAnimationSurface
    {
        AnimationSurfaces Kind { get; }
        bool IsAvailable { get; }
        void Play(AnimationRequest request, Action done);
    }

    // Events are detected once, then handed to Raise. The router looks up where that animation is switched on and gives it to each of those
    // surfaces. Every surface plays one animation at a time and queues the rest; a request still waiting after MaxWait is dropped, since
    // a stale notification (a system the commander has already left) is worse than none. All calls are on the UI thread.
    public static class AnimationRouter
    {
        private const double MaxWaitSeconds = 10;

        private sealed class Lane
        {
            public IAnimationSurface Surface = null!;
            public readonly Queue<AnimationRequest> Queue = new();
            public bool Busy;
        }

        private static readonly Dictionary<AnimationSurfaces, Lane> _lanes = new();

        public static void Register(IAnimationSurface surface) => _lanes[surface.Kind] = new Lane { Surface = surface };

        public static void Unregister(IAnimationSurface surface)
        {
            if (_lanes.TryGetValue(surface.Kind, out var lane) && ReferenceEquals(lane.Surface, surface)) _lanes.Remove(surface.Kind);
        }

        public static void Raise(AnimationRequest request)
        {
            var targets = AnimationCatalog.SurfacesFor(request.Def, AppSettings.Load());
            foreach (var kv in _lanes)
            {
                if ((targets & kv.Key) == 0) continue;
                kv.Value.Queue.Enqueue(request);
                Pump(kv.Value);
            }
        }

        private static void Pump(Lane lane)
        {
            if (lane.Busy) return;
            while (lane.Queue.Count > 0)
            {
                var req = lane.Queue.Dequeue();
                if ((DateTime.UtcNow - req.At).TotalSeconds > MaxWaitSeconds)
                {
                    Log.Write($"Animation '{req.Def.Id}' dropped on {lane.Surface.Kind}: waited too long");
                    continue;
                }
                if (!lane.Surface.IsAvailable)
                {
                    Log.Write($"Animation '{req.Def.Id}' skipped on {lane.Surface.Kind}: surface not available");
                    continue;
                }
                lane.Busy = true;
                lane.Surface.Play(req, () => { lane.Busy = false; Pump(lane); });
                return;
            }
        }
    }
}
