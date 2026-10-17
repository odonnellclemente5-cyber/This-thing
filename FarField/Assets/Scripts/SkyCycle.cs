using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Drives the prototype's lightweight day/night palette, directional lights, and procedural star field.</summary>
public sealed class SkyCycle : MonoBehaviour
{
    private const float FullDayHours = 24f;
    private const float UpdateIntervalSeconds = 0.15f;
    private const float StarVisibilityThreshold = 0.12f;

    private Camera playerCamera;
    private Light sunLight;
    private Light moonLight;
    private Renderer starRenderer;
    private float hourOfDay;
    private float hoursPerSecond;
    private float nextLightingUpdate;
    private bool starsVisible;

    private readonly Color nightSkyColor = new Color(0.008f, 0.018f, 0.055f);
    private readonly Color daySkyColor = new Color(0.36f, 0.59f, 0.78f);
    private readonly Color nightAmbientColor = new Color(0.055f, 0.075f, 0.14f);
    private readonly Color dayAmbientColor = new Color(0.58f, 0.62f, 0.66f);

    /// <summary>Initializes the cycle and its generated scene references.</summary>
    public void Initialize(Camera cameraToDrive, Light generatedSun, Light generatedMoon, Renderer generatedStars, float startingHour, float cycleHoursPerSecond)
    {
        playerCamera = cameraToDrive;
        sunLight = generatedSun;
        moonLight = generatedMoon;
        starRenderer = generatedStars;
        hourOfDay = Mathf.Repeat(startingHour, FullDayHours);
        hoursPerSecond = Mathf.Max(0f, cycleHoursPerSecond);
        nextLightingUpdate = 0f;
        starsVisible = false;
        if (starRenderer != null)
        {
            starRenderer.enabled = false;
        }
    }

    private void Update()
    {
        hourOfDay = Mathf.Repeat(hourOfDay + hoursPerSecond * Time.deltaTime, FullDayHours);
        if (Time.time < nextLightingUpdate)
        {
            return;
        }

        nextLightingUpdate = Time.time + UpdateIntervalSeconds;
        UpdateLighting();
    }

    private void LateUpdate()
    {
        if (playerCamera != null && starRenderer != null)
        {
            starRenderer.transform.position = playerCamera.transform.position;
        }
    }

    private void UpdateLighting()
    {
        float solarAltitude = Mathf.Sin(((hourOfDay - 6f) / 12f) * Mathf.PI);
        float daylight = Mathf.SmoothStep(-0.16f, 0.2f, solarAltitude);
        float night = 1f - daylight;
        float solarAngle = hourOfDay / FullDayHours * 360f;

        if (sunLight != null)
        {
            sunLight.transform.rotation = Quaternion.Euler(solarAngle - 90f, 155f, 0f);
            sunLight.intensity = Mathf.Lerp(0.02f, 1.15f, daylight);
            sunLight.color = Color.Lerp(new Color(0.56f, 0.66f, 1f), new Color(1f, 0.88f, 0.72f), daylight);
        }

        if (moonLight != null)
        {
            moonLight.transform.rotation = Quaternion.Euler(solarAngle + 90f, 35f, 0f);
            moonLight.intensity = Mathf.Lerp(0.2f, 0f, daylight);
        }

        Color skyColor = Color.Lerp(nightSkyColor, daySkyColor, daylight);
        Color ambientColor = Color.Lerp(nightAmbientColor, dayAmbientColor, daylight);
        if (playerCamera != null)
        {
            playerCamera.clearFlags = CameraClearFlags.SolidColor;
            playerCamera.backgroundColor = skyColor;
        }

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambientColor;
        RenderSettings.ambientEquatorColor = Color.Lerp(ambientColor * 0.76f, dayAmbientColor * 0.86f, daylight);
        RenderSettings.ambientGroundColor = Color.Lerp(ambientColor * 0.48f, new Color(0.28f, 0.31f, 0.31f), daylight);
        RenderSettings.ambientIntensity = Mathf.Lerp(0.72f, 1f, daylight);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = Color.Lerp(nightSkyColor, new Color(0.55f, 0.65f, 0.7f), daylight);
        RenderSettings.fogDensity = Mathf.Lerp(0.0022f, 0.0014f, daylight);

        bool shouldShowStars = night > StarVisibilityThreshold;
        if (starRenderer != null && shouldShowStars != starsVisible)
        {
            starsVisible = shouldShowStars;
            starRenderer.enabled = starsVisible;
        }

        if (starRenderer != null && starRenderer.sharedMaterial != null)
        {
            Color starColor = Color.Lerp(new Color(0.28f, 0.38f, 0.62f), new Color(0.82f, 0.9f, 1f), night);
            if (starRenderer.sharedMaterial.HasProperty("_BaseColor"))
            {
                starRenderer.sharedMaterial.SetColor("_BaseColor", starColor);
            }
            else
            {
                starRenderer.sharedMaterial.color = starColor;
            }
        }
    }
}
