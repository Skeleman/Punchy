using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Punchy.Fauna;
using Punchy.Net;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Player
{
    [System.Serializable]
    public class StatDef
    {
        public string id, name, about;
        public float min = 1f, max = 20f;
    }

    [System.Serializable]
    public class StatBody
    {
        public string body;
        public float[] values;
    }

    [System.Serializable]
    public class StatDerived
    {
        public string id, name, from, format;
        public float start, per;
    }

    [System.Serializable]
    class StatFile
    {
        public bool enforceCarry;
        public StatDef[] stats;
        public StatBody[] bodies;
        public StatDerived[] derived;
    }

    /// <summary>
    /// The local player's stats (P-27), a template to change later. Names, ranges, each kind of body's values and what
    /// the stats give all come from StreamingAssets/stats.json, read again a second after it changes. A value is the
    /// body's plus what the player has earned (<see cref="Grant"/>), so earned points survive a change of body. Earned
    /// points are kept per machine with the settings until the game has a save file; they aren't sent to the host.
    /// Nothing reads a stat yet except the carry limit, and that only with enforceCarry set in the file. F2 grants a
    /// point to each stat in turn (a debug key). Test flag: "-statgrant id amount".
    /// </summary>
    public class PlayerStats : MonoBehaviour
    {
        const string FileName = "stats.json", SaveKey = "Punchy.Stats.Earned";
        const float CheckEvery = 1f, GrantArgsAfter = 8f;

        static PlayerStats instance;
        StatFile file;
        System.DateTime stamp;
        float checkAt, bodyAt;
        string body = "human";
        readonly Dictionary<string, float> earned = new Dictionary<string, float>();
        FaunaDirector fauna;
        int debugNext;
        string grantArg;
        float grantAmount;

        static string FilePath => Path.Combine(Application.streamingAssetsPath, FileName);

        /// <summary>Which of the file's bodies the player has now: human, small, medium or big.</summary>
        public static string Body => instance != null ? instance.body : "human";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (instance != null)
                return;
            var go = new GameObject("PlayerStats");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            instance = go.AddComponent<PlayerStats>();
            instance.LoadEarned();
            instance.Load("start");
            Punchy.UI.MainMenu.AddTab("Stats", 6, DrawTab);
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, "-statgrant");
            if (at >= 0 && at + 2 < args.Length && float.TryParse(args[at + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
                (instance.grantArg, instance.grantAmount) = (args[at + 1], amount);
        }

        void Load(string why)
        {
            try
            {
                stamp = File.GetLastWriteTimeUtc(FilePath);
                var read = JsonUtility.FromJson<StatFile>(File.ReadAllText(FilePath));
                if (read?.stats == null || read.bodies == null)
                    throw new System.IO.InvalidDataException("no stats or bodies");
                file = read;
                Debug.Log($"[stats] read {FileName} ({why}): {Describe()}");
            }
            catch (System.Exception e)
            {
                // (A half-saved edit keeps the last good file.)
                Debug.LogWarning($"[stats] couldn't read {FileName} ({why}): {e.Message}");
            }
        }

        void Update()
        {
            var now = Time.realtimeSinceStartup;
            if (now >= checkAt)
            {
                checkAt = now + CheckEvery;
                if (File.Exists(FilePath) && File.GetLastWriteTimeUtc(FilePath) != stamp)
                    Load("changed");
            }
            if (now >= bodyAt)
            {
                bodyAt = now + 0.5f;
                var kind = FindBody();
                if (kind != body)
                {
                    body = kind;
                    Debug.Log($"[stats] body is now {body}: {Describe()}");
                }
            }
            if (grantArg != null && now >= GrantArgsAfter)
            {
                Grant(grantArg, grantAmount, "-statgrant");
                grantArg = null;
            }
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f2Key.wasPressedThisFrame && file != null && file.stats.Length > 0)
                Grant(file.stats[debugNext++ % file.stats.Length].id, 1f, "debug key");
        }

        /// <summary>The kind of body: a human shape, else by size against a human (PlayerBody.Size).</summary>
        string FindBody()
        {
            if (fauna == null)
                fauna = Object.FindFirstObjectByType<FaunaDirector>();
            if (fauna == null || fauna.PlayerSpecies == null || CharacterTab.ClassOf(fauna.PlayerSpecies) == CharacterTab.BodyClass.Humanoid)
                return "human";
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return body;
            using var q = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerBody>());
            if (!q.TryGetSingleton<PlayerBody>(out var shape) || shape.Size <= 0f)
                return body;
            return shape.Size < 0.6f ? "small" : shape.Size > 1.3f ? "big" : "medium";
        }

        float BaseValue(int stat)
        {
            foreach (var b in file.bodies)
                if (b.body == body && b.values != null && stat < b.values.Length)
                    return b.values[stat];
            return file.stats[stat].min;
        }

        float ValueOf(int stat)
        {
            var def = file.stats[stat];
            earned.TryGetValue(def.id, out var extra);
            return Mathf.Clamp(BaseValue(stat) + extra, def.min, def.max);
        }

        int IndexOf(string id)
        {
            if (file != null)
                for (int i = 0; i < file.stats.Length; i++)
                    if (file.stats[i].id == id)
                        return i;
            return -1;
        }

        float DerivedOf(StatDerived d)
        {
            var from = IndexOf(d.from);
            return from >= 0 ? d.start + d.per * ValueOf(from) : d.start;
        }

        /// <summary>A stat's value now (0 if the file has no such stat).</summary>
        public static float Value(string id)
        {
            var i = instance != null ? instance.IndexOf(id) : -1;
            return i >= 0 ? instance.ValueOf(i) : 0f;
        }

        /// <summary>A derived value now ("carry", "run", "talk"; 0 if the file has none).</summary>
        public static float Derived(string id)
        {
            if (instance?.file?.derived != null)
                foreach (var d in instance.file.derived)
                    if (d.id == id)
                        return instance.DerivedOf(d);
            return 0f;
        }

        /// <summary>The way a stat grows: <paramref name="amount"/> points earned, for <paramref name="reason"/>.</summary>
        public static void Grant(string id, float amount, string reason)
        {
            if (instance == null || instance.IndexOf(id) < 0)
            {
                Debug.LogWarning($"[stats] no stat '{id}' to grant ({reason})");
                return;
            }
            instance.earned.TryGetValue(id, out var had);
            if (Mathf.Approximately(had + amount, 0f))
                instance.earned.Remove(id);
            else
                instance.earned[id] = had + amount;
            instance.SaveEarned();
            Debug.Log($"[stats] {id} {amount:+0.#;-0.#} ({reason}): {instance.Describe()}");
        }

        /// <summary>Whether this much weight (kg) can be carried: always, unless the file sets enforceCarry.</summary>
        public static bool CanCarry(float weight) =>
            instance?.file == null || !instance.file.enforceCarry || weight <= Derived("carry");

        void SaveEarned()
        {
            var parts = new List<string>();
            foreach (var kv in earned)
                parts.Add(kv.Key + "=" + kv.Value.ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.SetString(SaveKey, string.Join(";", parts));
            PlayerPrefs.Save();
        }

        void LoadEarned()
        {
            foreach (var part in PlayerPrefs.GetString(SaveKey, "").Split(';'))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    earned[kv[0]] = v;
            }
        }

        string Line(StatDerived d) => DerivedOf(d).ToString(string.IsNullOrEmpty(d.format) ? "0.0" : d.format, CultureInfo.InvariantCulture);

        string Describe()
        {
            if (file == null)
                return "no stats";
            var parts = new List<string> { body };
            for (int i = 0; i < file.stats.Length; i++)
                parts.Add($"{file.stats[i].id} {ValueOf(i):0.#}");
            if (file.derived != null)
                foreach (var d in file.derived)
                    parts.Add($"{d.id} {Line(d)}");
            return string.Join(", ", parts);
        }

        /// <summary>The Stats tab: each value with what it means, then what the values give.</summary>
        static void DrawTab(Punchy.UI.MenuNav nav)
        {
            var title = new GUIStyle(GUI.skin.label) { richText = true };
            GUILayout.Label("<b>Stats</b>", title);
            if (instance == null || instance.file == null)
            {
                GUILayout.Label("No stats file.");
                return;
            }
            var file = instance.file;
            for (int i = 0; i < file.stats.Length; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(file.stats[i].name, GUILayout.Width(120f));
                GUILayout.Label(instance.ValueOf(i).ToString("0.#", CultureInfo.InvariantCulture), GUILayout.Width(50f));
                GUILayout.Label(file.stats[i].about);
                GUILayout.EndHorizontal();
            }
            if (file.derived == null)
                return;
            GUILayout.Space(8f);
            foreach (var d in file.derived)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(d.name, GUILayout.Width(120f));
                GUILayout.Label(instance.Line(d));
                GUILayout.EndHorizontal();
            }
        }
    }
}
