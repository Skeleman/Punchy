using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>Which body rig <see cref="ColossusRig"/> gives a walking colossus while it is awake (None: it walks unrigged or stands still).</summary>
    public enum ColossusRigType { None, Saguaro, Golem, Tortoise, MesaTortoise, Heron, Stag }

    /// <summary>
    /// A-81 row 1 (#40): one stone colossus as data, in Resources/Colossi. <see cref="StoneColossi"/> loads them all and
    /// gives each primary one a slot of its own (by <see cref="Order"/>); a kept kind (Primary off) only stands when a
    /// "-colossus" flag swaps it into the slot of the kind it <see cref="Replaces"/>. Where a colossus stands comes from
    /// its own salt on the world seed, so adding one moves no other colossus or landmark. Its look is its builder, a
    /// partial file of StoneColossi that registers itself for <see cref="Kind"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Colossus Definition", fileName = "Colossus")]
    public class ColossusDefinition : ScriptableObject
    {
        [Tooltip("Which builder makes it (StoneColossi.Kind; append new kinds, never reorder: assets store the number).")]
        public StoneColossi.Kind Kind;
        [Tooltip("On: it takes a slot of its own. Off: a kept kind, standing only when \"-colossus <Kind>\" swaps it in.")]
        public bool Primary = true;
        [Tooltip("A kept kind: the primary kind whose slot it takes when swapped in.")]
        public StoneColossi.Kind Replaces;
        [Tooltip("Slot order among the primaries (a kept kind uses the slot of the kind it replaces).")]
        public int Order;
        [Tooltip("Biome asset name prefixes it looks for; it stands in the biggest region of them.")]
        public string[] Biomes = new string[0];
        [Tooltip("Its salt on the world seed: picks its spot, facing and (for seeded builders) its shape. Never change a placed one's.")]
        public uint Salt;
        [Tooltip("Radius (m) kept clear of flora round it; 0.7 of it is the slope-check circle.")]
        public float KeepRadius = 34f;
        [Tooltip("Height and half-width (m) of the far shadow it casts past the real shadow distance.")]
        public Vector2 Cast = new Vector2(30f, 14f);
        [Tooltip("The body rig it gets while it walks.")]
        public ColossusRigType Rig;
        [Tooltip("On: flora is kept out of its keep circle (and a walker's loop).")]
        public bool FloraKeepOut = true;
        [Tooltip("Collision: a child (by name) whose sphere follows it as it moves, empty for none.")]
        public string CollisionFollow = "";
        [Tooltip("Collision: the following sphere's radius (m).")]
        public float CollisionFollowRadius;
        [Tooltip("Collision: a child (by name) left out of its solid shape, empty for none.")]
        public string CollisionLeave = "";
        [Tooltip("A-85: keep this far (m) from the colossi placed before it (a biome another kind already stands in); 0 for none.")]
        public float KeepApart;
    }
}
