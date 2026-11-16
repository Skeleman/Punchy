using Punchy.Settings;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// The sheets the townsfolk pin up on their town's notice board (A-116 row 2, #316), drawn: a small pool of thin
    /// paper cards per board, each shown while NoticeBoard says a sheet is up in its slot. Cosmetic, worked out on every
    /// machine from the shared day and clock, so a joined client sees what the host sees. Ticked by FaunaDirector after
    /// it sets the clock; rebuilt when the terrain is re-baked. Hidden once a sheet would be under a couple of pixels
    /// (DetailPolicy.ScreenSize), as the board's own small parts are lost by then anyway.
    /// </summary>
    public static class NoticeSheets
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static GameObject root;
        static Renderer[] sheets;
        static float3[] boards;
        static TerrainSource builtFor;
        static Material material;
        static MaterialPropertyBlock block;

        /// <summary>A sheet's size (m) and the pixels below which it's not drawn.</summary>
        const float Width = 0.2f, Height = 0.26f, MinPixels = 1.5f;

        static readonly Color[] Paper =
        {
            new Color(0.95f, 0.93f, 0.84f), new Color(0.96f, 0.9f, 0.68f), new Color(0.82f, 0.88f, 0.92f),
            new Color(0.93f, 0.8f, 0.76f), new Color(0.86f, 0.9f, 0.78f), new Color(0.98f, 0.97f, 0.95f)
        };

        /// <summary>Shows the sheets up now on every board (the day count and the hour from the day/night cycle).</summary>
        public static void Tick(in TerrainSource source, int day, float hour, Camera cam)
        {
            if (!source.Settlements.IsCreated)
                return;
            if (sheets == null || !source.Equals(builtFor))
                Build(source);
            DetailPolicy.SetView(cam);
            var view = DetailPolicy.Current.Data;
            for (int t = 0; t < boards.Length; t++)
            {
                var near = DetailPolicy.ScreenSize(Height, math.distance(view.Camera, boards[t]), view.PixelsPerMetre) >= MinPixels;
                for (int slot = 0; slot < NoticeBoard.SlotCount; slot++)
                {
                    var r = sheets[t * NoticeBoard.SlotCount + slot];
                    int postDay = -1, k = -1;
                    var up = near && NoticeBoard.SheetIn(t, slot, day, hour, out postDay, out k);
                    if (r.enabled != up)
                    {
                        r.enabled = up;
                        if (up)
                            Dress(r, t, slot, postDay, k);
                    }
                }
            }
        }

        /// <summary>Tint and tilt by the sheet's own number, so one taken down and another put up looks new.</summary>
        static void Dress(Renderer r, int town, int slot, int postDay, int k)
        {
            var look = NoticeBoard.Look(town, postDay, k);
            block ??= new MaterialPropertyBlock();
            block.SetColor(BaseColorId, Paper[look % (uint)Paper.Length]);
            r.SetPropertyBlock(block);
            var tilt = ((look >> 8 & 0xFF) / 255f - 0.5f) * 14f;
            var tall = 0.85f + 0.3f * ((look >> 16 & 0xFF) / 255f);
            var tr = r.transform;
            tr.localRotation = Quaternion.Euler(0f, 0f, tilt);
            tr.localScale = new Vector3(Width * (1f + 0.25f * (look >> 24 & 1u)), Height * tall, 0.006f);
        }

        static void Build(in TerrainSource source)
        {
            if (root != null)
                Object.Destroy(root);
            builtFor = source;
            if (material == null)
                material = Resources.Load<Material>("ItemMaterial");
            root = new GameObject("Notice sheets") { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(root);
            ref var set = ref source.Settlements.Value.Settlements;
            boards = new float3[set.Length];
            sheets = new Renderer[set.Length * NoticeBoard.SlotCount];
            for (int t = 0; t < set.Length; t++)
            {
                var xz = NoticeBoard.At(set[t].Centre);
                boards[t] = new float3(xz.x, source.SampleHeight(xz), xz.y);
                for (int slot = 0; slot < NoticeBoard.SlotCount; slot++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.Destroy(go.GetComponent<Collider>());
                    go.hideFlags = HideFlags.HideInHierarchy;
                    go.transform.SetParent(root.transform, false);
                    // (In front of the board's face and Quests' own sheets, each slot a hair further out so none fight.)
                    var at = NoticeBoard.SlotAt(slot);
                    go.transform.position = (Vector3)boards[t] + new Vector3(at.x, at.y, -0.052f - 0.003f * slot);
                    var r = go.GetComponent<Renderer>();
                    if (material != null)
                        r.sharedMaterial = material;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.enabled = false;
                    sheets[t * NoticeBoard.SlotCount + slot] = r;
                }
            }
        }
    }
}
