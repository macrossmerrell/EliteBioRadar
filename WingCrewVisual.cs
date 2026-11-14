using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The wing and the crew as a roster card. INVITE (the journal's WingInvite): a flashing INVITED row is added. ADD (WingAdd): a pilot's row turns green with a tick. JOIN (WingJoin): you joined a wing, the
    // pilots already in it are listed one after another. LEAVE (WingLeave): the wing's rows slide, one after another, all the way off the left of the screen. CREW (CrewMemberJoins): a seat row is added in pink.
    // Detail = "INVITE|name", "ADD|name|member;member", "JOIN||member;member", "LEAVE||member;member" or "CREW|name".
    public sealed class WingCrewVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0, CardX = 56, CardY = 8, CardW = 128, RowH = 16, MaxRows = 4;
        private enum Kind { Invite, Add, Join, Leave, Crew }

        private sealed class Palette
        {
            public readonly Brush Solid, RowFill;
            public readonly Pen Dashed, Tick, Edge;
            public readonly Pen[] Ripple;
            public Palette(Color c)
            {
                Solid = Frozen(c); RowFill = Frozen(Color.FromArgb(36, c.R, c.G, c.B));
                var d = new Pen(Solid, 1) { DashStyle = new DashStyle(new[] { 3.0, 3.0 }, 0) }; d.Freeze(); Dashed = d;
                Tick = FrozenPen(c, 1.6); Edge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1);
                Ripple = AlphaPens(c, 1.2);
            }
        }
        private static readonly Color BlueColor = Color.FromRgb(120, 190, 255), GreenColor = Color.FromRgb(95, 227, 176), GreyColor = Color.FromRgb(165, 175, 195), PinkColor = Color.FromRgb(255, 130, 200);
        private static readonly Palette Blue = new(BlueColor), Green = new(GreenColor), Grey = new(GreyColor), Pink = new(PinkColor);
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), CardFill = Frozen(Color.FromArgb(15, BlueColor.R, BlueColor.G, BlueColor.B)), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), GreyNameBrush = Frozen(Color.FromRgb(0xaa, 0xb4, 0xc4));
        private static readonly Pen CardEdge = FrozenPen(Color.FromArgb(179, BlueColor.R, BlueColor.G, BlueColor.B), 1.4);

        private Kind _kind;
        private string _name = "";
        private readonly List<string> _members = new();

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string k = p.Length > 0 ? p[0].Trim().ToUpperInvariant() : "";
            _kind = k == "ADD" ? Kind.Add : k == "JOIN" ? Kind.Join : k == "LEAVE" ? Kind.Leave : k == "CREW" ? Kind.Crew : Kind.Invite;
            _name = p.Length > 1 ? p[1].Trim() : "";
            _members.Clear();
            if (p.Length > 2) foreach (var m in p[2].Split(';', StringSplitOptions.RemoveEmptyEntries)) if (m.Trim().Length > 0) _members.Add(m.Trim());
            if (_name.Length == 0 && _kind is Kind.Invite or Kind.Add or Kind.Crew) _name = "Pilot";
            if (_kind == Kind.Leave && _members.Count == 0) _members.Add("Wing");
        }


        // One row: a dot, the name and a label at the right.
        private void DrawRow(DrawingContext dc, double x, double y, string name, string label, Palette pal, Brush nameBrush)
        {
            dc.DrawEllipse(pal.Solid, null, new Point(x + 10, y + 7), 3, 3);
            DrawText(dc, name, MonoFace, Fit(name, 9, 74), nameBrush, x + 20, y + 2, 1, TextAlignment.Left);
            if (label.Length > 0) DrawText(dc, label, MonoFace, 6, pal.Solid, x + CardW - 8, y + 4, 0.8, TextAlignment.Right);
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            Palette pal = _kind switch { Kind.Add or Kind.Join => Green, Kind.Leave => Grey, Kind.Crew => Pink, _ => Blue };
            string head = _kind == Kind.Crew ? "CREW" : "WING";

            // The rows to show: you first, then the pilots (kept to what fits), then the one this is about.
            var rows = new List<(string name, string label, Palette pal, int kind)>();   // kind: 0 plain, 1 invited, 2 new, 3 leaving
            rows.Add(("YOU", "", Blue, 0));
            var shown = new List<string>(_members);
            if (_kind == Kind.Crew) shown.Clear();
            int room = (int)MaxRows - 1 - (_kind is Kind.Invite or Kind.Add or Kind.Crew ? 1 : 0);
            int extra = Math.Max(0, shown.Count - room);
            if (extra > 0) shown = shown.GetRange(0, room);
            for (int i = 0; i < shown.Count; i++) rows.Add((shown[i], _kind == Kind.Join ? "WING" : "", _kind == Kind.Join ? Green : Blue, _kind == Kind.Leave ? 3 : _kind == Kind.Join ? 2 : 0));
            if (_kind is Kind.Invite or Kind.Add or Kind.Crew) rows.Add((_name, _kind == Kind.Invite ? "INVITED" : _kind == Kind.Crew ? "CREW" : "JOINED", pal, _kind == Kind.Invite ? 1 : 2));
            if (extra > 0 && _kind != Kind.Leave) rows.Add(("+" + extra.ToString(CultureInfo.InvariantCulture) + " more", "", Blue, 0));
            double cardH = 18 + rows.Count * RowH + 4, x = CardX;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            for (int i = 0; i < 40; i++) dc.DrawRectangle(Level(Whites, 0.12 + 0.2 * Hash(i + 3)), null, new Rect(Hash(i) * SceneW, Hash(i + 9) * 92, 1, 1));

            dc.DrawRectangle(CardFill, CardEdge, new Rect(x, CardY, CardW, cardH));
            DrawSpaced(dc, head, MonoFace, 8, pal.Solid, x + CardW / 2, CardY + 4, 0.25, 1);

            double slideOff = x + CardW + px / k + 12;   // far enough to clear the left edge of the screen
            for (int i = 0; i < rows.Count; i++)
            {
                var (name, label, rp, rk) = rows[i];
                double y = CardY + 18 + i * RowH;
                if (rk == 3)
                {
                    // Leaving: struck through, then slid all the way off to the left (one after another), never faded.
                    double strike = Phase(t, 0.4 + i * 0.1, 0.25), run = Phase(t, 0.9 + i * 0.15, 0.8);
                    double dx = -run * run * run * slideOff;
                    dc.PushTransform(new TranslateTransform(dx, 0));
                    dc.DrawRectangle(Grey.RowFill, null, new Rect(x + 4, y - 2, CardW - 8, RowH - 1));
                    DrawRow(dc, x, y, name, "LEFT", Grey, GreyNameBrush);
                    if (strike > 0) dc.DrawLine(Grey.Tick, new Point(x + 14, y + 7), new Point(x + 14 + (CardW - 30) * strike, y + 7));
                    dc.Pop();
                    continue;
                }
                if (rk == 1)
                {
                    double s = EaseOut(Phase(t, 0.1, 0.4)), bl = 0.55 + 0.45 * Math.Sin(t * 8);
                    dc.PushOpacity(s);
                    dc.PushOpacity(bl); dc.DrawRectangle(rp.RowFill, rp.Dashed, new Rect(x + 4, y - 2, CardW - 8, RowH - 1)); dc.Pop();
                    DrawRow(dc, x, y, name, label, rp, NameBrush);
                    dc.Pop();
                    continue;
                }
                if (rk == 2)
                {
                    // A pilot who is in (joined, or on your crew): the row shows up, turns on, with a tick and a ripple.
                    double delay = _kind == Kind.Join ? 0.15 + (i - 1) * 0.2 : 0.1, s = EaseOut(Phase(t, delay, 0.4)), f = Phase(t, delay + 0.3, 0.4);
                    dc.PushOpacity(s);
                    dc.DrawRectangle(rp.RowFill, null, new Rect(x + 4, y - 2, CardW - 8, RowH - 1));
                    DrawRow(dc, x, y, name, label, rp, NameBrush);
                    dc.Pop();
                    if (_kind != Kind.Crew && f > 0)
                    {
                        dc.PushOpacity(f);
                        var tick = new StreamGeometry();
                        using (var c = tick.Open()) { c.BeginFigure(new Point(x + CardW - 24, y + 5), false, false); c.LineTo(new Point(x + CardW - 21, y + 9), true, false); c.LineTo(new Point(x + CardW - 16, y + 2), true, false); }
                        tick.Freeze(); dc.DrawGeometry(null, rp.Tick, tick);
                        dc.Pop();
                    }
                    double q = Phase(t, delay + 0.3, 0.7);
                    if (q > 0 && q < 1) dc.DrawEllipse(null, rp.Ripple[(int)Math.Round((1 - q) * 8)], new Point(x + 10, y + 7), 3 + q * 14, 3 + q * 14);
                    continue;
                }
                DrawRow(dc, x, y, name, label, rp, NameBrush);
            }

            // The plate.
            int pilots = _kind switch { Kind.Add => _members.Count + 2, Kind.Join => _members.Count + 1, _ => 0 };
            string l1 = _kind switch { Kind.Invite => "WING INVITE", Kind.Add => "JOINED THE WING", Kind.Join => "JOINED A WING", Kind.Leave => "LEFT THE WING", _ => "CREW MEMBER JOINED" };
            string l2 = _kind switch { Kind.Invite or Kind.Add or Kind.Crew => _name, Kind.Join => _members.Count == 1 ? _members[0] : _members.Count.ToString(CultureInfo.InvariantCulture) + " pilots already in it", _ => "You left the wing" };
            string l3 = _kind switch { Kind.Invite => "invites you to join a wing", Kind.Add or Kind.Join => "WING  -  " + pilots.ToString(CultureInfo.InvariantCulture) + " PILOTS", Kind.Crew => "aboard your ship", _ => "" };
            double plateAt = 1.4, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, pal.Edge, new Rect(26, 103, 188, l3.Length > 0 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, l1, MonoFace, Fit(l1, 12, 180), pal.Solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, l2, MonoFace, Fit(l2, 10, 180), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (l3.Length > 0) DrawSpaced(dc, l3, MonoFace, Fit(l3, 8, 180), pal.Solid, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.85);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
