using UnityEngine;

namespace Punchy.UI
{
    /// <summary>
    /// Turns every GuiHook on or off once a frame, in LateUpdate (after the owners' Updates have set what they show), so a
    /// hook with nothing to draw stays out of Unity's IMGUI pass (P-20). One, made with the first hook; it has no OnGUI.
    /// </summary>
    public class GuiHooks : MonoBehaviour
    {
        static GuiHooks instance;

        internal static void Ensure()
        {
            if (instance != null)
                return;
            var go = new GameObject("GuiHooks") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GuiHooks>();
        }

        void LateUpdate() => GuiHook.Refresh();
    }
}
