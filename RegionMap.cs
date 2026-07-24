// Adapted from https://github.com/klightspeed/EliteDangerousRegionMap (MIT
// License, Copyright (c) 2020 Ben Peddell). Trimmed to the offline
// coordinate->region lookup only — the original also includes an EDSM-backed
// system-name lookup and a CLI entrypoint, neither of which EliteBioRadar
// needs, since it already has each system's StarPos from FSDJump/Location.
namespace EliteDangerousRegionMap
{
    public class Region
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public static partial class RegionMap
    {
        private const double x0 = -49985;
        private const double y0 = -40985;
        private const double z0 = -24105;

        public static Region? FindRegion(double x, double y, double z)
        {
            var px = (int)((x - x0) * 83 / 4096);
            var pz = (int)((z - z0) * 83 / 4096);

            if (px < 0 || pz < 0 || pz >= RegionMapLines.Length)
                return null;

            var row = RegionMapLines[pz];
            var rx = 0;
            var pv = 0;

            foreach (var (rl, rv) in row)
            {
                if (px < rx + rl)
                {
                    pv = rv;
                    break;
                }
                rx += rl;
            }

            return pv == 0 ? null : new Region { Id = pv, Name = RegionNames[pv]! };
        }
    }
}
