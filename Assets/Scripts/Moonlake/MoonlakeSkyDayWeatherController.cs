using UnityEngine;
using UnityEngine.Rendering;

namespace NexusLink.Moonlake.HeroSlice
{
    /// <summary>
    /// Presentation-only sky, solar arc and weather controller for the fixed
    /// Moonlake habitat geometry. Weather never replaces scene geometry and
    /// does not write progression, relationship, memory or gameplay state --
    /// those belong to this project's gameplay systems, not to a renderer.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class MoonlakeSkyDayWeatherController : MonoBehaviour
    {
        public enum WeatherState
        {
            Clear = 0,
            PartlyCloudy = 1,
            Rain = 2,
            Mist = 3
        }

        [Header("Time of day")]
        [Range(0f, 24f)] public float timeOfDay = 10.5f;
        public bool autoAdvanceTime;
        [Min(30f)] public float fullDaySeconds = 480f;

        [Header("Weather")]
        public WeatherState weather = WeatherState.Clear;
        public float cloudDriftDegreesPerSecond = 0.35f;

        [Header("Scene references")]
        public Camera heroCamera;
        public Light sun;
        public Light skyFill;
        public Transform cloudLayer;
        public Renderer[] cloudRenderers = new Renderer[0];
        public ParticleSystem rainParticles;
        public ParticleSystem mistParticles;
        public Light[] nightLights = new Light[0];
        public Renderer[] crystalAndLanternRenderers = new Renderer[0];
        public MoonlakeCampfireFx campfire;

        [Header("Asset skyboxes")]
        public Material clearDaySky;
        public Material sunsetSky;
        public Material overcastSky;
        public Material nightSky;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        MaterialPropertyBlock _propertyBlock;
        Material _activeSky;
        float _lastTime = float.NaN;
        WeatherState _lastWeather = (WeatherState)(-1);
        bool _lastRain;
        bool _lastMist;

        public float Daylight01
        {
            get
            {
                var radians = (timeOfDay - 6f) / 12f * Mathf.PI;
                return Mathf.Clamp01(Mathf.Sin(radians));
            }
        }

        void OnEnable()
        {
            _propertyBlock = new MaterialPropertyBlock();
            ApplyImmediate(true);
        }

        void OnValidate()
        {
            timeOfDay = Mathf.Repeat(timeOfDay, 24f);
            fullDaySeconds = Mathf.Max(30f, fullDaySeconds);
            ApplyImmediate(true);
        }

        void Update()
        {
            if (Application.isPlaying && autoAdvanceTime)
                timeOfDay = Mathf.Repeat(timeOfDay + Time.deltaTime * 24f / fullDaySeconds, 24f);

            if (Application.isPlaying && cloudLayer != null)
                cloudLayer.Rotate(Vector3.up, cloudDriftDegreesPerSecond * Time.deltaTime, Space.World);

            if (!Mathf.Approximately(_lastTime, timeOfDay) || _lastWeather != weather)
                ApplyImmediate(false);
        }

        [ContextMenu("Weather/Clear")]
        public void SetClear() { SetWeather(WeatherState.Clear); }

        [ContextMenu("Weather/Partly Cloudy")]
        public void SetPartlyCloudy() { SetWeather(WeatherState.PartlyCloudy); }

        [ContextMenu("Weather/Rain")]
        public void SetRain() { SetWeather(WeatherState.Rain); }

        [ContextMenu("Weather/Mist")]
        public void SetMist() { SetWeather(WeatherState.Mist); }

        public void SetWeather(WeatherState state)
        {
            weather = state;
            ApplyImmediate(true);
        }

        public void SetTime(float hour)
        {
            timeOfDay = Mathf.Repeat(hour, 24f);
            ApplyImmediate(true);
        }

        public void ApplyImmediate()
        {
            ApplyImmediate(true);
        }

        public void ApplyImmediate(bool forceParticles)
        {
            var daylight = Daylight01;
            var dawn = 1f - Mathf.Clamp01(Mathf.Abs(timeOfDay - 6.5f) / 2.2f);
            var dusk = 1f - Mathf.Clamp01(Mathf.Abs(timeOfDay - 18f) / 2.6f);
            var warmEdge = Mathf.Max(dawn, dusk);
            var rainAmount = weather == WeatherState.Rain ? 1f : 0f;
            var mistAmount = weather == WeatherState.Mist ? 1f : 0f;

            var sunMultiplier = 1f;
            var cloudOpacity = 0.46f;
            // Fog distances are tied to how deep the habitat actually is. They
            // were tuned when the scene ended about 12 units out, where the far
            // bank sat 40 m from the hero camera. The channel now runs to z=26,
            // putting the far bank at 54 m and the ridges at 58 m — under the old
            // ranges that washed the whole midground out at 36-57% fog. These
            // keep the plaza clear of fog entirely and leave roughly 20-30% at
            // the horizon, which is aerial perspective rather than haze.
            var fogEnd = Mathf.Lerp(62f, 110f, daylight);
            var fogStart = Mathf.Lerp(16f, 38f, daylight);
            switch (weather)
            {
                case WeatherState.PartlyCloudy:
                    sunMultiplier = 0.84f;
                    cloudOpacity = 0.48f;
                    fogEnd -= 10f;
                    break;
                case WeatherState.Rain:
                    sunMultiplier = 0.54f;
                    cloudOpacity = 0.90f;
                    fogStart = 22f;
                    fogEnd = 66f;
                    break;
                case WeatherState.Mist:
                    sunMultiplier = 0.66f;
                    cloudOpacity = 0.68f;
                    fogStart = 10f;
                    fogEnd = 44f;
                    break;
            }

            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(timeOfDay / 24f * 360f - 90f, -32f, 0f);
                sun.color = Color.Lerp(new Color(1f, 0.55f, 0.30f), new Color(1f, 0.94f, 0.82f), daylight);
                sun.color = Color.Lerp(sun.color, new Color(1f, 0.66f, 0.38f), warmEdge * 0.62f);
                // Key-to-fill was running near 1:1 against an ambient of 0.84-1.0,
                // which is why the habitat read flat and chalky next to the
                // reference. Lifting the key and cutting the fill lets form and
                // contact shadow come back without touching the palette.
                sun.intensity = Mathf.Lerp(0.04f, 1.62f, daylight) * sunMultiplier;
                sun.shadowStrength = Mathf.Lerp(0.22f, 0.84f, daylight) * Mathf.Lerp(1f, 0.70f, rainAmount + mistAmount * 0.5f);
            }

            if (skyFill != null)
            {
                skyFill.color = Color.Lerp(new Color(0.24f, 0.32f, 0.56f), new Color(0.73f, 0.86f, 0.95f), daylight);
                skyFill.intensity = Mathf.Lerp(0.22f, 0.40f, daylight) * Mathf.Lerp(1f, 0.78f, rainAmount);
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            var dayFog = Color.Lerp(new Color(0.12f, 0.17f, 0.29f), new Color(0.56f, 0.78f, 0.89f), daylight);
            var wetFog = Color.Lerp(dayFog, new Color(0.38f, 0.50f, 0.57f), rainAmount * 0.72f);
            var mistFogTarget = Color.Lerp(new Color(0.20f, 0.29f, 0.39f), new Color(0.69f, 0.79f, 0.80f), daylight);
            RenderSettings.fogColor = Color.Lerp(wetFog, mistFogTarget, mistAmount * 0.62f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(new Color(0.25f, 0.35f, 0.55f), new Color(0.76f, 0.89f, 0.95f), daylight);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.18f, 0.28f, 0.44f), new Color(0.60f, 0.75f, 0.72f), daylight);
            RenderSettings.ambientGroundColor = Color.Lerp(new Color(0.12f, 0.18f, 0.28f), new Color(0.34f, 0.42f, 0.32f), daylight);
            RenderSettings.ambientIntensity = Mathf.Lerp(0.58f, 0.66f, daylight) * Mathf.Lerp(1f, 0.82f, rainAmount);
            RenderSettings.reflectionIntensity = Mathf.Lerp(0.34f, 0.68f, daylight);

            if (heroCamera != null)
            {
                heroCamera.clearFlags = CameraClearFlags.Skybox;
                heroCamera.backgroundColor = RenderSettings.fogColor;
            }

            var sky = SelectSky(daylight);
            if (sky != null && (_activeSky != sky || RenderSettings.skybox != sky))
            {
                _activeSky = sky;
                RenderSettings.skybox = sky;
                DynamicGI.UpdateEnvironment();
            }

            ApplyCloudOpacity(cloudOpacity * Mathf.Lerp(0.62f, 1f, daylight));
            SetParticleState(rainParticles, rainAmount > 0.5f, ref _lastRain, forceParticles);
            SetParticleState(mistParticles, mistAmount > 0.5f, ref _lastMist, forceParticles);

            var night = 1f - daylight;
            foreach (var light in nightLights)
            {
                if (light == null) continue;
                light.intensity = night * Mathf.Lerp(6.2f, 8.5f, 1f - rainAmount);
            }
            ApplyNightEmission(night);

            if (campfire != null)
                campfire.SetEnvironment(daylight, rainAmount);

            _lastTime = timeOfDay;
            _lastWeather = weather;
        }

        Material SelectSky(float daylight)
        {
            if (daylight < 0.10f && nightSky != null)
                return nightSky;
            if ((weather == WeatherState.Rain || weather == WeatherState.Mist) && overcastSky != null)
                return overcastSky;
            if ((timeOfDay >= 16.4f && timeOfDay <= 20.1f) || (timeOfDay >= 4.7f && timeOfDay <= 7.1f))
                return sunsetSky != null ? sunsetSky : clearDaySky;
            return clearDaySky;
        }

        void ApplyCloudOpacity(float alpha)
        {
            if (_propertyBlock == null)
                _propertyBlock = new MaterialPropertyBlock();

            var color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            foreach (var renderer in cloudRenderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorId, color);
                _propertyBlock.SetColor(ColorId, color);
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        void ApplyNightEmission(float night)
        {
            if (_propertyBlock == null)
                _propertyBlock = new MaterialPropertyBlock();

            var cyan = new Color(0.12f, 1.05f, 1.75f, 1f) * Mathf.Lerp(0.30f, 1.35f, night);
            var warm = new Color(1.75f, 0.72f, 0.20f, 1f) * Mathf.Lerp(0.35f, 1.40f, night);
            foreach (var renderer in crystalAndLanternRenderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(EmissionColorId, renderer.gameObject.name.Contains("Glow") ? warm : cyan);
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        static void SetParticleState(ParticleSystem system, bool active, ref bool previous, bool force)
        {
            if (system == null) return;
            if (!force && previous == active) return;

            if (active)
            {
                if (Application.isPlaying)
                    system.Play(true);
                else
                    system.Simulate(2f, true, true, true);
            }
            else
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            previous = active;
        }
    }
}
