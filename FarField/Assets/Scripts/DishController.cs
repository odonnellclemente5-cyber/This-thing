using UnityEngine;

/// <summary>Controls the observatory dish's azimuth, elevation, and scan sweep.</summary>
public sealed class DishController : MonoBehaviour
{
    private const float AzimuthRateDegreesPerSecond = 26f;
    private const float ElevationRateDegreesPerSecond = 18f;
    private const float AzimuthMoveDegreesPerSecond = 32f;
    private const float ElevationMoveDegreesPerSecond = 22f;
    private const float MinimumElevationDegrees = 4f;
    private const float MaximumElevationDegrees = 88f;
    private const float ScanHalfRangeDegrees = 120f;
    private const float ScanCyclesPerSecond = 0.04f;
    private const float SettledAngleToleranceDegrees = 0.18f;

    private Transform azimuthPivot;
    private Transform elevationPivot;
    private float azimuthDegrees;
    private float targetAzimuthDegrees;
    private float elevationDegrees;
    private float targetElevationDegrees;
    private float azimuthInput;
    private float elevationInput;
    private float scanOriginDegrees;
    private bool scanning;

    /// <summary>Current physical dish azimuth in degrees.</summary>
    public float AzimuthDegrees => azimuthDegrees;

    /// <summary>Current physical dish elevation in degrees.</summary>
    public float ElevationDegrees => elevationDegrees;

    /// <summary>Whether an automatic azimuth scan sweep is active.</summary>
    public bool IsScanning => scanning;

    /// <summary>Whether the dish has stopped moving and reached its commanded position.</summary>
    public bool IsSettled => !scanning
        && Mathf.Abs(azimuthInput) < 0.01f
        && Mathf.Abs(elevationInput) < 0.01f
        && Mathf.Abs(Mathf.DeltaAngle(azimuthDegrees, targetAzimuthDegrees)) < SettledAngleToleranceDegrees
        && Mathf.Abs(elevationDegrees - targetElevationDegrees) < SettledAngleToleranceDegrees;

    /// <summary>Connects the generated dish pivots and establishes its initial pointing angles.</summary>
    public void Initialize(Transform generatedAzimuthPivot, Transform generatedElevationPivot, float initialAzimuth, float initialElevation)
    {
        azimuthPivot = generatedAzimuthPivot;
        elevationPivot = generatedElevationPivot;
        azimuthDegrees = Mathf.Repeat(initialAzimuth, 360f);
        targetAzimuthDegrees = azimuthDegrees;
        elevationDegrees = Mathf.Clamp(initialElevation, MinimumElevationDegrees, MaximumElevationDegrees);
        targetElevationDegrees = elevationDegrees;
        ApplyRotations();
    }

    /// <summary>Sets operator slew input, where horizontal controls azimuth and vertical controls elevation.</summary>
    public void SetSlewInput(float horizontal, float vertical)
    {
        azimuthInput = Mathf.Clamp(horizontal, -1f, 1f);
        elevationInput = Mathf.Clamp(vertical, -1f, 1f);
        if (Mathf.Abs(azimuthInput) > 0.01f || Mathf.Abs(elevationInput) > 0.01f)
        {
            if (scanning)
            {
                targetAzimuthDegrees = azimuthDegrees;
            }
            scanning = false;
        }
    }

    /// <summary>Starts an automatic scan centered on the dish's current azimuth, or stops an active scan.</summary>
    public void ToggleScan()
    {
        if (scanning)
        {
            scanning = false;
            targetAzimuthDegrees = azimuthDegrees;
            return;
        }

        scanOriginDegrees = azimuthDegrees;
        scanning = true;
    }

    /// <summary>Stops scanning and clears any held manual slew input.</summary>
    public void StopMotion()
    {
        scanning = false;
        azimuthInput = 0f;
        elevationInput = 0f;
        targetAzimuthDegrees = azimuthDegrees;
        targetElevationDegrees = elevationDegrees;
    }

    private void Update()
    {
        if (azimuthPivot == null || elevationPivot == null)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        if (scanning)
        {
            float sweep = Mathf.Sin(Time.time * (ScanCyclesPerSecond * Mathf.PI * 2f)) * ScanHalfRangeDegrees;
            targetAzimuthDegrees = Mathf.Repeat(scanOriginDegrees + sweep, 360f);
        }
        else
        {
            targetAzimuthDegrees = Mathf.Repeat(targetAzimuthDegrees + azimuthInput * AzimuthRateDegreesPerSecond * deltaTime, 360f);
            targetElevationDegrees = Mathf.Clamp(targetElevationDegrees + elevationInput * ElevationRateDegreesPerSecond * deltaTime, MinimumElevationDegrees, MaximumElevationDegrees);
        }

        azimuthDegrees = Mathf.Repeat(Mathf.MoveTowardsAngle(azimuthDegrees, targetAzimuthDegrees, AzimuthMoveDegreesPerSecond * deltaTime), 360f);
        elevationDegrees = Mathf.MoveTowards(elevationDegrees, targetElevationDegrees, ElevationMoveDegreesPerSecond * deltaTime);
        ApplyRotations();
    }

    private void ApplyRotations()
    {
        if (azimuthPivot != null)
        {
            azimuthPivot.localRotation = Quaternion.Euler(0f, azimuthDegrees, 0f);
        }

        if (elevationPivot != null)
        {
            elevationPivot.localRotation = Quaternion.Euler(-elevationDegrees, 0f, 0f);
        }
    }
}
