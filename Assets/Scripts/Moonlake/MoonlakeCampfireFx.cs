using UnityEngine;

namespace NexusLink.Moonlake.HeroSlice
{
    /// <summary>
    /// Presentation-only campfire animation for the Moonlake habitat.
    /// Weather and time-of-day are supplied by MoonlakeSkyDayWeatherController;
    /// this component owns no gameplay, save, warmth, damage or reward state.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class MoonlakeCampfireFx : MonoBehaviour
    {
        public Light fireLight;
        public Renderer[] flameRenderers = new Renderer[0];
        public ParticleSystem flameParticles;
        public ParticleSystem emberParticles;

        [Header("Look")]
        public float dayIntensity = 2.4f;
        public float nightIntensity = 12f;
        [Range(0f, 0.6f)] public float flickerAmount = 0.18f;
        public float flickerSpeed = 9f;

        [Header("Environment input")]
        [Range(0f, 1f)] public float daylight = 1f;
        [Range(0f, 1f)] public float rainDamping;

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        MaterialPropertyBlock _propertyBlock;

        void OnEnable()
        {
            _propertyBlock = new MaterialPropertyBlock();
            ApplyImmediate();
        }

        void OnValidate()
        {
            ApplyImmediate();
        }

        void Update()
        {
            ApplyImmediate();
        }

        public void SetEnvironment(float daylightAmount, float rainAmount)
        {
            daylight = Mathf.Clamp01(daylightAmount);
            rainDamping = Mathf.Clamp01(rainAmount);
            ApplyImmediate();
        }

        public void ApplyImmediate()
        {
            var baseIntensity = Mathf.Lerp(nightIntensity, dayIntensity, daylight);
            var weatherScale = Mathf.Lerp(1f, 0.48f, rainDamping);
            var flicker = Application.isPlaying
                ? 1f + (Mathf.PerlinNoise(Time.time * flickerSpeed, 0.37f) - 0.5f) * flickerAmount * 2f
                : 1f;

            if (fireLight != null)
            {
                fireLight.intensity = baseIntensity * weatherScale * flicker;
                fireLight.range = Mathf.Lerp(4.2f, 2.9f, rainDamping);
            }

            if (_propertyBlock == null)
                _propertyBlock = new MaterialPropertyBlock();

            var emission = new Color(1.9f, 0.58f, 0.12f, 1f) * Mathf.Lerp(1.15f, 0.62f, rainDamping);
            foreach (var renderer in flameRenderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(EmissionColorId, emission);
                renderer.SetPropertyBlock(_propertyBlock);
            }

            SetParticleEmission(flameParticles, Mathf.Lerp(18f, 7f, rainDamping));
            SetParticleEmission(emberParticles, Mathf.Lerp(6f, 1f, rainDamping));
        }

        static void SetParticleEmission(ParticleSystem system, float rate)
        {
            if (system == null) return;
            var emission = system.emission;
            emission.rateOverTime = rate;
        }
    }
}
