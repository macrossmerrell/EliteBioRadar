using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Pickups of the same material within a couple of seconds are one animation: the watcher adds each to this tally and the visual, still playing, ticks its counter up and pulses again.
    public static class MaterialPickupState
    {
        private static readonly object Lock = new();
        private static string _key = "";
        private static int _total, _stock = -1, _cap = -1, _version;
        private static DateTime _lastAt = DateTime.MinValue;

        // Records a pickup. True when it continues the animation already showing / waiting for this material (no new animation needed).
        public static bool Add(string key, int count, int stock, int cap)
        {
            lock (Lock)
            {
                var now = DateTime.UtcNow;
                bool cont = string.Equals(key, _key, StringComparison.OrdinalIgnoreCase) && (now - _lastAt).TotalSeconds < 2.2;
                if (cont) { _total += count; _version++; }
                else { _key = key; _total = count; _version = 0; }
                _stock = stock; _cap = cap; _lastAt = now;
                return cont;
            }
        }

        public static (string key, int total, int stock, int cap, int version) Snapshot() { lock (Lock) return (_key, _total, _stock, _cap, _version); }
    }

    // Something was collected (the journal's MaterialCollected): a periodic-table style tile flips in (symbol, number, name) with a pulse for each pickup, "+N" counting up in its corner, and
    // a bar showing the stock against the cap (raw materials only; amber and "FULL" at the cap). Detail = "name|category|internal name|count|stock|cap" (stock / cap -1 when unknown).
    public sealed class MaterialVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, TileX = 87.5, TileY = 6, TileW = 65, TileH = 62;

        private static readonly Dictionary<string, (string sym, int num)> Elements = new(StringComparer.OrdinalIgnoreCase)
        {
            ["carbon"] = ("C", 6), ["iron"] = ("Fe", 26), ["nickel"] = ("Ni", 28), ["phosphorus"] = ("P", 15), ["sulphur"] = ("S", 16), ["arsenic"] = ("As", 33), ["chromium"] = ("Cr", 24),
            ["germanium"] = ("Ge", 32), ["manganese"] = ("Mn", 25), ["vanadium"] = ("V", 23), ["zinc"] = ("Zn", 30), ["zirconium"] = ("Zr", 40), ["cadmium"] = ("Cd", 48), ["mercury"] = ("Hg", 80),
            ["molybdenum"] = ("Mo", 42), ["niobium"] = ("Nb", 41), ["tin"] = ("Sn", 50), ["tungsten"] = ("W", 74), ["antimony"] = ("Sb", 51), ["polonium"] = ("Po", 84), ["ruthenium"] = ("Ru", 44),
            ["selenium"] = ("Se", 34), ["technetium"] = ("Tc", 43), ["tellurium"] = ("Te", 52), ["yttrium"] = ("Y", 39),
        };

        private static readonly Color Mint = Color.FromRgb(0x5f, 0xe3, 0xb0), Amber = Color.FromRgb(0xff, 0xb8, 0x4d);
        private static readonly Brush MintBrush = Frozen(Mint), AmberBrush = Frozen(Amber), LightBrush = Frozen(Color.FromRgb(0xe8, 0xf6, 0xf0)), MutedBrush = Frozen(Color.FromRgb(0x6f, 0xa8, 0x9a));
        private static readonly Brush PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), TileMint = Frozen(Color.FromArgb(31, Mint.R, Mint.G, Mint.B)), TileAmber = Frozen(Color.FromArgb(31, Amber.R, Amber.G, Amber.B));
        private static readonly Brush BarBack = Frozen(Color.FromArgb(77, 120, 140, 150)), BarMint = Frozen(Color.FromArgb(230, Mint.R, Mint.G, Mint.B)), BarAmber = Frozen(Color.FromArgb(230, Amber.R, Amber.G, Amber.B));
        private static readonly Pen MintEdge = FrozenPen(Mint, 1.5), AmberEdge = FrozenPen(Amber, 1.5);
        private static readonly Pen MintPlateEdge = FrozenPen(Color.FromArgb(153, Mint.R, Mint.G, Mint.B), 1), AmberPlateEdge = FrozenPen(Color.FromArgb(153, Amber.R, Amber.G, Amber.B), 1);
        private static readonly Pen[] MintPulse = AlphaPens(Mint, 1.5), AmberPulse = AlphaPens(Amber, 1.5);

        private string _display = "", _category = "", _key = "", _symbol = "", _number = "";
        private int _kind;   // 0 raw element (chemical symbol), 1 manufactured (chip glyph), 2 encoded (bits glyph)
        private int _total = 1, _stock = -1, _cap = -1, _version;
        private bool _hasStock;
        private double _stockFrom, _stockTo, _stockAt = 0.8, _lastPick = 0.8;
        private readonly List<double> _picks = new();
        private readonly ScaleTransform _outer = new(), _fit = new();   // reused every frame instead of new transforms
        private readonly TranslateTransform _shift = new();

        private double FadeAt => Math.Max(3.6, _lastPick + 2.4);
        protected override double Duration => FadeAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            _display = p.Length > 0 ? p[0].Trim() : "";
            _category = p.Length > 1 ? p[1].Trim() : "";
            _key = p.Length > 2 ? p[2].Trim() : "";
            _total = p.Length > 3 && int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? Math.Max(1, c) : 1;
            _stock = p.Length > 4 && int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : -1;
            _cap = p.Length > 5 && int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cp) ? cp : -1;
            if (_display.Length == 0) _display = "Material";
            _kind = _category.StartsWith("Manu", StringComparison.OrdinalIgnoreCase) ? 1 : _category.StartsWith("Enc", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
            if (Elements.TryGetValue(_key, out var el)) { _symbol = el.sym; _number = el.num.ToString(CultureInfo.InvariantCulture); }
            else
            {
                var initials = new string(_display.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])).Take(3).ToArray());
                _symbol = initials.Length > 0 ? initials : "?";
                _number = _category.StartsWith("Manu", StringComparison.OrdinalIgnoreCase) ? "MFG" : _category.StartsWith("Enc", StringComparison.OrdinalIgnoreCase) ? "ENC" : "";
            }
            _hasStock = _stock >= 0 && _cap > 0;
            _stockFrom = _hasStock ? Math.Max(0, _stock - _total) : 0; _stockTo = _stock;
            _stockAt = 0.8; _lastPick = 0.8;
            _picks.Clear(); _picks.Add(0.8);
            _version = MaterialPickupState.Snapshot().version;   // pickups added after this one are the ones to pulse for
        }

        // The chemical symbol for a raw material's internal name (used by the discovery card); false for names the table does not know.
        internal static bool TryElementSymbol(string key, out string symbol)
        {
            if (Elements.TryGetValue(key ?? "", out var el)) { symbol = el.sym; return true; }
            symbol = ""; return false;
        }

        private static readonly Pen PinPen = FrozenPen(Mint, 1);
        private static readonly Brush BitOff = Frozen(Color.FromArgb(51, Mint.R, Mint.G, Mint.B));

        // Manufactured: a circuit chip with a die in the middle and four pins on each side.
        private static void DrawChip(DrawingContext dc, double cx, double cy)
        {
            dc.DrawRectangle(null, MintEdge, new Rect(cx - 12, cy - 12, 24, 24));
            dc.DrawRectangle(null, MintEdge, new Rect(cx - 5.5, cy - 5.5, 11, 11));
            for (int i = 0; i < 4; i++)
            {
                double o = -7.5 + i * 5;
                dc.DrawLine(PinPen, new Point(cx + o, cy - 12), new Point(cx + o, cy - 18));
                dc.DrawLine(PinPen, new Point(cx + o, cy + 12), new Point(cx + o, cy + 18));
                dc.DrawLine(PinPen, new Point(cx - 12, cy + o), new Point(cx - 18, cy + o));
                dc.DrawLine(PinPen, new Point(cx + 12, cy + o), new Point(cx + 18, cy + o));
            }
        }

        // Encoded: a 5 by 5 grid of bits that flickers on and off.
        private static void DrawBits(DrawingContext dc, double cx, double cy, double t)
        {
            int step = (int)(t * 6);
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                    dc.DrawRectangle(Hash(r * 5 + c + step * 3) > 0.5 ? MintBrush : BitOff, null, new Rect(cx - 18 + c * 7.5, cy - 18 + r * 7.5, 5.5, 5.5));
        }

        private void PollPickups(double t)
        {
            var s = MaterialPickupState.Snapshot();
            if (!string.Equals(s.key, _key, StringComparison.OrdinalIgnoreCase) || s.version == _version) return;
            _version = s.version;
            double shown = _hasStock ? _stockFrom + (_stockTo - _stockFrom) * EaseOut(Phase(t, _stockAt, 0.5)) : 0;
            _total = s.total; _stock = s.stock; _cap = s.cap;
            _hasStock = _stock >= 0 && _cap > 0;
            _stockFrom = shown; _stockTo = _stock; _stockAt = t; _lastPick = t;
            if (_picks.Count < 12) _picks.Add(t);
        }

        protected override void Render(DrawingContext dc, double w, double h, double t)
        {
            PollPickups(t);
            var (fadeOut, env, u, W, H) = LayoutScene(w, h, t, FadeAt);
            if (env <= 0) return;
            double a = Clamp01(t / 0.2) * fadeOut, k = Math.Min(1.25, (W - 40) / SceneW), px = (W - SceneW * k) / 2, py = (H - SceneH * k) / 2;

            _outer.ScaleX = _outer.ScaleY = u; dc.PushTransform(_outer);
            dc.DrawRectangle(Level(Backdrops, BackdropOpacity * env), null, new Rect(-40, -40, W + 80, H + 80));
            if (a < 0.999) dc.PushOpacity(a);
            _shift.X = px; _shift.Y = py; dc.PushTransform(_shift);
            _fit.ScaleX = _fit.ScaleY = k; dc.PushTransform(_fit);

            bool full = _hasStock && _stock >= _cap;
            var accent = full ? AmberBrush : MintBrush;
            double fl = EaseOut(Phase(t, 0.2, 0.6)), tw = TileW * Math.Abs(Math.Cos(Math.PI / 2 * (1 - fl))), cx = TileX + TileW / 2;
            var tile = new Rect(cx - tw / 2, TileY, tw, TileH);
            dc.DrawRectangle(full ? TileAmber : TileMint, full ? AmberEdge : MintEdge, tile);
            if (fl > 0.7)
            {
                if (_kind == 1) DrawChip(dc, cx, TileY + 27);
                else if (_kind == 2) DrawBits(dc, cx, TileY + 27, t);
                else DrawSpaced(dc, _symbol, MonoFace, _symbol.Length > 2 ? 20 : 28, LightBrush, cx, TileY + 12, 0.02, 1);
                if (_number.Length > 0) DrawSpaced(dc, _number, MonoFace, 7, LightBrush, TileX + 11, TileY + 5, 0.05, 1);
                DrawSpaced(dc, "+" + _total.ToString(CultureInfo.InvariantCulture), MonoFace, 8, accent, TileX + TileW - 12, TileY + 5, 0.05, 1);
                DrawSpaced(dc, _display.ToUpperInvariant(), MonoFace, Math.Min(7, 56 / (Math.Max(1, _display.Length) * 0.62)), MutedBrush, cx, TileY + 50, 0.05, 1);
            }
            foreach (double pk in _picks)
            {
                double f = Phase(t, pk, 0.4);
                if (f <= 0 || f >= 1) continue;
                double grow = f * 7;
                dc.DrawRectangle(null, (full ? AmberPulse : MintPulse)[Math.Clamp((int)Math.Round((1 - f) * 8), 0, 8)], new Rect(TileX - grow, TileY - grow, TileW + grow * 2, TileH + grow * 2));
            }

            if (_hasStock && fl > 0.7)
            {
                double shown = _stockFrom + (_stockTo - _stockFrom) * EaseOut(Phase(t, _stockAt, 0.5));
                const double bx = 75, by = 76, bw = 90;
                dc.DrawRectangle(BarBack, null, new Rect(bx, by, bw, 5));
                dc.DrawRectangle(full ? BarAmber : BarMint, null, new Rect(bx, by, bw * Clamp01(shown / _cap), 5));
            }

            double plateAt = 1.6, pa = Phase(t, plateAt, 0.5);
            if (pa > 0)
            {
                dc.PushOpacity(Phase(t, plateAt - 0.1, 0.3));
                dc.DrawRoundedRectangle(PlateBrush, full ? AmberPlateEdge : MintPlateEdge, new Rect(26, 103, 188, 45), 6, 6);
                dc.Pop();
                DrawSpaced(dc, "MATERIAL COLLECTED", MonoFace, 12, accent, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _display + "  +" + _total.ToString(CultureInfo.InvariantCulture), MonoFace, Math.Min(10, 190 / ((_display.Length + 5) * 0.66)), LightBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                string cat = _category.ToUpperInvariant();
                string line3 = _hasStock ? $"{cat}  -  STOCK {_stock} / {_cap}" + (full ? "  FULL" : "") : cat;
                DrawSpaced(dc, line3, MonoFace, 8, full ? AmberBrush : MutedBrush, SceneW / 2, 134, 0.1, Phase(t, plateAt + 0.4, 0.4));
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
