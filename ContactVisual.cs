using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace EliteBioRadar
{
    // Friends and chat (Friends events, ReceiveText from a player, friend, wing or squadron): a contact card slides in with a photo block, a name and a status LED.
    //   ON green, pulsing LED    OFF grey    REQ cyan, pulsing    ADD green    LOST red    DECL amber    DM violet / WING amber / SQ gold, the message types out on the card
    // Detail = "KIND|commander name|chip line|message text".
    public sealed class ContactVisual : AnimationVisual
    {
        private const double SceneW = 240, SceneH = 150, FadeOutAt = 4.6;
        private sealed class Palette
        {
            public readonly Brush Solid;
            public readonly Pen PlateEdge;
            public readonly Color C;
            public Palette(Color c) { C = c; Solid = Frozen(c); PlateEdge = FrozenPen(Color.FromArgb(153, c.R, c.G, c.B), 1); }
        }
        private static readonly Palette GreenPal = new(Color.FromRgb(95, 227, 176)), GreyPal = new(Color.FromRgb(127, 154, 154)), CyanPal = new(Color.FromRgb(85, 242, 255)), RedPal = new(Color.FromRgb(255, 107, 107)), AmberPal = new(Color.FromRgb(255, 180, 80)), VioletPal = new(Color.FromRgb(199, 155, 255)), GoldPal = new(Color.FromRgb(255, 216, 100));
        private static readonly Brush Paper = Frozen(Color.FromRgb(0xe9, 0xe4, 0xd6)), Ink = Frozen(Color.FromRgb(0x2a, 0x2a, 0x2a)), BandInk = Frozen(Color.FromRgb(8, 13, 13)), PhotoBack = Frozen(Color.FromRgb(0xb9, 0xb4, 0xa6)), PhotoInk = Frozen(Color.FromRgb(0x6a, 0x68, 0x60));
        private static readonly Brush NameBrush = Frozen(Color.FromRgb(0xe8, 0xf2, 0xff)), Gold = Frozen(Color.FromRgb(255, 216, 144)), PlateBrush = Frozen(Color.FromArgb(209, 5, 10, 20)), LedOff = Frozen(Color.FromRgb(0x77, 0x77, 0x77));

        private Palette _pal = GreenPal;
        private string _kind = "ON", _name = "", _sub = "", _title = "", _band = "CONTACT", _status = "", _text = "";
        private readonly List<string> _lines = new();
        private int _totalChars;

        protected override double Duration => FadeOutAt + 0.6;

        protected override void OnStart(string subject, string detail)
        {
            var p = (detail ?? "").Split('|');
            string P(int i) => p.Length > i ? p[i].Trim() : "";
            _kind = P(0).ToUpperInvariant(); _name = P(1).ToUpperInvariant(); _sub = P(2).ToUpperInvariant(); _text = P(3);
            _band = "CONTACT"; _status = ""; _lines.Clear(); _totalChars = 0;
            switch (_kind)
            {
                case "OFF": _pal = GreyPal; _title = "FRIEND OFFLINE"; _status = "OFFLINE"; break;
                case "REQ": _pal = CyanPal; _title = "FRIEND REQUEST"; _status = "REQUEST"; break;
                case "ADD": _pal = GreenPal; _title = "FRIEND ADDED"; _status = "FRIEND"; break;
                case "LOST": _pal = RedPal; _title = "FRIEND REMOVED"; _status = "REMOVED"; break;
                case "DECL": _pal = AmberPal; _title = "REQUEST DECLINED"; _status = "DECLINED"; break;
                case "DM": _pal = VioletPal; _title = "MESSAGE RECEIVED"; _band = "MESSAGE"; break;
                case "WING": _pal = AmberPal; _title = "WING MESSAGE"; _band = "WING"; break;
                case "SQ": _pal = GoldPal; _title = "SQUADRON MESSAGE"; _band = "SQUADRON"; break;
                default: _kind = "ON"; _pal = GreenPal; _title = "FRIEND ONLINE"; _status = "ONLINE"; break;
            }
            if (IsMsg)
            {
                // Wrap by characters (about 22 a line at this size), at most three lines.
                string txt = _text.Length > 63 ? _text.Substring(0, 60).TrimEnd() + "..." : _text;
                string line = "";
                foreach (var word in txt.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Length > 0 && line.Length + 1 + word.Length > 22 && _lines.Count < 2) { _lines.Add(line); line = word; }
                    else line = line.Length == 0 ? word : line + " " + word;
                }
                if (line.Length > 0) _lines.Add(line);
                foreach (var l in _lines) _totalChars += l.Length;
            }
        }

        private bool IsMsg => _kind == "DM" || _kind == "WING" || _kind == "SQ";
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

            const double cw = 156, ch = 62;
            double slide = EaseOut(Phase(t, 0.1, 0.55));
            if (slide > 0.01)
            {
                dc.PushTransform(new TranslateTransform(120 - (1 - slide) * 200, 52));
                dc.DrawRectangle(Paper, null, new Rect(-cw / 2, -ch / 2, cw, ch));
                dc.DrawRectangle(_pal.Solid, null, new Rect(-cw / 2, -ch / 2, cw, 10));
                DrawText(dc, _band, MonoFace, 7, BandInk, -cw / 2 + 5, -ch / 2 + 1.5, 1, TextAlignment.Left);
                var photo = new Rect(-cw / 2 + 6, -ch / 2 + 15, 28, 40);
                dc.DrawRectangle(PhotoBack, null, photo);
                dc.PushClip(ClipOf(photo));
                dc.DrawEllipse(PhotoInk, null, new Point(photo.X + 14, photo.Y + 13), 6, 7);
                dc.DrawEllipse(PhotoInk, null, new Point(photo.X + 14, photo.Y + 38), 12, 12);
                dc.Pop();
                double tx = -cw / 2 + 40, tw = cw - 46;
                DrawText(dc, _name, MonoFace, Fit(_name, 8, tw), Ink, tx, -ch / 2 + 14, 1, TextAlignment.Left);
                if (!IsMsg)
                {
                    bool live = _kind == "ON" || _kind == "REQ";
                    var led = _kind == "OFF" ? LedOff : _pal.Solid;
                    if (live) { dc.PushOpacity(0.35 + 0.35 * Math.Sin(t * 6)); dc.DrawEllipse(_pal.Solid, null, new Point(tx + 5, -ch / 2 + 35), 6, 6); dc.Pop(); }
                    dc.DrawEllipse(led, null, new Point(tx + 5, -ch / 2 + 35), 3.5, 3.5);
                    DrawText(dc, _status, MonoFace, 8, Ink, tx + 14, -ch / 2 + 30, 1, TextAlignment.Left);
                }
                else
                {
                    int shown = (int)(Math.Max(0, t - 0.9) * 26);
                    for (int i = 0; i < _lines.Count; i++)
                    {
                        int take = Math.Min(_lines[i].Length, shown);
                        if (take > 0) DrawText(dc, _lines[i].Substring(0, take), MonoFace, 7, Ink, tx, -ch / 2 + 27 + i * 9, 1, TextAlignment.Left);
                        shown -= _lines[i].Length;
                        if (shown <= 0) break;
                    }
                }
                dc.Pop();
            }

            double plateAt = 2.3, pa = Phase(t, plateAt, 0.4);
            if (pa > 0)
            {
                bool has3 = _sub.Length > 0;
                dc.PushOpacity(pa);
                dc.DrawRoundedRectangle(PlateBrush, _pal.PlateEdge, new Rect(20, 103, 200, has3 ? 45 : 34), 6, 6);
                dc.Pop();
                DrawSpaced(dc, _title, MonoFace, Fit(_title, 12, 190), _pal.Solid, SceneW / 2, 106, 0.08, pa);
                DrawSpaced(dc, _name, MonoFace, Fit(_name, 9, 190), NameBrush, SceneW / 2, 120, 0.08, Phase(t, plateAt + 0.2, 0.4));
                if (has3) DrawSpaced(dc, _sub, MonoFace, Fit(_sub, 8, 190), Gold, SceneW / 2, 134, 0.08, Phase(t, plateAt + 0.4, 0.4) * 0.9);
            }

            dc.Pop(); dc.Pop();
            if (a < 0.999) dc.Pop();
            dc.Pop();
        }
    }
}
