using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace InteriorMapping
{
    /// <summary>
    /// Drives the scene's sun, ambient light and environment reflection through a day/night cycle.
    /// </summary>
    /// <remarks>
    /// The facade reads the ambient probe and the glass reflects the environment cubemap, so
    /// rotating the light alone would leave a building lit at noon standing under a midnight sky.
    /// Ambient moves to Trilight, which gives the probe a direction the brick normals can use.
    /// Editing the ramps on an existing component does nothing until it is Reset - Unity only
    /// calls Reset() when the component is first added, and the old values are already serialized.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public sealed class DayNightCycle : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Real seconds for one full 24 hour cycle.")]
        [SerializeField] private float _dayLengthSeconds = 120f;

        [Tooltip("Hour the cycle starts from.")]
        [SerializeField] [Range(0f, 24f)] private float _startHour = 7f;

        [Header("Sun and Moon")]
        [Tooltip("Compass bearing the sun rises from, in degrees.")]
        [SerializeField] [Range(-180f, 180f)] private float _sunriseBearing = -30f;

        [Tooltip("Light colour across one day. The night half of the ramp is the moon.")]
        [FormerlySerializedAs("_sunColor")]
        [SerializeField] private Gradient _lightColor;

        [Tooltip("Light intensity across one day. Must reach zero at both horizon crossings.")]
        [FormerlySerializedAs("_sunIntensity")]
        [SerializeField] private AnimationCurve _lightIntensity;

        [Header("Ambient")]
        [Tooltip("Overhead ambient. Mostly reaches the roof.")]
        [SerializeField] private Gradient _skyColor;

        [Tooltip("Horizon ambient. This is the one the facade actually sees.")]
        [SerializeField] private Gradient _equatorColor;

        [Tooltip("Upward bounce from the ground.")]
        [SerializeField] private Gradient _groundColor;

        [Header("Environment")]
        [Tooltip("Sky brightness across one day. Needs a skybox with an _Exposure property.")]
        [SerializeField] private AnimationCurve _skyExposure;

        [Tooltip("Seconds between reflection cubemap refreshes. Zero leaves it alone.")]
        [SerializeField] private float _reflectionRefreshSeconds = 0.25f;

        private static readonly int SkyboxExposureId = Shader.PropertyToID("_Exposure");

        private Light _sunLight;
        private Material _originalSkybox;
        private Material _skyboxInstance;
        private float _timeOfDay;
        private float _secondsSinceReflectionRefresh;

        private void Reset()
        {
            BuildDefaultRamps();
        }

        private void Awake()
        {
            _sunLight = GetComponent<Light>();
            _timeOfDay = _startHour;

            // A component added in code never gets Reset(), so nothing here is allowed to be null.
            BuildDefaultRamps(onlyIfMissing: true);

            // Trilight splits ambient into sky, horizon and ground instead of one flat colour.
            RenderSettings.ambientMode = AmbientMode.Trilight;

            // Otherwise the procedural skybox picks its sun by brightness, which changes as we dim.
            RenderSettings.sun = _sunLight;

            // The moon swings overhead, and a procedural sky scatters that as noon. One light
            // cannot be under the horizon for the sky and over it for the shader, so sky
            // brightness is driven on its own curve instead of falling out of the sun angle.
            _originalSkybox = RenderSettings.skybox;

            if (_originalSkybox != null && _originalSkybox.HasProperty(SkyboxExposureId))
            {
                // Instanced, or this edits the shared skybox asset for the whole project.
                _skyboxInstance = new Material(_originalSkybox);
                RenderSettings.skybox = _skyboxInstance;
            }

            ApplyTimeOfDay();
        }

        private void Update()
        {
            float hoursPerSecond = 24f / Mathf.Max(_dayLengthSeconds, 1f);
            _timeOfDay = Mathf.Repeat(_timeOfDay + hoursPerSecond * Time.deltaTime, 24f);

            ApplyTimeOfDay();
            RefreshReflectionOnSchedule();
        }

        private void ApplyTimeOfDay()
        {
            float dayFraction = _timeOfDay / 24f;

            // Minus 90 puts midnight at straight up, which lands 6am on the horizon.
            float sunPitch = dayFraction * 360f - 90f;

            // URP has one main light and the shader reads only that, so a separate moon would
            // light nothing. The same light swings back overhead below the horizon instead,
            // tracing the mirrored arc a moon would. Intensity is zero at the swap, hiding it.
            bool sunIsAboveHorizon = Mathf.Sin(sunPitch * Mathf.Deg2Rad) > 0f;
            float lightPitch = sunIsAboveHorizon ? sunPitch : sunPitch + 180f;

            // Pitch alone would sweep one fixed arc; the bearing yaws it round the compass.
            transform.rotation = Quaternion.Euler(lightPitch, _sunriseBearing, 0f);

            _sunLight.color = _lightColor.Evaluate(dayFraction);

            // Left enabled at zero rather than switched off, so the shader keeps a valid main
            // light direction and simply multiplies everything by black.
            _sunLight.intensity = Mathf.Max(_lightIntensity.Evaluate(dayFraction), 0f);

            RenderSettings.ambientSkyColor = _skyColor.Evaluate(dayFraction);
            RenderSettings.ambientEquatorColor = _equatorColor.Evaluate(dayFraction);
            RenderSettings.ambientGroundColor = _groundColor.Evaluate(dayFraction);

            if (_skyboxInstance != null)
            {
                float exposure = Mathf.Max(_skyExposure.Evaluate(dayFraction), 0f);
                _skyboxInstance.SetFloat(SkyboxExposureId, exposure);
            }
        }

        private void OnDestroy()
        {
            // Play mode would revert this anyway, but leaving a stray material behind would not.
            if (_skyboxInstance == null)
            {
                return;
            }

            RenderSettings.skybox = _originalSkybox;
            Destroy(_skyboxInstance);
        }

        private void RefreshReflectionOnSchedule()
        {
            // Re-rendering the environment cubemap is not free, and the sky moves slowly enough
            // that a few updates a second are indistinguishable from every frame.
            if (_reflectionRefreshSeconds <= 0f)
            {
                return;
            }

            _secondsSinceReflectionRefresh += Time.deltaTime;

            if (_secondsSinceReflectionRefresh < _reflectionRefreshSeconds)
            {
                return;
            }

            _secondsSinceReflectionRefresh = 0f;
            DynamicGI.UpdateEnvironment();
        }

        private void BuildDefaultRamps(bool onlyIfMissing = false)
        {
            if (!onlyIfMissing || _lightColor == null)
            {
                // Cold at both ends, where the light is standing in for moonlight.
                _lightColor = BuildGradient(
                    (0.00f, new Color(0.55f, 0.65f, 0.95f)),
                    (0.18f, new Color(0.55f, 0.62f, 0.90f)),
                    (0.26f, new Color(0.95f, 0.50f, 0.25f)),
                    (0.36f, new Color(1.00f, 0.84f, 0.66f)),
                    (0.50f, new Color(1.00f, 0.96f, 0.90f)),
                    (0.68f, new Color(1.00f, 0.82f, 0.62f)),
                    (0.78f, new Color(0.95f, 0.42f, 0.20f)),
                    (1.00f, new Color(0.55f, 0.65f, 0.95f)));
            }

            if (!onlyIfMissing || _lightIntensity == null || _lightIntensity.length == 0)
            {
                // The zeroes at 0.25 and 0.75 are load bearing: that is where the light flips
                // from sun to moon, and anything above zero there shows the swap as a pop.
                _lightIntensity = BuildCurve(
                    (0.00f, 0.18f), (0.18f, 0.16f), (0.23f, 0.04f), (0.25f, 0f), (0.30f, 0.45f),
                    (0.38f, 1.5f), (0.50f, 2f), (0.62f, 1.5f), (0.70f, 0.45f), (0.75f, 0f),
                    (0.77f, 0.04f), (0.82f, 0.16f), (1.00f, 0.18f));
            }

            if (!onlyIfMissing || _skyExposure == null || _skyExposure.length == 0)
            {
                // Unity's procedural skybox sits at 1.3 by default, so that is the daytime peak.
                _skyExposure = BuildCurve(
                    (0.00f, 0.10f), (0.20f, 0.12f), (0.25f, 0.55f), (0.33f, 1.2f), (0.50f, 1.3f),
                    (0.67f, 1.2f), (0.75f, 0.55f), (0.80f, 0.12f), (1.00f, 0.10f));
            }

            if (!onlyIfMissing || _skyColor == null)
            {
                _skyColor = BuildGradient(
                    (0.00f, new Color(0.05f, 0.07f, 0.14f)),
                    (0.23f, new Color(0.12f, 0.12f, 0.20f)),
                    (0.30f, new Color(0.35f, 0.40f, 0.58f)),
                    (0.50f, new Color(0.45f, 0.56f, 0.78f)),
                    (0.72f, new Color(0.36f, 0.32f, 0.40f)),
                    (0.80f, new Color(0.10f, 0.10f, 0.18f)),
                    (1.00f, new Color(0.05f, 0.07f, 0.14f)));
            }

            if (!onlyIfMissing || _equatorColor == null)
            {
                // The facade sees this one almost exclusively, so its night end is what decides
                // whether the brick stays readable after dark.
                _equatorColor = BuildGradient(
                    (0.00f, new Color(0.06f, 0.07f, 0.12f)),
                    (0.24f, new Color(0.20f, 0.14f, 0.14f)),
                    (0.30f, new Color(0.34f, 0.30f, 0.28f)),
                    (0.50f, new Color(0.42f, 0.43f, 0.44f)),
                    (0.72f, new Color(0.34f, 0.24f, 0.18f)),
                    (0.79f, new Color(0.09f, 0.09f, 0.14f)),
                    (1.00f, new Color(0.06f, 0.07f, 0.12f)));
            }

            if (!onlyIfMissing || _groundColor == null)
            {
                _groundColor = BuildGradient(
                    (0.00f, new Color(0.02f, 0.02f, 0.04f)),
                    (0.28f, new Color(0.08f, 0.07f, 0.06f)),
                    (0.50f, new Color(0.18f, 0.16f, 0.13f)),
                    (0.74f, new Color(0.08f, 0.06f, 0.05f)),
                    (1.00f, new Color(0.02f, 0.02f, 0.04f)));
            }
        }

        private static Gradient BuildGradient(params (float DayFraction, Color Color)[] keys)
        {
            GradientColorKey[] colorKeys = new GradientColorKey[keys.Length];

            for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            {
                colorKeys[keyIndex] = new GradientColorKey(keys[keyIndex].Color, keys[keyIndex].DayFraction);
            }

            Gradient gradient = new Gradient();
            gradient.SetKeys(colorKeys, new[] { new GradientAlphaKey(1f, 0f) });
            return gradient;
        }

        private static AnimationCurve BuildCurve(params (float DayFraction, float Value)[] keys)
        {
            Keyframe[] keyframes = new Keyframe[keys.Length];

            for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            {
                keyframes[keyIndex] = new Keyframe(keys[keyIndex].DayFraction, keys[keyIndex].Value);
            }

            AnimationCurve curve = new AnimationCurve(keyframes);

            // Default tangents are flat, which steps between keys instead of ramping through them.
            for (int keyIndex = 0; keyIndex < keyframes.Length; keyIndex++)
            {
                curve.SmoothTangents(keyIndex, 0f);
            }

            return curve;
        }
    }
}
