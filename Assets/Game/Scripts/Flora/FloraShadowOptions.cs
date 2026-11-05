using System;
using System.Globalization;

namespace Punchy.Rendering
{
    /// <summary>
    /// W-107: three prototypes for cheaper tree shadows, to compare against the sun's shadow distance and cascades and
    /// the flora cast distance (W-102). Each is a runtime switch, off by default (all three off draws exactly as before),
    /// and also a command-line argument, so the profiler's bench can set them per run. Real-time shadows never reach
    /// past the near range: the options only change how trees inside it cast, or what stands in for them beyond it.
    /// FloraSystem reads them every frame, so flipping one takes effect on the next frame.
    /// </summary>
    public static class FloraShadowOptions
    {
        /// <summary>
        /// Cheap far caster (-cheapfarcaster): trees past NearRange cast into the shadow maps with their far level of
        /// detail (a few dozen triangles shaped like the crown and trunk) instead of the full mesh.
        /// </summary>
        public static bool CheapFarCaster = Has("-cheapfarcaster");

        /// <summary>
        /// Still far shadows (-stillfarshadows): in the flora shadow pass, wind sway and leaf flutter fade out round
        /// StillFrom, so far shadows stop moving (and the cascades stop shimmering) while the plants keep swaying.
        /// </summary>
        public static bool StillFarShadows = Has("-stillfarshadows");

        /// <summary>
        /// Cached far shadows (-cachedfarshadows): trees between NearRange and CacheRange cast into a cached texture
        /// laid from the sun, redrawn (over a few frames) only when the sun turns CacheSunDegrees or the camera moves
        /// CacheMove; only trees inside NearRange cast in the real-time cascades.
        /// </summary>
        public static bool CachedFarShadows = Has("-cachedfarshadows");

        /// <summary>Where the near range ends (m, -shadownear): full-mesh, real-time tree shadows stay inside it.</summary>
        public static float NearRange = Number("-shadownear", 25f);

        /// <summary>Where far shadows stop swaying (m, -stillfrom, 30-50): the sway fades out over 5 m either side.</summary>
        public static float StillFrom = Number("-stillfrom", 40f);

        /// <summary>How far the cached shadows reach (m, -shadowcachefar).</summary>
        public static float CacheRange = Number("-shadowcachefar", 50f);

        /// <summary>The cache is redrawn when the sun has turned this far (degrees) or the camera moved this far (m).</summary>
        public static float CacheSunDegrees = 0.5f, CacheMove = 10f;

        /// <summary>The cache's texture size (texels a side) and how many frames a redraw is spread over.</summary>
        public static int CacheSize = 1024, CacheFrames = 4;

        static bool Has(string arg) => Array.IndexOf(Environment.GetCommandLineArgs(), arg) >= 0;

        static float Number(string arg, float fallback)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, arg);
            return i >= 0 && i + 1 < args.Length &&
                   float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }
    }
}
