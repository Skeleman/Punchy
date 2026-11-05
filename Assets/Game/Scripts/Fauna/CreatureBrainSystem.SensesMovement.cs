using Punchy.Flora;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Punchy.Fauna
{
    public partial struct CreatureBrainSystem
    {
        // BrainJob, part: finding predators/prey/kin, Advance, walls, solids, terrain helpers. Split from CreatureBrainSystem.cs by area (#61);
        // the system, Execute and Think live there.
        partial struct BrainJob
        {
            // ------------------------------------------------------------------------------- seeing each other

            /// <summary>A predator of this species close by (further off if it's hunting this animal).</summary>
            bool FindPredator(Entity self, ref CreatureState s, int species, float alert, out float2 at)
            {
                using var profiled = PredatorMarker.Auto();
                System.Threading.Interlocked.Increment(ref Searches.Data.Predator);
                at = default;
                var best = float.MaxValue;
                var c = Cell(s.Position.xz);
                for (int z = -2; z <= 2; z++)
                for (int x = -2; x <= 2; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        // (A calm dog that never hunts is no hunter: prey takes no notice of it.)
                        if (Entities[j] == self || !Hunts(ref Bodies[j].Blob.Value, species) ||
                            !CreaturePersonality.WillHunt(ref Bodies[j].Blob.Value, States[j].Individual))
                            continue;
                        var other = States[j];
                        // In three dimensions: a bird circling high overhead isn't close.
                        var d = math.distance(other.Position, s.Position);
                        // (A hunter still creeping up on it is noticed only close; one running at it from well off.)
                        var range = (other.Mode == CreatureMode.Hunt && other.Partner == self ? (other.Stage == 0 ? 12f : 30f) : 8f) * alert;
                        // A bird of prey overhead goes unnoticed until it dives (a fish only sees it hit the water).
                        if (Bodies[j].Blob.Value.Habitat == Habitat.Flies)
                            range = other.Mode == CreatureMode.Hunt && other.Partner == self && other.Stage == 2 ? 3f : 0f;
                        if (d < range && d < best)
                        {
                            best = d;
                            at = other.Position.xz;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return best < float.MaxValue;
            }

            /// <summary>Another of its kind close by that has just started running: where from (what it runs from).</summary>
            bool FindAlarm(Entity self, ref CreatureState s, int species, out float2 from)
            {
                using var profiled = AlarmMarker.Auto();
                System.Threading.Interlocked.Increment(ref Searches.Data.Alarm);
                from = default;
                var c = Cell(s.Position.xz);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var other = States[j];
                        if (Entities[j] != self && Bodies[j].Species == species && other.Mode == CreatureMode.Flee && other.ModeTime < 3f &&
                            math.distance(other.Position.xz, s.Position.xz) < 12f)
                        {
                            from = other.Focus;
                            return true;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            /// <summary>Another bird of its kind close by that has just taken off somewhere (not fleeing).</summary>
            bool FindTakeoff(Entity self, ref CreatureState s, int species, out CreatureState leader)
            {
                leader = default;
                var c = Cell(s.Position.xz);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var other = States[j];
                        if (Entities[j] != self && Bodies[j].Species == species && other.Mode == CreatureMode.Wander && other.Stage >= 1 &&
                            other.ModeTime < 1.5f && math.distance(other.Position.xz, s.Position.xz) < 12f)
                        {
                            leader = other;
                            return true;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            /// <summary>Takes off after a flock-mate, to land a little way from where it's going (same height: a branch, or the ground).</summary>
            void Follow(ref CreatureState s, in CreatureState leader, ref Random rng)
            {
                SetMode(ref s, CreatureMode.Wander);
                s.Stage = 1;
                var groundThere = Terrain.SampleHeight(leader.Target);
                var onBranch = leader.TargetHeight > groundThere + 0.5f;
                s.Target = leader.Target + rng.NextFloat2Direction() * (onBranch ? rng.NextFloat(0.3f, 1.2f) : rng.NextFloat(0.8f, 3f));
                s.TargetHeight = onBranch ? leader.TargetHeight + rng.NextFloat(-0.4f, 0.4f) : Terrain.SampleHeight(s.Target);
            }

            /// <summary>Another of its kind within earshot, howling.</summary>
            bool Howling(Entity self, ref CreatureState s, int species)
            {
                var c = Cell(s.Position.xz);
                for (int z = -3; z <= 3; z++)
                for (int x = -3; x <= 3; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j] != self && Bodies[j].Species == species && States[j].Action == IdleAction.Howl &&
                            States[j].ActionTime > 0.2f)
                            return true;
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            static bool Hunts(ref CreatureBlob b, int species)
            {
                for (int k = 0; k < b.Prey.Length; k++)
                    if (b.Prey[k] == species)
                        return true;
                return false;
            }

            /// <summary>Someone close by who has started a game of chase with this animal.</summary>
            bool FindPlaymate(Entity self, ref CreatureState s, out Entity playmate)
            {
                using var profiled = PlaymateMarker.Auto();
                playmate = Entity.Null;
                var c = Cell(s.Position.xz);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var other = States[j];
                        if (other.Mode == CreatureMode.Play && other.Partner == self && other.Stage == 0)
                        {
                            playmate = Entities[j];
                            return true;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            /// <summary>The nearest free animal of the same species within range.</summary>
            bool FindKin(Entity self, ref CreatureState s, int species, float range, out Entity found)
            {
                found = Entity.Null;
                var best = range;
                var c = Cell(s.Position.xz);
                var r = (int)math.ceil(range / CellSize);
                for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j] == self || Bodies[j].Species != species || !Free(States[j].Mode))
                            continue;
                        var d = math.distance(States[j].Position.xz, s.Position.xz);
                        if (d < best)
                        {
                            best = d;
                            found = Entities[j];
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return found != Entity.Null;
            }

            /// <summary>The nearest animal it hunts within range.</summary>
            bool FindPrey(Entity self, ref CreatureState s, ref CreatureBlob b, float range, out Entity found)
            {
                found = Entity.Null;
                var best = range;
                var c = Cell(s.Position.xz);
                var r = (int)math.ceil(range / CellSize);
                for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        // (Never one someone is holding or petting, or one already caught.)
                        if (Entities[j] == self || !Hunts(ref b, Bodies[j].Species) || States[j].Holder != 0 ||
                            States[j].Mode == CreatureMode.Held || States[j].Gone)
                            continue;
                        var d = math.distance(States[j].Position.xz, s.Position.xz);
                        if (d < best)
                        {
                            best = d;
                            found = Entities[j];
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return found != Entity.Null;
            }

            // ------------------------------------------------------------------------------- ground

            /// <summary>
            /// Looks around for water within 50 m: the spot to stand (on dry land, front end at the edge) and the
            /// water it faces.
            /// </summary>
            bool FindWater(ref CreatureState s, ref CreatureBlob b, ref Random rng, out float2 shore, out float2 water)
            {
                shore = water = default;
                var start = rng.NextFloat(math.PI * 2f);
                for (int k = 0; k < 8; k++)
                {
                    var a = start + k * math.PI * 0.25f;
                    var dir = new float2(math.sin(a), math.cos(a));
                    for (var d = 6f; d <= 50f; d += 8f)
                    {
                        var p = s.Position.xz + dir * d;
                        if (Walkable(p, Habitat.Land))
                            continue;
                        // Back up from the water to dry land.
                        var q = p;
                        for (int step = 0; step < 30 && !Walkable(q, Habitat.Land); step++)
                            q -= dir;
                        if (!Walkable(q, Habitat.Land))
                            break;
                        water = p;
                        shore = q + dir * 0.6f - dir * (b.BodyLength * 0.4f * s.Scale);
                        return true;
                    }
                }
                return false;
            }

            /// <summary>
            /// Moves along the heading and advances the gait by the distance covered, so feet don't slide. Land animals
            /// keep to the ground (unless <paramref name="keepHeight"/>: a bird on a branch); swimmers and floaters to
            /// the water.
            /// </summary>
            void Advance(ref CreatureState s, ref CreatureBlob b, float dt, bool keepHeight = false)
            {
                using var profiled = AdvanceMarker.Auto();
                var forward = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
                CreatureMotion.Accelerate(ref s, ref b, dt);
                if (b.Crawls && CaveWalker(s, ref b))
                {
                    Crawl(ref s, ref b, dt);
                    return;
                }
                if (CaveWalker(s, ref b))
                {
                    // On the passage floor, never into rock: blocked, it slides along the wall (the nearest open way
                    // to either side of its heading, turning towards it), or stops.
                    var step = s.Speed * dt;
                    var next = s.Position.xz + forward * step;
                    var floorY = s.Position.y;
                    var stepped = step > 0f && CaveStep(next, s.Position.y, step, out floorY);
                    if (step > 0f && !stepped)
                    {
                        var moved = false;
                        // (All the way round if need be: cornered, it turns back the way it came.)
                        for (int k = 1; k <= 7 && !moved; k++)
                            for (int side = -1; side <= 1 && !moved; side += 2)
                            {
                                var angle = s.Yaw + side * k * 0.45f;
                                var slide = s.Position.xz + new float2(math.sin(angle), math.cos(angle)) * step;
                                if (CaveStep(slide, s.Position.y, step, out floorY))
                                {
                                    stepped = true;
                                    next = slide;
                                    s.Yaw = WrapAngle(s.Yaw + WrapAngle(angle - s.Yaw) * 0.3f);
                                    moved = true;
                                }
                            }
                        if (!moved)
                        {
                            next = s.Position.xz;
                            s.Speed = 0f;
                        }
                    }
                    s.Position.xz = next;
                    // (Only a floor in the cave: outside it FloorUnder gives the surface, which would lift it through
                    // the rock to the ground overhead.)
                    // Onto the floor: up a step at once (only as far as the step allowed, A-69), down off a ledge falling.
                    if (stepped || CaveFloorAt(s.Position.xz, s.Position.y, out floorY, 0.05f))
                        s.Position.y = floorY > s.Position.y ? floorY : math.max(floorY, s.Position.y - dt * 6f);
                    CreatureMotion.AdvancePhase(ref s, ref b, dt);
                    return;
                }
                var to = s.Position.xz + forward * s.Speed * dt;
                if (s.Speed > 0f && WalksRoundWalls(in s, ref b, keepHeight))
                {
                    using var walls = WallsMarker.Auto();
                    // Buildings are solid: walled off, it walks along the wall (the nearest open way to either side,
                    // turning towards it), so it goes round the building rather than through it; boxed in, it stops.
                    // (One already inside, spawned there, walks out.)
                    var radius = math.max(b.FrontRadius * s.Scale, 0.2f);
                    if (Walled(to, radius) && !Walled(s.Position.xz, radius))
                    {
                        var step = s.Speed * dt;
                        var moved = false;
                        for (int k = 1; k <= 7 && !moved; k++)
                            for (int side = -1; side <= 1 && !moved; side += 2)
                            {
                                var angle = s.Yaw + side * k * 0.45f;
                                var slide = s.Position.xz + new float2(math.sin(angle), math.cos(angle)) * step;
                                if (!Walled(slide, radius))
                                {
                                    to = slide;
                                    s.Yaw = WrapAngle(s.Yaw + WrapAngle(angle - s.Yaw) * 0.3f);
                                    moved = true;
                                }
                            }
                        if (!moved)
                        {
                            to = s.Position.xz;
                            s.Speed = 0f;
                        }
                    }
                }
                // A-44: near a player, cave walls, towers and icebergs are solid too: it slides along them by its own
                // body's size, and one already in one is pushed out sideways.
                if (nearSolids && !keepHeight && s.Fly < 0.5f && s.Mode != CreatureMode.Held && !b.CaveDweller && !underRock)
                {
                    using var solids = SolidsMarker.Auto();
                    to = KeepOutOfSolids(ref s, ref b, to, forward, dt);
                }
                s.Position.xz = to;
                if (b.Haunts)
                {
                    // Drifting half a metre above the ground or water, rising and sinking slowly.
                    var g = Terrain.SampleGround(s.Position.xz);
                    var floor = g.HasWater ? math.max(g.Height, g.WaterSurface) : g.Height;
                    var drift = floor + 0.45f * s.Scale + math.sin(s.Clock * 0.9f + (s.Individual % 7)) * 0.12f * s.Scale;
                    s.Position.y += (drift - s.Position.y) * math.min(1f, dt * 2f);
                }
                else if (b.Habitat == Habitat.Land || b.Habitat == Habitat.Flies)
                {
                    // (A-44: over a cave entrance it stands on the carved ground, not where the heightmap would have been.)
                    if (!keepHeight)
                        s.Position.y = CreatureMotion.SolidsOff.Data != 0 ? Terrain.SampleHeight(s.Position.xz)
                                                                          : CreatureMotion.Ground(Terrain, s.Position.xz, s.Position.y);
                    // Out of its depth, a land animal that swims sits low: only its head, neck and a strip of back
                    // above the surface (a bear's broad back shows more than a deer's), legs paddling below, bobbing
                    // gently. Wading in, it settles from standing on the bottom into that as the water deepens.
                    // Folk out swimming lie in the water, back just under the surface, bobbing; wading in or out they
                    // stand on the bottom (settling into the swim as the water deepens).
                    if (b.Upright && s.Mode == CreatureMode.Swim)
                    {
                        var g = Terrain.SampleGround(s.Position.xz);
                        if (g.HasWater && g.WaterSurface > g.Height)
                        {
                            var deep = math.smoothstep(b.StandHeight * s.Scale * 0.6f, b.StandHeight * s.Scale * 0.9f, g.WaterSurface - g.Height);
                            // (Low: the back a hand's breadth under, only the shoulders and the raised head out.)
                            var floatAt = g.WaterSurface - 0.24f * s.Scale + math.sin(s.Clock * 1.9f) * 0.02f * s.Scale;
                            s.Position.y = math.lerp(g.Height, floatAt, deep);
                        }
                    }
                    if (b.CrossesWater)
                    {
                        var g = Terrain.SampleGround(s.Position.xz);
                        if (g.HasWater && g.WaterSurface > g.Height)
                        {
                            var swim = SwimDepth(ref b, s.Scale, g.WaterSurface - g.Height);
                            var floatAt = g.WaterSurface - b.StandHeight * s.Scale - b.FrontRadius * b.BodyFlatten * s.Scale * 0.55f +
                                          math.sin(s.Clock * 2.3f) * 0.015f * s.Scale;
                            s.Position.y = math.lerp(g.Height, floatAt, swim);
                        }
                    }
                }
                else
                {
                    // Floating on the surface, or swimming a little under it (shallower in shallow water); a leap
                    // arcs up out of the water and back.
                    var g = Terrain.SampleGround(s.Position.xz);
                    var surface = g.HasWater ? g.WaterSurface : g.Height;
                    // (A lava dweller basks at the surface, its back and head out: lava hides anything below it.)
                    var y = b.Habitat == Habitat.Floats ? surface + b.StandHeight * 0.35f * s.Scale
                          : b.LavaDweller ? surface + b.FrontRadius * s.Scale * 0.4f
                          : surface - math.clamp((surface - g.Height) * 0.45f, 0.25f, b.SwimDepth);
                    if (s.Air > 0f)
                    {
                        s.Air += dt / (0.6f + s.AirHeight * 0.35f);
                        if (s.Air >= 1f)
                            s.Air = 0f;
                        else
                            y = math.max(y, surface + math.sin(math.PI * s.Air) * s.AirHeight - (surface - y) * (1f - math.sin(math.PI * s.Air)));
                    }
                    s.Position.y = y;
                }
                CreatureMotion.AdvancePhase(ref s, ref b, dt);
            }

            /// <summary>
            /// Whether buildings' walls stop it: anything walking (not a ghost, nor a bird on the wing or a climber),
            /// except in the last couple of metres to where it's going (a work spot at a counter, a door).
            /// </summary>
            bool WalksRoundWalls(in CreatureState s, ref CreatureBlob b, bool keepHeight) =>
                HasFlora && !b.Haunts && !keepHeight && s.Mode != CreatureMode.Held &&
                (b.Habitat == Habitat.Land || b.Habitat == Habitat.Flies && s.Fly < 0.5f) &&
                math.distancesq(s.Position.xz, s.Target) > 4f;

            bool Walled(float2 at, float radius) => FloraPlacement.Blocked(ref Flora.Value, Terrain, at, radius);

            bool CaveWalkerNot(in CreatureState s, ref CreatureBlob b) => !CaveWalker(s, ref b);

            /// <summary>
            /// Crawls over the cave's rock in three dimensions: straight for its target (Target, TargetHeight), then
            /// back onto the surface it's on (its feet on the rock, its body off it), so it goes up walls and across the
            /// ceiling as readily as along the floor.
            /// </summary>
            void Crawl(ref CreatureState s, ref CreatureBlob b, float dt)
            {
                var target = new float3(s.Target.x, s.TargetHeight, s.Target.y);
                var way = target - s.Position;
                var dir = math.normalizesafe(way);
                var p = s.Position + dir * math.min(s.Speed * dt, math.length(way));
                // Onto the rock: the distance field is the way to it (its slope points into rock, its value how far).
                const float e = 0.1f;
                for (int k = 0; k < 2; k++)
                {
                    var d = Terrain.CaveDistance(p);
                    var grad = math.normalizesafe(new float3(
                        Terrain.CaveDistance(p + new float3(e, 0f, 0f)) - Terrain.CaveDistance(p - new float3(e, 0f, 0f)),
                        Terrain.CaveDistance(p + new float3(0f, e, 0f)) - Terrain.CaveDistance(p - new float3(0f, e, 0f)),
                        Terrain.CaveDistance(p + new float3(0f, 0f, e)) - Terrain.CaveDistance(p - new float3(0f, 0f, e))));
                    p -= grad * d;
                }
                s.Position = p;
                if (math.lengthsq(dir.xz) > 0.01f)
                    s.Yaw = math.atan2(dir.x, dir.z);
                CreatureMotion.AdvancePhase(ref s, ref b, dt);
            }

            /// <summary>
            /// A point on the cave's rock a few metres off in a random direction (up a wall, across the ceiling, along
            /// the floor): marched out from the body until it meets rock.
            /// </summary>
            bool CrawlTarget(in CreatureState s, ref CreatureBlob b, ref Random rng, out float3 spot)
            {
                spot = default;
                var from = s.Position + CreatureMotion.Surface(Terrain, s.Position, out _, out _) * 0.3f;
                var dir = rng.NextFloat3Direction();
                for (float t = 0.5f; t < 8f; t += 0.25f)
                {
                    var p = from + dir * t;
                    if (Terrain.CaveDistance(p) >= 0f)
                    {
                        spot = p - dir * 0.05f;
                        return t > 1f;
                    }
                }
                return false;
            }

            /// <summary>The steepest of a few places round a point (a rock face to bask on), if any is a real slope.</summary>
            bool SteepestOf(float2 near, float radius, ref Random rng, out float2 best)
            {
                best = near;
                var flattest = 0.85f;
                for (int k = 0; k < 5; k++)
                {
                    var p = near + rng.NextFloat2Direction() * radius * rng.NextFloat(0.2f, 0.8f);
                    if (!OpenGround(p))
                        continue;
                    var n = Terrain.SampleNormal(p, 0.5f);
                    if (n.y < flattest)
                    {
                        flattest = n.y;
                        best = p;
                    }
                }
                return flattest < 0.85f;
            }

            /// <summary>
            /// Moves as a cave animal does (on the cave floor, along its walls): cave dwellers always, and a surface animal
            /// that's sometimes found in caves (CaveAffinity) while it's under rock.
            /// </summary>
            bool CaveWalker(in CreatureState s, ref CreatureBlob b) => b.CaveDweller || underRock;

            /// <summary>This tick's creature is a cave visitor (CaveAffinity) under rock (see Execute).</summary>
            bool underRock;

            /// <summary>This tick's creature is a wild land animal near a player, so it keeps out of solids (A-44, see Think).</summary>
            bool nearSolids;

            /// <summary>Its body's half width (m, the radius it keeps from solids).</summary>
            static float BodyRadius(in CreatureState s, ref CreatureBlob b) => math.max(math.max(b.FrontRadius, b.RearRadius) * s.Scale, 0.1f);

            /// <summary>Whether its body would be in a solid at xz facing forward (CreatureMotion.Solid with its own size).</summary>
            bool SolidAt(in CreatureState s, ref CreatureBlob b, float2 xz, float2 forward, out float sink) =>
                SolidAt(s, ref b, xz, forward, out sink, out _);

            /// <summary>SolidAt, and what was in the way. A-44b: settlement buildings and landmark props too
            /// (FloraPlacement.Blocked, as workers and worms test them), which wild animals walked into before.</summary>
            bool SolidAt(in CreatureState s, ref CreatureBlob b, float2 xz, float2 forward, out float sink, out CreatureMotion.SolidKind kind)
            {
                if (CreatureMotion.StatsOn.Data != 0)
                    System.Threading.Interlocked.Increment(ref CreatureMotion.Stats.Data.Queries);
                var radius = BodyRadius(s, ref b);
                var halfLength = b.BodyLength * 0.5f * s.Scale;
                var hit = CreatureMotion.Solid(Terrain, xz, forward, s.Position.y, radius, halfLength,
                                               math.max((b.StandHeight + b.FrontRadius) * s.Scale, radius * 2f), out sink, out kind);
                if (!HasFlora)
                    return hit;
                float built;
                bool walled;
                using (BuiltMarker.Auto())
                    built = BuildingSink(xz, forward, radius, halfLength, out walled);
                if (walled && !hit)
                    kind = CreatureMotion.SolidKind.Building;
                if (built > sink)
                {
                    sink = built;
                    kind = CreatureMotion.SolidKind.Building;
                }
                return hit || walled;
            }

            /// <summary>
            /// How far its body (a circle of its radius at xz) is into a building or prop, in steps (Blocked only answers
            /// yes or no): 0 clear, 0.1 touching, 0.16 or more when over 0.15 m in, most of its radius when mostly in.
            /// Walled: that, or its nose touching one, stops a step.
            /// </summary>
            float BuildingSink(float2 xz, float2 forward, float radius, float halfLength, out bool walled)
            {
                walled = false;
                if (FloraPlacement.Blocked(ref Flora.Value, Terrain, xz, radius))
                {
                    walled = true;
                    if (!FloraPlacement.Blocked(ref Flora.Value, Terrain, xz, math.max(radius - 0.15f, 0.02f)))
                        return 0.1f;
                    return FloraPlacement.Blocked(ref Flora.Value, Terrain, xz, radius * 0.3f) ? math.max(radius * 0.8f, 0.3f)
                                                                                             : math.max(radius * 0.5f, 0.16f);
                }
                walled = FloraPlacement.Blocked(ref Flora.Value, Terrain, xz + forward * halfLength, radius * 0.6f);
                return 0f;
            }

            /// <summary>
            /// A-44: a step from where it stands to `to` that would put its body in a solid is turned to the nearest open way
            /// either side (turning it a little that way), or cancelled; already in one, it is pushed out sideways. Counts
            /// contacts for "-solidstats".
            /// </summary>
            float2 KeepOutOfSolids(ref CreatureState s, ref CreatureBlob b, float2 to, float2 forward, float dt)
            {
                var stats = CreatureMotion.StatsOn.Data != 0;
                // Already in one (spawned there, or a wall that turned up under it): out sideways, to the nearest clear side.
                if (SolidAt(s, ref b, s.Position.xz, forward, out var sinkNow) && sinkNow > 0.05f)
                {
                    // (A-46: most often it only turned its nose into the wall this tick, a bear's 1.3 m ahead of it: first
                    // it turns back away, on the spot, to the nearest facing that's clear.)
                    for (int k = 1; k <= 7; k++)
                        for (int side = -1; side <= 1; side += 2)
                        {
                            var angle = s.Yaw + side * k * 0.45f;
                            var face = new float2(math.sin(angle), math.cos(angle));
                            if (!SolidAt(s, ref b, s.Position.xz, face, out var sinkFacing) || sinkFacing <= 0.05f)
                            {
                                s.Yaw = WrapAngle(angle);
                                s.Speed = math.min(s.Speed, 0.3f * s.Scale);
                                return s.Position.xz;
                            }
                        }
                    var push = math.min(sinkNow + 0.05f, 2f * dt + 0.05f);
                    for (int k = 0; k < 8; k++)
                    {
                        var angle = s.Yaw + math.PI * 0.5f + k * math.PI * 0.25f;
                        var dir = new float2(math.sin(angle), math.cos(angle));
                        if (!SolidAt(s, ref b, s.Position.xz + dir * push, forward, out var sinkThere) || sinkThere < sinkNow - 0.01f)
                            return s.Position.xz + dir * push;
                    }
                    return s.Position.xz;
                }
                if (math.distancesq(to, s.Position.xz) < 1e-8f || !SolidAt(s, ref b, to, forward, out _))
                    return to;
                if (stats)
                    System.Threading.Interlocked.Increment(ref CreatureMotion.Stats.Data.Contacts);
                var step = math.distance(to, s.Position.xz);
                for (int k = 1; k <= 7; k++)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var angle = s.Yaw + side * k * 0.45f;
                        var way = new float2(math.sin(angle), math.cos(angle));
                        var slide = s.Position.xz + way * step;
                        // (A-45: tested facing the way it will face too, only part turned towards the slide: a bear's nose
                        // is 1.3 m ahead, and swung back towards the wall it went 0.30 m into the rock.)
                        var turned = WrapAngle(s.Yaw + WrapAngle(angle - s.Yaw) * 0.3f);
                        if (!SolidAt(s, ref b, slide, way, out _))
                        {
                            // (A-46: if the part turn would swing its nose in, it turns the whole way to the slide, which is
                            // tested clear, rather than stopping: a dormouse in a narrow gap stuck 27 ticks on that test.)
                            s.Yaw = SolidAt(s, ref b, slide, new float2(math.sin(turned), math.cos(turned)), out _) ? WrapAngle(angle) : turned;
                            return slide;
                        }
                    }
                s.Speed = 0f;
                if (stats)
                    System.Threading.Interlocked.Increment(ref CreatureMotion.Stats.Data.Stuck);
                return s.Position.xz;
            }

            /// <summary>"-solidstats": how deep this tick's creature near a player is in a solid, and whether it floats over a
            /// cave entrance's carved ground.</summary>
            void CountSolids(in CreatureState s, ref CreatureBlob b)
            {
                if (b.Upright || b.Haunts || b.Tunnels || b.Habitat != Habitat.Land && b.Habitat != Habitat.Flies || s.Fly > 0.5f ||
                    s.Mode == CreatureMode.Held || b.CaveDweller || underRock)
                    return;
                var near = false;
                for (int p = 0; p < Players.Length && !near; p++)
                    near = math.distancesq(Players[p].xz, s.Position.xz) < CreatureMotion.SolidReach * CreatureMotion.SolidReach;
                if (!near)
                    return;
                ref var stats = ref CreatureMotion.Stats.Data;
                System.Threading.Interlocked.Increment(ref stats.Samples);
                var forward = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
                SolidAt(s, ref b, s.Position.xz, forward, out var sink, out var kind);
                System.Threading.Interlocked.Decrement(ref stats.Queries); // (a count, not a move: Queries are the moves' cost)
                if (sink > 0.15f)
                {
                    System.Threading.Interlocked.Increment(ref stats.Hidden);
                    CreatureMotion.Cases.Data.KeepSink(new float4(s.Position.xz, sink, (float)kind));
                }
                var mm = (int)(sink * 1000f);
                for (int was = stats.MaxSinkMm; mm > was; was = stats.MaxSinkMm)
                    if (System.Threading.Interlocked.CompareExchange(ref stats.MaxSinkMm, mm, was) == was)
                        break;
                // (Standing over an entrance: well above the floor there. Up a tree or on a perch it is meant to be.)
                var gap = s.Position.y - CreatureMotion.Ground(Terrain, s.Position.xz, s.Position.y);
                if (s.Position.y < Terrain.SampleHeight(s.Position.xz) + 0.3f && gap > 0.2f)
                {
                    System.Threading.Interlocked.Increment(ref stats.Floating);
                    CreatureMotion.Cases.Data.KeepFloat(new float4(s.Position.xz, gap, (float)s.Mode + (s.Speed > 0.01f ? 100f : 0f)));
                }
            }

            bool UnderRock(in CreatureState s, ref CreatureBlob b)
            {
                if (b.CaveDweller || b.CaveAffinity <= 0f || !Terrain.Caves.IsCreated)
                    return false;
                // (Up on the surface, as it nearly always is: one sample, and no march down the cave's field.)
                if (s.Position.y >= Terrain.SampleHeight(s.Position.xz) - 1f)
                    return false;
                Terrain.FloorUnder(s.Position + new float3(0f, 0.5f, 0f), out var inCave);
                return inCave;
            }

            /// <summary>
            /// Under the rock, for any creature: underRock for a cave visitor; for a cave dweller (bat folk, pygmies;
            /// underRock is never worked out for them) whether it's below the ground here, at home rather than up on a trip.
            /// </summary>
            bool Below(in CreatureState s, ref CreatureBlob b) =>
                underRock || b.CaveDweller && s.Position.y < Terrain.SampleHeight(s.Position.xz) - 1f;

            /// <summary>
            /// Land on the far side of water within 60 m, in one of a few directions tried: the way there starts on
            /// land, crosses water it can swim, and ends on dry land.
            /// </summary>
            bool FarShore(float2 from, ref Random rng, out float2 shore)
            {
                shore = default;
                var start = rng.NextFloat(math.PI * 2f);
                for (int k = 0; k < 6; k++)
                {
                    var a = start + k * math.PI / 3f;
                    var dir = new float2(math.sin(a), math.cos(a));
                    var wet = false;
                    for (var d = 4f; d <= 60f; d += 4f)
                    {
                        var p = from + dir * d;
                        if (Swimmable(p))
                            wet = true;
                        else if (Walkable(p, Habitat.Land))
                        {
                            if (!wet)
                                continue;
                            shore = p + dir * 3f;
                            return Walkable(shore, Habitat.Land);
                        }
                        else
                            break;   // a cave hole, lava, too shallow to count: not this way
                    }
                }
                return false;
            }

            /// <summary>How far a land animal that swims has gone from wading (0) to swimming (1) at this depth.</summary>
            static float SwimDepth(ref CreatureBlob b, float scale, float depth) =>
                math.smoothstep(b.StandHeight * scale * 0.6f, b.StandHeight * scale * 1.1f, depth);

            /// <summary>The nearest dry land within 80 m, looking out in 12 directions.</summary>
            bool NearestShore(float2 from, out float2 shore)
            {
                shore = default;
                var best = float.MaxValue;
                for (int k = 0; k < 12; k++)
                {
                    var a = k * math.PI / 6f;
                    var dir = new float2(math.sin(a), math.cos(a));
                    for (var d = 3f; d <= 80f && d < best; d += 3f)
                        if (Walkable(from + dir * d, Habitat.Land))
                        {
                            best = d;
                            shore = from + dir * (d + 2f);
                            break;
                        }
                }
                return best < float.MaxValue;
            }

            /// <summary>Whether a step from one spot to the next is open ground for it: walkable, and (for a land-bound
            /// animal) not too steep. The running veers' look either side (Z-07).</summary>
            bool Open(float2 from, float2 to, Habitat habitat, bool landBound) =>
                Walkable(to, habitat) && !(landBound && SlopeAhead(from, to) > 0.8f);

            /// <summary>Lava deep enough to swim in (a volcano's crater lake, a lava biome's pools).</summary>
            bool InLava(float2 xz)
            {
                var g = Terrain.SampleGround(xz);
                return g.HasWater && g.WaterSurface - g.Height > 0.5f &&
                       Terrain.LavaAt(new float3(xz.x, g.WaterSurface - 0.2f, xz.y)) > 0.5f;
            }

            /// <summary>How deep the water is here (0 on dry land).</summary>
            float WaterDepth(float2 xz)
            {
                var g = Terrain.SampleGround(xz);
                return g.HasWater ? math.max(0f, g.WaterSurface - g.Height) : 0f;
            }

            /// <summary>Water a land animal that swims can cross: open water (not a cave's hole, not lava).</summary>
            bool Swimmable(float2 xz)
            {
                var g = Terrain.SampleGround(xz);
                return g.HasWater && g.WaterSurface > g.Height && !Terrain.IsCaveHole(xz) &&
                       Terrain.LavaAt(new float3(xz.x, g.WaterSurface - 0.2f, xz.y)) < 0.3f;
            }

            /// <summary>Whether an animal of this habitat can go here: dry land, or water deep enough to swim or float in.</summary>
            /// <summary>Somewhere to go on foot or to land: walkable ground that isn't inside a building's walls.</summary>
            bool OpenGround(float2 xz) => Walkable(xz, Habitat.Land) && !(HasFlora && Walled(xz, 0.3f));

            bool Walkable(float2 xz, Habitat habitat)
            {
                var g = Terrain.SampleGround(xz);
                var depth = g.HasWater ? g.WaterSurface - g.Height : -1f;
                if (habitat == Habitat.Swims)
                    return depth >= 0.7f;
                if (habitat == Habitat.Floats)
                    return depth >= 0.3f;
                return depth <= 0.15f && !Terrain.IsCaveHole(xz);
            }

            float SlopeAhead(float2 from, float2 to)
            {
                var rise = Terrain.SampleHeight(to) - Terrain.SampleHeight(from);
                return rise / math.max(math.distance(from, to), 0.1f);
            }

            static float WrapAngle(float a) => a - 2f * math.PI * math.floor((a + math.PI) / (2f * math.PI));
        }
    }
}
