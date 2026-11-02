namespace Punchy.UI
{
    /// <summary>
    /// Whether any on-screen panel is open (F2 multiplayer, F5 graphics): the camera leaves the mouse free so its
    /// controls can be clicked, and the player's movement and interaction keys wait.
    /// </summary>
    public static class Overlay
    {
        public static bool AnyOpen => Punchy.Net.NetMenu.IsOpen || Punchy.Settings.GraphicsSettings.PanelOpen;
    }
}
