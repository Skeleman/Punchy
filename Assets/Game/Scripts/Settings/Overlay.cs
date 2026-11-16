namespace Punchy.UI
{
    /// <summary>
    /// What has the input. AnyOpen: a menu or a talk has the buttons, so the game's own button actions (jump, roll, use,
    /// interact, emotes, attacks, prompts) wait. Frozen: the main menu (Esc or Start) or a talk also stops the walk and
    /// frees the mouse. The in-world menu (FieldMenu: inventory) holds the buttons but not the walk (P-39, #397: the left
    /// stick and WASD still move the player while the d-pad and arrows drive the menu).
    /// </summary>
    public static class Overlay
    {
        public static bool AnyOpen => Frozen || FieldMenu;

        /// <summary>The main menu or a talk: the player stands still and the mouse is free.</summary>
        public static bool Frozen => MainMenu.Open || Talking;

        /// <summary>The in-world menu is up, this frame or the frame it closed (so its B or A doesn't roll or jump).</summary>
        public static bool FieldMenu => UnityEngine.Time.frameCount <= fieldFrame;
        static int fieldFrame = -1;

        /// <summary>Called each frame the in-world menu is up.</summary>
        public static void HoldForFieldMenu() => fieldFrame = UnityEngine.Time.frameCount + 1;

        /// <summary>A talk with a folk has the input (P-26), this frame or the frame it ended.</summary>
        public static bool Talking => UnityEngine.Time.frameCount <= talkFrame;
        static int talkFrame = -1;

        /// <summary>Called each frame of a talk: holds the input through this frame and the next.</summary>
        public static void HoldForTalk() => talkFrame = UnityEngine.Time.frameCount + 1;
    }
}
