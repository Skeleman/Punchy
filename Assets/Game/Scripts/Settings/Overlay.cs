namespace Punchy.UI
{
    /// <summary>
    /// Whether the main menu is open (Esc or Start; its Multiplayer and Graphics tabs): the camera leaves the mouse free
    /// so its controls can be clicked, and the player's movement and interaction keys wait.
    /// </summary>
    public static class Overlay
    {
        public static bool AnyOpen => MainMenu.Open || Talking;

        /// <summary>A talk with a folk has the input (P-26), this frame or the frame it ended.</summary>
        public static bool Talking => UnityEngine.Time.frameCount <= talkFrame;
        static int talkFrame = -1;

        /// <summary>Called each frame of a talk: holds the input through this frame and the next.</summary>
        public static void HoldForTalk() => talkFrame = UnityEngine.Time.frameCount + 1;
    }
}
