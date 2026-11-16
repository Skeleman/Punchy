using System.Collections.Generic;
using Punchy.Fauna;
using Punchy.UI;
using UnityEngine;

namespace Punchy.Net
{
    /// <summary>
    /// The menu's Character tab: pick a body by class, then the species in that class, then its colouring when the
    /// species has several. Classes are read from each species' own data (upright, flies, swims, crawls), and only the
    /// classes some playable species falls in are shown. The last pick per class is remembered, so switching class is one
    /// click. The body travels as its Playable Species index and the colouring as NetPlayerLook.Coat, so a pick works
    /// solo, hosting and joined alike.
    /// </summary>
    public static class CharacterTab
    {
        public enum BodyClass { Humanoid, Quadruped, Bird, Fish, Other }

        static readonly List<int> inClass = new List<int>();

        /// <summary>A species' class, from its body's data (variants take their body's).</summary>
        public static BodyClass ClassOf(CreatureSpecies species)
        {
            var body = species.Body;
            if (body.Upright)
                return BodyClass.Humanoid;
            if (body.Habitat == Habitat.Flies || body.Habitat == Habitat.Floats)
                return BodyClass.Bird;
            if (body.Habitat == Habitat.Swims)
                return BodyClass.Fish;
            return body.Crawls ? BodyClass.Other : BodyClass.Quadruped;
        }

        static string PickKey(BodyClass c) => "Punchy.Character." + c;

        /// <summary>Switches to a class: its last pick if still playable, else its first species.</summary>
        public static void PickClass(NetAvatars avatars, BodyClass c)
        {
            var list = avatars.PlayableSpecies;
            var remembered = PlayerPrefs.GetString(PickKey(c), "");
            int first = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null || ClassOf(list[i]) != c)
                    continue;
                if (first < 0)
                    first = i;
                if (list[i].name == remembered)
                {
                    PickSpecies(avatars, i);
                    return;
                }
            }
            if (first >= 0)
                PickSpecies(avatars, first);
        }

        /// <summary>Takes a body (remembered as its class's pick) with its remembered colouring.</summary>
        public static void PickSpecies(NetAvatars avatars, int index)
        {
            var s = avatars.PlayableSpecies[index];
            PlayerPrefs.SetString(PickKey(ClassOf(s)), s.name);
            avatars.LocalCoat = PlayerPrefs.GetInt("Punchy.Coat." + s.name, -1);
            avatars.SetLocalSpecies(index);
        }

        static int lastDrawFrame = -100;

        public static void Draw(NetAvatars avatars, MenuNav nav)
        {
            var list = avatars.PlayableSpecies;
            if (list.Count == 0)
            {
                GUILayout.Label("No playable bodies.");
                return;
            }
            var index = avatars.LocalIndex;
            var current = index >= 0 ? list[index] : null;
            var currentClass = current != null ? ClassOf(current) : (BodyClass)(-1);
            // P-18: on opening (the first draw after a gap), the pad focus starts on the body being played, not the
            // first button: its stop is the class buttons shown before it plus its place in its class's row.
            if (Time.frameCount - lastDrawFrame > 2 && current != null && Event.current.type == EventType.Layout)
            {
                var classes = 0;
                for (var c = BodyClass.Humanoid; c <= BodyClass.Other; c++)
                    foreach (var s in list)
                        if (s != null && ClassOf(s) == c)
                        {
                            classes++;
                            break;
                        }
                var place = 0;
                for (int i = 0; i < index; i++)
                    if (list[i] != null && ClassOf(list[i]) == currentClass)
                        place++;
                nav.Focus = classes + place;
                Debug.Log($"CharacterTab: opened on {current.name} (stop {nav.Focus})");
            }
            lastDrawFrame = Time.frameCount;

            // Classes: only those some playable species falls in.
            GUILayout.Label("Class");
            GUILayout.BeginHorizontal();
            for (var c = BodyClass.Humanoid; c <= BodyClass.Other; c++)
            {
                var any = false;
                foreach (var s in list)
                    any |= s != null && ClassOf(s) == c;
                if (!any)
                    continue;
                if (nav.Button(c == currentClass ? "[" + c + "]" : c.ToString()) && c != currentClass)
                    PickClass(avatars, c);
            }
            GUILayout.EndHorizontal();
            if (current == null)
                return;

            // Species in the class.
            GUILayout.Space(6);
            GUILayout.Label("Species");
            inClass.Clear();
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && ClassOf(list[i]) == currentClass)
                    inClass.Add(i);
            for (int row = 0; row < inClass.Count; row += 4)
            {
                GUILayout.BeginHorizontal();
                for (int k = row; k < row + 4 && k < inClass.Count; k++)
                {
                    var i = inClass[k];
                    if (nav.Button(i == index ? "[" + list[i].name + "]" : list[i].name) && i != index)
                        PickSpecies(avatars, i);
                }
                GUILayout.EndHorizontal();
            }

            DrawBuild(avatars, nav, current);

            // Colourings, only when the species has several.
            if (current.Coats.Count >= 2)
            {
                GUILayout.Space(6);
                GUILayout.Label("Colour");
                // (Long names, like the folk's "Glacier robe, olive skin", go two to a row so none runs off the panel.)
                var longest = 0;
                foreach (var coat in current.Coats)
                    longest = Mathf.Max(longest, coat.Name != null ? coat.Name.Length : 0);
                var perRow = longest > 14 ? 2 : 4;
                for (int row = 0; row < current.Coats.Count; row += perRow)
                {
                    GUILayout.BeginHorizontal();
                    for (int k = row; k < row + perRow && k < current.Coats.Count; k++)
                    {
                        var label = current.Coats[k].Name;
                        if (nav.Button(k == avatars.LocalCoat ? "[" + label + "]" : label))
                        {
                            avatars.LocalCoat = k;
                            PlayerPrefs.SetInt("Punchy.Coat." + current.name, k);
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }

            // P-29: hair, for bodies that wear gear (people): one pick for every such body.
            if (currentClass != BodyClass.Humanoid)
                return;
            GUILayout.Space(6);
            GUILayout.Label("Hair");
            Choices(nav, CreatureHair.Names, PlayerHair.Style, k => PlayerHair.Set((byte)k, PlayerHair.Colour));
            GUILayout.Space(6);
            GUILayout.Label("Hair colour");
            Choices(nav, CreatureHair.ColourNames, PlayerHair.Colour, k => PlayerHair.Set(PlayerHair.Style, (byte)k));
        }

        /// <summary>A grid of buttons, four to a row, the current one in brackets.</summary>
        static void Choices(MenuNav nav, string[] names, int current, System.Action<int> pick)
        {
            for (int row = 0; row < names.Length; row += 4)
            {
                GUILayout.BeginHorizontal();
                for (int k = row; k < row + 4 && k < names.Length; k++)
                    if (nav.Button(k == current ? "[" + names[k] + "]" : names[k]) && k != current)
                        pick(k);
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// A-124: the body's build (one of the species' BodyBuilds.Count) and height (16 steps over its Size range),
        /// stepped or rolled; "Own" goes back to the one this player's individual rolls. Sent as NetPlayerLook.Build.
        /// </summary>
        static void DrawBuild(NetAvatars avatars, MenuNav nav, CreatureSpecies current)
        {
            if (!current.BuildRange.Any)
                return;
            var code = avatars.LocalBuildCode;
            var build = BodyBuilds.PlayerBuild(code);
            var height = BodyBuilds.PlayerHeightStep(code);
            GUILayout.Space(6);
            GUILayout.Label(avatars.LocalBuild >= 0 ? "Build" : "Build (your own)");
            GUILayout.BeginHorizontal();
            if (nav.Button("< Build"))
                build = (build + BodyBuilds.Count - 1) % BodyBuilds.Count;
            GUILayout.Label($"{build + 1}/{BodyBuilds.Count}", GUILayout.Width(48));
            if (nav.Button("Build >"))
                build = (build + 1) % BodyBuilds.Count;
            if (nav.Button("< Height"))
                height = Mathf.Max(0, height - 1);
            GUILayout.Label($"{height + 1}/16", GUILayout.Width(48));
            if (nav.Button("Height >"))
                height = Mathf.Min(15, height + 1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (nav.Button("Roll"))
            {
                build = Random.Range(0, BodyBuilds.Count);
                height = Random.Range(0, 16);
            }
            var own = avatars.LocalBuild >= 0 && nav.Button("Own");
            GUILayout.EndHorizontal();
            if (own)
                avatars.SetLocalBuild(-1);
            else if (BodyBuilds.Player(build, height) != code)
            {
                avatars.SetLocalBuild(BodyBuilds.Player(build, height));
                Debug.Log($"CharacterTab: {current.name} build {build}, height step {height}");
            }
        }
    }

    /// <summary>
    /// The player's hair (P-29): a CreatureHair style and colour, picked on the Character tab and saved for next time
    /// (as ids, which only ever grow). Items sends it to the host with the gear, so other players see it. "-hair" sets it
    /// for one run without saving (a shot line can change it).
    /// </summary>
    public static class PlayerHair
    {
        const string StyleKey = "Punchy.Character.Hair", ColourKey = "Punchy.Character.HairColour";
        static bool loaded;
        static byte style, colour;

        public static byte Style
        {
            get
            {
                Load();
                return style;
            }
        }

        public static byte Colour
        {
            get
            {
                Load();
                return colour;
            }
        }

        /// <summary>A pick on the Character tab: worn now and saved.</summary>
        public static void Set(byte newStyle, byte newColour)
        {
            Test(newStyle, newColour);
            PlayerPrefs.SetInt(StyleKey, style);
            PlayerPrefs.SetInt(ColourKey, colour);
            PlayerPrefs.Save();
        }

        /// <summary>Worn now, not saved ("-hair").</summary>
        public static void Test(byte newStyle, byte newColour)
        {
            loaded = true;
            style = newStyle < CreatureHair.Names.Length ? newStyle : CreatureHair.DefaultStyle;
            colour = newColour < CreatureHair.ColourNames.Length ? newColour : CreatureHair.DefaultColour;
            Debug.Log($"[hair] {CreatureHair.Names[style]}, {CreatureHair.ColourNames[colour]}");
        }

        static void Load()
        {
            if (loaded)
                return;
            loaded = true;
            // (An id this build doesn't know, from a newer one: the default.)
            var s = PlayerPrefs.GetInt(StyleKey, CreatureHair.DefaultStyle);
            var c = PlayerPrefs.GetInt(ColourKey, CreatureHair.DefaultColour);
            style = s >= 0 && s < CreatureHair.Names.Length ? (byte)s : CreatureHair.DefaultStyle;
            colour = c >= 0 && c < CreatureHair.ColourNames.Length ? (byte)c : CreatureHair.DefaultColour;
        }
    }
}
