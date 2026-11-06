using Unity.Profiling;
using UnityEngine;

namespace Punchy.Debugging
{
    /// <summary>
    /// "-gclog" (tests, P-20): managed garbage per frame, logged every 5 s as the average bytes and allocations a frame
    /// over that window ("GcLog: 5.0 s, 612 frames: 10.4 KB / 138 allocs a frame"). Uses the profiler's GC counters, which
    /// only count in a Development build; elsewhere it logs once that it can't measure.
    /// </summary>
    public class GcLog : MonoBehaviour
    {
        const float Window = 5f;
        ProfilerRecorder bytes, count;
        float from;
        long sumBytes, sumCount;
        int frames;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (System.Array.IndexOf(DebugControls.LaunchArgs, "-gclog") < 0)
                return;
            var go = new GameObject("GcLog") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            go.AddComponent<GcLog>();
        }

        void OnEnable()
        {
            bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count");
            from = Time.realtimeSinceStartup;
            if (!bytes.Valid)
                Debug.Log("GcLog: no GC counter in this build (Development builds only)");
        }

        void OnDisable()
        {
            bytes.Dispose();
            count.Dispose();
        }

        void Update()
        {
            if (!bytes.Valid)
                return;
            // (Last frame's totals: the counters are filled when a frame ends.)
            sumBytes += bytes.LastValue;
            sumCount += count.Valid ? count.LastValue : 0;
            frames++;
            var now = Time.realtimeSinceStartup;
            if (now - from < Window)
                return;
            Debug.Log($"GcLog: {now - from:F1} s, {frames} frames: {sumBytes / 1024f / frames:F2} KB / {(float)sumCount / frames:F1} allocs a frame");
            (from, sumBytes, sumCount, frames) = (now, 0, 0, 0);
        }
    }
}
