using System.Collections.Generic;
using Unity.Entities;

namespace Punchy.Net
{
    /// <summary>
    /// Entity queries made once per world and kept, for MonoBehaviours that look something up every frame (making a
    /// query each time allocates and searches the world's query cache; kind-possum's audit). The world owns them and
    /// disposes them with itself; a new world (hosting, joining, going back to solo) gets new ones.
    /// </summary>
    public static class CachedQueries
    {
        static class Of<T>
        {
            public static readonly Dictionary<World, EntityQuery> Queries = new Dictionary<World, EntityQuery>();
        }

        static class Of<T1, T2>
        {
            public static readonly Dictionary<World, EntityQuery> Queries = new Dictionary<World, EntityQuery>();
        }

        /// <summary>The query for read-only <typeparamref name="T"/> in <paramref name="world"/>.</summary>
        public static EntityQuery Get<T>(World world) =>
            Find(Of<T>.Queries, world, out var query) ? query
                : Keep(Of<T>.Queries, world, world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<T>()));

        /// <summary>The query for read-only <typeparamref name="T1"/> and <typeparamref name="T2"/> in <paramref name="world"/>.</summary>
        public static EntityQuery Get<T1, T2>(World world) =>
            Find(Of<T1, T2>.Queries, world, out var query) ? query
                : Keep(Of<T1, T2>.Queries, world, world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<T1>(), ComponentType.ReadOnly<T2>()));

        static bool Find(Dictionary<World, EntityQuery> queries, World world, out EntityQuery query) => queries.TryGetValue(world, out query);

        static EntityQuery Keep(Dictionary<World, EntityQuery> queries, World world, EntityQuery query)
        {
            // Drop the queries of worlds that are gone (their worlds disposed them already).
            if (queries.Count >= 4)
            {
                var gone = new List<World>();
                foreach (var w in queries.Keys)
                    if (!w.IsCreated)
                        gone.Add(w);
                foreach (var w in gone)
                    queries.Remove(w);
            }
            queries[world] = query;
            return query;
        }
    }
}
