using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    public partial struct CreatureBrainSystem
    {
        // BrainJob, part: townsfolk and animals (A-120 row 2). A household pet's own system (HouseholdPetSystem) picks who
        // goes over to it and does the petting to the pet; this part walks the folk there, crouches and strokes, and
        // stands again. Players wearing an animal's body are noticed here, at the folk's own decision moments.
        partial struct BrainJob
        {
            const byte PetGo = CreatureAnimator.PetGo, PetCrouch = CreatureAnimator.PetCrouch, PetShoo = CreatureAnimator.PetShoo,
                       PetFetch = CreatureAnimator.PetFetch;
            /// <summary>How far off a player in an animal's body is noticed (m), and how far they may go before the folk gives up.</summary>
            const float PlayerPetNotice = 6f, PlayerPetLose = 4f;
            /// <summary>Where the folk stops to crouch, from the animal's middle (m).</summary>
            const float PetReach = 0.75f;

            /// <summary>
            /// At one of its decision moments (Idling, timer out): a player near it in an animal's body may be gone over to
            /// and petted, or shooed off, by its Beasts trait (FolkPersona.BeastsOf). True when it took one up.
            /// </summary>
            bool NoticePlayerAnimal(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                if (!b.Upright || !b.Hands || b.Haunts || s.Leash <= 0f || s.ChatRole != 0 || s.Held == HeldItem.Letter)
                    return false;
                var best = -1;
                var bestD = PlayerPetNotice * PlayerPetNotice;
                for (int p = 0; p < Players.Length; p++)
                {
                    var d = math.distancesq(Players[p].xz, s.Position.xz);
                    if (d < bestD && p < PlayerSpecies.Length && !Interactions.CanHold(Carry, PlayerSpecies[p]))
                    {
                        bestD = d;
                        best = p;
                    }
                }
                if (best < 0)
                    return false;
                var beasts = FolkPersona.BeastsOf(s.Individual);
                var shoo = FolkPersona.Band(beasts) == 0;
                var chance = shoo ? 0.5f * (1f - 3f * beasts) : 0.5f * beasts * beasts;
                if (rng.NextFloat() >= chance)
                    return false;
                SetMode(ref s, CreatureMode.Idle);
                s.Stage = shoo ? PetShoo : PetGo;
                s.Focus = Players[best].xz;
                s.Timer = shoo ? 2.5f : 15f;
                return true;
            }

            /// <summary>
            /// Idle stages PetGo, PetCrouch and PetShoo: over to the animal (Partner, or Focus for a player in an animal's
            /// body), crouched stroking it while it's being petted (Holder: this folk's key), or flapping it away. Stands
            /// and goes on when the animal is done, walks off, or its time runs out. PetFetch (row 3): a child over to its
            /// own pet, to wait by it to be picked up. True while it's walking.
            /// </summary>
            bool Petting(Entity self, ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt)
            {
                s.Timer -= dt;
                float3 at;
                var animal = s.Partner != Entity.Null;
                var o = default(CreatureState);
                if (animal)
                {
                    if (!Lookup.TryGetValue(s.Partner, out var j) || (o = States[j]).Gone)
                    {
                        EndPetting(ref s, ref rng);
                        return false;
                    }
                    at = o.Position;
                }
                else if (!NearestPlayer(s.Focus, out at))
                {
                    EndPetting(ref s, ref rng);
                    return false;
                }
                else
                    s.Focus = at.xz;
                var to = at.xz - s.Position.xz;
                var gap = math.length(to);
                if ((s.Stage == PetGo || s.Stage == PetFetch) && s.Timer > 0f)
                {
                    M.LookKind = 2;
                    M.LookAt = at;
                    s.LookFor = math.max(s.LookFor, 0.3f);
                    if (gap > PetReach + 0.2f)
                    {
                        // (At its own walk: the caller sets the speed. Capping s.Speed here kept it from ever getting going.)
                        s.Target = at.xz - to / math.max(gap, 0.01f) * PetReach;
                        return true;
                    }
                }
                if (s.Stage == PetGo && s.Timer > 0f)
                {
                    // (Down to it: the crouch and the stroke come up together over the action's first moment, CreatureAnimator.)
                    s.Stage = PetCrouch;
                    s.StageTime = 0f;
                    s.Action = IdleAction.Pet;
                    s.ActionTime = 0f;
                    s.ActionLength = animal ? 10f : 7f;
                    s.Timer = s.ActionLength + 1f;
                }
                s.Speed = 0f;
                if (gap > 0.01f)
                    s.Yaw = WrapAngle(s.Yaw + math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -b.TurnRate * dt, b.TurnRate * dt));
                // (Row 3: a child by its own pet stands facing it till HouseholdPetSystem picks it up into its arms and
                // takes the child home, or lets it be.)
                if (s.Stage == PetShoo || s.Stage == PetFetch)
                {
                    if (s.Timer <= 0f)
                        EndPetting(ref s, ref rng);
                    return false;
                }
                if (s.Stage != PetCrouch || s.Timer <= 0f || s.Action != IdleAction.Pet)
                {
                    EndPetting(ref s, ref rng);
                    return false;
                }
                // The animal done with it (or never let it: walked off), or the player gone on: up again, easing out of
                // the crouch over a moment rather than snapping up (the action's tail, CreatureAnimator.ActionEnvelope).
                var done = animal ? s.StageTime > 2.5f && !(o.Mode == CreatureMode.Petted && o.Holder == FolkKey(self))
                                  : gap > PlayerPetLose;
                if (done && s.ActionTime >= 0.12f && s.ActionTime < 0.85f)
                {
                    s.ActionTime = 0.85f;
                    s.ActionLength = 6f;
                }
                return false;
            }

            /// <summary>Back to standing about, to decide on something else in a moment.</summary>
            void EndPetting(ref CreatureState s, ref Random rng)
            {
                SetMode(ref s, CreatureMode.Idle);
                s.Timer = rng.NextFloat(1f, 3f);
            }

            /// <summary>The player nearest a spot, within PlayerPetLose of it (a player in an animal's body being petted).</summary>
            bool NearestPlayer(float2 near, out float3 at)
            {
                at = default;
                var bestD = PlayerPetLose * PlayerPetLose;
                var found = false;
                for (int p = 0; p < Players.Length; p++)
                {
                    var d = math.distancesq(Players[p].xz, near);
                    if (d < bestD)
                    {
                        bestD = d;
                        at = Players[p];
                        found = true;
                    }
                }
                return found;
            }
        }
    }
}
