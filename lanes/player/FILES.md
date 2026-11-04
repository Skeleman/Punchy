# Player lane: key files

| Area | Files | Notes |
|---|---|---|
| Settings | `Assets/Game/Scripts/Settings/GraphicsSettings.cs`, `FrameRateCap.cs`, `Overlay.cs` | Owned by this lane |
| Menus | `Assets/Game/Scripts/UI/MainMenu.cs`, `MenuNav.cs` | Owned by this lane |
| Camera | `Assets/Game/Scripts/Camera/ThirdPersonCamera.cs` | Owned by this lane from 11-03 (watch mode came from the animal lane: keep it) |
| Net menu | `Assets/Game/Scripts/Net/NetMenu.cs` | Shared: tell the supervisor before changing net tables |
| Debug flags | `Scripts/Debugging/DebugControls.cs` | Animal lane owns; additive only, tell the supervisor |
| Player movement | `Scripts/Player/PlayerMovementSystem.cs` | Lent to the world lane (W-67); don't edit |

Read `docs/agents/multiplayer.md` before anything other players see.
