using System.Collections.Generic;
using UnityEngine;

namespace PyxlMedia.InteriorMapping.Samples
{
    /// <summary>
    /// Scatters plain blocks in a ring around the building on Play, so the sun has something to cast from.
    /// </summary>
    /// <remarks>
    /// The ring is sized in multiples of the building, the same unit the orbit camera uses, so the default
    /// starts outside its orbit and nothing ever comes between the camera and the facade.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class StructureScatter : MonoBehaviour
    {
        private const string BuildingShaderName = "InteriorMapping/SingleFile";

        // Tries per block before it is skipped, so a crowded ring cannot stall entering Play.
        private const int AttemptsPerStructure = 12;

        // Used only when the target turns out to have nothing to measure.
        private const float FallbackRadius = 5f;

        [Tooltip("Leave empty to ring the first renderer using the interior mapping shader.")]
        [SerializeField] private Transform _target;

        [Tooltip("Zero picks a new layout every Play and logs its seed. Paste a logged seed here to keep that layout.")]
        [SerializeField] private int _seed;

        [Tooltip("Blocks that do not fit after a few tries are skipped, so a crowded ring may place fewer.")]
        [SerializeField] [Range(1, 64)] private int _structureCount = 14;

        [Tooltip("Distance from the building's centre to the near side of each block, in multiples of the " +
                 "building's size. The orbit camera sits at 2.")]
        [SerializeField] private Vector2 _ringRange = new Vector2(3f, 6f);

        [Tooltip("Footprint side length in metres.")]
        [SerializeField] private Vector2 _widthRange = new Vector2(2f, 6f);

        [Tooltip("Height in metres.")]
        [SerializeField] private Vector2 _heightRange = new Vector2(6f, 40f);

        [Tooltip("Shared by every block. Leave empty for the render pipeline's default material.")]
        [SerializeField] private Material _material;

        private void Start()
        {
            if (_target == null)
            {
                _target = FindBuilding();
            }

            if (_target == null)
            {
                Debug.LogWarning("Nothing to scatter around: no target set, and no renderer in the scene " +
                                 $"uses '{BuildingShaderName}'.", this);
                return;
            }

            int seed = _seed;

            if (seed == 0)
            {
                seed = new System.Random().Next(1, int.MaxValue);
                Debug.Log($"Scattered structures with seed {seed}.", this);
            }

            Scatter(MeasureBounds(_target), new System.Random(seed));
        }

        private void Scatter(Bounds building, System.Random random)
        {
            float buildingSize = building.extents.magnitude;
            Vector2 buildingCentre = new Vector2(building.center.x, building.center.z);

            // Squared to the same street grid as the building, so the blocks read as a city rather than debris.
            Quaternion alignment = Quaternion.Euler(0f, _target.eulerAngles.y, 0f);

            // One sector per block covers every bearing, so some block stands between the building and the sun
            // whatever the hour.
            float sectorDegrees = 360f / _structureCount;
            float startBearing = RandomBetween(random, 0f, 360f);
            List<(Vector2 Centre, float Radius)> footprints = new List<(Vector2 Centre, float Radius)>();

            for (int structureIndex = 0; structureIndex < _structureCount; structureIndex++)
            {
                for (int attempt = 0; attempt < AttemptsPerStructure; attempt++)
                {
                    Vector2 footprint = new Vector2(RandomBetween(random, _widthRange),
                                                    RandomBetween(random, _widthRange));
                    float footprintRadius = footprint.magnitude * 0.5f;

                    // Measured to the near side, so a wide block cannot reach in towards the camera.
                    float distance = RandomBetween(random, _ringRange) * buildingSize + footprintRadius;
                    float bearing = startBearing + (structureIndex + (float)random.NextDouble()) * sectorDegrees;
                    Vector3 direction = Quaternion.Euler(0f, bearing, 0f) * Vector3.forward;
                    Vector2 centre = buildingCentre + new Vector2(direction.x, direction.z) * distance;

                    if (OverlapsAny(footprints, centre, footprintRadius))
                    {
                        continue;
                    }

                    footprints.Add((centre, footprintRadius));

                    // Squared so most blocks stay low and a few tower over them, which reads as a skyline.
                    float heightBlend = (float)random.NextDouble();
                    float height = Mathf.Lerp(Mathf.Min(_heightRange.x, _heightRange.y),
                                              Mathf.Max(_heightRange.x, _heightRange.y),
                                              heightBlend * heightBlend);

                    // Bases sit level with the building's, so whatever ground it stands on, they do too.
                    Vector3 position = new Vector3(centre.x, building.min.y + height * 0.5f, centre.y);
                    CreateStructure(structureIndex, position, alignment,
                                    new Vector3(footprint.x, height, footprint.y));
                    break;
                }
            }
        }

        private void CreateStructure(int structureIndex, Vector3 position, Quaternion rotation, Vector3 size)
        {
            GameObject structure = GameObject.CreatePrimitive(PrimitiveType.Cube);
            structure.name = $"Structure {structureIndex}";

            // Nothing here is simulated.
            Destroy(structure.GetComponent<Collider>());

            if (_material != null)
            {
                structure.GetComponent<MeshRenderer>().sharedMaterial = _material;
            }

            // Posed before parenting, so a moved or scaled scatter object cannot shift the ring.
            structure.transform.SetPositionAndRotation(position, rotation);
            structure.transform.localScale = size;
            structure.transform.SetParent(transform, worldPositionStays: true);
        }

        private static bool OverlapsAny(List<(Vector2 Centre, float Radius)> footprints, Vector2 centre,
                                        float radius)
        {
            foreach ((Vector2 otherCentre, float otherRadius) in footprints)
            {
                if (Vector2.Distance(centre, otherCentre) < radius + otherRadius)
                {
                    return true;
                }
            }

            return false;
        }

        private static float RandomBetween(System.Random random, Vector2 range)
        {
            return RandomBetween(random, Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
        }

        private static float RandomBetween(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        private static Bounds MeasureBounds(Transform target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
            {
                return new Bounds(target.position, Vector3.one * (FallbackRadius * 2f));
            }

            Bounds bounds = renderers[0].bounds;

            for (int rendererIndex = 1; rendererIndex < renderers.Length; rendererIndex++)
            {
                bounds.Encapsulate(renderers[rendererIndex].bounds);
            }

            return bounds;
        }

        private static Transform FindBuilding()
        {
            foreach (Renderer candidate in FindObjectsByType<Renderer>())
            {
                Material material = candidate.sharedMaterial;

                if (material != null && material.shader != null && material.shader.name == BuildingShaderName)
                {
                    return candidate.transform;
                }
            }

            return null;
        }
    }
}
