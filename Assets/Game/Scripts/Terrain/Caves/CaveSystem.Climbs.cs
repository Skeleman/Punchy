using Unity.Mathematics;

namespace Punchy.Terrain
{
    public partial class CaveSystem
    {
        /// <summary>W-181: hands the cave climbs (CaveWallBuild.Climbs) near the camera to CaveClimbSystem.</summary>
        void GatherClimbs(float3 camPos)
        {
            ref var near = ref CaveClimbRoutes.Near.Data;
            near.Clear();
            CaveClimbRoutes.Frame.Data = UnityEngine.Time.frameCount;
            foreach (var pair in wallBuilds)
                foreach (var l in pair.Value.Climbs)
                {
                    var t = math.saturate(math.dot(camPos - l.Foot, l.Top - l.Foot) / math.max(math.distancesq(l.Top, l.Foot), 1e-4f));
                    if (near.Length < near.Capacity && math.distance(math.lerp(l.Foot, l.Top, t), camPos) < CaveClimbRoutes.Gather)
                        near.Add(l);
                }
        }
    }
}
