using System.Collections.Generic;
using System.IO;
using System.Linq;
using Punchy.Fauna;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Punchy.EditorTools
{
    /// <summary>
    /// M-06 row 3 (#413): the Model Lab's body builder, for changing a creature's shape and not only its proportions.
    /// The Lab edits a work copy: the loaded species (and the species it is a variant of) and every limb type its slots
    /// use, copied in memory, so the assets on disk never change. Per segment it edits length, start and end radius
    /// (the taper), flatten and colour (accent mix, fixed colour, glow); it splits a segment in two or removes one,
    /// keeping all seven key poses in step; per slot it edits the rest direction, and for an upright body the torso's
    /// lean. Only what the body data already holds, so the game bakes the same shape from the same fields
    /// (docs/agents/model-lab-builder.md). A limb type used by several slots changes on all of them.
    /// <para>
    /// Builder edits are kept by "Save as new creature" (ModelLab.SaveSpecies.cs), which writes them as a new species;
    /// the loaded asset never changes. An untouched work copy bakes byte for byte as the asset does
    /// (<see cref="BuilderCheck"/>).
    /// </para>
    /// </summary>
    public partial class ModelLab
    {
        /// <summary>The species asset loaded (<see cref="species"/> is its work copy while a creature is loaded).</summary>
        CreatureSpecies source;
        /// <summary>Work copies to destroy on unload: the species chain and the copied limb types.</summary>
        readonly List<Object> workCopies = new List<Object>();
        /// <summary>Each copied limb type's asset, for slider ranges and Revert.</summary>
        readonly Dictionary<LimbType, LimbType> limbSource = new Dictionary<LimbType, LimbType>();
        int buildSlot, buildSegment;
        bool built;

        // ---------- the work copy ----------

        /// <summary>Copies <paramref name="asset"/>, the species it is a variant of and every limb type their slots use.</summary>
        CreatureSpecies WorkCopy(CreatureSpecies asset)
        {
            DestroyWorkCopy();
            var limbs = new Dictionary<LimbType, LimbType>();
            CreatureSpecies Copy(CreatureSpecies s, int depth)
            {
                var c = Instantiate(s);
                c.name = s.name;   // (tunables and the fingerprint key on the name)
                c.hideFlags = HideFlags.HideAndDontSave;
                workCopies.Add(c);
                foreach (var slot in c.Parts)
                {
                    if (slot.Limb == null)
                        continue;
                    if (!limbs.TryGetValue(slot.Limb, out var limb))
                    {
                        limb = Instantiate(slot.Limb);
                        limb.name = slot.Limb.name;
                        limb.hideFlags = HideFlags.HideAndDontSave;
                        workCopies.Add(limb);
                        limbs.Add(slot.Limb, limb);
                        limbSource[limb] = slot.Limb;
                    }
                    slot.Limb = limb;
                }
                if (s.VariantOf != null && s.VariantOf != s && depth < 8)
                    c.VariantOf = Copy(s.VariantOf, depth + 1);
                return c;
            }
            built = false;
            return Copy(asset, 0);
        }

        void DestroyWorkCopy()
        {
            foreach (var o in workCopies)
                if (o != null)
                    DestroyImmediate(o);
            workCopies.Clear();
            limbSource.Clear();
        }

        /// <summary>Drops every builder edit: a fresh work copy of the loaded asset.</summary>
        void RevertBuild()
        {
            if (source == null)
                return;
            species = WorkCopy(source);
            Rebuild();
        }

        // ---------- edits ----------

        LimbSlot Slot(int slot) => species != null && slot >= 0 && slot < species.Body.Parts.Count ? species.Body.Parts[slot] : null;

        static LimbPose[] Keys(LimbType limb) =>
            new[] { limb.Idle, limb.WalkReach, limb.WalkPush, limb.WalkLift, limb.RunReach, limb.RunPush, limb.RunLift };

        /// <summary>Splits a segment into two halves, the same shape at rest: the first keeps its joint angle in every key
        /// pose, the second carries straight on (no bend). The radius between them is the one halfway along.</summary>
        static void SplitSegment(LimbType limb, int i)
        {
            var a = limb.Segments[i];
            var b = JsonUtility.FromJson<LimbSegment>(JsonUtility.ToJson(a));
            var mid = math.lerp(a.RadiusStart, a.RadiusEnd, 0.5f);
            a.Length *= 0.5f;
            b.Length = a.Length;
            a.RadiusEnd = mid;
            b.RadiusStart = math.max(0.001f, mid);
            limb.Segments.Insert(i + 1, b);
            foreach (var key in Keys(limb))
                if (key != null && key.Joints.Count > i)
                    key.Joints.Insert(i + 1, Vector3.zero);
        }

        /// <summary>Removes a segment and its joint angle in every key pose (a limb keeps at least one segment).</summary>
        static void RemoveSegment(LimbType limb, int i)
        {
            if (limb.Segments.Count <= 1)
                return;
            limb.Segments.RemoveAt(i);
            foreach (var key in Keys(limb))
                if (key != null && key.Joints.Count > i)
                    key.Joints.RemoveAt(i);
        }

        /// <summary>A torso's rest pitch when it stands straight up; the lean is measured from it.</summary>
        const float UpPitch = -90f;

        /// <summary>The upright body's torso: its first non-leg part on the body (CreatureAnimator's torso test).</summary>
        int TorsoSlot()
        {
            if (species == null || !species.Body.Upright)
                return -1;
            var parts = species.Body.Parts;
            for (int i = 0; i < parts.Count; i++)
                if (parts[i].Limb != null && parts[i].Parent < 0 && parts[i].Limb.Role == LimbRole.Feature)
                    return i;
            return -1;
        }

        void Edited()
        {
            built = true;
            Rebuild();
        }

        // ---------- the panel ----------

        void DrawBuilder()
        {
            GUILayout.Label("Body builder", EditorStyles.boldLabel);
            var slots = species.Body.Parts;
            if (slots.Count == 0)
                return;

            var torso = TorsoSlot();
            if (torso >= 0)
            {
                EditorGUI.BeginChangeCheck();
                var lean = EditorGUILayout.Slider("Torso lean", slots[torso].Direction.x - UpPitch, -45f, 75f);
                if (EditorGUI.EndChangeCheck())
                {
                    slots[torso].Direction.x = UpPitch + lean;
                    Edited();
                }
            }

            var names = slots.Select((p, i) => $"{i}: {(p.Limb != null ? p.Limb.name : "(none)")}").ToArray();
            buildSlot = math.clamp(EditorGUILayout.Popup("Limb slot", buildSlot, names), 0, slots.Count - 1);
            var slot = slots[buildSlot];
            var limb = slot.Limb;
            if (limb == null)
                return;
            var sharers = slots.Where((p, i) => i != buildSlot && p.Limb == limb).Count();
            if (sharers > 0)
                EditorGUILayout.HelpBox($"{limb.name} is used by {sharers} more slot(s); a segment edit changes them too.", MessageType.None);

            EditorGUI.BeginChangeCheck();
            var dir = EditorGUILayout.Vector3Field("Rest direction", slot.Direction);
            if (EditorGUI.EndChangeCheck())
            {
                slot.Direction = dir;
                Edited();
            }

            buildSegment = EditorGUILayout.IntSlider("Segment", buildSegment, 0, limb.Segments.Count - 1);
            var seg = limb.Segments[buildSegment];
            limbSource.TryGetValue(limb, out var original);
            var was = original != null && buildSegment < original.Segments.Count ? original.Segments[buildSegment] : seg;
            // (Ranges from the asset's own values, so they hold still while dragging.)
            var lengthMax = math.max(0.3f, was.Length * 3f);
            var radiusMax = math.max(0.1f, math.max(was.RadiusStart, was.RadiusEnd) * 3f);

            EditorGUI.BeginChangeCheck();
            var length = EditorGUILayout.Slider("Length", seg.Length, 0f, lengthMax);
            var r0 = EditorGUILayout.Slider("Radius start", seg.RadiusStart, 0.001f, radiusMax);
            var r1 = EditorGUILayout.Slider("Radius end", seg.RadiusEnd, 0f, radiusMax);
            var flatten = EditorGUILayout.Slider("Flatten", seg.Flatten, 0.05f, 3f);
            GUILayout.Label("Colour", EditorStyles.miniBoldLabel);
            var accent = EditorGUILayout.Slider("Accent (base to accent)", seg.Accent, 0f, 1f);
            var fixedColour = EditorGUILayout.Toggle("Fixed colour", seg.UseFixedColor);
            Color colour;
            using (new EditorGUI.DisabledScope(!fixedColour))
                colour = EditorGUILayout.ColorField("Colour", seg.FixedColor);
            var glow = EditorGUILayout.Slider("Glow", seg.Glow, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                seg.Length = length; seg.RadiusStart = r0; seg.RadiusEnd = r1; seg.Flatten = flatten;
                seg.Accent = accent; seg.UseFixedColor = fixedColour; seg.FixedColor = colour; seg.Glow = glow;
                Edited();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Split segment"))
                {
                    SplitSegment(limb, buildSegment);
                    Edited();
                }
                using (new EditorGUI.DisabledScope(limb.Segments.Count <= 1))
                    if (GUILayout.Button("Remove segment"))
                    {
                        RemoveSegment(limb, buildSegment);
                        buildSegment = math.min(buildSegment, limb.Segments.Count - 1);
                        Edited();
                    }
            }
            if (limb.Role == LimbRole.Leg)
                EditorGUILayout.HelpBox("Legs measure segment 0 as the thigh and 1 as the shin (stance and reach): splitting or removing those moves the knee.", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!built))
                    if (GUILayout.Button("Revert builder edits"))
                        RevertBuild();
            }
            EditorGUILayout.LabelField(built ? "Builder edits are in memory only (not saved yet)." : "No builder edits.", EditorStyles.miniLabel);
            EditorGUILayout.Space();
        }

        // ---------- hooks for checks by eval ----------

        /// <summary>Sets one field of a segment of the loaded creature's work copy (Length, RadiusStart, RadiusEnd,
        /// Flatten, Accent, Glow) and rebuilds.</summary>
        public static void EditSegment(int slot, int segment, string field, float value)
        {
            var w = Open();
            var limb = w.Slot(slot)?.Limb;
            if (limb == null || segment < 0 || segment >= limb.Segments.Count)
                return;
            typeof(LimbSegment).GetField(field).SetValue(limb.Segments[segment], value);
            w.Edited();
        }

        /// <summary>Splits (or with <paramref name="remove"/>, removes) a segment of the loaded creature's work copy.</summary>
        public static void EditSegmentCount(int slot, int segment, bool remove)
        {
            var w = Open();
            var limb = w.Slot(slot)?.Limb;
            if (limb == null || segment < 0 || segment >= limb.Segments.Count)
                return;
            if (remove)
                RemoveSegment(limb, segment);
            else
                SplitSegment(limb, segment);
            w.Edited();
        }

        /// <summary>Sets an upright creature's torso lean (degrees of pitch from straight up), or returns false when it has no torso.</summary>
        public static bool SetTorsoLean(float degrees)
        {
            var w = Open();
            var torso = w.TorsoSlot();
            if (torso < 0)
                return false;
            w.species.Body.Parts[torso].Direction.x = UpPitch + degrees;
            w.Edited();
            return true;
        }

        public static void RevertBuilder() => Open().RevertBuild();

        /// <summary>
        /// The fingerprint check: bakes every species from its asset and from an untouched work copy and compares the
        /// blob bytes. Returns how many species were checked and which (if any) bake differently.
        /// </summary>
        public static string BuilderCheck()
        {
            var w = Open();
            var keep = w.loadedName;
            w.Unload();   // (the check makes and drops its own work copies)
            var differ = new List<string>();
            var count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(CreatureSpecies)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null)
                    continue;
                count++;
                var copy = w.WorkCopy(asset);
                if (!BlobBytes(asset).SequenceEqual(BlobBytes(copy)))
                    differ.Add(asset.name);
            }
            w.DestroyWorkCopy();
            if (!string.IsNullOrEmpty(keep))
                w.LoadByName(keep);
            return $"{count} species, {differ.Count} bake differently{(differ.Count > 0 ? ": " + string.Join(", ", differ) : "")}";
        }

        /// <summary>A species' baked blob as bytes, written the way FaunaDirector's fingerprint writes it.</summary>
        static byte[] BlobBytes(CreatureSpecies s)
        {
            var blob = CreatureBlobBuilder.Build(s);
            var path = Path.Combine(Application.temporaryCachePath, "model-lab-blob.bin");
            try
            {
                var type = typeof(Unity.Entities.Serialization.BinaryWriter).Assembly.GetType("Unity.Entities.Serialization.StreamBinaryWriter");
                using (var writer = (Unity.Entities.Serialization.BinaryWriter)System.Activator.CreateInstance(type,
                           System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                           null, new object[] { path, 65536, default(EntityManager) }, null))
                    BlobAssetSerializeExtensions.Write(writer, blob);
                return File.ReadAllBytes(path);
            }
            finally
            {
                blob.Dispose();
                File.Delete(path);
            }
        }
    }
}
