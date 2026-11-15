using Punchy.Player;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Debugging
{
    public partial class DebugControls
    {
        /// <summary>"-playerdive" (A-102 row 3): the player put in deep water dives, rises and runs out of breath.</summary>
        bool playerDive;
        /// <summary>"-playerdive water": the player's body is made a water breather (PlayerBody.BreathesWater) as it goes
        /// in; "-pickspecies SeaFolk -playerdive" tests a real sea folk body (A-103 row 4).</summary>
        bool pdWater;
        /// <summary>"-playerdive [water] follow" (A-102 row 3b): the player's own camera (ThirdPersonCamera) follows the
        /// dive, no side shot camera, and each second's log says whether its lens is under (Underwater.LensUnder).</summary>
        bool pdFollow;
        float pdLensDeep;
        /// <summary>When the test put the player in (Time.time), and what it saw: the deepest dive in the first crouch,
        /// whether jump brought it back up, when it first gasped, how deep it was when it caught its breath, whether its dive ever
        /// grew while gasping, whether it dived again after catching its breath, the most breath used, and the water there.</summary>
        float pdAt = -1f, pdDeep, pdGaspAt = -1f, pdAfterGasp = -1f, pdUsed, pdLastDive, pdSurface;
        bool pdDone, pdRose, pdGrewGasping, pdDivedAgain, pdNaN;
        float2 pdSpot;
        /// <summary>The script (s after going in): float, crouch, jump, float, then crouch held to the end.</summary>
        const float PdCrouch = 3f, PdJump = 8f, PdRest = 11f, PdLong = 14f, PdEnd = 70f;

        /// <summary>
        /// "-playerdive": once the world has settled, finds water at least 4 m deep within 80 m of the player and puts the
        /// player in it at the swim depth, then drives the input: 5 s of crouch (down), 3 s of jump held (up), a rest, then
        /// crouch held to the end, long enough for an air breather to run out of breath. Logs every second how far down it
        /// is (PlayerMotion.Dive) and its breath; the shot camera watches from the side. PASS for an air breather when it
        /// went down over 1.5 m, came back up on jump, gasped, was pushed up to the surface (its head out, under 0.35 m down)
        /// before it caught its breath, with its dive never growing meanwhile, and went down again once it had caught its breath; for a water breather (a body
        /// with BreathesWater: "-playerdive water" makes the player's one) when it went down over 1.5 m and never used any breath.
        /// </summary>
        void PlayerDiveTest()
        {
            if (!playerDive || pdDone || Time.timeSinceLevelLoad - optionsAt < settle || !EnsureLocations(out var source) ||
                !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var em = queryWorld.EntityManager;
            if (!em.HasComponent<PlayerMotion>(player))
                return;
            var t = em.GetComponentData<LocalTransform>(player);
            if (pdAt < 0f)
            {
                var found = false;
                for (var r = 4f; r <= 80f && !found; r += 4f)
                    for (int k = 0; k < 24 && !found; k++)
                    {
                        var a = k * math.PI * 2f / 24f;
                        var xz = t.Position.xz + new float2(math.sin(a), math.cos(a)) * r;
                        var g = source.SampleGround(xz);
                        if (g.HasWater && g.WaterSurface - g.Height >= 4f)
                        {
                            found = true;
                            pdSpot = xz;
                            pdSurface = g.WaterSurface;
                        }
                    }
                if (!found)
                {
                    Debug.LogWarning($"PlayerDive: no water 4 m deep within 80 m of ({t.Position.x:F0}, {t.Position.z:F0})");
                    pdDone = true;
                    return;
                }
                var motor = em.GetComponentData<PlayerMotor>(player);
                t.Position = new float3(pdSpot.x, pdSurface - motor.SwimDepth, pdSpot.y);
                em.SetComponentData(player, t);
                var m0 = em.GetComponentData<PlayerMotion>(player);
                m0.VerticalVelocity = 0f;
                m0.Dive = 0f;
                m0.BreathUsed = 0f;
                m0.Gasping = false;
                em.SetComponentData(player, m0);
                if (pdWater)
                {
                    using var waterBodies = em.CreateEntityQuery(typeof(PlayerBody));
                    if (waterBodies.TryGetSingletonEntity<PlayerBody>(out var bodyEntity))
                    {
                        var wb = em.GetComponentData<PlayerBody>(bodyEntity);
                        wb.BreathesWater = true;
                        em.SetComponentData(bodyEntity, wb);
                    }
                }
                pdAt = Time.time;
                var g0 = source.SampleGround(pdSpot);
                Debug.Log($"PlayerDive: in at ({pdSpot.x:F1}, {pdSurface:F2}, {pdSpot.y:F1}), {g0.WaterSurface - g0.Height:F1} m deep");
                return;
            }
            var since = Time.time - pdAt;
            var m = em.GetComponentData<PlayerMotion>(player);
            // (The body is a singleton, set by FaunaDirector from the species played.)
            using var bodies = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerBody>());
            bodies.TryGetSingleton<PlayerBody>(out var body);
            // The input for this part of the script.
            PlayerInputSystem.TestCrouch = since >= PdCrouch && since < PdJump || since >= PdLong && since < PdEnd;
            PlayerInputSystem.TestClimb = since >= PdJump && since < PdRest;
            if (!math.isfinite(m.Dive + m.BreathUsed + t.Position.y))
                pdNaN = true;
            if (since < PdJump)
                pdDeep = math.max(pdDeep, m.Dive);
            if (since >= PdJump && since < PdRest && m.Dive < 0.05f)
                pdRose = true;
            pdUsed = math.max(pdUsed, m.BreathUsed);
            if (m.Gasping)
            {
                if (pdGaspAt < 0f)
                    pdGaspAt = since;
                if (m.Dive > pdLastDive + 1e-4f)
                    pdGrewGasping = true;
            }
            else if (pdGaspAt >= 0f)
            {
                // Caught its breath: how far down it was then (its head must have been out to get any back).
                if (pdAfterGasp < 0f)
                    pdAfterGasp = m.Dive;
                if (m.Dive > 0.5f)
                    pdDivedAgain = true;
            }
            pdLastDive = m.Dive;
            // Watching from 6 m off to the side, 2 m over the water.
            var camXZ = pdSpot + new float2(6f, 0f);
            shotCamFrom = new Vector3(camXZ.x, math.max(pdSurface + 2f, source.SampleHeight(camXZ) + 1f), camXZ.y);
            shotCamTo = new Vector3(t.Position.x, t.Position.y + 0.5f, t.Position.z);
            shotCamOn = !pdFollow;
            pdLensDeep = math.max(pdLensDeep, Punchy.CameraRig.Underwater.LensDepth);
            if (Mathf.FloorToInt(since) != Mathf.FloorToInt(since - Time.deltaTime))
                Debug.Log($"PlayerDive {since:F0} s: dive {m.Dive:F2} m, feet {pdSurface - t.Position.y:F2} m under, breath {1f - m.BreathUsed:F2}" +
                          $"{(m.Gasping ? " gasping" : "")}, crouch {PlayerInputSystem.TestCrouch}, jump {PlayerInputSystem.TestClimb}" +
                          $"{(pdFollow ? $", lens under {Punchy.CameraRig.Underwater.LensUnder} ({Punchy.CameraRig.Underwater.LensDepth:F2} m, {Punchy.CameraRig.Underwater.LensWater}), lens y {(Camera.main != null ? Camera.main.transform.position.y - pdSurface : float.NaN):F2} vs surface" : "")}");
            if (since < PdEnd)
                return;
            pdDone = true;
            PlayerInputSystem.TestCrouch = false;
            PlayerInputSystem.TestClimb = false;
            bool pass;
            if (body.BreathesWater)
                pass = pdDeep > 1.5f && pdUsed <= 0f && pdGaspAt < 0f;
            else
                pass = pdDeep > 1.5f && pdRose && pdGaspAt >= 0f && pdAfterGasp >= 0f && pdAfterGasp < 0.35f && !pdGrewGasping && pdDivedAgain;
            pass &= !pdNaN;
            Debug.Log($"PlayerDive: {(pass ? "PASS" : "FAIL")}: {(body.BreathesWater ? "water" : "air")} breather, deepest {pdDeep:F2} m, rose on jump {pdRose}, " +
                      $"most breath used {pdUsed:F2}, gasped at {pdGaspAt:F0} s, dive when it caught its breath {pdAfterGasp:F2} m, grew while gasping {pdGrewGasping}, " +
                      $"dived again {pdDivedAgain}, NaN {pdNaN}{(pdFollow ? $", deepest lens {pdLensDeep:F2} m" : "")}");
        }
    }
}
