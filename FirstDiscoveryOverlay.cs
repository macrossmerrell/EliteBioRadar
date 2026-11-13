using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The First Discovery moment: plays on arrival in a system nobody had discovered (see AnimationRouter for where it is shown). A generic system map (orbits
    // drawing in, bodies circling, drifting and slowly zooming) with rings pulsing out of the star, corner brackets locking on, then
    // the title spreading in over the system name. About 4 seconds, then it fades and the tab underneath is back. Drawn frame by
    // frame from one clock, so the title's letter-spacing animation (which a TextBlock cannot do) stays in step with the rest.
    public sealed class FirstDiscoveryVisual : AnimationVisual
    {
        // The build-up runs 20% slower than first drawn (Slow), then holds for four more seconds before the final fade.
        private const double Slow = 1.2;
        private const double FadeOutAt = 3.3 * Slow + 4.0;           // seconds
        protected override double Duration => FadeOutAt + 0.6 * Slow;      // seconds, including the final fade

        private static readonly Brush Backdrop   = Frozen(Color.FromRgb(0x08, 0x0d, 0x0d));
        private static readonly Brush TitleStrip = Frozen(Color.FromArgb(0xd9, 0x08, 0x0d, 0x0d));
        private static readonly Brush Amber      = Frozen(Color.FromRgb(0xf2, 0xb8, 0x4b));
        private static readonly Brush Pale       = Frozen(Color.FromRgb(0xc9, 0xd6, 0xdc));
        private static readonly Brush Body       = Frozen(Color.FromRgb(0x9f, 0xd8, 0xd4));
        private static readonly Brush StarFill   = Frozen(Color.FromRgb(0xe8, 0xf4, 0xf2));
        private static readonly Pen GridPen   = FrozenPen(Color.FromRgb(0x11, 0x20, 0x26), 1);
        private static readonly Pen OrbitPen  = FrozenPen(Color.FromRgb(0x2f, 0x6a, 0x70), 1.4);
        private static readonly Pen RingPen   = FrozenPen(Color.FromRgb(0x3f, 0xd0, 0xc9), 1.6);
        private static readonly Pen CrossPen  = FrozenPen(Color.FromRgb(0x3f, 0xd0, 0xc9), 1);
        private static readonly Pen BracketPen = FrozenPen(Color.FromRgb(0xf2, 0xb8, 0x4b), 2.2);

        // Orbit radius (fraction of the map size) and angular speed of the body riding it.
        private static readonly (double r, double speed)[] Orbits =
            { (.12, .9), (.22, .7), (.36, .55), (.52, .4), (.72, .3), (.95, .22) };

        private static readonly Typeface TitleFace = new(AppFonts.Mono, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Typeface SubFace   = new(AppFonts.Mono, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        private string _subtitle = "";

        // Subject = system name, detail = star class.
        protected override void OnStart(string systemName, string starClass)
        {
            _subtitle = starClass.Length > 0 ? $"{systemName}  ·  {starClass}" : systemName;
            _subtitle = _subtitle.ToUpperInvariant();
        }

        protected override void Render(DrawingContext dc, double w, double h, double real)
        {
            double fadeOut = real > FadeOutAt ? Clamp01(1 - (real - FadeOutAt) / (Duration - FadeOutAt)) : 1;
            double t = real / Slow;   // animation time: every phase below is written in the original timings
            double env = Math.Min(1, t / 0.5) * fadeOut;
            if (env <= 0) return;
            double k = Math.Max(0.8, Math.Min(w, h) / 300);   // sizes below are written for a ~300 px panel

            dc.PushOpacity(BackdropOpacity * env);
            dc.DrawRectangle(Backdrop, null, new Rect(0, 0, w, h));
            dc.Pop();

            // Faint grid.
            dc.PushOpacity(env);
            double step = 34 * k;
            for (double x = 0; x < w; x += step) dc.DrawLine(GridPen, new Point(x, 0), new Point(x, h));
            for (double y = 0; y < h; y += step) dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
            dc.Pop();

            DrawMap(dc, w, h, k, t, env);
            DrawRings(dc, w, h, t);
            DrawBrackets(dc, w, h, k, t, fadeOut);
            DrawTitle(dc, w, h, k, t, fadeOut);
        }

        private void DrawMap(DrawingContext dc, double w, double h, double k, double t, double env)
        {
            double zoom = 0.8 + 0.3 * (t / 4);
            double cx = w / 2 - 14 * k * t, cy = h / 2;
            double baseR = Math.Min(w, h * 1.4) * 0.72;
            const double tilt = -0.3, flat = 0.42;
            double ct = Math.Cos(tilt), st = Math.Sin(tilt);
            Point At(double r, double a)
            {
                double x = Math.Cos(a) * r * zoom, y = Math.Sin(a) * r * zoom * flat;
                return new Point(cx + x * ct - y * st, cy + x * st + y * ct);
            }

            dc.PushOpacity(env);
            for (int i = 0; i < Orbits.Length; i++)
            {
                double r = Orbits[i].r * baseR;
                double p = Phase(t, 0.1 + i * 0.12, 1.1);
                if (p <= 0) continue;
                int n = Math.Max(2, (int)(120 * p));
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(At(r, 0), false, false);
                    for (int s = 1; s <= n; s++) ctx.LineTo(At(r, Math.PI * 2 * p * s / n), true, true);
                }
                g.Freeze();
                dc.DrawGeometry(null, OrbitPen, g);
                if (p >= 1)
                {
                    double a = t * Orbits[i].speed * 0.9 + i * 1.7;
                    double br = (i % 2 == 1 ? 2.6 : 3.4) * k;
                    dc.DrawEllipse(i == 3 ? Amber : Body, null, At(r, a), br, br);
                }
            }
            var c = new Point(cx, cy);
            dc.DrawEllipse(StarFill, null, c, 4 * k, 4 * k);
            dc.DrawLine(CrossPen, new Point(cx - 10 * k, cy), new Point(cx - 6 * k, cy));
            dc.DrawLine(CrossPen, new Point(cx + 6 * k, cy), new Point(cx + 10 * k, cy));
            dc.DrawLine(CrossPen, new Point(cx, cy - 10 * k), new Point(cx, cy - 6 * k));
            dc.DrawLine(CrossPen, new Point(cx, cy + 6 * k), new Point(cx, cy + 10 * k));
            dc.Pop();
        }

        private static void DrawRings(DrawingContext dc, double w, double h, double t)
        {
            double maxR = Math.Min(w, h) * 0.65;
            foreach (double start in new[] { 0.1, 0.4 })
            {
                double p = Phase(t, start, 1.4);
                if (p <= 0 || p >= 1) continue;
                double r = 10 + (maxR - 10) * EaseOut(p);
                dc.PushOpacity(0.9 * (1 - EaseOut(p)));
                dc.DrawEllipse(null, RingPen, new Point(w / 2, h / 2), r, r);
                dc.Pop();
            }
        }

        private static void DrawBrackets(DrawingContext dc, double w, double h, double k, double t, double fadeOut)
        {
            double p = Phase(t, 0.5, 0.7);
            if (p <= 0) return;
            double e = EaseOut(p);
            double scale = 2.2 - 1.2 * e;
            double size = 22 * k, inset = 16 * k, half = size / 2 * scale;
            dc.PushOpacity(e * fadeOut);
            // (box centre, horizontal direction, vertical direction) per corner: the L opens toward the panel's centre.
            foreach (var (bx, by, dx, dy) in new[]
            {
                (inset + size / 2, inset + size / 2, 1.0, 1.0), (w - inset - size / 2, inset + size / 2, -1.0, 1.0),
                (inset + size / 2, h - inset - size / 2, 1.0, -1.0), (w - inset - size / 2, h - inset - size / 2, -1.0, -1.0),
            })
            {
                var corner = new Point(bx - dx * half, by - dy * half);
                dc.DrawLine(BracketPen, corner, new Point(corner.X + dx * 2 * half, corner.Y));
                dc.DrawLine(BracketPen, corner, new Point(corner.X, corner.Y + dy * 2 * half));
            }
            dc.Pop();
        }

        private void DrawTitle(DrawingContext dc, double w, double h, double k, double t, double fadeOut)
        {
            double pt = Phase(t, 1.1, 0.9), ps = Phase(t, 1.7, 0.6);
            if (pt <= 0) return;
            double ease = EaseOut(pt);
            double titleSize = 17 * k, subSize = 12 * k;
            double titleY = h * 0.40, subY = titleY + titleSize + 18 * k;

            dc.PushOpacity(ease * fadeOut);
            dc.DrawRectangle(TitleStrip, null, new Rect(0, titleY - 14 * k, w, titleSize + subSize + 52 * k));
            dc.Pop();

            DrawSpaced(dc, "FIRST DISCOVERY", TitleFace, titleSize, Amber, w / 2, titleY, 1.0 - 0.58 * ease, ease * fadeOut);
            if (ps > 0) DrawSpaced(dc, _subtitle, SubFace, subSize, Pale, w / 2, subY, 0.14, ps * fadeOut);
        }

    }

    public partial class MainWindow
    {
        // What the departure screen shows about the next system: "LIVE|class|distance ly|jump number|jumps in route|scoopable". The distance and jump numbers come from the plotted route (blank when
        // the next system is not on it, or is the first stop with nothing before it to measure from).
        private static string JumpDepartureDetail(DestinationInfo? d)
        {
            if (d == null) return "LIVE";
            try
            {
                string cls = d.StarClass ?? "";
                var hops = d.FullRouteHops;
                int idx = string.IsNullOrEmpty(d.NextSystem) ? -1 : hops.FindIndex(h => string.Equals(h.StarSystem, d.NextSystem, StringComparison.OrdinalIgnoreCase));
                string dist = idx > 0 && hops[idx].DistanceFromPrevLy > 0 ? hops[idx].DistanceFromPrevLy.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "";
                string no = idx > 0 ? idx.ToString(System.Globalization.CultureInfo.InvariantCulture) : "", total = idx > 0 ? (hops.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
                return $"LIVE|{cls}|{dist}|{no}|{total}|{(IsScoopableStar(cls) ? 1 : 0)}";
            }
            catch { return "LIVE"; }   // the route was being rebuilt while this read it: show the screen without the extras
        }

        private void InitAnimations(EliteWatcherService svc)
        {
            AnimationRouter.Register(new MainWindowAnimationSurface(this));            // Raised on the journal thread. The router decides which window(s) show it.
            svc.SystemFirstDiscovered += (system, starType) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.FirstDiscovery, system,
                    starType.Length > 0 ? StarClassNames.GetDisplayName(starType) : "")));
            svc.ShipLanded += (body, stats) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Landed, body, stats)));
            svc.ShipLiftoff += (body, stats) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Liftoff, body, stats)));
            svc.JumpChargeStarted += next => Dispatcher.InvokeAsync(() =>
            {
                JumpDepartureState.ChargeStart = DateTime.UtcNow;
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.HyperspaceJump, next, JumpDepartureDetail(svc.CurrentDestination)));
            });
            svc.HyperspaceJumpStarted += () => Dispatcher.InvokeAsync(() => JumpDepartureState.DriveFiredAt = DateTime.UtcNow);
            svc.JumpChargeCancelled += () => Dispatcher.InvokeAsync(JumpDepartureState.RequestEnd);
            svc.SystemArrived +=(system, starClass) => Dispatcher.InvokeAsync(() =>
            {
                JumpDepartureState.RequestEnd();   // the tunnel (if one is playing) ends in a flash; this screen fades in from it
                string type = SystemArrivalVisual.StarTypeLabel(starClass);
                double k = SystemArrivalVisual.ClassKelvin(starClass);
                string detail = (type.Length > 0 ? $"TYPE={type}" : "") + (k > 0 ? (type.Length > 0 ? "|" : "") + $"~STAR={k:F0}" : "");
                if (!string.IsNullOrWhiteSpace(starClass)) detail += (detail.Length > 0 ? "|" : "") + $"~CLS={starClass.Trim()}";   // the class itself, for the star's real colours
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Arrival, system, detail));
            });
            // The supercruise animation follows the charge: it starts when the FSD starts charging, spools up for as long as the charge takes, and bursts when the drive engages.
            svc.SupercruiseChargeStarted += () => Dispatcher.InvokeAsync(() =>
            {
                SupercruiseState.ChargeStart = DateTime.UtcNow;
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Supercruise, "", ""));
            });
            svc.SupercruiseChargeCancelled += () => Dispatcher.InvokeAsync(SupercruiseState.RequestEnd);
            // The journal's StartJump: the spool-up is done (the ring is full) and the game's own countdown to the entry begins. Measured, so the next ring is paced to it.
            svc.SupercruiseDriveFired += () => Dispatcher.InvokeAsync(() =>
            {
                var now = DateTime.UtcNow;
                if (SupercruiseState.ChargeStart > SupercruiseState.LastEnd && SupercruiseState.ChargeStart > SupercruiseState.EnteredAt)
                {
                    SupercruiseState.ChargeSeconds = Math.Clamp((now - SupercruiseState.ChargeStart).TotalSeconds, 1.5, 30);
                    SupercruiseState.DriveFiredAt = now;
                }
            });
            svc.SupercruiseEntered += () => Dispatcher.InvokeAsync(() =>
            {
                var now = DateTime.UtcNow;
                bool charging = SupercruiseState.ChargeStart > SupercruiseState.LastEnd && SupercruiseState.ChargeStart > SupercruiseState.EnteredAt && (now - SupercruiseState.ChargeStart).TotalSeconds < 120;
                SupercruiseState.EnteredAt = now;
                if (!charging) AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Supercruise, "", "ENTRY"));   // no charge was seen (the app started mid-charge): just the engage
            });
            svc.SupercruiseOverdriveChanged += on => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.SupercruiseOverdrive, "", on ? "ON" : "OFF")));
            svc.OrganismSampleTaken += (stage, name) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.OrganismScan, name, Math.Clamp(stage, 1, 4).ToString())));
            svc.HullThresholdCrossed += (level, was, now) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.HullDamage, "",
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(level == 2 ? "CRITICAL" : "DAMAGED")}|{was:0.###}|{now:0.###}"))));
            svc.DockingResponse += (station, stage) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.DockingRequest, station, stage)));
            svc.FsdCooldownChanged += cooling => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.FsdCooldown, "", cooling ? "COOLING" : "READY")));
            svc.MassLockChanged += locked => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.MassLock, "", locked ? "LOCKED" : "CLEARED")));
            svc.FighterLaunched += you => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Fighter, "", you ? "LAUNCH|YOU" : "LAUNCH|CREW")));
            svc.FighterRecovered += () => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Fighter, "", "RECOVER")));
            svc.FighterLost += rebuilt => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Fighter, "", rebuilt ? "REBUILT" : "DESTROYED")));
            svc.SupercruiseExited += () => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.SupercruiseExit, "", "")));
            svc.Supercharged += (type, boost) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Supercharge, "", type + "|" + boost.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))));
            svc.RouteChanged += (stage, jumps, ly, classes) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Route, "", $"{stage}|{jumps}|{ly.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}|{classes}")));
            svc.FssCompleted += (system, count) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.FssComplete, system, count.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            svc.CodexEntryFound += (name, category, sub) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.CodexEntry, name, category + "|" + sub)));
            svc.ShipLostChanged += (stage, ship, info) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.ShipLost, "", stage + "|" + ship + "|" + info)));
            svc.VitalsLow += (kind, level) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Vitals, "", kind + "|" + level.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))));
            svc.ThermalWarning += (hot, tier, kelvin) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Thermal, "", $"{(hot ? "HOT" : "COLD")}|{tier}|{kelvin.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}")));
            svc.ColonyRangeChanged += (entered, name, range, next) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.ColonyRange, "", $"{(entered ? "ENTERED" : "LEFT")}|{name}|{range}|{next}")));
            svc.MaterialPicked += (display, category, key, count, stock, cap) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Material, "", $"{display}|{category}|{key}|{count}|{stock}|{cap}")));
            svc.ExplorationSold += (systems, baseValue, bonus, total) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Payout, "", $"{systems}|{baseValue}|{bonus}|{total}")));
            svc.BountyPaid += (kind, count, name, victim, reward, awarder) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Bounty, "", $"{kind}|{count}|{name.Replace('|', '/')}|{victim.Replace('|', '/')}|{reward}|{awarder.Replace('|', '/')}")));
            svc.CargoMoved += (kind, name, tonnes, used, cap) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Cargo, "", $"{kind}|{name.Replace('|', '/')}|{tonnes}|{used}|{cap}")));
            svc.PlanetMapped += (body, probes, target) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Mapped, body, $"{probes}|{target}")));
            svc.RankedUp += (category, rank) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.RankUp, "", $"{category}|{rank}")));
            svc.MissionsChanged += (kind, count, name, faction, reward) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Mission, "", $"{kind}|{count}|{name.Replace('|', '/')}|{faction.Replace('|', '/')}|{reward}")));
            svc.SrvChanged += (name, deployed) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Srv, name, deployed ? "DEPLOYED" : "RECOVERED")));
            svc.SupercruiseAssistChanged += on => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.SupercruiseAssist, "", on ? "ON" : "OFF")));
            svc.FuelScoopStarted += (star, detail) => Dispatcher.InvokeAsync(() =>
            {
                FuelScoopState.LastStart = DateTime.UtcNow;
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.FuelScoop, star, detail));
            });
            svc.HudModeChanged += analysis => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(analysis ? AnimationCatalog.AnalysisMode : AnimationCatalog.CombatMode, "", "")));
            svc.GearChanged += down => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.LandingGear, "", down ? "DOWN" : "UP")));
            svc.HardpointsChanged += deployed => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Hardpoints, "", deployed ? "OUT" : "IN")));
            svc.CargoScoopChanged += deployed => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.CargoScoop, "", deployed ? "OUT" : "IN")));
            svc.HeadlightsChanged += mode => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Headlights, "", mode)));
            svc.FuelWarning += level => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.LowFuel, "", level)));
            svc.OverheatStarted += () => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Overheat, "", "")));
            svc.InterdictionStarted += () => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Interdiction, "", "")));
            svc.OrganicSoldEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.ExobiologySold, "", detail)));
            svc.CargoSoldEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.TradeProfit, "", detail)));
            svc.CrimeEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Crime, "", detail)));
            svc.DiscoveryScanEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.DiscoveryScan, "", detail)));
            svc.ModuleEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Module, "", detail)));
            svc.SynthesisEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Synthesis, "", detail)));
            svc.CommunityGoalEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.CommunityGoal, "", detail)));
            svc.SettlementEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Settlement, "", detail)));
            svc.TaxiEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Taxi, "", detail)));
            svc.GearEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Gear, "", detail)));
            svc.ServicesEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Services, "", detail)));
            svc.ShipyardEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Shipyard, "", detail)));
            svc.SelfDestructEvent += () => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.SelfDestruct, "", "")));
            svc.VoucherEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Voucher, "", detail)));
            svc.SearchRescueEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.SearchRescue, "", detail)));
            svc.ShipCallEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.ShipCall, "", detail)));
            svc.NavBeaconEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.NavBeacon, "", detail)));
            svc.SignalSourceEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.SignalSource, "", detail)));
            svc.MiningEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Mining, "", detail)));
            svc.BodySignalsEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.BodySignals, "", detail)));
            svc.CockpitBreachedEvent += () => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.CockpitBreach, "", "")));
            svc.WingCrewEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.WingCrew, "", detail)));
            svc.CombatAlertEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.CombatAlert, "", detail)));
            svc.CarrierJumpEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.CarrierJump, "", detail)));
            svc.TechBrokerEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.TechBroker, "", detail)));
            svc.EngineerEvent += detail => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Engineer, "", detail)));
            svc.InterdictionEnded += (kind, who) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.InterdictionOutcome, "", $"{kind}|{who.Replace('|', '/')}")));
            svc.SilentRunningChanged += on => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.SilentRunning, "", on ? "ON" : "OFF")));
            svc.DriveAssistChanged += on => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.DriveAssist, "", on ? "ON" : "OFF")));
            svc.HandbrakeChanged += on => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Handbrake, "", on ? "ON" : "OFF")));
            svc.TurretChanged += deployed => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Turret, "", deployed ? "ON" : "OFF")));
            svc.ShieldsChanged += up => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Shields, "", up ? "UP" : "DOWN")));
            svc.NightVisionChanged += on => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.NightVision, "", on ? "ON" : "OFF")));
            svc.FlightAssistChanged += assistOn => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.FlightAssist, "", assistOn ? "ON" : "OFF")));
            svc.PlayerDisembarked += vehicle => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Disembark, "", vehicle)));
            svc.PlayerEmbarked += vehicle => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Embark, "", vehicle)));
            svc.FuelScoopEnded += () => Dispatcher.InvokeAsync(FuelScoopState.RequestEnd);
            svc.FirstFootfall += (body, stats) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.FirstFootfall, body, stats)));
            svc.ShipDocked += (station, stats) => Dispatcher.InvokeAsync(() =>
                AnimationRouter.Raise(new AnimationRequest(AnimationCatalog.Docked, station, stats)));
        }

        // The Elite Bio Radar window as an animation surface: the overlay layer sits over whichever tab is showing.
        private sealed class MainWindowAnimationSurface : IAnimationSurface
        {
            private readonly MainWindow _w;
            private Action? _done;
            private readonly Dictionary<string, AnimationVisual> _visuals;

            public MainWindowAnimationSurface(MainWindow w)
            {
                _w = w;
                _visuals = new Dictionary<string, AnimationVisual>
                {
                    ["first-discovery"] = _w.firstDiscoveryVisual,
                    ["landed"] = _w.landedVisual,
                    ["liftoff"] = _w.liftoffVisual,
                    ["first-footfall"] = _w.firstFootfallVisual,
                    ["arrival"] = _w.arrivalVisual,
                    ["hyperspace-jump"] = _w.jumpDepartureVisual,
                    ["fuel-scoop"] = _w.fuelScoopVisual,
                    ["combat-mode"] = _w.combatModeVisual,
                    ["analysis-mode"] = _w.analysisModeVisual,
                    ["landing-gear"] = _w.landingGearVisual,
                    ["hardpoints"] = _w.hardpointsVisual,
                    ["cargo-scoop"] = _w.cargoScoopVisual,
                    ["headlights"] = _w.headlightsVisual,
                    ["night-vision"] = _w.nightVisionVisual,
                    ["shields"] = _w.shieldsVisual,
                    ["low-fuel"] = _w.lowFuelVisual,
                    ["overheat"] = _w.overheatVisual,
                    ["interdiction"] = _w.interdictionVisual,
                    ["interdiction-outcome"] = _w.interdictionOutcomeVisual,
                    ["engineer"] = _w.engineerVisual,
                    ["combat-alert"] = _w.combatAlertVisual,
                    ["wing-crew"] = _w.wingCrewVisual,
                    ["body-signals"] = _w.bodySignalsVisual,
                    ["mining"] = _w.miningVisual,
                    ["signal-source"] = _w.signalSourceVisual,
                    ["nav-beacon"] = _w.navBeaconVisual,
                    ["ship-call"] = _w.shipCallVisual,
                    ["search-rescue"] = _w.searchRescueVisual,
                    ["voucher"] = _w.voucherVisual,
                    ["self-destruct"] = _w.selfDestructVisual,
                    ["shipyard"] = _w.shipyardVisual,
                    ["module"] = _w.moduleVisual,
                    ["services"] = _w.servicesVisual,
                    ["gear"] = _w.gearVisual,
                    ["taxi"] = _w.taxiVisual,
                    ["approach-settlement"] = _w.settlementVisual,
                    ["synthesis"] = _w.synthesisVisual,
                    ["community-goal"] = _w.communityGoalVisual,
                    ["discovery-scan"] = _w.discoveryScanVisual,
                    ["crime"] = _w.crimeVisual,
                    ["trade-profit"] = _w.tradeProfitVisual,
                    ["exobiology-sold"] = _w.exobiologySoldVisual,
                    ["cockpit-breach"] = _w.cockpitBreachVisual,
                    ["carrier-jump"] = _w.carrierJumpVisual,
                    ["tech-broker"] = _w.techBrokerVisual,
                    ["silent-running"] = _w.silentRunningVisual,
                    ["supercruise"] = _w.supercruiseVisual,
                    ["supercruise-overdrive"] = _w.supercruiseOverdriveVisual,
                    ["supercruise-assist"] = _w.supercruiseAssistVisual,
                    ["supercruise-exit"] = _w.supercruiseExitVisual,
                    ["supercharge"] = _w.superchargeVisual,
                    ["route"] = _w.routeVisual,
                    ["fss-complete"] = _w.fssCompleteVisual,
                    ["codex-entry"] = _w.codexEntryVisual,
                    ["material"] = _w.materialVisual,
                    ["mission"] = _w.missionVisual,
                    ["payout"] = _w.payoutVisual,
                    ["bounty"] = _w.bountyVisual,
                    ["cargo"] = _w.cargoVisual,
                    ["planet-mapped"] = _w.mappedVisual,
                    ["srv"] = _w.srvVisual,
                    ["organism-scan"] = _w.organismScanVisual,
                    ["colony-range"] = _w.colonyRangeVisual,
                    ["hull-damage"] = _w.hullDamageVisual,
                    ["vitals"] = _w.vitalsVisual,
                    ["rank-up"] = _w.rankUpVisual,
                    ["thermal"] = _w.thermalVisual,
                    ["ship-lost"] = _w.shipLostVisual,
                    ["fighter"] = _w.fighterVisual,
                    ["mass-lock"] = _w.massLockVisual,
                    ["fsd-cooldown"] = _w.fsdCooldownVisual,
                    ["docking-request"] = _w.dockingRequestVisual,
                    ["drive-assist"] = _w.driveAssistVisual,
                    ["handbrake"] = _w.handbrakeVisual,
                    ["turret"] = _w.turretVisual,
                    ["flight-assist"] = _w.flightAssistVisual,
                    ["disembark"] = _w.disembarkVisual,
                    ["embark"] = _w.embarkVisual,
                    ["docked"] = _w.dockedVisual,
                };
                foreach (var def in AnimationCatalog.All)
                    if (!def.Live && !_visuals.ContainsKey(def.Id)) Log.Write($"Animation '{def.Id}' has no visual in the Elite Bio Radar window");
                foreach (var v in _visuals.Values)
                    v.Finished += () =>
                    {
                        _w.firstDiscoveryLayer.Visibility = Visibility.Collapsed;
                        var d = _done; _done = null; d?.Invoke();
                    };
            }

            public AnimationSurfaces Kind => AnimationSurfaces.Main;
            public bool IsAvailable => _w.IsVisible && _w.WindowState != WindowState.Minimized;

            public void Play(AnimationRequest request, Action done)
            {
                _done = done;
                Log.Write($"Animation '{request.Def.Id}' playing in the Elite Bio Radar window: {request.Subject}");
                if (!_visuals.TryGetValue(request.Def.Id, out var v)) { Log.Write($"Animation '{request.Def.Id}' has no visual in the Elite Bio Radar window"); _done = null; done(); return; }
                v.BackdropOpacity = request.Def.BackdropOpacity;
                _w.firstDiscoveryLayer.Visibility = Visibility.Visible;
                v.Start(request.Subject, request.Detail);
            }
        }
    }
}
