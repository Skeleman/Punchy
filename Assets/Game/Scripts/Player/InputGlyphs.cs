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
    /// Prompts can show buttons as icons (UI.Prompts): Token wraps a control's path, and Icon finds its picture for the
    /// device in use in Resources/Prompts/&lt;kind&gt;/&lt;control&gt; (kind keyboard, mouse, xbox or playstation; the
    /// control's path after the device, with '/' as '_': buttonSouth, dpad_up, escape), from Kenney's Input Prompts
    /// (CC0). Anything without a picture is named instead. "-forcepad xbox|playstation" shows a pad's prompts without
    /// one (for shots).
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
            if (forcedKind != null)
                return;
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
        public static string PadKind => forcedKind ?? UnityEngine.InputSystem.Gamepad.current switch
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
        public static string Action(string name) => Path(ActionPath(name));

        /// <summary>The path of the control bound to an action on the device in use ("&lt;Keyboard&gt;/e"), or null.</summary>
        public static string ActionPath(string name)
        {
            var action = InputSystem.actions?.FindAction(name);
            if (action == null)
                return null;
            var group = Gamepad ? "Gamepad" : "Keyboard&Mouse";
            foreach (var binding in action.bindings)
            {
                if (binding.isComposite || binding.isPartOfComposite || binding.groups == null || !binding.groups.Contains(group))
                    continue;
                return binding.effectivePath;
            }
            return null;
        }

        /// <summary>A control in a prompt, drawn as its icon or name by UI.Prompts ("[[&lt;Gamepad&gt;/buttonNorth]]"); null for none.</summary>
        public static string Token(string path) => string.IsNullOrEmpty(path) ? null : "[[" + path + "]]";

        /// <summary>The button bound to an action, as a prompt token (see Token), or null when nothing on the device is bound.</summary>
        public static string ActionToken(string name) => Token(ActionPath(name));

        /// <summary>A prompt's text with its tokens as button names (for logs and plain labels).</summary>
        public static string Plain(string prompt)
        {
            if (string.IsNullOrEmpty(prompt))
                return prompt;
            var at = prompt.IndexOf("[[", System.StringComparison.Ordinal);
            while (at >= 0)
            {
                var close = prompt.IndexOf("]]", at + 2, System.StringComparison.Ordinal);
                if (close < 0)
                    break;
                var path = prompt.Substring(at + 2, close - at - 2);
                var name = Path(path) ?? path;
                prompt = prompt.Substring(0, at) + name + prompt.Substring(close + 2);
                at = prompt.IndexOf("[[", at + name.Length, System.StringComparison.Ordinal);
            }
            return prompt;
        }

        static readonly System.Collections.Generic.Dictionary<string, Texture2D> icons = new System.Collections.Generic.Dictionary<string, Texture2D>();

        /// <summary>
        /// A control's icon for the device in use (a keycap on the keyboard, the pad's own symbol), or null where the set
        /// has none (and on a generic pad, whose buttons have no agreed symbols).
        /// </summary>
        public static Texture2D Icon(string path)
        {
            if (string.IsNullOrEmpty(path) || path[0] != '<')
                return null;
            var end = path.IndexOf('>');
            if (end < 0 || end + 2 > path.Length)
                return null;
            var device = path.Substring(1, end - 1);
            var kind = device == "Keyboard" ? "keyboard" : device == "Mouse" ? "mouse" : device == "Gamepad" ? PadKind : null;
            if (kind == null || kind == "generic")
                return null;
            var name = "Prompts/" + kind + "/" + path.Substring(end + 2).Replace('/', '_');
            if (!icons.TryGetValue(name, out var icon))
            {
                icon = Resources.Load<Texture2D>(name);
                icons[name] = icon;
            }
            return icon;
        }

        /// <summary>"-forcepad xbox|playstation": the pad kind shown, as if that pad were in use (null: none forced).</summary>
        static string forcedKind;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ReadCommandLine()
        {
            forcedKind = null;
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, "-forcepad");
            if (at < 0)
                return;
            forcedKind = at + 1 < args.Length && args[at + 1] == "playstation" ? "playstation" : "xbox";
            Gamepad = true;
            Version++;
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
            // (No pad plugged in, as with -forcepad: name the button by the pad kind, not "Button South".)
            if (control == null && path.StartsWith("<Gamepad>/"))
                return PadKind == "generic" ? Generic(path) : Named(path, PadKind == "playstation");
            if (control == null)
                return InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            if (device is Keyboard && control is UnityEngine.InputSystem.Controls.KeyControl key && !string.IsNullOrEmpty(key.displayName))
                return key.displayName;
            if (device is UnityEngine.InputSystem.Gamepad && PadKind == "generic")
                return Generic(path);
            return !string.IsNullOrEmpty(control.shortDisplayName) ? control.shortDisplayName : control.displayName;
        }

        /// <summary>An Xbox or PlayStation pad's buttons by their printed names (when no such pad is plugged in to ask).</summary>
        static string Named(string path, bool playstation) => path.Substring("<Gamepad>/".Length) switch
        {
            "buttonSouth" => playstation ? "Cross" : "A",
            "buttonEast" => playstation ? "Circle" : "B",
            "buttonWest" => playstation ? "Square" : "X",
            "buttonNorth" => playstation ? "Triangle" : "Y",
            "leftShoulder" => playstation ? "L1" : "LB",
            "rightShoulder" => playstation ? "R1" : "RB",
            "leftTrigger" => playstation ? "L2" : "LT",
            "rightTrigger" => playstation ? "R2" : "RT",
            "leftStickPress" => playstation ? "L3" : "LS",
            "rightStickPress" => playstation ? "R3" : "RS",
            "start" => playstation ? "Options" : "Menu",
            "select" => playstation ? "Share" : "View",
            var other => Generic(path)
        };

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
