using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Ambience
{
    /// <summary>
    /// W-127: the beacon towers' fires (FloraPlacement.BeaconSpot: one in each big town's square). Lit at night with the
    /// landmarks' night fires: near the camera a standard fire flame in the tower's bowl that lights the square (a
    /// LightBudget fire, wider than a campfire's), and past that a far flame (FireSystem's far level of detail, a flame and
    /// a glow card) out to BeaconFarRange, so a town's beacon is seen across the island. The towers never move, so their
    /// fires are found once per world (all settlements, a few dozen) and only drawn after that. Cosmetic only.
    /// </summary>
    public partial class LandmarkEffectSystem
    {
        /// <summary>Beacon fires keep a far flame out to this distance (GraphicsSettings.ViewRange scales it).</summary>
        const float BeaconFarRange = 1600f;
        const float BeaconFlame = 1.5f, BeaconFarFlame = 3f, BeaconFarGrow = 200f;
        const int BeaconSeedBase = 0x6E000000;
        /// <summary>Debug switch for before/after costs.</summary>
        public static bool BeaconsOn = true;

        readonly List<float3> beacons = new List<float3>();
        TerrainSource beaconSource;
        bool beaconsFound;

        /// <summary>The beacon fires found in this world (for probes and tests).</summary>
        public static int LastBeacons;

        void Beacons(in TerrainSource source, Vector3 eye, float lit, float flameRange)
        {
            if (!beaconsFound || !source.Equals(beaconSource))
            {
                if (!source.Settlements.IsCreated || !TryGetFlora(out var blob))
                    return;
                beaconSource = source;
                beaconsFound = true;
                beacons.Clear();
                var count = source.Settlements.Value.Settlements.Length;
                for (int s = 0; s < count; s++)
                    if (Punchy.Flora.FloraPlacement.BeaconFire(ref blob.Value, source, s, out var fire))
                        beacons.Add(fire);
                LastBeacons = beacons.Count;
            }
            if (!BeaconsOn || lit <= 0.01f || beacons.Count == 0)
                return;
            // Tonight's fire colour, as the Ember Forest's: the world map's fire channel lights the square from afar in it.
            var colour = cycle != null ? ForestColour((uint)source.World.Seed, cycle.Day, cycle.TimeOfDay) : FireSystem.Wood;
            var far = Punchy.Settings.GraphicsSettings.ViewRange(BeaconFarRange);
            var t = Time.time;
            for (int i = 0; i < beacons.Count; i++)
            {
                Vector3 at = beacons[i];
                var seed = BeaconSeedBase + i * 977;
                var d = Vector3.Distance(at, eye);
                var fade = Punchy.Settings.DetailPolicy.Crossfade(d, flameRange * HandOverFrom, flameRange);
                if (d < flameRange)
                {
                    FireSystem.AddFlame(at, BeaconFlame * lit * (1f - fade), seed, colour);
                    var flicker = 0.85f + 0.1f * Mathf.Sin(t * 7.1f + (i & 255)) + 0.05f * Mathf.Sin(t * 19.3f + i);
                    var light = FireSystem.Light(colour, flicker * lit * 1.8f);
                    light.w *= 2.6f;   // it lights the square below, not only the deck
                    Punchy.Settings.LightBudget.Add(Punchy.Settings.LightKind.Fire, seed, at, light);
                }
                if (d > flameRange * HandOverFrom && d < far)
                {
                    // It grows with distance so it stays a few pixels across, a point of fire on the horizon.
                    var size = BeaconFarFlame * math.max(1f, d / BeaconFarGrow) * lit * fade * math.saturate((far - d) / (far * 0.15f));
                    if (size > 0.05f)
                        FireSystem.AddFarFlame(at, size, seed + 1, colour);
                }
            }
        }
    }
}
