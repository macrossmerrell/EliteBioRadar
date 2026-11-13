using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Vouchers cashed in at a station (the journal's RedeemVoucher): a voucher with a barcode, a red scan line sweeps down it, then a green PAID stamp lands. The plate gives the voucher type, the credits (counting up)
    // and the factions that paid when the journal names them. The voucher takes its colour from the type: bounty orange-red, combat bond green, codex violet, settlement cyan, anything else gold.
    // Detail = "TYPE|amount|faction;faction" with TYPE bounty, CombatBond, codex, settlement or any other word.
    public sealed class VoucherVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.0, TX = 120, TY = 39, TW = 72, TH = 48;
        private sealed class Palette
        {
            public readonly Brush Solid;
            public readonly Pen Edge;
            public Palette(Color c) { Solid = Frozen(c); Edge = FrozenPen(c, 1.2); }
        }
        private static readonly Palette BountyPal = new(Color.FromRgb(255, 138, 106)), BondPal = new(Color.FromRgb(125, 255, 176)), CodexPal = new(Color.FromRgb(200, 122, 255)),
            SettlePal = new(Color.FromRgb(85, 242, 255)), OtherPal = new(Color.FromRgb(255, 216, 144));
        private static readonly Brush GoldBrush = Frozen(Color.FromRgb(255, 216, 144)), NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)),
            TicketBrush = Frozen(Color.FromRgb(0x1b, 0x22, 0x30)), FaintBrush = Frozen(Color.FromArgb(77, 255, 255, 255)), BarBrush = Frozen(Colors.White), PaidBrush = Frozen(Color.FromRgb(125, 255, 176));
        private static readonly Pen PlateEdge = FrozenPen(Color.FromArgb(153, 255, 216, 144), 1), PaidPen = FrozenPen(Color.FromRgb(125, 255, 176), 2), ScanPen = FrozenPen(Color.FromArgb(230, 255, 60, 60), 1.5);

        private Palette _pal = OtherPal;
        private string _type = "VOUCHER";
        private long _amount;
        private string _factions = "";

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string kind = p.Length > 0 ? p[0].Trim() : "";
            _amount = p.Length > 1 && long.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) ? Math.Max(0, a) : 0;
            var names = new List<string>();
            if (p.Length > 2) foreach (var f in p[2].Split(';', StringSplitOptions.RemoveEmptyEntries)) if (f.Trim().Length > 0 && names.Count < 2) names.Add(f.Trim().ToUpperInvariant());
            _factions = string.Join(" / ", names);
            switch (kind.ToLowerInvariant())
            {
                case "bounty": _pal = BountyPal; _type = "BOUNTY VOUCHERS"; break;
                case "combatbond": _pal = BondPal; _type = "COMBAT BONDS"; break;
                case "codex": _pal = CodexPal; _type = "CODEX VOUCHER"; break;
                case "settlement": _pal = SettlePal; _type = "SETTLEMENT VOUCHER"; break;
                default: _pal = OtherPal; _type = (kind.Length > 0 ? kind.ToUpperInvariant() + " " : "") + "VOUCHER"; break;
            }
        }

        private static double Fit(string text, double max, double width) => Math.Min(max, width / (Math.Max(1, text.Length) * 0.66));

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeOutAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.3) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            dc.PushTransform(new ScaleTransform(u, u));
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            dc.PushTransform(new TranslateTransform(px, py));
            dc.PushTransform(new ScaleTransform(k, k));

            // The voucher drops in, with a coloured heading bar, two faint lines and a barcode.
            double drop = EaseOut(Phase(t, 0.1, 0.4));
            dc.PushOpacity(drop);
            dc.PushTransform(new TranslateTransform(0, (1 - drop) * -10));
            dc.DrawRoundedRectangle(TicketBrush, _pal.Edge, new Rect(TX - TW / 2, TY - TH / 2, TW, TH), 3, 3);
            dc.DrawRectangle(_pal.Solid, null, new Rect(TX - TW / 2 + 6, TY - TH / 2 + 6, TW * 0.4, 2.5));
            dc.DrawRectangle(FaintBrush, null, new Rect(TX - TW / 2 + 6, TY - TH / 2 + 12, TW * 0.6, 1.5));
            dc.DrawRectangle(FaintBrush, null, new Rect(TX - TW / 2 + 6, TY - TH / 2 + 17, TW * 0.5, 1.5));
            for (int i = 0; i < 24; i++) dc.DrawRectangle(BarBrush, null, new Rect(TX - 30 + i * 2.5, TY + 2, i % 3 == 0 ? 1.8 : 1.0, 10));
            dc.Pop(); dc.Pop();

            // The scan line.
            double sc = Phase(t, 0.5, 1.2);
            if (sc > 0 && sc < 1) dc.DrawLine(ScanPen, new Point(TX - TW / 2 - 6, TY - TH / 2 - 2 + sc * (TH + 4)), new Point(TX + TW / 2 + 6, TY - TH / 2 - 2 + sc * (TH + 4)));

            // PAID.
            double d = Phase(t, 1.8, 0.3);
            if (d > 0)
            {
                double s = 1 + (1 - EaseOut(d)) * 0.7;
                dc.PushTransform(new TranslateTransform(TX, TY)); dc.PushTransform(new RotateTransform(-10)); dc.PushTransform(new ScaleTransform(s, s));
                dc.PushOpacity(EaseOut(d));
                dc.DrawRectangle(null, PaidPen, new Rect(-20, -9, 40, 18));
                DrawSpaced(dc, "PAID", MonoFace, 12, PaidBrush, 0, -8, 0.06, 1);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }

            double plateAt = 2.3, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                long shown = (long)Math.Round(_amount * EaseOut(Phase(t, plateAt + 0.1, 1.0)));
                string credits = "+" + shown.ToString("N0", CultureInfo.InvariantCulture) + " CR" + (_factions.Length > 0 ? "  -  " + _factions : "");
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, PlateEdge, new Rect(20, 103, 200, 45), 6, 6);
                dc.Pop();
                DrawSpaced(dc, "VOUCHERS REDEEMED", MonoFace, 12, GoldBrush, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _type, MonoFace, Fit(_type, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                DrawSpaced(dc, credits, MonoFace, Fit(credits, 9, 190), _pal.Solid, SceneW / 2, 134, 0.06, Phase(t, plateAt + 0.4, 0.4));
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
