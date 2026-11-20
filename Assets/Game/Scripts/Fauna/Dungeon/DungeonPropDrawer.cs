using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>#24 phase 2 row 4: draws the dungeon props once a frame, late, after the camera follows the player
    /// (<see cref="DungeonProps.Draw"/>). One hidden object, made with the first prop.</summary>
    public sealed class DungeonPropDrawer : MonoBehaviour
    {
        void LateUpdate() => DungeonProps.Draw();
    }
}
