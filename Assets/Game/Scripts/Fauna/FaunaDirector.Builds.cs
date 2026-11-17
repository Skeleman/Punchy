using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-124 (Zach, 11-14): every creature its own build, so a crowd of one race doesn't read as copies.
    public partial class FaunaDirector
    {
        /// <summary>What a species' blob was baked from (Build), kept so its builds bake from the same.</summary>
        struct BlobInputs
        {
            public List<int> Prey;
            public bool Hunted;
            public float[] Regard;
            public byte[] Kinship;
            public Vector2? Home;
        }

        readonly List<BlobInputs> blobInputs = new List<BlobInputs>();
        /// <summary>Per species, its <see cref="BodyBuilds.Count"/> build blobs, each baked the first time a creature
        /// needs it (so a species never seen costs nothing). Kept apart from <c>blobs</c>, which stay the species' own
        /// and alone make the fauna fingerprint.</summary>
        readonly List<BlobAssetReference<CreatureBlob>[]> buildBlobs = new List<BlobAssetReference<CreatureBlob>[]>();

        /// <summary>Builds made so far this session (for the fauna log and tests).</summary>
        public int BuildBlobCount { get; private set; }

        /// <summary>A species' body in one of its builds; its own blob when its range is empty.</summary>
        public BlobAssetReference<CreatureBlob> BuildBlob(int speciesIndex, int build)
        {
            if (speciesIndex < 0 || speciesIndex >= blobs.Count)
                return default;
            var range = species[speciesIndex].BuildRange;
            if (build < 0 || !range.Any || speciesIndex >= blobInputs.Count)
                return blobs[speciesIndex];
            while (buildBlobs.Count <= speciesIndex)
                buildBlobs.Add(null);
            var row = buildBlobs[speciesIndex] ??= new BlobAssetReference<CreatureBlob>[BodyBuilds.Count];
            build %= BodyBuilds.Count;
            if (!row[build].IsCreated)
            {
                var input = blobInputs[speciesIndex];
                row[build] = CreatureBlobBuilder.Build(species[speciesIndex], input.Prey, input.Hunted, input.Regard, input.Kinship,
                                                       input.Home, BodyBuilds.Of(range, build));
                BuildBlobCount++;
            }
            return row[build];
        }

        /// <summary>The body an individual of a species has, the same on every machine.</summary>
        public BlobAssetReference<CreatureBlob> BlobFor(int speciesIndex, uint individual) =>
            BuildBlob(speciesIndex, BodyBuilds.Pick(individual));

        /// <summary>
        /// Gives a creature the build its (final) Individual draws: after Init and its spawn's own settling (a household
        /// member takes its house's Individual). <paramref name="height"/>: also its extra height, once, at its spawn.
        /// </summary>
        void ApplyBuild(EntityManager em, Entity e, bool height)
        {
            if (!em.HasComponent<CreatureBody>(e) || !em.HasComponent<CreatureState>(e))
                return;
            var body = em.GetComponentData<CreatureBody>(e);
            if (body.Species < 0 || body.Species >= species.Count)
                return;
            var state = em.GetComponentData<CreatureState>(e);
            var blob = BlobFor(body.Species, state.Individual);
            if (blob.IsCreated && blob != body.Blob)
            {
                body.Blob = blob;
                em.SetComponentData(e, body);
            }
            var h = height ? BodyBuilds.Height(species[body.Species].BuildRange, state.Individual) : 1f;
            if (h != 1f)
            {
                state.Scale *= h;
                em.SetComponentData(e, state);
            }
        }

        /// <summary>Frees the build blobs (with the species' own, after the snapshot has let go of them).</summary>
        void DisposeBuilds()
        {
            foreach (var row in buildBlobs)
                if (row != null)
                    foreach (var b in row)
                        if (b.IsCreated)
                            b.Dispose();
            buildBlobs.Clear();
            blobInputs.Clear();
            BuildBlobCount = 0;
        }
    }
}
