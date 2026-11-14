using Punchy.Settings;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// The surface glow's frame update, moved out of CaveSystem.cs unchanged (R-25 row 2, rule 28): gathers the near
    /// glow lights and street lights for LightBudget, and hands the far, world and town glow maps to the shaders
    /// (Glow.hlsl). The maps themselves are SurfaceGlow's.
    /// </summary>
    public partial class CaveSystem
    {
        readonly SurfaceGlow surfaceGlow = new SurfaceGlow();

        /// <summary>
        /// Glowing plants on the surface near the camera join the glow list (the nearest 16 lights of any kind win), so
        /// their light falls on the ground, plants and creatures round them as cave plants' does in caves.
        /// </summary>
        void GatherSurfaceGlow(in TerrainSource source, float3 camPos)
        {
            Shader.SetGlobalVector(GlowMapParamsId, Vector4.zero);
            Shader.SetGlobalVector(GlowWorldParamsId, Vector4.zero);
            Shader.SetGlobalVector(GlowTownParamsId, Vector4.zero);
            if (paletteQuery.IsEmpty || Underground > 0.9f)
                return;
            var palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            if (flora == null && Time.unscaledTime >= nextFloraLookup)
            {
                nextFloraLookup = Time.unscaledTime + 1f;
                flora = FindAnyObjectByType<Punchy.Flora.FloraSystem>();
            }
            surfaceGlow.Update(source, palette, flora, camPos);
            var night = Shader.GetGlobalFloat(NightId);
            var windowsOn = night < 0.25f ? 0f : math.smoothstep(0.25f, 0.55f, night);
            surfaceGlow.DrawSpill(windowsOn * (1f - Underground), camPos);   // W-127: lit windows' pools outside
            // The whole island's static glow (R-2, Glow.hlsl), past the far map and fading into it.
            if (surfaceGlow.WorldReady)
            {
                Shader.SetGlobalTexture(GlowWorldId, surfaceGlow.WorldMap);
                Shader.SetGlobalVector(GlowWorldRectId, new Vector4(surfaceGlow.WorldCorner.x, surfaceGlow.WorldCorner.y, 1f / surfaceGlow.WorldExtent, 0f));
                var glowFade = DetailPolicy.FadeReach(DetailPolicy.WorldGlowSize, DetailPolicy.WorldGlowFromPixels, DetailPolicy.WorldGlowToPixels, WorldGlowEdge);
                Shader.SetGlobalVector(GlowWorldParamsId, new Vector4((1f - Underground) * WorldGlowStrength, night, glowFade.x, glowFade.y));
                // R-10 row 4: distant towns' windows and street lights (on with the windows), fading in where the far
                // map's street lights fade out at its edge, and the light dome over each in the air.
                if (SurfaceGlow.TownGlowEnabled && surfaceGlow.TownMap != null)
                {
                    Shader.SetGlobalTexture(GlowTownId, surfaceGlow.TownMap);
                    // (Rule 20: by a street light's lit ground on screen, as the world glow above; R-25 row 3.)
                    var townFade = DetailPolicy.FadeReach(DetailPolicy.TownGlowSize, DetailPolicy.TownGlowFromPixels, DetailPolicy.TownGlowToPixels, TownGlowEdge);
                    Shader.SetGlobalVector(GlowTownParamsId, new Vector4(windowsOn * (1f - Underground) * TownGlowStrength,
                                                                         TownGlowLevelOff, townFade.x, townFade.y));
                    Shader.SetGlobalVector(GlowTownColorId, TownGlowColor);
                    if (weather == null && Time.unscaledTime >= nextWeatherLookup)
                    {
                        nextWeatherLookup = Time.unscaledTime + 1f;
                        weather = FindAnyObjectByType<Punchy.Weather.WeatherSystem>();
                    }
                    surfaceGlow.DrawDomes(windowsOn * (1f - Underground), weather != null ? weather.Local.Fog : 1f, camPos);
                }
                // R-10 row 4c: every static light seen from afar as a point, bright by its tier (FarLightPolicy).
                surfaceGlow.DrawFarLights(windowsOn * (1f - Underground), camPos);
            }
            // The far tier (Glow.hlsl): every glowing plant and lit window within about 300 m, baked into a light map,
            // fading in where LightBudget hands a typical glow from its slots to the far tier (refactor 3.1).
            var handOff = Punchy.Settings.LightBudget.GlowHandOff(Punchy.Settings.LightKind.AmbientGlow, TypicalGlowReach);
            // Street lights are real lights (pooled) and then glow slots much further out, so the night map has its own.
            var fixtureHandOff = Punchy.Settings.LightBudget.GlowHandOff(Punchy.Settings.LightKind.Fixture, TypicalFixtureReach);
            if (surfaceGlow.MapReady && (GlowTiers & 2) != 0)
            {
                Shader.SetGlobalTexture(GlowMapId, surfaceGlow.MapPlants);
                Shader.SetGlobalTexture(GlowMapNightId, surfaceGlow.MapWindows);
                Shader.SetGlobalVector(GlowMapRectId, new Vector4(surfaceGlow.MapCorner.x, surfaceGlow.MapCorner.y,
                                                                  1f / (SurfaceGlow.MapSize * SurfaceGlow.MapTexel), 0f));
                // (y: the night map, street lights only (W-127). Lit windows' light is indoor (P-21, item 31), so
                // SurfaceGlow leaves them out of it: see the near tier below.)
                Shader.SetGlobalVector(GlowMapParamsId, new Vector4((1f - Underground) * FarGlowStrength, windowsOn * (1f - Underground), handOff.x * 0.7f, handOff.y));
                // (R-10: from the Fixture hand-off, or nearer where the budget ran out first: past the nearest street
                // light that neither a real light nor a slot lights in full, the map must carry them, or the town
                // goes dark in between.)
                float nightFrom = fixtureHandOff.x * 0.85f, nightTo = fixtureHandOff.y;
                var gap = Punchy.Settings.LightBudget.StreetLightGapAt;
                // (And no further out than the far scan lists street lights for the budget.)
                gap = math.min(gap, SurfaceGlow.FarFixtureReach * 0.75f);
                if (gap < nightFrom)
                    (nightFrom, nightTo) = (gap * 0.85f, math.max(gap + 15f, gap * 1.4f));
                Shader.SetGlobalVector(GlowMapNightFadeId, new Vector4(nightFrom, nightTo, 0f, 0f));
            }
            if ((GlowTiers & 1) == 0)
                return;
            // W-127: street lights (lanterns, braziers, pier lamps), lit after dark like windows, but outdoors. Each is
            // lent a pooled real light (R-10) or a glow slot; past the hand-off the night map lights it in full, so it
            // asks for nothing there. The near scan reaches only about 50 m, so further out they come from the far
            // scan's list (the tree layer's, where the kit's street lights grow), out to where it is sure to be whole.
            var fixtureReach = math.min(fixtureHandOff.y, SurfaceGlow.FarFixtureReach);
            var farFixtures = surfaceGlow.FarFixtures.Count > 0;
            if (night >= 0.25f && farFixtures)
                foreach (var (position, light) in surfaceGlow.FarFixtures)
                {
                    var d = Vector3.Distance(position, camPos);
                    if (d > NearFixtures && d <= fixtureReach)
                        AddStreetLight(position, light, windowsOn);
                }
            // The near tier: each a request; LightBudget fades them out by on-screen size as the far map takes over.
            foreach (var (position, light) in surfaceGlow.Found)
            {
                var l = light;
                if (Punchy.Flora.FloraPlacement.IsFixtureGlow(l.w))
                {
                    var d = Vector3.Distance(position, camPos);
                    if (night >= 0.25f && d <= (farFixtures ? NearFixtures : fixtureHandOff.y))
                        AddStreetLight(position, light, windowsOn);
                    continue;
                }
                if (l.w < 0f)
                {
                    // Lit windows (only after dark) are the light inside a house: an indoor fixture (P-21, playtest 11-08
                    // item 31), which lights only its building's inside. It used to go out as a glow from the house's
                    // middle and lit its outer walls and the ground all round through the walls. The panes keep their
                    // own night glow (Flora shader).
                    if (night < 0.25f)
                        continue;
                    l = new Vector4(l.x, l.y, l.z, 0f) * windowsOn;
                    l.w = -light.w;
                    Punchy.Settings.LightBudget.AddIndoor(Punchy.Settings.LightKind.Fixture, position, l);
                    continue;
                }
                Punchy.Settings.LightBudget.AddStill(Punchy.Settings.LightKind.AmbientGlow, position, l);
            }
        }

        /// <summary>Within this of the camera (m) street lights come from the near scan (every layer, fresh every 4 m);
        /// past it, from the far scan's list. A seam between two lists of the same lights (same keys, same request
        /// either side), inside the near scan's radius: streaming, not a look, so it stays in metres (R-25 row 3).</summary>
        const float NearFixtures = 40f;

        static void AddStreetLight(Vector3 position, Vector4 light, float windowsOn)
        {
            var l = new Vector4(light.x, light.y, light.z, 0f) * windowsOn;
            l.w = Punchy.Flora.FloraPlacement.GlowReach(light.w);
            Punchy.Settings.LightBudget.AddPooled(Punchy.Settings.LightKind.Fixture, position, l);
        }

        static readonly int NightId = Shader.PropertyToID("_PunchyNight");
        static readonly int GlowMapId = Shader.PropertyToID("_PunchyGlowMap");
        static readonly int GlowMapNightId = Shader.PropertyToID("_PunchyGlowMapNight");
        static readonly int GlowMapRectId = Shader.PropertyToID("_PunchyGlowMapRect");
        static readonly int GlowMapParamsId = Shader.PropertyToID("_PunchyGlowMapParams");
        static readonly int GlowMapNightFadeId = Shader.PropertyToID("_PunchyGlowMapNightFade");
        static readonly int GlowWorldId = Shader.PropertyToID("_PunchyGlowWorld");
        static readonly int GlowWorldRectId = Shader.PropertyToID("_PunchyGlowWorldRect");
        static readonly int GlowWorldParamsId = Shader.PropertyToID("_PunchyGlowWorldParams");
        /// <summary>The world glow's brightness. Its plants and lava fade in over a camera distance by how big a glow looks
        /// (DetailPolicy.WorldGlowFromPixels to WorldGlowToPixels: 240 m to 320 m at the reference view), never past
        /// WorldGlowEdge, where the 640 m far map fades out towards its edge.</summary>
        public static float WorldGlowStrength = 1f;
        const float WorldGlowEdge = 320f; // streaming: the far map's coverage, not a look
        static readonly int GlowTownId = Shader.PropertyToID("_PunchyGlowTown");
        static readonly int GlowTownParamsId = Shader.PropertyToID("_PunchyGlowTownParams");
        static readonly int GlowTownColorId = Shader.PropertyToID("_PunchyGlowTownColor");
        /// <summary>R-10 row 4: the town map's brightness, how hard a dense town levels off and its warm colour. It fades
        /// in by screen size (DetailPolicy.TownGlow*), at the far map's edge, where its street lights fade out. Row 4b: about three
        /// times as bright (was 0.12), so the glow sits on the town rather than in the sky. Row 4c: 0.22 (was 0.35; Zach:
        /// the town a little less intense), so with the far lights a town reads 20-30% dimmer.</summary>
        public static float TownGlowStrength = 0.22f;
        const float TownGlowLevelOff = 0.15f;
        const float TownGlowEdge = 330f; // streaming: just past the far map's coverage, not a look
        static readonly Vector4 TownGlowColor = new Vector4(1f, 0.6f, 0.28f, 0f);
        Punchy.Weather.WeatherSystem weather;
        float nextWeatherLookup;
        /// <summary>A typical surface glow's reach (m), for where the far map takes over (LightBudget.GlowHandOff): the
        /// far map fades in from 70% of where the near lights start fading to where they're gone, so the hand-over adds
        /// up to about the same light.</summary>
        const float TypicalGlowReach = 8f;
        /// <summary>A typical street light's reach (m), for where the night map takes over from them.</summary>
        const float TypicalFixtureReach = 9f;
        /// <summary>Which surface glow tiers are on (1 near lights, 2 far map): both, bar a frame-cost measurement.</summary>
        public static int GlowTiers = 3;
        /// <summary>The far map's brightness: a little under the near lights', so a cluster reads as a soft wash from
        /// afar rather than a floodlit floor.</summary>
        public static float FarGlowStrength = 1f;
        /// <summary>The far map's last scan time (ms, start to pick-up), for the frame-cost report.</summary>
        public float FarGlowScanMs => surfaceGlow.FarScanMs;

        Punchy.Flora.FloraSystem flora;
    }
}
