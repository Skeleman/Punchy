using System.Collections.Generic;
using Punchy.Flora;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-81 row 1: the RockGolem colossus's builder, moved out of StoneColossi.cs as it was. Its ColossusDefinition
    // asset (Resources/Colossi) says where it stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsRockGolem = Register(Kind.RockGolem, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildGolem(at, yaw, seed));

        // A-77 (item 24, Zach): near-black, like the floating rocks of the magnetic monolith it walks among.
        static readonly Color GolemBasalt = new Color(0.10f, 0.09f, 0.10f);
        static readonly Color BasaltPale = new Color(0.17f, 0.15f, 0.16f);
        static readonly Color Scorch = new Color(0.12f, 0.11f, 0.12f);

        /// <summary>
        /// W-89 (#40, Zach's ElectricWastelands design): a golem about 55 m tall built from the area's floating rocks, caught
        /// mid-stride. Every body part is its own jagged rock (the floating rocks' dark basalt with violet and blue shards),
        /// hanging a couple of metres from the next, and each one bobs and turns on its own; the floating rocks' arcs
        /// (ArcBolt, as between the arcing spires) crackle across every gap and hold it together (<see cref="GolemArcs"/>).
        /// Seeded per world: its size, the step, how far each arm swings and the tilt of the head. Details: shards orbiting
        /// the chest with arcs to some of them, crystal eyes, a scorched mound under each foot, and a rock it has dropped
        /// lying in the dirt with one weak arc still reaching up to its hand.
        /// </summary>
        static Transform BuildGolem(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("RockGolem").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 0.8f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed);
            var scale = rng.NextFloat(0.9f, 1.12f);
            root.localScale = Vector3.one * scale;
            var step = rng.NextFloat(4f, 7f);
            var swingL = rng.NextFloat(-6f, -2f);
            var swingR = rng.NextFloat(3f, 9f);
            var tilt = Quaternion.Euler(rng.NextFloat(-6f, 12f), rng.NextFloat(-20f, 20f), rng.NextFloat(-8f, 8f));
            var arcs = root.gameObject.AddComponent<GolemArcs>();
            var shape = Unity.Mathematics.Random.CreateFromIndex(seed ^ 0x9E37u);

            int Rock(string name, Vector3 centre, Vector3 radii, Quaternion rot, float bob, int shards)
            {
                var m = new DunesColossus.MeshBuilder();
                m.Ellipsoid(Vector3.zero, radii, rot, GolemBasalt, 7, 4);
                for (int k = 0; k < 3; k++)                                                                     // jagged lumps
                {
                    var d = (Vector3)shape.NextFloat3Direction();
                    m.Ellipsoid(Vector3.Scale(d, radii) * 0.55f, radii * shape.NextFloat(0.45f, 0.7f), Quaternion.Euler((Vector3)shape.NextFloat3(0f, 360f)), k == 0 ? BasaltPale : GolemBasalt, 6, 3);
                }
                for (int k = 0; k < shards; k++)                                                                // crystal shards poking out
                {
                    var d = (Vector3)shape.NextFloat3Direction();
                    var foot = Vector3.Scale(d, radii) * 0.85f;
                    m.Limb(foot, foot + d * shape.NextFloat(1.2f, 2.6f), shape.NextFloat(0.3f, 0.6f), 0.04f, k % 2 == 0 ? Violet : Crystal, 5);
                }
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.localPosition = centre;
                AddPart("Rock", go.transform, m.ToMesh(name));
                return arcs.AddSegment(go.transform, Mathf.Min(radii.x, Mathf.Min(radii.y, radii.z)) * 0.9f, bob, shape.NextFloat(0f, 6.28f));
            }

            // Legs mid-stride: the left forward, the right back with its heel lifted.
            int lFoot = Rock("LeftFoot", new Vector3(-5.5f, 2f, step), new Vector3(3.6f, 2f, 5f), Quaternion.identity, 0.1f, 1);
            int rFoot = Rock("RightFoot", new Vector3(5.5f, 3f, -step - 1f), new Vector3(3.4f, 2f, 4.6f), Quaternion.Euler(-18f, 0f, 0f), 0.2f, 1);
            int lShin = Rock("LeftShin", new Vector3(-5.5f, 10.5f, step * 0.7f), new Vector3(3.2f, 4f, 3.2f), Quaternion.Euler(-8f, 0f, 0f), 0.5f, 1);
            int rShin = Rock("RightShin", new Vector3(5.5f, 11f, -step * 0.8f), new Vector3(3.2f, 4f, 3.2f), Quaternion.Euler(14f, 0f, 0f), 0.5f, 1);
            int lThigh = Rock("LeftThigh", new Vector3(-5f, 20.5f, step * 0.35f), new Vector3(3.8f, 4.2f, 3.8f), Quaternion.Euler(-14f, 0f, 0f), 0.7f, 2);
            int rThigh = Rock("RightThigh", new Vector3(5f, 20.5f, -step * 0.35f), new Vector3(3.8f, 4.2f, 3.8f), Quaternion.Euler(12f, 0f, 0f), 0.7f, 2);
            int pelvis = Rock("Pelvis", new Vector3(0f, 28.5f, 0f), new Vector3(7f, 2.8f, 4.5f), Quaternion.identity, 0.8f, 2);
            int torso = Rock("Torso", new Vector3(0f, 40f, 0.8f), new Vector3(10f, 7f, 6.5f), Quaternion.Euler(6f, 0f, 0f), 0.9f, 6);
            int head = Rock("Head", new Vector3(0f, 52.5f, 2f), new Vector3(3.8f, 4f, 4.2f), tilt, 1.2f, 3);
            // Crystal eyes in the head's face.
            {
                var eyes = new DunesColossus.MeshBuilder();
                for (int side = -1; side <= 1; side += 2)
                    eyes.Ellipsoid(tilt * new Vector3(side * 1.5f, 0.6f, 3.9f), new Vector3(0.8f, 0.5f, 0.5f), tilt, Crystal, 6, 3);
                AddPart("Eyes", arcs.Segment(head), eyes.ToMesh("RockGolemEyes"));
            }
            // Arms swinging against the legs: shoulder, upper arm, forearm, fist, each hanging free.
            var fists = new int[2];
            for (int side = -1; side <= 1; side += 2)
            {
                var swing = side < 0 ? swingL : swingR;
                var n = side < 0 ? "Left" : "Right";
                int shoulder = Rock(n + "Shoulder", new Vector3(side * 15f, 45.5f, 0.5f), new Vector3(3.6f, 3.4f, 3.6f), Quaternion.identity, 1f, 2);
                int upper = Rock(n + "UpperArm", new Vector3(side * 17f, 37f, swing * 0.4f), new Vector3(3f, 4.2f, 3f), Quaternion.Euler(-swing * 2f, 0f, side * 6f), 1.1f, 1);
                int fore = Rock(n + "Forearm", new Vector3(side * 18f, 27f, swing * 0.9f), new Vector3(3f, 4f, 3f), Quaternion.Euler(-swing * 3f, 0f, side * 4f), 1.2f, 1);
                int fist = Rock(n + "Fist", new Vector3(side * 18.5f, 18f, swing * 1.4f), new Vector3(3.6f, 3.4f, 3.6f), Quaternion.identity, 1.3f, 3);
                fists[side < 0 ? 0 : 1] = fist;
                arcs.Link(torso, shoulder, 2);
                arcs.Link(shoulder, upper, 1);
                arcs.Link(upper, fore, 2);
                arcs.Link(fore, fist, 1);
            }
            arcs.Link(lFoot, lShin, 1); arcs.Link(lShin, lThigh, 2); arcs.Link(lThigh, pelvis, 1);
            arcs.Link(rFoot, rShin, 1); arcs.Link(rShin, rThigh, 2); arcs.Link(rThigh, pelvis, 1);
            arcs.Link(pelvis, torso, 3);
            arcs.Link(torso, head, 2);
            // Shards orbiting the chest; every other one is wired to it.
            for (int k = 0; k < 7; k++)
            {
                var a = k / 7f * Mathf.PI * 2f + rng.NextFloat(-0.3f, 0.3f);
                var p = new Vector3(Mathf.Cos(a) * 15f, 40f + rng.NextFloat(-6f, 7f), Mathf.Sin(a) * 11f);
                var piece = rng.NextFloat(0.8f, 1.6f);
                int shard = Rock("Shard" + k, p, new Vector3(piece, piece * 1.3f, piece), Quaternion.identity, 2f, 0);
                if (k % 2 == 0)
                    arcs.Link(torso, shard, 1);
            }

            // On the ground: a scorched, cracked mound under each foot, magnetic rubble, and the dropped rock.
            var g = new DunesColossus.MeshBuilder();
            foreach (var foot in new[] { new Vector3(-5.5f, 0.3f, step), new Vector3(5.5f, 0.3f, -step - 1f) })
            {
                g.Ellipsoid(foot, new Vector3(7f, 0.7f, 8f), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), Scorch, 10, 3);
                for (int k = 0; k < 4; k++)
                {
                    var ca = rng.NextFloat(0f, Mathf.PI * 2f);
                    g.Limb(foot + Vector3.up * 0.5f, foot + new Vector3(Mathf.Cos(ca) * 9f, 0.2f, Mathf.Sin(ca) * 9f), 0.35f, 0.08f, Hollow, 4);
                }
            }
            for (int k = 0; k < 14; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rng.NextFloat(10f, 28f);
                var r = rng.NextFloat(0.5f, 1.8f);
                p.y = r * 0.4f;
                g.Ellipsoid(p, new Vector3(r, r * 0.7f, r * 0.9f), Quaternion.Euler(rng.NextFloat(0f, 360f), rng.NextFloat(0f, 360f), 0f), k % 3 == 0 ? BasaltPale : GolemBasalt, 6, 3);
            }
            var dropped = new Vector3(rng.NextFloat(22f, 26f), 1.6f, rng.NextFloat(-2f, 8f));
            g.Ellipsoid(dropped, new Vector3(3f, 2f, 2.6f), Quaternion.Euler(12f, 40f, 8f), GolemBasalt, 7, 4);
            g.Ellipsoid(dropped + new Vector3(0.4f, -1.2f, 0f), new Vector3(4.5f, 0.5f, 4f), Quaternion.identity, Scorch, 8, 3);
            g.Limb(dropped + Vector3.up * 1.6f, dropped + new Vector3(-0.6f, 4f, 0.4f), 0.4f, 0.04f, Violet, 5);
            AddPart("Ground", root, g.ToMesh("RockGolemGround"));
            var droppedPoint = new GameObject("DroppedRock").transform;
            droppedPoint.SetParent(root, false);
            droppedPoint.localPosition = dropped;
            arcs.Link(arcs.AddSegment(droppedPoint, 1.8f, 0f, 0f), fists[1], 1, 0.5f);
            return root;
        }
    }
}
