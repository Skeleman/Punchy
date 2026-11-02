namespace Punchy.UI
{
    /// <summary>
    /// Whether the main menu is open (Esc or Start; its Multiplayer and Graphics tabs): the camera leaves the mouse free
    /// so its controls can be clicked, and the player's movement and interaction keys wait.
    /// </summary>
    public static class Overlay
    {
        public static bool AnyOpen => MainMenu.Open;
    }
}
