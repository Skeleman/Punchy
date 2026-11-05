using Punchy.Flora;
using Punchy.Terrain;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    public partial struct CreatureBrainSystem
    {
        /// <summary>
        /// A-73 row 3, a fishing folk's stages out on its wharf (Work mode; 1 to 3 are Fishing's, 4 is mending nets):
        /// walking out along the deck, walking back, carrying a crate out, and setting it down.
        /// </summary>
        public const byte PierOut = 40, PierBack = 41, PierCarry = 42, PierSetDown = 43, PierNets = 4;

        /// <summary>Out on a wharf's deck (or walking there): its height is the deck's, not the terrain floor's.</summary>
        internal static bool OnPier(in CreatureState s) =>
            s.PierSpot > 0 && s.Task == CreatureTask.Fish && s.Mode == CreatureMode.Work && s.Stage >= 1;

        // BrainJob, part: fishing folk on their village's wharf (A-73 row 3). Split by area (#61); the system, Execute
        // and Think live in CreatureBrainSystem.cs.
        partial struct BrainJob
        {
            /// <summary>
            /// A village fisher's day on its wharf: its village's wharf (the landmark's Wharf prop, placed as the art is)
            /// and a spot on it: mostly fishing off the end or a side, sometimes mending nets by the crates, now and then
            /// carrying a crate out. False (PierSpot 0) when there's no wharf, and it fishes from the shore as before.
            /// </summary>
            bool FindWharf(ref CreatureState s, ref Random rng)
            {
                if (!HasFlora || !HasWharves || !Terrain.Landmarks.IsCreated)
                    return false;
                ref var f = ref Flora.Value;
                ref var w = ref Wharves.Value;
                if (!LandmarkSampling.Find(ref Terrain.Landmarks.Value, s.Home, out var landmark) || landmark.Type >= f.LandmarkTypeCount)
                    return false;
                ref var set = ref Terrain.Landmarks.Value;
                for (int l = 0; l < 3; l++)
                {
                    var placed = f.PropRanges[landmark.Type * 3 + l];
                    for (int e = placed.x; e < placed.x + placed.y; e++)
                    {
                        var prop = f.Props[e];
                        if (prop.Count == 0 || f.Rules[prop.Species].PerVillage == 0 || f.Rules[prop.Species].FaceLandmark != 2)
                            continue;
                        var spot = LandmarkSampling.ToWorld(set.Instances[landmark.Instance],
                            LandmarkLayout.Spot(prop.Ring.x, prop.Angle), landmark.Radius);
                        var wharf = FloraPlacement.Place(ref f, Terrain, (int2)math.floor(spot / f.CellSizes[l]), l);
                        if (wharf.Model < 0 || wharf.Model >= w.ModelSpots.Length)
                            continue;
                        var range = w.ModelSpots[wharf.Model];
                        if (range.y == 0)
                            continue;
                        // What it does this time: 60% fishing (the end more often than a side), 25% nets, 15% a crate.
                        var roll = rng.NextFloat();
                        var want = roll < 0.6f ? (rng.NextFloat() < 0.5f ? WharfSpotKind.End : WharfSpotKind.Side)
                                 : roll < 0.85f ? WharfSpotKind.Nets : WharfSpotKind.Crate;
                        int pick = -1, seen = 0;
                        for (int k = range.x; k < range.x + range.y; k++)
                        {
                            var kind = w.Spots[k].Kind;
                            var fits = kind == want || want == WharfSpotKind.Side && kind == WharfSpotKind.End;
                            if (fits && rng.NextInt(++seen) == 0)
                                pick = k;
                        }
                        // (Nothing of that kind on this wharf: fishing, wherever there's room.)
                        for (int k = range.x; pick < 0 && k < range.x + range.y; k++)
                            if (w.Spots[k].Kind == WharfSpotKind.End || w.Spots[k].Kind == WharfSpotKind.Side)
                                pick = k;
                        if (pick < 0)
                            return false;
                        s.PierSpot = (short)(pick + 1);
                        s.PierOrigin = wharf.Position;
                        s.PierYaw = wharf.Yaw;
                        s.PierScale = wharf.Scale;
                        s.Stage = 0;
                        return true;
                    }
                }
                return false;
            }

            /// <summary>Point k of its spot's walk out, in the world, at the deck's height (or the ground's, off the end of the treads).</summary>
            float3 PierPoint(in CreatureState s, ref WharfSpotSet w, in WharfSpot spot, int k)
            {
                var local = w.Points[spot.Path.x + math.clamp(k, 0, spot.Path.y - 1)];
                var xz = WharfSpotSet.ToWorld(local, s.PierOrigin, s.PierYaw, s.PierScale);
                var deck = s.PierOrigin.y + WharfSpotSet.DeckHeight(local) * s.PierScale;
                var ground = Terrain.FloorUnder(new float3(xz.x, deck + 1f, xz.y), out _);
                return new float3(xz.x, math.max(deck, ground), xz.y);
            }

            /// <summary>A step along the deck towards a point (WalkOn, at the deck's height rather than the floor's); how far it still is.</summary>
            float WalkDeck(ref CreatureState s, ref CreatureBlob b, float3 target, float3 from, float speed, float dt)
            {
                var to = target.xz - s.Position.xz;
                var d = math.length(to);
                s.Speed = speed;
                CreatureMotion.Accelerate(ref s, ref b, dt);
                speed = s.Speed;
                if (d > 1e-3f)
                {
                    s.Position.xz += to / d * math.min(d, speed * dt);
                    var wanted = WrapAngle(math.atan2(to.x, to.y) - s.Yaw);
                    s.Yaw = WrapAngle(s.Yaw + math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt));
                }
                s.Speed = d > 0.05f ? speed : 0f;
                // (The height along the leg, so the treads are a ramp.)
                var leg = math.distance(from.xz, target.xz);
                var t = leg > 1e-3f ? math.saturate(1f - math.distance(s.Position.xz, target.xz) / leg) : 1f;
                s.Position.y = math.lerp(from.y, target.y, t);
                CreatureMotion.AdvancePhase(ref s, ref b, dt);
                return d;
            }

            /// <summary>
            /// Work on the wharf (PierSpot set): to the treads' foot like any walk to a post, then corner to corner along
            /// the deck to its spot (custom: off the terrain's floor), fishing, mending nets or setting a crate down there,
            /// and back the same way to the shore when its time's up, at dusk or in the rain. False if the spot is gone.
            /// </summary>
            bool PierWork(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, ref float targetSpeed,
                          ref bool moving, ref bool custom, ref float desiredLook)
            {
                if (!HasWharves || s.PierSpot - 1 >= Wharves.Value.Spots.Length)
                {
                    s.PierSpot = 0;
                    return false;
                }
                ref var w = ref Wharves.Value;
                var spot = w.Spots[s.PierSpot - 1];
                var last = spot.Path.y - 1;
                var wet = M.DiscomfortKind is ComfortKind.Rain or ComfortKind.Snow or ComfortKind.Storm && M.Discomfort > 0.3f;
                var leave = s.Timer <= 0f || Night > 0.5f || wet;
                if (s.Stage >= 1)
                    s.Timer -= dt;
                if (s.Stage == 0)
                {
                    var foot = PierPoint(in s, ref w, in spot, 0);
                    if (leave)
                    {
                        s.PierSpot = 0;
                        Pause(ref s, ref b, ref rng);
                        return true;
                    }
                    s.Target = foot.xz;
                    targetSpeed = walk;
                    moving = true;
                    if (math.distance(s.Position.xz, foot.xz) < 0.6f)
                    {
                        s.Stage = spot.Kind == WharfSpotKind.Crate ? PierCarry : PierOut;
                        s.StageTime = 0f;
                        s.ActionTime = 1f;
                    }
                    return true;
                }
                custom = true;
                switch (s.Stage)
                {
                    case PierOut:
                    case PierCarry:
                    {
                        if (leave && s.Stage == PierOut)
                        {
                            Back(ref s, last - 1);
                            break;
                        }
                        var k = (int)s.ActionTime;
                        var target = PierPoint(in s, ref w, in spot, k);
                        var from = PierPoint(in s, ref w, in spot, k - 1);
                        // (A crate's a load: a slower step.)
                        if (WalkDeck(ref s, ref b, target, from, walk * (s.Stage == PierCarry ? 0.75f : 1f), dt) < 0.08f)
                        {
                            s.ActionTime += 1f;
                            if (k >= last)
                            {
                                var face = WharfSpotSet.ToWorldYaw(spot.Yaw, s.PierYaw);
                                s.WorkFace = s.Position.xz + new float2(math.sin(face), math.cos(face)) * 4f;
                                s.Stage = spot.Kind == WharfSpotKind.Crate ? PierSetDown : spot.Kind == WharfSpotKind.Nets ? PierNets : (byte)1;
                                s.StageTime = 0f;
                                s.ActionTime = 0f;
                            }
                        }
                        break;
                    }
                    case PierBack:
                    {
                        var k = (int)s.ActionTime;
                        var target = PierPoint(in s, ref w, in spot, k);
                        var from = PierPoint(in s, ref w, in spot, k + 1);
                        if (WalkDeck(ref s, ref b, target, from, walk, dt) < 0.08f)
                        {
                            s.ActionTime -= 1f;
                            if (k <= 0)
                            {
                                // Back on the shore: on with its day.
                                s.PierSpot = 0;
                                Pause(ref s, ref b, ref rng);
                            }
                        }
                        break;
                    }
                    case PierSetDown:
                        // Bends and sets the crate down (the load's gone once it's here), straightens, and heads back.
                        s.Speed = 0f;
                        CreatureMotion.AdvancePhase(ref s, ref b, dt);
                        if (s.StageTime > 1.6f)
                            Back(ref s, last);
                        break;
                    case PierNets:
                    {
                        // Mending nets: facing the crates, hands busy, a look round now and then.
                        if (leave)
                        {
                            Back(ref s, last);
                            break;
                        }
                        var toWork = s.WorkFace - s.Position.xz;
                        var wanted = WrapAngle(math.atan2(toWork.x, toWork.y) - s.Yaw);
                        s.Yaw = WrapAngle(s.Yaw + math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt));
                        desiredLook = math.frac(s.ModeTime / 13f) > 0.85f ? math.sin(s.ModeTime * 0.7f) * 0.7f : 0f;
                        s.Speed = 0f;
                        CreatureMotion.AdvancePhase(ref s, ref b, dt);
                        break;
                    }
                    default:
                        // Fishing off the end or the side (Fishing's stages), till dusk, rain or its time's up.
                        if (leave)
                        {
                            Back(ref s, last);
                            break;
                        }
                        s.Speed = 0f;
                        Fishing(ref s, ref b, ref rng, dt, ref desiredLook);
                        CreatureMotion.AdvancePhase(ref s, ref b, dt);
                        break;
                }
                return true;
            }

            /// <summary>Turns round for the shore, from point k of the walk.</summary>
            static void Back(ref CreatureState s, int k)
            {
                s.Stage = PierBack;
                s.StageTime = 0f;
                s.ActionTime = math.max(k, 0);
            }
        }
    }
}
