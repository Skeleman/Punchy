using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.XInput;

namespace Punchy.Player
{
    /// <summary>
    /// Which input device the player last used (the keyboard and mouse, or a gamepad), and what its buttons are called,
    /// so every prompt names the button they'd actually press: read from the actions' own bindings (so they stay right
    /// if a binding changes), in the gamepad's own names where it says what it is (A/B/X/Y on an Xbox pad, Cross/
    /// Circle/Square/Triangle on a PlayStation one), generic otherwise. Switches only on real use (an idle gamepad
    /// still sends updates), and not back within a moment of switching, so prompts don't flicker.
    /// </summary>
    public static class InputGlyphs
    {
        /// <summary>The player last used a gamepad (rather than the keyboard and mouse).</summary>
        public static bool Gamepad { get; private set; }

        /// <summary>Bumped on every switch, so cached prompt text knows to rebuild.</summary>
        public static int Version { get; private set; }

        const float Hysteresis = 0.4f;
        static float switchedAt = -10f;
        static int updatedFrame = -1;

        /// <summary>Notes which device was used this frame (once a frame, whoever calls it first).</summary>
        public static void Update()
        {
            if (updatedFrame == Time.frameCount)
                return;
            updatedFrame = Time.frameCount;
            if (Time.unscaledTime - switchedAt < Hysteresis)
                return;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            var keys = Keyboard.current;
            var mouse = Mouse.current;
            var padUsed = pad != null && (pad.leftStick.ReadValue().sqrMagnitude > 0.2f || pad.rightStick.ReadValue().sqrMagnitude > 0.2f ||
                                          pad.dpad.ReadValue().sqrMagnitude > 0.5f || AnyButton(pad));
            var keysUsed = keys != null && keys.anyKey.wasPressedThisFrame ||
                           mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.delta.ReadValue().sqrMagnitude > 25f);
            if (padUsed && !Gamepad)
                Switch(true);
            else if (keysUsed && !padUsed && Gamepad)
                Switch(false);
        }

        static bool AnyButton(UnityEngine.InputSystem.Gamepad pad)
        {
            foreach (var c in pad.allControls)
                if (c is UnityEngine.InputSystem.Controls.ButtonControl b && !b.synthetic && b.wasPressedThisFrame)
                    return true;
            return false;
        }

        static void Switch(bool gamepad)
        {
            Gamepad = gamepad;
            switchedAt = Time.unscaledTime;
            Version++;
        }

        /// <summary>Tests: as if the player had just used a gamepad (true) or the keyboard (false).</summary>
        public static void Force(bool gamepad) { if (gamepad != Gamepad) Switch(gamepad); }

        /// <summary>The kind of gamepad, for naming its buttons: "xbox", "playstation", or "generic".</summary>
        public static string PadKind => UnityEngine.InputSystem.Gamepad.current switch
        {
            DualShockGamepad => "playstation",
            XInputController => "xbox",
            null => "generic",
            _ => "generic"
        };

        /// <summary>
        /// The button bound to an action ("Player/Interact") on the device in use: "E", "Left Shift", "Y", "Triangle",
        /// "LS"... (the first binding for that device), or null when nothing on it is bound.
        /// </summary>
        public static string Action(string name)
        {
            var action = InputSystem.actions?.FindAction(name);
            if (action == null)
                return null;
            var group = Gamepad ? "Gamepad" : "Keyboard&Mouse";
            foreach (var binding in action.bindings)
            {
                if (binding.isComposite || binding.isPartOfComposite || binding.groups == null || !binding.groups.Contains(group))
                    continue;
                return Path(binding.effectivePath);
            }
            return null;
        }

        /// <summary>
        /// A keyboard key or gamepad control by its path, whichever the player is using: the keyboard's when on the
        /// keyboard, the gamepad's (null for none) on a gamepad.
        /// </summary>
        public static string Either(string keyboardPath, string gamepadPath) => Path(Gamepad ? gamepadPath : keyboardPath);

        /// <summary>One control's name, as the device in use calls it ("Left Shift", "Cross", "A"...).</summary>
        public static string Path(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            InputDevice device = path.StartsWith("<Gamepad>") ? UnityEngine.InputSystem.Gamepad.current :
                                 path.StartsWith("<Keyboard>") ? Keyboard.current : path.StartsWith("<Mouse>") ? Mouse.current : null;
            var control = device != null ? InputControlPath.TryFindControl(device, path) : null;
            if (control == null)
                return InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            if (device is Keyboard && control is UnityEngine.InputSystem.Controls.KeyControl key && !string.IsNullOrEmpty(key.displayName))
                return key.displayName;
            if (device is UnityEngine.InputSystem.Gamepad && PadKind == "generic")
                return Generic(path);
            return !string.IsNullOrEmpty(control.shortDisplayName) ? control.shortDisplayName : control.displayName;
        }

        /// <summary>A generic gamepad's buttons by where they are.</summary>
        static string Generic(string path) => path.Substring("<Gamepad>/".Length) switch
        {
            "buttonSouth" => "bottom button",
            "buttonEast" => "right button",
            "buttonWest" => "left button",
            "buttonNorth" => "top button",
            "leftStickPress" => "left stick click",
            "rightStickPress" => "right stick click",
            "dpad/up" => "d-pad up",
            "dpad/down" => "d-pad down",
            "dpad/left" => "d-pad left",
            "dpad/right" => "d-pad right",
            "leftShoulder" => "LB",
            "rightShoulder" => "RB",
            "leftTrigger" => "LT",
            "rightTrigger" => "RT",
            "start" => "Start",
            "select" => "Select",
            var other => other
        };
    }
}
