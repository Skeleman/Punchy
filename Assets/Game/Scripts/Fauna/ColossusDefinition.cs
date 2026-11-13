using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>Which body rig <see cref="ColossusRig"/> gives a walking colossus while it is awake (None: it walks unrigged or stands still).</summary>
    public enum ColossusRigType { None, Saguaro, Golem, Tortoise, MesaTortoise, Heron, Stag }

    /// <summary>A-107: how a climbable section of a colossus looks (its details: ColossusClimbDetails). Append only.</summary>
    public enum ClimbLook { Cracks, Vines, Ledges, Fur, Moss }

    /// <summary>
    /// A-107 (#276): one climbable section of a colossus, graded on the shared climbability scale (Climbing): a wedge of
    /// its body seen from above, from its middle out past its edge, Yaw degrees round from the way it faces (90 to its
    /// right) and Arc degrees wide, at least a column wide all the way in, so every section reaches its top.
    /// </summary>
    [System.Serializable]
    public struct ColossusClimbSection
    {
        public ClimbLook Look;
        [Tooltip("Climbing grade (Climbing: 2 assisted, fur or moss; 3 tree-like, vines or ledges; 4 craggy, cracked stone).")]
        public int Grade;
        [Tooltip("Degrees round from the colossus's forward, clockwise from above (90: its right flank, 180: its rear).")]
        public float Yaw;
        [Tooltip("The wedge's width (degrees).")]
        public float Arc;
    }

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
        [Tooltip("Collision: children (by name, comma-separated) left out of its solid shape, empty for none.")]
        public string CollisionLeave = "";
        [Tooltip("A-85: keep this far (m) from the colossi placed before it (a biome another kind already stands in); 0 for none.")]
        public float KeepApart;
        [Tooltip("A-88: the name of a landmark it lives on (its middle); its biomes are the fallback when the world has none. Empty for none.")]
        public string Landmark = "";
        [Tooltip("A-107: its climbable sections (the rest of it is a smooth wall, grade 5). Empty: nothing on it to climb.")]
        public ColossusClimbSection[] ClimbSections = new ColossusClimbSection[0];
    }
}
