using Chem;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

namespace Punchy.Chemistry
{
    /// <summary>
    /// The wizard's hands on a pad (D16 in Spell Chemistry Valley: one way to cast, hands with a charge), driven through the
    /// core's own hand logic (Chem.World's HandDown / HandUp / Attune commands and the rules' hand table in hold.json), the
    /// same call sequence as that game's SpellCaster.Hands. LT / LB are the left hand's thrust / throw, RT / RB the right's;
    /// a hand's trigger and bumper together (inside the table's window) are its sweep. A button held charges in the core,
    /// and its release sets the hand off at the charge it reached (a tap is charge 1); both hands making the same gesture
    /// merge into their pair's spell (fire + water: Scalding Steam). What the core sets off is aimed at the ground under the
    /// screen centre, pulled back to the gesture's range, and cast into the window like the keys' spells. Holding d-pad up
    /// opens an element radial: the right stick picks, and letting go attunes the hand whose trigger is held (else the
    /// left; one element attuned is on both hands). The pad rumbles as a hand charges, at each level and as it goes off.
    /// The actions are the bridge's own map, so Punchy's InputSystem_Actions is untouched. Every hand command is a request on
    /// the session's combos channel (ChemistryBridge.Lockstep.cs): the hands' state is the shared world's, so it lands on
    /// the same tick on every peer, and only this peer aims what its own wizard (Me) sets off.
    /// </summary>
    public partial class ChemistryBridge
    {
        // How long d-pad up is held before the radial opens (shorter is a tap: Punchy's wave).
        public const float RadialHold = 0.25f;
        const float StickPick = 0.5f;

        InputActionMap padMap;
        InputAction thrustLeft, throwLeft, thrustRight, throwRight, attune, pick;
        // The hands whose HandDown went in while their buttons are still down: each sends its HandUp as they let go.
        readonly bool[] handHeld = new bool[HandsDef.Count];
        readonly FiredCastsHeard heard = new FiredCastsHeard();

        float attuneDownAt = -1f;
        bool radialOpen;
        int radialPick = -1;

        // The rumble: a hum while a hand charges, a pulse at each level, a thump as a hand goes off.
        int rumbleLevel;
        float pulseUntil, pulseLow, pulseHigh;
        Vector2 motors;

        // The HUD's merge flash: the merged spell, from when both hands made it, and the last one fired.
        string mergeShown, lastMerge;
        float mergeFlashAt = float.NegativeInfinity;

        /// <summary>For tests: where a hand's spell is aimed and cast from (null: the screen centre and the player).</summary>
        public Vector3? TestAim { get; set; }
        public Vector3? TestCaster { get; set; }
        /// <summary>The last hand spell cast (spell id, charge, cell, by both hands), and how many so far.</summary>
        public string LastHandSpell { get; private set; }
        public int LastHandCharge { get; private set; }
        public Vector2Int LastHandCell { get; private set; }
        public bool LastHandMerged { get; private set; }
        public int HandCasts { get; private set; }
        /// <summary>The element radial is open (the camera leaves the right stick to it).</summary>
        public static bool RadialOpen => Instance != null && Instance.radialOpen;
        /// <summary>D-pad up is down for the radial (Punchy's wave waits for a tap).</summary>
        public static bool RadialPending => Instance != null && Instance.enabled && Instance.attuneDownAt >= 0f;
        public World Core => world;

        void StartHands()
        {
            padMap = new InputActionMap("ChemistryHands");
            thrustLeft = padMap.AddAction("ThrustLeft", InputActionType.Button, "<Gamepad>/leftTrigger");
            throwLeft = padMap.AddAction("ThrowLeft", InputActionType.Button, "<Gamepad>/leftShoulder");
            thrustRight = padMap.AddAction("ThrustRight", InputActionType.Button, "<Gamepad>/rightTrigger");
            throwRight = padMap.AddAction("ThrowRight", InputActionType.Button, "<Gamepad>/rightShoulder");
            attune = padMap.AddAction("Attune", InputActionType.Button, "<Gamepad>/dpad/up");
            pick = padMap.AddAction("Pick", InputActionType.Value, "<Gamepad>/rightStick");
            pick.expectedControlType = "Vector2";
            padMap.Enable();
            Punchy.UI.GuiHook.Add(gameObject, DrawHud, HudWanted, false);
        }

        void StopHands()
        {
            padMap?.Disable();
            radialOpen = false;
            attuneDownAt = -1f;
            SetMotors(Vector2.zero);
        }

        void NewHandsWorld()
        {
            System.Array.Clear(handHeld, 0, handHeld.Length);
            heard.StartFrom(world);
            mergeShown = lastMerge = null;
        }

        // Asked of the host, so taken or not is known a tick or so later: what the core won't take does nothing there.
        bool Send(ComboCommand command)
        {
            session.Request(combos, command);
            return true;
        }

        void Hands(TerrainSource terrain, float3 playerPos)
        {
            if (padMap == null || !world.HandsOn(Me))
                return;
            // A menu, a talk, the inventory or a text field has the pad: the hands let go and take nothing new.
            bool off = Punchy.UI.Overlay.AnyOpen || Punchy.Items.Items.PanelOpen || GUIUtility.keyboardControl != 0;
            Radial(off);
            bool still = off || radialOpen;
            // Read from the buttons, not their release this frame: whatever turns the hands off lets go too, or a hand would
            // charge for ever. Before the presses, so a tap inside one frame lets go on the next.
            for (int hand = 0; hand < HandsDef.Count; hand++)
                if (handHeld[hand] && (still || !ButtonDown(hand)))
                {
                    handHeld[hand] = false;
                    // (Always, though a merge may have let this hand go in the core already: its HandDown may not have
                    // landed yet either, and a HandUp the core doesn't take does nothing.)
                    Send(ComboCommand.HandUp(Me, hand));
                }
            if (!still)
            {
                Press(HandsDef.Right, thrustRight.WasPressedThisFrame(), throwRight.WasPressedThisFrame());
                Press(HandsDef.Left, thrustLeft.WasPressedThisFrame(), throwLeft.WasPressedThisFrame());
            }
            var merge = world.CastOf(Me)?.Merge;
            if (merge != null && merge != lastMerge)
            {
                mergeFlashAt = Time.unscaledTime;
                mergeShown = merge;
                Debug.Log($"[Chemistry] hands: merged into {rules.Spell(merge)?.Name ?? merge}");
            }
            lastMerge = merge;
            foreach (var fired in heard.Take(world))
                if (fired.Player == Me)
                    FromHand(fired, terrain, playerPos);
            Rumble();
        }

        // A hand's buttons pressed this frame: the thrust, the throw, or the throw with the thrust down, its sweep (the core
        // makes a sweep of either order inside the window).
        void Press(int hand, bool thrust, bool thrown)
        {
            if (thrust && Send(ComboCommand.HandDown(Me, hand)))
                handHeld[hand] = true;
            if (thrown && Send(ComboCommand.HandDown(Me, hand, bumper: true)))
                handHeld[hand] = true;
        }

        bool ButtonDown(int hand) => hand == HandsDef.Left
            ? thrustLeft.IsPressed() || throwLeft.IsPressed()
            : thrustRight.IsPressed() || throwRight.IsPressed();

        // ------------------------------------------------------------------------------------------------ attuning

        void Radial(bool off)
        {
            if (off)
            {
                attuneDownAt = -1f;
                radialOpen = false;
                return;
            }
            if (attune.WasPressedThisFrame())
            {
                attuneDownAt = Time.unscaledTime;
                radialPick = -1;
            }
            if (attuneDownAt < 0f)
                return;
            if (!radialOpen && attune.IsPressed() && Time.unscaledTime - attuneDownAt >= RadialHold)
                radialOpen = true;
            if (radialOpen)
            {
                var stick = pick.ReadValue<Vector2>();
                if (stick.magnitude > StickPick)
                    radialPick = PickOf(stick, rules.Combos.Elements.Count);
            }
            if (attune.IsPressed())
                return;
            if (radialOpen && radialPick >= 0)
                AttuneTo(RadialHand(), rules.Combos.Elements[radialPick]);
            attuneDownAt = -1f;
            radialOpen = false;
        }

        // The radial's elements go round clockwise from the top.
        static int PickOf(Vector2 stick, int count)
        {
            var angle = Mathf.Atan2(stick.x, stick.y) * Mathf.Rad2Deg;
            if (angle < 0f)
                angle += 360f;
            return Mathf.RoundToInt(angle / (360f / count)) % count;
        }

        /// <summary>The hand the radial attunes: the one whose trigger is held, else the left.</summary>
        int RadialHand() => thrustRight.IsPressed() && !thrustLeft.IsPressed() ? HandsDef.Right : HandsDef.Left;

        /// <summary>Attunes a hand to an element (it takes the rules' short moment); null if it went in, else why not.</summary>
        public string AttuneTo(int hand, string element)
        {
            if (world == null)
                return "no chemistry window yet";
            var h = world.HandsOf(Me);
            string why = null;
            if (h.Holds[hand] == element)
                return null;
            if (h.AttuneLeft > 0)
                why = "one hand at a time";
            else if (world.CastOf(Me)?.Casting == true || h.Casting)
                why = "not mid-cast";
            else if (!Send(ComboCommand.Attune(Me, hand, element)))
                why = "the hands can't attune now";
            Debug.Log(why == null ? $"[Chemistry] hands: attuning the {Side(hand)} hand to {element}" : $"[Chemistry] hands: can't attune the {Side(hand)} hand: {why}");
            return why;
        }

        static string Side(int hand) => hand == HandsDef.Left ? "left" : "right";

        // ------------------------------------------------------------------------------------------------ casting

        // What the core set off: a spell aimed at the ground under the screen centre, pulled back to its gesture's range,
        // or an empty hand's act (nothing in this window to punch or shove).
        void FromHand(ComboFired fired, TerrainSource terrain, float3 playerPos)
        {
            pulseUntil = Time.unscaledTime + 0.14f;
            (pulseLow, pulseHigh) = (0.55f, 0.35f);
            if (fired.Spell == null)
            {
                Debug.Log($"[Chemistry] hands: {fired.Act} (an empty hand's act: nothing here takes it)");
                return;
            }
            var from = TestCaster.HasValue ? (float3)TestCaster.Value : playerPos;
            var cam = Camera.main;
            float3 hit;
            if (TestAim.HasValue)
                hit = TestAim.Value;
            else if (cam == null || !Aim(terrain, cam.transform.position, cam.transform.forward, out hit))
            {
                var ahead = cam != null ? (float3)cam.transform.forward : new float3(0, 0, 1);
                hit = from + math.normalizesafe(new float3(ahead.x, 0, ahead.z), new float3(0, 0, 1)) * 4f;
            }
            CastHand(fired, from, hit);
        }

        // As SpellCaster.Aim in Spell Chemistry Valley: a line or a cone points at the aim (off the grid's four ways if it
        // is) and starts beside the caster; a burst from the caster is on the caster; anything else lands on the target.
        void CastHand(ComboFired fired, float3 from, float3 hit)
        {
            var s = rules.Spell(fired.Spell);
            if (s == null)
                return;
            int mx = (int)math.floor(from.x) - corner.x, my = (int)math.floor(from.z) - corner.y;
            int hx = (int)math.floor(hit.x) - corner.x, hy = (int)math.floor(hit.z) - corner.y;
            var (tx, ty) = HandsDef.Within(mx, my, hx, hy, fired.Range);
            int aimX = 0, aimY = 0;
            if (s.Shape == "line" || s.Shape == "cone")
            {
                var d = hit - from;
                (aimX, aimY) = World.SnapAim((int)math.round(d.x * AimSteps), (int)math.round(d.z * AimSteps));
            }
            int? dir = aimX != 0 || aimY != 0 ? World.DirOf(aimX, aimY) : DirFrom(mx, my, tx, ty);
            int x = tx, y = ty;
            if (s.Origin == "self")
            {
                if (s.Shape == "burst")
                    (x, y) = (mx, my);
                else
                {
                    var (dx, dy) = Step(dir ?? 2);
                    (x, y) = (mx + dx, my + dy);
                }
            }
            if (x < 0 || y < 0 || x >= Size || y >= Size)
            {
                Debug.Log($"[Chemistry] hands: {s.Name} at cell ({x}, {y}) is outside the chemistry window");
                return;
            }
            bool merged = fired.Name != null && rules.Sim.Hold.Hands.Find(fired.Spell).slot?.Contains('+') == true;
            if (merged)
                (mergeFlashAt, mergeShown) = (Time.unscaledTime, fired.Spell);
            session.Request(casts, new CastCommand(fired.Spell, x, y, dir ?? 2, fired.Charge, aimX, aimY, Me));
            LastHandSpell = fired.Spell;
            LastHandCharge = fired.Charge;
            LastHandCell = new Vector2Int(x, y);
            LastHandMerged = merged;
            HandCasts++;
            Debug.Log($"[Chemistry] hands: {(merged ? "both hands" : "a hand")} cast {s.Name} (charge {fired.Charge}, range {fired.Range}) at cell ({x}, {y}) " +
                      $"seeded {KindName(seeded[y * Size + x])}, aimed ({hx}, {hy}): asked for (tick {session.NetTick})");
        }

        // Fine steps per cell of an aim's vector (World.SnapAim's).
        const int AimSteps = 16;

        // 0 = y-1, 1 = x+1, 2 = y+1, 3 = x-1 (the core's directions; the window's y is Punchy's z).
        static int? DirFrom(int ax, int ay, int bx, int by)
        {
            int dx = bx - ax, dy = by - ay;
            if (dx == 0 && dy == 0)
                return null;
            if (math.abs(dx) >= math.abs(dy))
                return dx > 0 ? 1 : 3;
            return dy > 0 ? 2 : 0;
        }

        static (int, int) Step(int dir) => dir switch { 0 => (0, -1), 1 => (1, 0), 2 => (0, 1), _ => (-1, 0) };

        /// <summary>The charge the hands have built: the most of a hand held, or merged, the lower of the two.</summary>
        public int HeldCharge()
        {
            var h = world.HandsOf(Me);
            int left = world.HandCharge(Me, HandsDef.Left), right = world.HandCharge(Me, HandsDef.Right);
            if (world.CastOf(Me)?.Merge != null)
                return math.max(1, math.min(left, right));
            int most = 1;
            if (h.Down[HandsDef.Left])
                most = math.max(most, left);
            if (h.Down[HandsDef.Right])
                most = math.max(most, right);
            return most;
        }

        // ------------------------------------------------------------------------------------------------ rumble

        void Rumble()
        {
            var h = world.HandsOf(Me);
            bool charging = h.Down[HandsDef.Left] || h.Down[HandsDef.Right];
            int level = charging ? HeldCharge() : 0;
            if (level > rumbleLevel && rumbleLevel > 0)
            {
                pulseUntil = Time.unscaledTime + 0.09f;
                (pulseLow, pulseHigh) = (0.15f, 0.6f);
            }
            rumbleLevel = level;
            var want = Time.unscaledTime < pulseUntil ? new Vector2(pulseLow, pulseHigh)
                     : charging ? new Vector2(0.04f + 0.05f * level, 0.02f * level) : Vector2.zero;
            SetMotors(want);
        }

        void SetMotors(Vector2 speeds)
        {
            if (speeds == motors)
                return;
            motors = speeds;
            Gamepad.current?.SetMotorSpeeds(speeds.x, speeds.y);
        }
    }
}
