using System;
using UnityEditor;
using UnityEngine;

namespace Punchy.EditorTools
{
    /// <summary>
    /// W-68: the editor's island setting. Punchy/Island/v1, v2 or v3 sets PUNCHY_ISLAND inside this editor process (which
    /// IslandV2.RequestedVersion reads at the next play) and keeps the choice in EditorPrefs across editor restarts.
    /// "Default" clears both (v2 at the next play, unless the command line says otherwise).
    /// </summary>
    [InitializeOnLoad]
    public static class IslandVersionMenu
    {
        const string Pref = "Punchy.IslandVersion", Env = "PUNCHY_ISLAND";

        static IslandVersionMenu()
        {
            var v = EditorPrefs.GetString(Pref, "");
            if (v.Length > 0) Environment.SetEnvironmentVariable(Env, v);
        }

        static void Set(string v)
        {
            if (v == null) EditorPrefs.DeleteKey(Pref); else EditorPrefs.SetString(Pref, v);
            Environment.SetEnvironmentVariable(Env, v);
            Debug.Log($"[island] editor island set to {v ?? "default"} (applies at the next play)");
        }

        [MenuItem("Punchy/Island/v1")] static void V1() => Set("v1");
        [MenuItem("Punchy/Island/v2")] static void V2() => Set("v2");
        [MenuItem("Punchy/Island/v3")] static void V3() => Set("v3");
        [MenuItem("Punchy/Island/Default")] static void Default() => Set(null);

        [MenuItem("Punchy/Island/v1", true)] static bool V1On() => Check("v1");
        [MenuItem("Punchy/Island/v2", true)] static bool V2On() => Check("v2");
        [MenuItem("Punchy/Island/v3", true)] static bool V3On() => Check("v3");

        static bool Check(string v)
        {
            Menu.SetChecked("Punchy/Island/" + v, string.Equals(Environment.GetEnvironmentVariable(Env), v, StringComparison.OrdinalIgnoreCase));
            return true;
        }
    }
}
