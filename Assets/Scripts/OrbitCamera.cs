using UnityEngine;

namespace InteriorMapping
{
    /// <summary>
    /// Slowly orbits the camera around the building at a fixed elevation, always aimed at it.
    /// </summary>
    /// <remarks>
    /// Parallax is the whole point of the shader and none of it reads in a still frame, so a
    /// turntable is the cheapest way to tell whether the interiors and blinds are working.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class OrbitCamera : MonoBehaviour
    {
        private const string BuildingShaderName = "InteriorMapping/SingleFile";

        // Used only when the target turns out to have nothing to measure.
        private const float FallbackRadius = 5f;

        [Tooltip("Leave empty to orbit the first renderer using the interior mapping shader.")]
        [SerializeField] private Transform _target;

        [Tooltip("Orbit speed. At 8 a full revolution takes 45 seconds.")]
        [SerializeField] private float _degreesPerSecond = 8f;

        [Tooltip("Degrees above the horizon, measured from the centre of the building.")]
        [SerializeField] [Range(0f, 80f)] private float _elevationAngle = 18f;

        [Tooltip("Radius as a multiple of the building's size, so rescaling it keeps the framing.")]
        [SerializeField] private float _distanceMultiplier = 2f;

        private Renderer[] _targetRenderers;
        private float _currentAngle;

        private void Start()
        {
            if (_target == null)
            {
                _target = FindBuilding();
            }

            if (_target == null)
            {
                Debug.LogWarning("Nothing to orbit: no target set, and no renderer in the scene " +
                                 $"uses '{BuildingShaderName}'.", this);
                enabled = false;
                return;
            }

            // Cached because the bounds are re-measured every frame and this call allocates.
            _targetRenderers = _target.GetComponentsInChildren<Renderer>();

            // Start from the bearing the camera already has, so entering play mode does not jump.
            Vector3 offsetFromTarget = transform.position - GetTargetBounds().center;
            _currentAngle = Mathf.Atan2(-offsetFromTarget.x, -offsetFromTarget.z) * Mathf.Rad2Deg;

            ApplyOrbitPose();
        }

        private void LateUpdate()
        {
            // Runs late so the orbit lands after anything else that moved the building.
            if (_target == null)
            {
                return;
            }

            _currentAngle = Mathf.Repeat(_currentAngle + _degreesPerSecond * Time.deltaTime, 360f);
            ApplyOrbitPose();
        }

        private void ApplyOrbitPose()
        {
            Bounds targetBounds = GetTargetBounds();
            float radius = Mathf.Max(targetBounds.extents.magnitude, 0.01f) *
                           Mathf.Max(_distanceMultiplier, 0.1f);

            // Euler pitches the offset up before yaw swings it around, so the elevation stays
            // constant instead of the orbit plane tilting with it.
            Quaternion orbitRotation = Quaternion.Euler(_elevationAngle, _currentAngle, 0f);
            transform.position = targetBounds.center + orbitRotation * (Vector3.back * radius);
            transform.LookAt(targetBounds.center);
        }

        /// <summary>World bounds of every renderer under the target, re-measured each frame.</summary>
        /// <remarks>Live so that rescaling the building while tuning keeps the framing honest.</remarks>
        private Bounds GetTargetBounds()
        {
            if (_targetRenderers == null || _targetRenderers.Length == 0)
            {
                return new Bounds(_target.position, Vector3.one * (FallbackRadius * 2f));
            }

            Bounds bounds = _targetRenderers[0].bounds;

            for (int rendererIndex = 1; rendererIndex < _targetRenderers.Length; rendererIndex++)
            {
                bounds.Encapsulate(_targetRenderers[rendererIndex].bounds);
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
