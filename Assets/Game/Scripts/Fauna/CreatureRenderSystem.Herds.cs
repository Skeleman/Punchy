using System.Collections.Generic;
using Punchy.Settings;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// The baked herds' far form (A-108 row 2), drawn only: every herd on the island, where the shared clock (HerdClock)
    /// has it, out to the edge of the far terrain, so hillsides far off have deer, goats and the like grazing on them.
    /// How much is drawn goes by each animal's size on screen (DetailPolicy.Herd*Pixels): a body, neck, head and legs
    /// while it's a few pixels long, a body and head below that, then a single body kept a pixel or so wide, fading out
    /// when even that is too small to see. Near a player the herd's animals are real (FaunaDirector.Herds), and the far
    /// form fades out where the live ones fade in, at the same places.
    /// A-108 row 4: a herd on the move over dry, loose ground (dunes, desert, ash, salt) trails a haze of dust behind
    /// each animal: a few soft see-through puffs that drift back, swell and thin out, in the ground's colour, picked by
    /// their own size on screen and faded with the animal.
    /// </summary>
    public partial class CreatureRenderSystem
    {
        /// <summary>One herding species' look, from its blob: size, coat colours, and its legs' hips and feet.</summary>
        sealed class HerdLook
        {
            public BlobAssetReference<CreatureBlob> Blob;
            public HerdShape Shape;
            public float RearRadius, FrontRadius, Flatten, StandHeight;
            public float3 Mouth;
            public bool HasMouth;
            public int HeadSegment = -1;
            public float3[] Hips, Feet;
            public float[] LegRadius;
            public int[] LegSegment;
        }

        struct HerdMember
        {
            public HerdLook Look;
            public float3 Position;
            public float Yaw, Scale, Faded, Ground;
            public int Coat;
            /// <summary>0 a single body, 1 body and head, 2 with neck and legs.</summary>
            public byte Tier;
            public bool Lying, Walking;
        }

        /// <summary>A herd's ground as a plane round its middle (height and slope), sampled again once it's moved on.</summary>
        struct HerdGround
        {
            public float2 At;
            public float Height;
            public float2 Slope;
        }

        /// <summary>A-108 row 4: one dust puff behind a walking far animal.</summary>
        struct HerdDust
        {
            public float3 At;
            public float Size, Faded;
            public float4 Colour;
        }

        /// <summary>Puffs trailing each walking animal on dusty ground, and how big (reference pixels) one must look to be drawn.</summary>
        const int HerdDustPuffs = 3;
        const float HerdDustPixels = 1.5f;
        readonly List<HerdDust> herdDust = new List<HerdDust>();
        /// <summary>Herds walking on dusty ground in sight last frame (probes).</summary>
        public static int HerdsDusty;

        /// <summary>The dust a biome's loose ground throws up (by its name hash, as AmbientLife's table), or w 0 for none.</summary>
        static float4 DustOf(uint biome) => biome switch
        {
            315160912u => new float4(0.80f, 0.68f, 0.48f, 1f),   // Dunes
            3368052845u => new float4(0.74f, 0.58f, 0.42f, 1f),  // Sonoran
            659108819u => new float4(0.74f, 0.50f, 0.34f, 1f),   // MesaCanyonlands
            3103161546u => new float4(0.82f, 0.76f, 0.62f, 1f),  // GlassDesert
            441784083u => new float4(0.46f, 0.44f, 0.43f, 1f),   // Ashlands
            3701048830u => new float4(0.90f, 0.88f, 0.84f, 1f),  // SaltFlat
            _ => default
        };

        const int HerdMaxLegs = 8;
        /// <summary>A live herd's far form shows in full this long after it woke, while its animals are made and fade in (s).</summary>
        const float HerdWakeGrace = 1.5f;
        /// <summary>Where a live herd's far form gives way to its animals by distance from the camera (m): the same band in
        /// which PoseJob fades the live animals out (FadeFar 165 less FadeOutRange 25), so the two crossfade.</summary>
        const float HerdHandOver = 140f, HerdHandOverRange = 25f;
        /// <summary>The shortest time a live herd's far form takes to fade out once its wake grace is over (s).</summary>
        const float HerdHandOverTime = 1.5f;

        readonly List<HerdMember> herdMembers = new List<HerdMember>();
        readonly Dictionary<int, HerdLook> herdLooks = new Dictionary<int, HerdLook>();
        int herdLooksVersion = -1;
        readonly Dictionary<int, HerdGround> herdGround = new Dictionary<int, HerdGround>();
        readonly Dictionary<long, float3> herdFeetGround = new Dictionary<long, float3>();
        int herdGroundKey;
        /// <summary>Herds live on this machine's reckoning (by index), and when they woke (render clock).</summary>
        readonly Dictionary<int, float> herdLive = new Dictionary<int, float>();
        readonly List<int> herdLiveGone = new List<int>();
        /// <summary>Herds and animals drawn last frame, and ground samples taken since the start (probes).</summary>
        public static int HerdsDrawn, HerdMembersDrawn, HerdGroundSamples;
        /// <summary>"-herdlog": a line every 5 s with the clock, herds drawn and which, so solo, host and client runs at the
        /// same camera can be compared (builds only; the editor reads the statics by eval).</summary>
        static readonly bool HerdLogOn = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-herdlog") >= 0;
        uint herdDrawnHash;
        float herdLogNext;

        void LogHerds(float hour, float now)
        {
            var hash = herdDrawnHash;
            herdDrawnHash = 17u;
            if (!HerdLogOn || now < herdLogNext)
                return;
            herdLogNext = now + 5f;
            var cam = UnityEngine.Camera.main;
            UnityEngine.Debug.Log($"Herds: hour {hour:F2} drawn {HerdsDrawn} members {HerdMembersDrawn} live {herdLive.Count} " +
                                  $"woken {FaunaDirector.HerdsWoken} set {hash:X8} cam {(cam != null ? cam.transform.position : default)}");
        }

        HerdLook LookFor(int key)
        {
            if (herdLooksVersion != FaunaDirector.HerdBlobsVersion)
            {
                herdLooks.Clear();
                herdLooksVersion = FaunaDirector.HerdBlobsVersion;
            }
            if (herdLooks.TryGetValue(key, out var look))
                return look;
            if (!FaunaDirector.HerdBlobs.TryGetValue(key, out var blob) || !blob.IsCreated)
            {
                herdLooks[key] = null;
                return null;
            }
            ref var b = ref blob.Value;
            look = new HerdLook
            {
                Blob = blob,
                Shape = HerdShape.Of(ref b),
                RearRadius = b.RearRadius,
                FrontRadius = b.FrontRadius,
                Flatten = b.BodyFlatten,
                StandHeight = b.StandHeight,
                Mouth = b.Mouth,
                HasMouth = b.HasMouth
            };
            var hips = new List<float3>();
            var feet = new List<float3>();
            var radius = new List<float>();
            var segment = new List<int>();
            for (int p = 0; p < b.Parts.Length; p++)
            {
                ref var part = ref b.Parts[p];
                if (part.Role == LimbRole.Head && look.HeadSegment < 0 && part.SegmentCount > 0)
                    look.HeadSegment = part.FirstSegment;
                if (part.Role != LimbRole.Leg || part.Parent != -1 || part.SegmentCount == 0 || hips.Count >= HerdMaxLegs)
                    continue;
                hips.Add(part.Socket);
                feet.Add(part.Socket + part.Stance);
                radius.Add(b.Segments[part.FirstSegment].RadiusStart);
                segment.Add(part.FirstSegment);
            }
            look.Hips = hips.ToArray();
            look.Feet = feet.ToArray();
            look.LegRadius = radius.ToArray();
            look.LegSegment = segment.ToArray();
            herdLooks[key] = look;
            return look;
        }

        static int HerdParts(in HerdMember m) => m.Tier == 2 ? 3 + m.Look.Hips.Length : m.Tier == 1 ? 2 : 1;

        /// <summary>The herds round the camera now (t, hour: the shared clock). Returns how many capsules they need.</summary>
        int GatherHerds(float3 cam, double t, float hour, in TerrainSource terrain, float now)
        {
            herdMembers.Clear();
            herdDust.Clear();
            HerdsDrawn = HerdMembersDrawn = HerdsDusty = 0;
            herdDrawnHash = 17u;
            if (!terrain.Herds.IsCreated || FaunaDirector.HerdBlobs.Count == 0 || CaveSystem.SurfaceHidden >= 0.99f)
                return 0;
            ref var set = ref terrain.Herds.Value;
            var key = terrain.Herds.GetHashCode();
            if (key != herdGroundKey || herdGround.Count > 4096 || herdFeetGround.Count > 4096)
            {
                herdGround.Clear();
                herdFeetGround.Clear();
                if (key != herdGroundKey)
                    herdLive.Clear();
                herdGroundKey = key;
            }
            if (skyCycle == null)
                skyCycle = UnityEngine.Object.FindAnyObjectByType<Punchy.Weather.DayNightCycle>();
            var time = HerdTime.From(skyCycle, SystemAPI.Time.ElapsedTime);
            time.T = t;
            time.Hour = hour;
            var ppm = DetailPolicy.Current.Data.PixelsPerMetre;
            var draw = TerrainStreamingSystem.DrawDistance;
            var players = ColossusClimbSystem.Players;
            if (players.Length == 0)
                players.Add(cam.xz);

            // Live herds this machine reckons have been left (the same rule as the host's FaunaDirector.Herds).
            herdLiveGone.Clear();
            foreach (var kv in herdLive)
                if (kv.Key >= set.Herds.Length)
                    herdLiveGone.Add(kv.Key);
            foreach (var i in herdLiveGone)
                herdLive.Remove(i);

            var parts = 0;
            // (Job 115: the herds in sight, their animals worked out together in Burst after this loop, CreatureRenderSystem.HerdJob.)
            var herdWork = new NativeList<HerdWork>(64, Allocator.TempJob);
            herdWorkInfo.Clear();
            for (int i = 0; i < set.Herds.Length; i++)
            {
                ref var h = ref set.Herds[i];
                var homeFar = math.distance(cam.xz, h.Home) - h.Range;
                var near = Nearest(players, h.Home) - h.Range;
                // (Coarse: beyond the far terrain's edge, or too small to see even at its nearest spot, and nobody near.)
                if (homeFar > draw && near > FaunaDirector.HerdLiveOut)
                    continue;
                var look = LookFor(h.Biome * 256 + h.Slot);
                if (look == null)
                    continue;
                var biggest = look.Shape.BodyLength * look.Shape.SizeMax;
                if (DetailPolicy.ScreenSize(biggest, math.max(homeFar, 1f), ppm) < DetailPolicy.HerdGonePixels && near > FaunaDirector.HerdLiveOut)
                    continue;
                var place = HerdClock.Place(ref set, h, look.Shape, time);
                // Live (its animals are real) on the same rule as the host: within HerdLiveIn of a player, until beyond HerdLiveOut.
                var nearest = Nearest(players, place.Centre);
                var live = herdLive.TryGetValue(i, out var woke);
                if (!live && nearest <= FaunaDirector.HerdLiveIn)
                {
                    herdLive[i] = woke = now;
                    live = true;
                }
                else if (live && nearest > FaunaDirector.HerdLiveOut)
                {
                    herdLive.Remove(i);
                    live = false;
                }
                var handing = live && now - woke > HerdWakeGrace;
                var dust = default(float4);
                if (place.Walking && terrain.Biomes.IsCreated && h.Biome < terrain.Biomes.Value.Biomes.Length)
                    dust = DustOf(terrain.Biomes.Value.Biomes[h.Biome].NameHash);
                if (dust.w > 0f)
                    HerdsDusty++;
                var ground = GroundUnder(terrain, i, place.Centre, HerdClock.Spread(h, look.Shape));
                if (!OldHerds)
                {
                    herdWork.Add(new HerdWork { Herd = h, Shape = look.Shape, Place = place, Ground = ground, Handing = handing, Woke = woke });
                    herdWorkInfo.Add((look, i, dust));
                    continue;
                }
                var drawn = false;
                for (int k = 0; k < h.Count; k++)
                {
                    var xz = place.Centre + HerdClock.Offset(h, look.Shape, k, t, place.Walking);
                    var y = ground.Height + math.dot(xz - ground.At, ground.Slope);
                    var at = new float3(xz.x, y, xz.y);
                    var d = math.distance(at, cam);
                    if (d > draw)
                        continue;
                    var scale = HerdClock.Scale(h, look.Shape, k);
                    var px = DetailPolicy.ScreenSize(look.Shape.BodyLength * scale, d, ppm);
                    if (px < DetailPolicy.HerdGonePixels)
                        continue;
                    var show = math.saturate((px - DetailPolicy.HerdGonePixels) / (DetailPolicy.HerdFadePixels - DetailPolicy.HerdGonePixels));
                    show *= 1f - math.saturate((d - draw * 0.85f) / (draw * 0.1f));
                    // Gives way to the live animals by distance, but never faster than HerdHandOverTime: a herd woken
                    // already inside the band (a quick rider) still crossfades rather than blinking out.
                    if (handing)
                        show *= math.max(math.saturate((d - HerdHandOver) / HerdHandOverRange),
                                         1f - math.saturate((now - woke - HerdWakeGrace) / HerdHandOverTime));
                    if (show <= 0.001f)
                        continue;
                    var member = new HerdMember
                    {
                        Look = look,
                        Position = at,
                        Ground = y,
                        Yaw = HerdClock.Yaw(h, place, k, t),
                        Scale = scale,
                        Faded = 1f - show,
                        Coat = HerdClock.Coat(ref look.Blob.Value, h, k),
                        Tier = (byte)(px >= DetailPolicy.HerdLegsPixels ? 2 : px >= DetailPolicy.HerdBodyPixels ? 1 : 0),
                        Lying = place.Lying,
                        Walking = place.Walking
                    };
                    // (Legs stand on the ground under the animal itself, not the herd's plane.)
                    if (member.Tier == 2)
                        member.Ground = MemberGround(terrain, i, k, xz, y);
                    herdMembers.Add(member);
                    parts += HerdParts(member);
                    if (dust.w > 0f)
                        parts += HerdDustBehind(member, k, t, d, ppm, show, dust);
                    drawn = true;
                }
                if (drawn)
                {
                    HerdsDrawn++;
                    herdDrawnHash = herdDrawnHash * 31u + (uint)i + 1u;
                }
            }
            parts += GatherHerdMembers(herdWork, terrain, cam, t, draw, ppm, now);
            herdWork.Dispose();
            HerdMembersDrawn = herdMembers.Count;
            LogHerds(hour, now);
            return parts;
        }

        /// <summary>
        /// A-108 row 4: the dust behind one walking animal: each puff starts at its heels, drifts back and up, swells
        /// and thins out over its cycle, then starts again, staggered so the trail is never empty. Returns puffs added.
        /// </summary>
        int HerdDustBehind(in HerdMember m, int k, double t, float distance, float ppm, float show, float4 colour)
        {
            var length = m.Look.Shape.BodyLength * m.Scale;
            var forward = new float3(math.sin(m.Yaw), 0f, math.cos(m.Yaw));
            var side = new float3(forward.z, 0f, -forward.x);
            var time = (float)(t % 10000.0);
            var added = 0;
            for (int j = 0; j < HerdDustPuffs; j++)
            {
                var u = math.frac(time * 0.45f + j / (float)HerdDustPuffs + k * 0.29f);
                var size = length * math.lerp(0.5f, 1.8f, u);
                if (DetailPolicy.ScreenSize(size, distance, ppm) < HerdDustPixels)
                    continue;
                var back = length * (0.6f + 2.2f * u);
                var sway = math.sin(u * 4f + k) * length * 0.3f;
                herdDust.Add(new HerdDust
                {
                    At = new float3(m.Position.x, m.Ground, m.Position.z) - forward * back + side * sway + new float3(0f, size * (0.3f + 0.4f * u), 0f),
                    Size = size,
                    // (Thin from the start and thinner as it swells: a haze, never a solid ball.)
                    Faded = 1f - show * 0.55f * math.smoothstep(0f, 0.15f, u) * (1f - u),
                    Colour = new float4(colour.xyz, 0f)
                });
                added++;
            }
            return added;
        }

        static float Nearest(in FixedList128Bytes<float2> players, float2 at)
        {
            var best = float.MaxValue;
            for (int p = 0; p < players.Length; p++)
                best = math.min(best, math.distance(players[p], at));
            return best;
        }

        /// <summary>A herd's ground plane round its middle, sampled again once the middle has moved 3 m.</summary>
        HerdGround GroundUnder(in TerrainSource terrain, int herd, float2 centre, float spread)
        {
            if (herdGround.TryGetValue(herd, out var g) && math.distancesq(g.At, centre) < 9f)
                return g;
            var step = math.max(spread, 4f);
            var h0 = terrain.SampleHeight(centre);
            var hx = terrain.SampleHeight(centre + new float2(step, 0f));
            var hz = terrain.SampleHeight(centre + new float2(0f, step));
            HerdGroundSamples += 3;
            g = new HerdGround { At = centre, Height = h0, Slope = new float2(hx - h0, hz - h0) / step };
            herdGround[herd] = g;
            return g;
        }

        /// <summary>The ground under one animal near enough for legs, sampled again once it's moved 1 m.</summary>
        float MemberGround(in TerrainSource terrain, int herd, int k, float2 xz, float fallback)
        {
            var id = (long)herd * 64 + k;
            if (herdFeetGround.TryGetValue(id, out var g) && math.distancesq(g.xy, xz) < 1f)
                return g.z;
            var y = terrain.SampleHeight(xz);
            HerdGroundSamples++;
            if (float.IsNaN(y))
                y = fallback;
            herdFeetGround[id] = new float3(xz, y);
            return y;
        }

        /// <summary>Writes the gathered herds' animals.</summary>
        int WriteHerds(double t, NativeArray<SegmentInstance> output, int at)
        {
            foreach (var puff in herdDust)
            {
                var r = puff.Size * 0.5f;
                output[at++] = Part(puff.At - new float3(r * 0.4f, 0f, 0f), quaternion.RotateY(1.5708f), r * 0.8f, r, r, 0.75f, puff.Colour, puff.Faded);
            }
            var up = new float3(0f, 1f, 0f);
            var time = (float)(t % 10000.0);
            var ppm = DetailPolicy.Current.Data.PixelsPerMetre;
            float3 cam = default;
            if (UnityEngine.Camera.main != null)
                cam = UnityEngine.Camera.main.transform.position;
            foreach (var m in herdMembers)
            {
                var look = m.Look;
                ref var b = ref look.Blob.Value;
                var s = m.Scale;
                var coat = b.Coats.Length > 0 ? b.Coats[math.clamp(m.Coat, 0, b.Coats.Length - 1)] : default;
                var colour = new float4(coat.Base.xyz, 0f);
                var forward = new float3(math.sin(m.Yaw), 0f, math.cos(m.Yaw));
                var rotation = quaternion.RotateY(m.Yaw);
                var length = look.Shape.BodyLength * s;
                var rear = look.RearRadius * s;
                var front = look.FrontRadius * s;
                // Lying bedded down: the body low on the ground, legs folded under (not drawn).
                var lift = m.Lying ? rear * 0.8f : math.max(look.StandHeight * s, rear);
                var middle = new float3(m.Position.x, m.Ground + lift, m.Position.z);
                if (m.Tier == 0)
                {
                    // A single body, never thinner than a pixel and a half on screen.
                    var widest = DetailPolicy.HerdDotPixels * 0.5f * math.distance(middle, cam) / math.max(ppm, 1f);
                    var r = math.max(math.max(rear, front), widest);
                    var l = math.max(length, widest * 2f);
                    output[at++] = Part(middle - forward * l * 0.5f, rotation, l, r, r, math.max(look.Flatten, 0.6f), colour, m.Faded);
                    continue;
                }
                output[at++] = Part(middle - forward * length * 0.5f, rotation, length, rear, front, look.Flatten, colour, m.Faded);
                // The head, out at the mouth (heads down to graze when standing still).
                var mouth = look.HasMouth ? look.Mouth * s : new float3(0f, front * 0.5f, length * 0.6f);
                if (!m.Walking && !m.Lying)
                    mouth.y -= look.StandHeight * s * (0.5f + 0.5f * math.sin(time * 0.3f + m.Yaw * 5f));
                var head = middle + math.mul(rotation, mouth);
                var neckFrom = middle + forward * length * 0.4f;
                var neck = head - neckFrom;
                var neckLength = math.length(neck);
                var neckTurn = quaternion.LookRotationSafe(neckLength > 1e-4f ? neck / neckLength : forward, up);
                var headColour = look.HeadSegment >= 0 ? b.SegmentColor(look.HeadSegment, m.Coat) : colour;
                if (m.Tier == 1)
                {
                    output[at++] = Part(neckFrom, neckTurn, neckLength, front * 0.55f, front * 0.4f, 1f, headColour, m.Faded);
                    continue;
                }
                output[at++] = Part(neckFrom, neckTurn, neckLength, front * 0.45f, front * 0.3f, 1f, colour, m.Faded);
                var headLength = front * 1.1f;
                output[at++] = Part(head - forward * headLength * 0.6f, rotation, headLength, front * 0.4f, front * 0.25f, 1f, headColour, m.Faded);
                // Legs from the hips to the ground under the feet (swinging on the move); folded away lying down.
                for (int l = 0; l < look.Hips.Length; l++)
                {
                    var hip = middle + math.mul(rotation, look.Hips[l] * s);
                    var foot = middle + math.mul(rotation, look.Feet[l] * s);
                    if (m.Walking)
                        foot += forward * math.sin(time * look.Shape.WalkSpeed * 3f + l * 1.7f) * length * 0.15f;
                    foot.y = m.Lying ? hip.y : m.Ground;
                    var leg = foot - hip;
                    var legLength = math.length(leg);
                    if (m.Lying || legLength < 1e-3f)
                    {
                        output[at++] = Part(hip, rotation, 0f, 0f, 0f, 1f, colour, 1f);
                        continue;
                    }
                    var legColour = b.SegmentColor(look.LegSegment[l], m.Coat);
                    var radius = look.LegRadius[l] * s;
                    output[at++] = Part(hip, quaternion.LookRotationSafe(leg / legLength, forward), legLength, radius, radius * 0.7f, 1f, legColour, m.Faded);
                }
            }
            return at;
        }
    }
}
