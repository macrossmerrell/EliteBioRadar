using System;
using System.Text;
using System.Windows.Media;

namespace EliteBioRadar
{
    // The embedded JetBrains Mono font (previously only referenced as MainWindow's own private
    // "GasGiantFont", scoped to the gas-giant/terrain/star HUD panels) is being rolled out across
    // the rest of the app one screen at a time — Radar tab first. Centralized here so every
    // screen picks up the exact same face/metrics instead of separately-declared FontFamily
    // instances drifting, and so the "tracked-out" letter-spacing look has one shared
    // implementation instead of being copy-pasted per screen.
    public static class AppFonts
    {
        // U+2060 WORD JOINER, zero-width, appended right after every hair space below. A hair
        // space is a legal line-break opportunity under Unicode line-breaking rules, so with one
        // between letters a wrapping TextBlock was free to break INSIDE a word — real report:
        // "Sulphur Dioxide Fumarole" in the Geo Survey sidebar wrapped as "Fu" / "marole". A word
        // joiner forbids a break on either side of it, so the only remaining break opportunities
        // are the real spaces between words. Measured against the embedded JetBrains Mono: it adds
        // no width (identical to the un-joined string) and no missing-glyph box, and at a 165px
        // column the un-joined text splits mid-word while the joined text wraps after "Dioxide".
        private const char WordJoiner = '⁠';

        public static readonly FontFamily Mono =
            new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#JetBrains Mono");

        // The gas-giant HUD's own Track() (MainWindow.xaml.cs) inserts a full thin space
        // (U+2009) between every letter — tuned for short, spacious stat labels/chips with a
        // lot of room to spare. Radar tab text sits in much tighter, more crowded space (blip
        // readouts glued to a moving dot, compass labels, range-ring ticks), so this uses a
        // hair space (U+200A, roughly half a thin space's width) instead — enough to read as
        // the same tracked-out "tech" style without needing every radar label's position
        // recalculated to avoid overlapping its neighbors.
        private const char HairSpace = ' ';

        public static string TrackLight(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == ' ') { sb.Append(' '); continue; }
                sb.Append(c).Append(HairSpace).Append(WordJoiner);
            }
            return sb.ToString();
        }

        // Half of TrackLight — a hair space after every OTHER letter instead of every letter.
        // The Bio Survey sidebar's per-entry rows (genus/species names, payout lines) sit even
        // tighter than the radar or Bio Sites sidebar (species names can run long, and the
        // payout line already has its own "Payout: 12,345 CR" text to fit), so even the hair
        // space's already-light tracking needed cutting again to keep things from wrapping.
        // Section headers/messages/the bottom Total Payout line use TrackLight instead — they
        // sit alone with real room to spare.
        public static string TrackMinimal(string s)
        {
            var sb = new StringBuilder();
            int letterIndex = 0;
            foreach (char c in s)
            {
                if (c == ' ') { sb.Append(' '); continue; }
                sb.Append(c);
                letterIndex++;
                if (letterIndex % 2 == 0) sb.Append(HairSpace).Append(WordJoiner);
            }
            return sb.ToString();
        }
    }
}
