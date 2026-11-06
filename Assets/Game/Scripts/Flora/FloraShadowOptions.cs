using System;
using System.Globalization;

namespace Punchy.Rendering
{
    /// <summary>
    /// W-107: three prototypes for cheaper tree shadows, to compare against the sun's shadow distance and cascades and
    /// the flora cast distance (W-102). Each is a runtime switch and also a command-line argument, so the profiler's
    /// bench can set them per run: "-name" or "-name 1" turns one on, "-name 0" turns it off (all three off draws
    /// exactly as before W-107). Since W-107 job 33 (Zach's call) the cheap far caster and the cached far shadows are on
    /// by default and still far shadows are off. Real-time shadows never reach past the near range: the options only
    /// change how trees inside it cast, or what stands in for them beyond it. FloraSystem reads them every frame, so
    /// flipping one takes effect on the next frame.
    /// </summary>
    public static class FloraShadowOptions
    {
        /// <summary>
        /// Cheap far caster (-cheapfarcaster, on by default): trees past NearRange cast into the shadow maps with their
        /// far level of detail (a few dozen triangles shaped like the crown and trunk) instead of the full mesh.
        /// </summary>
        public static bool CheapFarCaster = Switch("-cheapfarcaster", true);

        /// <summary>
        /// Still far shadows (-stillfarshadows, off by default): in the flora shadow pass, wind sway and leaf flutter
        /// fade out round StillFrom, so far shadows stop moving (and the cascades stop shimmering) while the plants keep
        /// swaying.
        /// </summary>
        public static bool StillFarShadows = Switch("-stillfarshadows", false);

        /// <summary>
        /// Cached far shadows (-cachedfarshadows, on by default): trees between NearRange and CacheRange cast into a
        /// cached texture laid from the sun, redrawn (over a few frames) only when the sun turns CacheSunDegrees or the
        /// camera moves CacheMove; only trees inside NearRange cast in the real-time cascades.
        /// </summary>
        public static bool CachedFarShadows = Switch("-cachedfarshadows", true);

        /// <summary>Where the near range ends (m, -shadownear): full-mesh, real-time tree shadows stay inside it.</summary>
        public static float NearRange = Number("-shadownear", 25f);

        /// <summary>Where far shadows stop swaying (m, -stillfrom, 30-50): the sway fades out over 5 m either side.</summary>
        public static float StillFrom = Number("-stillfrom", 40f);

        /// <summary>How far the cached shadows reach (m, -shadowcachefar). W-112: 120 (was 50, past which trees cast
        /// nothing at all, so a forest seen from afar had no shadows).</summary>
        public static float CacheRange = Number("-shadowcachefar", 120f);

        /// <summary>How much a cached shadow darkens what it falls on (0-1, -shadowcachedark, times the sun's shadow
        /// strength). W-112: 0.62, about what a real-time shadow takes away at midday (was 0.42, far shadows read pale).</summary>
        public static float CacheDarkness = Number("-shadowcachedark", 0.62f);

        /// <summary>The cache is redrawn when the sun has turned this far (degrees) or the camera moved this far (m).</summary>
        public static float CacheSunDegrees = 0.5f, CacheMove = 10f;

        /// <summary>The cache's texture size (texels a side) and how many frames a redraw is spread over.</summary>
        public static int CacheSize = 1024, CacheFrames = 4;

        /// <summary>A switch's value: the fallback when the argument is absent, off when "0" follows it, otherwise on.</summary>
        static bool Switch(string arg, bool fallback)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, arg);
            if (i < 0)
                return fallback;
            return !(i + 1 < args.Length && args[i + 1] == "0");
        }

        static float Number(string arg, float fallback)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, arg);
            return i >= 0 && i + 1 < args.Length &&
                   float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }
    }
}
