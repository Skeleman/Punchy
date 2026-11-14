using System.Collections.Generic;
using Chem;
using Chem.Lockstep;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

namespace Punchy.Chemistry
{
    /// <summary>
    /// The cells-only spells (README "Spells"): every spell whose effect is only on cells, cast from the pad's radial (groups
    /// as its top ring, each group's spells out round it: the core's SpellRadial) or the M key, and drawn from what the
    /// cells are: the shaders' third and fourth maps (lava, mud, a slick, rock lava set into; new growth, thorns, sand,
    /// charge) and ChemistryEffects' Punchy effects (ChemistryEffects.Spells.cs). A landed cast's burst is drawn from its
    /// cells, which every peer runs on the same tick. Also the test script's spell steps (all, big, measure) and the counts
    /// of what the new reactions did. -chem-plain leaves all of this off (the look before it, for A/B frame times).
    /// </summary>
    public partial class ChemistryBridge
    {
        /// <summary>The spells the bridge casts, by group (the radial's order): the six it began with and the 22 cells-only ones.</summary>
        public static readonly (string group, string[] spells)[] SpellGroups =
        {
            ("fire", new[] { "fire-bolt", "burning-hands", "fireball", "flame-wave", "meteor", "falling-star", "scalding-steam", "lava-pool" }),
            ("water", new[] { "create-water", "ray-of-frost", "frost", "sleet-storm", "wave", "flood", "geyser", "iceberg" }),
            ("storm", new[] { "shocking-grasp", "lightning-bolt", "thunder-strike", "rain-cloud", "dust-storm", "sand-blast" }),
            ("earth", new[] { "mud" }),
            ("nature", new[] { "acid-splash", "plant-growth", "spike-growth" }),
            ("arcane", new[] { "grease" }),
            ("time", new[] { "fog-cloud" }),
        };

        /// <summary>Every spell above, in order.</summary>
        public static readonly string[] Spells = AllSpells();

        static string[] AllSpells()
        {
            var all = new List<string>();
            foreach (var (_, spells) in SpellGroups)
                all.AddRange(spells);
            return all.ToArray();
        }

        // The element a group's hand attunes to (the radial with a trigger held), where it has one.
        static string ElementOf(string group) => group switch
        {
            "fire" => "fire",
            "water" => "water",
            "earth" => "earth",
            "storm" => "air",
            _ => null,
        };

        static readonly bool Plain = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-chem-plain") >= 0;

        SpellRadial spellRadial;
        int keySpell;
        float3 lastPlayer;

        // Thunder Strike's columns to come: the tick, the cell (row) and the radius.
        readonly List<(int tick, int x, int row, int radius)> columns = new List<(int, int, int, int)>();
        int shedSeen;
        // What the new reactions did since the view was set up (README "Spells"): lava set into rock in water, lava cooled
        // to rock, slicks and thorns that caught fire, fires a rain cloud put out, water that lightning ran through, growth
        // that came up, slicks eaten away (acid).
        int quenched, cooled, slicksLit, thornsLit, rainedOut, shocked, grown, slicksGone;
        int lavaCount, crustCount, mudCount, slickCount, growthCount, thornsCount, sandCount, chargedCount, fogCount, dustCount, cloudCount;

        public (int quenched, int cooled, int slicksLit, int thornsLit, int rainedOut, int shocked, int grown, int slicksGone) SpellReactions =>
            (quenched, cooled, slicksLit, thornsLit, rainedOut, shocked, grown, slicksGone);
        public string SpellCounts => $"{lavaCount} lava, {crustCount} crust, {mudCount} mud, {slickCount} slick, {growthCount} growth, {thornsCount} thorns, " +
                                     $"{sandCount} sand, {chargedCount} charged, {fogCount} fog, {dustCount} dust, {cloudCount} cloud";

        // ------------------------------------------------------------------------------------------------ the radial

        SpellRadial Radial()
        {
            if (spellRadial != null)
                return spellRadial;
            var groups = new List<RadialGroup>();
            foreach (var (group, spells) in SpellGroups)
            {
                var def = rules.Group(group);
                var ids = new List<string>();
                foreach (var id in spells)
                    if (rules.Spell(id) != null)
                        ids.Add(id);
                if (ids.Count > 0)
                    groups.Add(new RadialGroup(group, def?.Name ?? group, def?.Color ?? Chem.Rules.DefaultGroupColor, ids));
            }
            return spellRadial = new SpellRadial(new RadialLayout(groups));
        }

        /// <summary>The radial chose a spell: with a trigger held, that hand attunes to its group's element; else it's cast now.</summary>
        void PickedFromRadial(string spell)
        {
            Radial().Chose(spell);
            bool right = thrustRight.IsPressed(), left = thrustLeft.IsPressed();
            if (right || left)
            {
                var g = Radial().Layout.GroupOfSpell(spell);
                var element = g >= 0 ? ElementOf(Radial().Layout.Groups[g].Id) : null;
                if (element != null)
                    AttuneTo(right && !left ? HandsDef.Right : HandsDef.Left, element);
                else
                    Debug.Log($"[Chemistry] radial: the {Radial().Layout.Groups[g].Name} group has no element to attune");
                return;
            }
            CastAtAim(spell, 1, "the radial");
        }

        // ------------------------------------------------------------------------------------------------ keys

        /// <summary>M casts the key spell at the screen centre (Shift+M charged to 3); comma and period step through the list.</summary>
        void SpellKeys(TerrainSource terrain)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || GUIUtility.keyboardControl != 0)
                return;
            if (keyboard.commaKey.wasPressedThisFrame || keyboard.periodKey.wasPressedThisFrame)
            {
                keySpell = (keySpell + (keyboard.periodKey.wasPressedThisFrame ? 1 : Spells.Length - 1)) % Spells.Length;
                Debug.Log($"[Chemistry] key spell: {rules.Spell(Spells[keySpell])?.Name ?? Spells[keySpell]} (M casts it)");
                keyShownAt = Time.unscaledTime;
            }
            if (keyboard.mKey.wasPressedThisFrame)
                CastAtAim(Spells[keySpell], keyboard.shiftKey.isPressed ? 3 : 1, "M");
        }

        float keyShownAt = float.NegativeInfinity;

        /// <summary>The key spell's name while it was just stepped to (for the HUD), else null.</summary>
        string KeySpellShown => Time.unscaledTime - keyShownAt < 2f ? rules.Spell(Spells[keySpell])?.Name : null;

        // ------------------------------------------------------------------------------------------------ casting

        /// <summary>Casts a spell at the ground under the screen centre from the player, as a hand would (its gesture's range).</summary>
        void CastAtAim(string spell, int charge, string how)
        {
            var s = rules.Spell(spell);
            if (s == null || !EnsureQueries() || !terrainQuery.TryGetSingleton<TerrainSource>(out var terrain))
                return;
            var from = TestCaster.HasValue ? (float3)TestCaster.Value : lastPlayer;
            var cam = Camera.main;
            float3 hit;
            if (TestAim.HasValue)
                hit = TestAim.Value;
            else if (cam == null || !Aim(terrain, cam.transform.position, cam.transform.forward, out hit))
            {
                var ahead = cam != null ? (float3)cam.transform.forward : new float3(0, 0, 1);
                hit = from + math.normalizesafe(new float3(ahead.x, 0, ahead.z), new float3(0, 0, 1)) * 6f;
            }
            var range = rules.Sim.Hold.Hands.RangeOf(s.Origin == "self" ? HandsDef.Thrust : HandsDef.Throw);
            CastAimed(spell, charge, range, from, hit, how);
        }

        /// <summary>
        /// As SpellCaster.Aim in Spell Chemistry Valley: a line or a cone points at the aim (off the grid's four ways if it
        /// is) and starts beside the caster; a burst from the caster is on the caster; anything else lands on the target,
        /// pulled back to range. Asked of the session; false if it couldn't be (nothing filled there).
        /// </summary>
        bool CastAimed(string spell, int charge, int range, float3 from, float3 hit, string how)
        {
            var s = rules.Spell(spell);
            if (s == null)
                return false;
            int mx = (int)math.floor(from.x), my = (int)math.floor(from.z);
            int hx = (int)math.floor(hit.x), hy = (int)math.floor(hit.z);
            var (tx, ty) = HandsDef.Within(mx, my, hx, hy, range);
            int aimX = 0, aimY = 0;
            if (s.Shape == "line" || s.Shape == "cone")
            {
                var d = hit - from;
                (aimX, aimY) = World.SnapAim((int)math.round(d.x * AimSteps), (int)math.round(d.z * AimSteps));
            }
            int? dir = aimX != 0 || aimY != 0 ? World.DirOf(aimX, aimY) : DirFrom(mx, my, tx, ty);
            int x = tx, y = ty;
            if (s.Origin == "self")
            {
                if (s.Shape == "burst")
                    (x, y) = (mx, my);
                else
                {
                    var (dx, dy) = Step(dir ?? 2);
                    (x, y) = (mx + dx, my + dy);
                }
            }
            if (!Filled(x, y, how + ": " + s.Name))
                return false;
            session.Request(casts, new CastCommand(spell, x, ChunkKey.Row(viewLayer, y), dir ?? 2, charge, aimX, aimY, Me));
            (lastCastX, lastCastRow) = (x, ChunkKey.Row(viewLayer, y));
            Debug.Log($"[Chemistry] {how}: cast {s.Name} (charge {charge}, range {range}) at cell ({x}, {y}) in layer {viewLayer}, " +
                      $"ground {GroundName(x, y)}, aimed ({hx}, {hy}): asked for (tick {session.NetTick})");
            return true;
        }

        /// <summary>A spell cast at a world point from a point (the script's), charged.</summary>
        public bool CastFrom(string spell, Vector3 from, Vector3 at, int charge = 1)
        {
            var s = rules?.Spell(spell);
            if (!Ready || s == null)
                return false;
            var range = s.Origin == "self" ? 40 : 200;
            return CastAimed(spell, charge, range, from, at, "script");
        }

        // The cell (and row) of the last cast asked for here.
        int lastCastX, lastCastRow;

        // ------------------------------------------------------------------------------------------------ the script's spells

        // Casts queued by the script: when, the spell, where from and to, the charge.
        readonly List<(float at, string spell, float3 from, float3 to, int charge)> queued = new List<(float, string, float3, float3, int)>();

        void SpellQueue(float3 player)
        {
            lastPlayer = player;
            UpdateMeasure();
            if (queued.Count == 0 || Time.unscaledTime < queued[0].at)
                return;
            var q = queued[0];
            queued.RemoveAt(0);
            // A cast from the player follows the player (a walk), keeping its aim's offset.
            var from = q.from.y < -9000f ? player : q.from;
            var to = q.from.y < -9000f ? player + q.to : q.to;
            if (!CastFrom(q.spell, from, to, q.charge))
                Debug.Log($"[Chemistry] script: {q.spell} not cast");
        }

        /// <summary>The script's "all": every spell in turn, 1.5 s apart, round the player (the water ones on the nearest water).</summary>
        bool QueueAll(float3 player)
        {
            if (!Ready)
                return false;
            var water = Nearest(player, KindWater);
            var t = Time.unscaledTime + 0.5f;
            for (int k = 0; k < Spells.Length; k++)
            {
                var spell = Spells[k];
                var a = k * 2.39996f;
                var r = 9f + 5f * ((k * 7) % 5) / 4f;
                var to = new float3(math.cos(a) * r, 0f, math.sin(a) * r);
                if ((spell == "geyser" || spell == "iceberg") && water >= 0)
                {
                    var w = CellCentre(water % Size, water / Size);
                    queued.Add((t, spell, new float3(w.x, w.y, w.z) - new float3(0f, 0f, 6f), (float3)w, 1));
                }
                else
                    queued.Add((t, spell, new float3(0f, -10000f, 0f), to, 1));
                t += 1.5f;
            }
            return true;
        }

        /// <summary>
        /// The script's "big": the brief's big scene 20 m north of the player: grease over a 16 x 16 m meadow, flooded with
        /// water, a fog over it, then a Meteor into the middle (a little over 12 s in all).
        /// </summary>
        bool QueueBig(float3 player)
        {
            if (!Ready)
                return false;
            var centre = player + new float3(0f, 0f, 20f);
            var off = new float3(0f, -10000f, 0f);
            var t = Time.unscaledTime + 0.3f;
            for (int gy = 0; gy < 4; gy++)
                for (int gx = 0; gx < 4; gx++, t += 0.15f)
                    queued.Add((t, "grease", off, centre - player + new float3(-6f + gx * 4f, 0f, -6f + gy * 4f), 3));
            for (int gy = 0; gy < 3; gy++)
                for (int gx = 0; gx < 3; gx++, t += 0.2f)
                    queued.Add((t, "create-water", off, centre - player + new float3(-5f + gx * 5f, 0f, -5f + gy * 5f), 3));
            queued.Add((t + 0.5f, "fog-cloud", off, centre - player, 3));
            queued.Add((t + 6f, "meteor", off, centre - player, 1));
            return true;
        }

        /// <summary>The script's "face": the camera turned toward a heading (dx, dz), looking a little down.</summary>
        static void Face(float2 heading)
        {
            var cam = Object.FindAnyObjectByType<Punchy.CameraRig.ThirdPersonCamera>();
            if (cam != null)
                cam.SetView(math.degrees(math.atan2(heading.x, heading.y)), 22f);
        }

        // "measure s": the frame times over the next s seconds.
        readonly List<float> measured = new List<float>();
        float measureUntil = -1f;
        double measureBridgeMs;
        int measureFrames;

        void StartMeasure(float seconds)
        {
            measured.Clear();
            measureBridgeMs = 0;
            measureFrames = 0;
            measureUntil = Time.unscaledTime + seconds;
        }

        void UpdateMeasure()
        {
            if (measureUntil < 0f)
                return;
            measured.Add(Time.unscaledDeltaTime * 1000f);
            if (Time.unscaledTime < measureUntil)
                return;
            measureUntil = -1f;
            measured.Sort();
            float sum = 0f;
            foreach (var m in measured)
                sum += m;
            int n = math.max(1, measured.Count);
            Debug.Log($"[Chemistry] measure: {measured.Count} frames, frame {sum / n:F2} ms on average, p50 {measured[n / 2]:F2}, p95 {measured[(int)(n * 0.95f)]:F2}, " +
                      $"worst {measured[n - 1]:F2} ms; bridge {measureBridgeMs / math.max(1, measureFrames):F2} ms a frame; {(Plain ? "plain" : "spells' effects on")}; " +
                      $"{SpellCounts}; {effects.PuffCount} puffs, {effects.SpellFxCount} plumes/bolts/flames");
        }

        void MeasureBridge(double ms)
        {
            if (measureUntil < 0f)
                return;
            measureBridgeMs += ms;
            measureFrames++;
        }

        // ------------------------------------------------------------------------------------------------ the maps

        // Window cells already rock that lava set into (so a quench counts once).
        readonly bool[] crustSeen = new bool[Size * Size];

        void NewSpellsView()
        {
            System.Array.Clear(crustSeen, 0, crustSeen.Length);
            effects.ClearSpells();
            columns.Clear();
            shedSeen = world.Tick;
            quenched = cooled = slicksLit = thornsLit = rainedOut = shocked = grown = slicksGone = 0;
        }

        void StartSpellsMap()
        {
            lavaCount = crustCount = mudCount = slickCount = growthCount = thornsCount = sandCount = chargedCount = fogCount = dustCount = cloudCount = 0;
            effects.Lava.Clear();
            effects.Fog.Clear();
            effects.Dust.Clear();
            effects.Cloud.Clear();
            effects.Charged.Clear();
            effects.Stars.Clear();
        }

        // What grass became that isn't burnt: new growth, mud, sand, lava and the rock it leaves.
        static bool NotBurnt(Cell c, byte seed)
        {
            var t = c.T;
            return t == "sprouts" || t == "sapling" || t == "mud" || t == "sand" || t == "glass" || t == "lava-pool" || t == "lava" || t == "lava-crust" || t == "rock";
        }

        /// <summary>A cell's spell looks into maps 3 and 4, and onto the effects' lists; embers raise the burning glow a little.</summary>
        void SpellsCell(Cell c, int i, bool steam, ref byte r)
        {
            if (Plain)
            {
                texels3[i] = default;
                texels4[i] = default;
                return;
            }
            var t = c.T;
            var cover = c.Cover;
            byte lr = 0, mg = 0, sb = 0, ca = 0, gr = 0, th = 0, sa = 0, ch = 0;
            bool lava = t == "lava-pool" || t == "lava";
            if (lava)
            {
                lr = t == "lava-pool" ? (byte)255 : (byte)190;
                lavaCount++;
                effects.Lava.Add(new ChemistryEffects.Spot { Index = i, Amount = 1 });
            }
            bool crust = t == "lava-crust" || (t == "rock" && seeded[i] != KindRock);
            if (crust)
            {
                ca = 255;
                crustCount++;
            }
            if (t == "mud")
            {
                mg = 255;
                mudCount++;
            }
            if (cover == "grease" || cover == "oil")
            {
                sb = 255;
                slickCount++;
            }
            if (t == "sprouts" || t == "sapling" || cover == "vines")
            {
                gr = 255;
                growthCount++;
            }
            if (cover == "thorns")
            {
                th = 255;
                thornsCount++;
            }
            if (t == "sand" || t == "glass")
            {
                sa = 255;
                sandCount++;
            }
            if (c.Charge > 0)
            {
                ch = (byte)math.min(255, 85 * c.Charge);
                chargedCount++;
                effects.Charged.Add(new ChemistryEffects.Spot { Index = i, Amount = c.Charge });
            }
            if (cover == "embers" && c.Fire <= 0)
                r = (byte)math.max(r, 90);
            if (c.Gas != null && c.GasAmt > 0)
            {
                var spot = new ChemistryEffects.Spot { Index = i, Amount = c.GasAmt };
                switch (c.Gas)
                {
                    case "fog":
                        fogCount++;
                        effects.Fog.Add(spot);
                        break;
                    case "dust":
                        dustCount++;
                        effects.Dust.Add(spot);
                        break;
                    case "cloud":
                    case "storm":
                    case "snowcloud":
                        cloudCount++;
                        effects.Cloud.Add(spot);
                        break;
                }
            }
            if (c.Star > 0)
                effects.Stars.Add(new ChemistryEffects.Spot { Index = i, Amount = c.Star });
            texels3[i] = new Color32(lr, mg, sb, ca);
            texels4[i] = new Color32(gr, th, sa, ch);

            // What the new reactions did, as the map sees the cells change (lastState's high bits, SpellsState).
            var was = lastState[i];
            bool wasBurning = (was & 2) != 0, wasLava = (was & 8) != 0, wasSlick = (was & 16) != 0, wasGrowth = (was & 32) != 0,
                 wasCharged = (was & 64) != 0, wasThorns = (was & 128) != 0;
            // (Lava that lands in water sets at once, so it may never be seen as lava: rock that's new here, with water or steam
            // on it, is lava quenched too.)
            if ((wasLava && !lava) || (crust && !crustSeen[i] && shownTick >= 0 && (c.Water > 0 || steam)))
            {
                if (c.Water > 0 || steam || seeded[i] == KindWater)
                {
                    quenched++;   // lava-water: sets into rock at once, in a hiss of steam
                    effects.Quenched.Add(new ChemistryEffects.Spot { Index = i, Amount = 1 });
                }
                else
                    cooled++;
            }
            crustSeen[i] = crust;
            if (sb != 0 && c.Fire > 0 && !wasBurning)
                slicksLit++;   // fire-oil, lightning-oil
            if (wasThorns && c.Fire > 0 && !wasBurning)
                thornsLit++;   // fire-thorns
            if (wasSlick && sb == 0 && c.Fire <= 0 && !wasBurning)
                slicksGone++;   // acid-cover
            if (wasBurning && c.Fire <= 0 && (c.Gas == "cloud" || c.Gas == "storm"))
                rainedOut++;   // the rain cloud's water-fire
            if (ch != 0 && !wasCharged && (c.Water > 0 || seeded[i] == KindWater))
                shocked++;   // lightning-conduct through water
            if (gr != 0 && !wasGrowth && shownTick >= 0)
            {
                grown++;
                effects.Grown.Add(new ChemistryEffects.Spot { Index = i, Amount = 1 });
            }
        }

        byte SpellsState(Cell c, int i) => Plain ? (byte)0 : (byte)((texels3[i].r != 0 ? 8 : 0) | (texels3[i].b != 0 ? 16 : 0) | (texels4[i].r != 0 ? 32 : 0)
                                                                    | (texels4[i].a != 0 ? 64 : 0) | (texels4[i].g != 0 ? 128 : 0));

        void FinishSpellsMap()
        {
            if (Plain)
                return;
            effects.SpellsMapped(corner, Size, heights);
            // Thunder Strike's columns that are due, onto the cells it charged.
            for (int k = columns.Count - 1; k >= 0; k--)
            {
                var col = columns[k];
                if (world.Tick < col.tick)
                    continue;
                columns.RemoveAt(k);
                var (layer, y) = ChunkKey.OfRow(col.row);
                if (layer != viewLayer || !InWindow(col.x, y, out var centre))
                    continue;
                var struck = new List<Vector3>();
                for (int dy = -col.radius; dy <= col.radius; dy++)
                    for (int dx = -col.radius; dx <= col.radius; dx++)
                        if (InWindow(col.x + dx, y + dy, out var w) && world.At(col.x + dx, ChunkKey.Row(viewLayer, y + dy))?.Charge > 0
                            && (Hash32(col.x + dx, y + dy) & 3) == 0)
                            struck.Add(CellCentre(w % Size, w / Size));
                effects.StrikeColumn(CellCentre(centre % Size, centre / Size), struck);
            }
            // A storm cloud's strikes since the last map.
            foreach (var (sx, srow, tick) in world.ShedStrikesSince(shedSeen))
            {
                shedSeen = math.max(shedSeen, tick);
                var (layer, sy) = ChunkKey.OfRow(srow);
                if (layer == viewLayer && InWindow(sx, sy, out var w))
                    effects.CloudStrike(CellCentre(w % Size, w / Size));
            }
        }

        bool InWindow(int x, int y, out int index)
        {
            int ix = x - corner.x, iy = y - corner.y;
            index = iy * Size + ix;
            return ix >= 0 && iy >= 0 && ix < Size && iy < Size;
        }

        static uint Hash32(int x, int y)
        {
            uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u;
            h ^= h >> 13;
            h *= 0x85ebca6bu;
            h ^= h >> 16;
            return h;
        }

        /// <summary>A cast landed (every peer, as its tick ran): its burst from its cells, in the view's layer and window.</summary>
        void SpellLanded(CastCommand c)
        {
            if (Plain || !Ready)
                return;
            var s = rules.Spell(c.Spell);
            var (layer, y) = ChunkKey.OfRow(c.Y);
            if (s == null || layer != viewLayer)
                return;
            if (s.Delay > 0)
                effects.SetDelay(s.Delay, rules.Sim.TicksPerSecond);
            if (c.Spell == "thunder-strike")
                columns.Add((world.Tick + s.Delay, c.X, c.Y, s.Radius + World.ClampCharge(c.Charge) - 1));
            var cells = new List<int>();
            foreach (var (cx, crow) in world.SpellCells(s, c.X, c.Y, c.Dir, c.Charge, c.AimX, c.AimY))
            {
                var (cl, cy) = ChunkKey.OfRow(crow);
                if (cl == viewLayer && InWindow(cx, cy, out var w))
                    cells.Add(w);
            }
            if (cells.Count == 0)
            {
                if (!InWindow(c.X, y, out var at))
                    return;
                cells.Add(at);
            }
            // The hands: the caster's own (this peer's wizard), else a little behind and above the first cell.
            Vector3 hand;
            if (c.By == Me)
                hand = (Vector3)lastPlayer + Vector3.up * 1.3f;
            else
            {
                var (dx, dy) = Step(c.Dir);
                hand = CellCentre(cells[0] % Size, cells[0] / Size) - new Vector3(dx, -1.3f, dy);
            }
            // Lightning jumps to the nearest charged cell (water, metal) near its way, if there is one.
            Vector3? strikeTo = null;
            if (c.Spell == "lightning-bolt")
            {
                int best = int.MaxValue;
                for (int dy = -12; dy <= 12; dy++)
                    for (int dx = -12; dx <= 12; dx++)
                    {
                        int d = dx * dx + dy * dy;
                        if (d >= best || !InWindow(c.X + dx, y + dy, out var w) || !(world.At(c.X + dx, ChunkKey.Row(viewLayer, y + dy))?.Charge > 0))
                            continue;
                        best = d;
                        strikeTo = CellCentre(w % Size, w / Size) + Vector3.up * 0.2f;
                    }
            }
            effects.SpellCast(c.Spell, cells, corner, Size, heights, World.ClampCharge(c.Charge), hand, strikeTo);
        }

        void SpellsReport()
        {
            if (Plain || lavaCount + crustCount + mudCount + slickCount + growthCount + thornsCount + sandCount + chargedCount + fogCount + dustCount + cloudCount == 0
                && quenched + slicksLit + thornsLit + rainedOut + shocked + grown + slicksGone == 0)
                return;
            Debug.Log($"[Chemistry] spells: {SpellCounts}; so far {quenched} lava quenched to rock, {cooled} lava cooled, {slicksLit} slicks lit, " +
                      $"{thornsLit} thorns lit, {rainedOut} fires rained out, {shocked} water cells shocked, {grown} grown, {slicksGone} slicks eaten; " +
                      $"{effects.SpellFxCount} plumes/bolts/flames");
        }
    }
}
