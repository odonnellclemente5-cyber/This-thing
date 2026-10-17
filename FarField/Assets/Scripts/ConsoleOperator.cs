using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Provides dish operation, live radio observations, and a session field notebook.</summary>
public sealed class ConsoleOperator : MonoBehaviour
{
    private const float ReadoutRefreshSeconds = 0.12f;
    private const float FineTuneRateMHzPerSecond = 3f;
    private const float FineTuneStepMHz = 0.05f;
    private const float CoarseTuneRateMHzPerSecond = 100f;
    private const float CoarseTuneStepMHz = 100f;
    private const float MinimumFrequencyMHz = 50f;
    private const float MaximumFrequencyMHz = 1600f;
    private const float GainRateDbPerSecond = 16f;
    private const float GainStepDb = 1.5f;
    private const float MinimumGainDb = 0f;
    private const float MaximumGainDb = 36f;
    private const float SpectrumHalfSpanMHz = 0.8f;
    private const float ObservationDurationSeconds = 5f;
    private const float PulseObservationDurationSeconds = 8.5f;
    private const float CaptureTimeoutSeconds = 32f;
    private const float LockGraceSeconds = 0.65f;
    private const float DirectionConsistencyToleranceDegrees = 2.5f;
    private const float BeamSigmaDegrees = 5.2f;
    private const float PulsePeriodSeconds = 3.8f;
    private const float PulseOnDurationSeconds = 1.05f;
    private const int SpectrumBinCount = 49;
    private const float PanelWidth = 600f;
    private const float PanelHeight = 440f;
    private const string NotebookSaveKey = "FARFIELD_FieldNotebook_v1";
    private const int NotebookSaveVersion = 1;
    private const int MaximumResearchTextLength = 1800;
    private const int MaximumOpticalDetailLength = 800;
    private const int MaximumDisplayedOpticalObservations = 3;
    private const string ResearchTextControlName = "FarFieldResearchText";
    private const string HypothesisTextControlName = "FarFieldHypothesisText";

    private static readonly string[] ClassificationOptions =
    {
        "Unclassified",
        "Natural emission",
        "Rotating compact object",
        "Artificial / engineered",
        "Terrestrial interference",
        "Needs follow-up"
    };

    private enum SignalKind
    {
        HydrogenLine,
        Pulsar,
        DriftingCarrier
    }

    private sealed class SignalDefinition
    {
        public readonly SignalKind Kind;
        public readonly string NotebookName;
        public readonly string Signature;
        public readonly float CenterFrequencyMHz;
        public readonly float FrequencyWidthMHz;
        public readonly float AzimuthDegrees;
        public readonly float ElevationDegrees;
        public readonly float PeakAmplitude;

        public SignalDefinition(SignalKind kind, string notebookName, string signature, float centerFrequencyMHz, float frequencyWidthMHz, float azimuthDegrees, float elevationDegrees, float peakAmplitude)
        {
            Kind = kind;
            NotebookName = notebookName;
            Signature = signature;
            CenterFrequencyMHz = centerFrequencyMHz;
            FrequencyWidthMHz = frequencyWidthMHz;
            AzimuthDegrees = azimuthDegrees;
            ElevationDegrees = elevationDegrees;
            PeakAmplitude = peakAmplitude;
        }
    }

    private sealed class FieldNote
    {
        public string SourceKey;
        public string Name;
        public string Signature;
        public float FrequencyMHz;
        public float AzimuthDegrees;
        public float ElevationDegrees;
        public float IntegrationSeconds;
        public int PulseCount;
        public string Timestamp;
        public float Confidence;
        public int ObservationCount;
        public string ResearchNotes = string.Empty;
        public string Hypothesis = string.Empty;
        public string Classification = ClassificationOptions[0];
        public List<string> OpticalObservations = new List<string>();
    }

    [System.Serializable]
    private sealed class NotebookSaveData
    {
        public int version;
        public List<FieldNoteSaveData> items = new List<FieldNoteSaveData>();
    }

    [System.Serializable]
    private sealed class FieldNoteSaveData
    {
        public string sourceKey;
        public string name;
        public string signature;
        public float frequencyMHz;
        public float azimuthDegrees;
        public float elevationDegrees;
        public float integrationSeconds;
        public int pulseCount;
        public string timestamp;
        public float confidence;
        public int observationCount;
        public string researchNotes;
        public string hypothesis;
        public string classification;
        public List<string> opticalObservations = new List<string>();
    }

    private static readonly SignalDefinition[] SignalSources =
    {
        new SignalDefinition(SignalKind.HydrogenLine, "Neutral Hydrogen Line", "BROAD EMISSION LINE", 1420.405f, 0.28f, 73.4f, 24f, 30f),
        new SignalDefinition(SignalKind.Pulsar, "Repeating Pulse Train", "PERIODIC PULSE TRAIN", 437.250f, 0.11f, 247.8f, 38.5f, 44f),
        new SignalDefinition(SignalKind.DriftingCarrier, "Uncatalogued Drifting Carrier", "DRIFTING NARROW CARRIER", 91.700f, 0.13f, 306.2f, 62f, 39f)
    };

    private readonly List<FieldNote> fieldNotebook = new List<FieldNote>();
    private readonly float[] spectrumSamples = new float[SpectrumBinCount];

    [SerializeField] private DishController dishController;
    private TextMesh screenReadout;
    private bool isOperating;
    private bool notebookVisible;
    private bool observing;
    private float nextReadoutRefresh;
    private float tunedFrequencyMHz = 1420.405f;
    private float receiverGainDb = 18f;
    private float liveNoiseFloor;
    private float liveSignalExcess;
    private float liveSignalStrength;
    private float requiredSignalExcess;
    private int liveSignalIndex = -1;
    private int captureSignalIndex = -1;
    private float captureWallSeconds;
    private float captureIntegrationSeconds;
    private float captureLostLockSeconds;
    private float captureAnchorAzimuth;
    private float captureAnchorElevation;
    private int capturedPulseCount;
    private bool pulseWasActive;
    private string lastObservationMessage = "NO OBSERVATIONS LOGGED";
    private string formattedReadout = "FARFIELD ARRAY\nRECEIVER STANDBY\nNO OBSERVATION";
    private GUIStyle headingStyle;
    private GUIStyle bodyStyle;
    private GUIStyle smallStyle;
    private bool guiStylesReady;
    private bool notebookLoaded;
    private bool notebookTextFieldFocused;
    private int selectedNotebookIndex;
    private Vector2 notebookListScrollPosition;
    private Vector2 notebookDetailScrollPosition;
    private CursorLockMode previousCursorLockState;
    private bool previousCursorVisible;
    private bool notebookCursorStateCaptured;
    private GUIStyle notebookTextAreaStyle;

    /// <summary>Whether the player is currently operating this console.</summary>
    public bool IsOperating => isOperating;

    /// <summary>Adds an optical finding to the research notebook and persists it immediately.</summary>
    public void RecordOpticalObservation(string targetName, string observationDetail)
    {
        EnsureNotebookLoaded();
        string normalizedTargetName = LimitText(targetName == null ? string.Empty : targetName.Trim(), 80);
        string normalizedDetail = LimitText(observationDetail == null ? string.Empty : observationDetail.Trim(), MaximumOpticalDetailLength);
        if (string.IsNullOrWhiteSpace(normalizedTargetName) || string.IsNullOrWhiteSpace(normalizedDetail))
        {
            return;
        }

        string sourceKey = "optical:" + normalizedTargetName;
        FieldNote note = FindFieldNoteByName(normalizedTargetName);
        if (note == null)
        {
            note = FindFieldNote(sourceKey);
        }
        if (note == null)
        {
            note = new FieldNote
            {
                SourceKey = sourceKey,
                Name = normalizedTargetName,
                Signature = "OPTICAL FIELD ENTRY",
                Timestamp = GetSessionTimestamp(),
                Classification = ClassificationOptions[0],
                ObservationCount = 0
            };
            fieldNotebook.Add(note);
        }

        note.OpticalObservations.Add(normalizedDetail);
        note.Timestamp = GetSessionTimestamp();
        if (note.FrequencyMHz <= 0f)
        {
            note.ObservationCount++;
        }
        selectedNotebookIndex = fieldNotebook.IndexOf(note);
        lastObservationMessage = $"OPTICAL FINDING LOGGED // {normalizedTargetName.ToUpperInvariant()}";
        SaveNotebook();
    }

    public void ConnectDish(DishController dish) => dishController = dish;

    private void Start()
    {
        if (dishController != null) Initialize(dishController);
    }

    /// <summary>Connects the console to its dish and creates its visible screen readout.</summary>
    public void Initialize(DishController generatedDish)
    {
        EnsureNotebookLoaded();
        dishController = generatedDish;
        if (screenReadout == null) CreateScreenReadout();
        UpdateMeasurement();
        RefreshReadout();
    }

    /// <summary>Enters operation mode when the generated dish connection is available.</summary>
    public bool TryBeginOperation()
    {
        if (dishController == null)
        {
            return false;
        }

        isOperating = true;
        notebookVisible = false;
        nextReadoutRefresh = 0f;
        return true;
    }

    /// <summary>Leaves operation mode and stops all active dish motion.</summary>
    public void ExitOperation()
    {
        if (!isOperating)
        {
            return;
        }

        SetNotebookVisible(false);
        isOperating = false;
        if (dishController != null)
        {
            dishController.StopMotion();
        }

        CancelObservation("OBSERVATION PAUSED // CONSOLE RELEASED");
        nextReadoutRefresh = 0f;
        RefreshReadout();
    }

    private void Awake()
    {
        EnsureNotebookLoaded();
    }

    private void Update()
    {
        if (!isOperating)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            ExitOperation();
            return;
        }

        if (dishController == null)
        {
            ExitOperation();
            return;
        }

        if (!notebookVisible && keyboard.nKey.wasPressedThisFrame)
        {
            SetNotebookVisible(true);
        }
        else if (notebookVisible && !notebookTextFieldFocused && keyboard.nKey.wasPressedThisFrame)
        {
            SetNotebookVisible(false);
        }

        if (notebookVisible)
        {
            if (!notebookTextFieldFocused)
            {
                if (keyboard.leftBracketKey.wasPressedThisFrame)
                {
                    SelectNotebookEntry(-1);
                }
                if (keyboard.rightBracketKey.wasPressedThisFrame)
                {
                    SelectNotebookEntry(1);
                }
            }

            dishController.SetSlewInput(0f, 0f);
            UpdateMeasurement();
            UpdateObservation(Time.unscaledDeltaTime);
            if (Time.unscaledTime >= nextReadoutRefresh)
            {
                nextReadoutRefresh = Time.unscaledTime + ReadoutRefreshSeconds;
                RefreshReadout();
            }
            return;
        }

        if (keyboard.qKey.wasPressedThisFrame)
        {
            dishController.ToggleScan();
        }
        if (keyboard.rKey.wasPressedThisFrame)
        {
            ToggleObservation();
        }

        UpdateTuningAndGain(keyboard, Time.unscaledDeltaTime);

        float azimuthInput = 0f;
        float elevationInput = 0f;
        if (keyboard.aKey.isPressed)
        {
            azimuthInput -= 1f;
        }
        if (keyboard.dKey.isPressed)
        {
            azimuthInput += 1f;
        }
        if (keyboard.sKey.isPressed)
        {
            elevationInput -= 1f;
        }
        if (keyboard.wKey.isPressed)
        {
            elevationInput += 1f;
        }

        dishController.SetSlewInput(azimuthInput, elevationInput);
        UpdateMeasurement();
        UpdateObservation(Time.unscaledDeltaTime);

        if (Time.unscaledTime >= nextReadoutRefresh)
        {
            nextReadoutRefresh = Time.unscaledTime + ReadoutRefreshSeconds;
            RefreshReadout();
        }
    }

    private void UpdateTuningAndGain(Keyboard keyboard, float deltaTime)
    {
        float fineTuneDirection = (keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.leftArrowKey.isPressed ? 1f : 0f);
        float coarseTuneDirection = (keyboard.pageUpKey.isPressed ? 1f : 0f) - (keyboard.pageDownKey.isPressed ? 1f : 0f);
        float gainDirection = (keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.downArrowKey.isPressed ? 1f : 0f);

        float frequencyChange = fineTuneDirection * FineTuneRateMHzPerSecond * deltaTime
            + coarseTuneDirection * CoarseTuneRateMHzPerSecond * deltaTime;
        if (keyboard.leftArrowKey.wasPressedThisFrame)
        {
            frequencyChange -= FineTuneStepMHz;
        }
        if (keyboard.rightArrowKey.wasPressedThisFrame)
        {
            frequencyChange += FineTuneStepMHz;
        }
        if (keyboard.pageDownKey.wasPressedThisFrame)
        {
            frequencyChange -= CoarseTuneStepMHz;
        }
        if (keyboard.pageUpKey.wasPressedThisFrame)
        {
            frequencyChange += CoarseTuneStepMHz;
        }
        tunedFrequencyMHz = Mathf.Clamp(tunedFrequencyMHz + frequencyChange, MinimumFrequencyMHz, MaximumFrequencyMHz);

        float gainChange = gainDirection * GainRateDbPerSecond * deltaTime;
        if (keyboard.upArrowKey.wasPressedThisFrame)
        {
            gainChange += GainStepDb;
        }
        if (keyboard.downArrowKey.wasPressedThisFrame)
        {
            gainChange -= GainStepDb;
        }
        receiverGainDb = Mathf.Clamp(receiverGainDb + gainChange, MinimumGainDb, MaximumGainDb);
    }

    private void ToggleObservation()
    {
        if (observing)
        {
            CancelObservation("OBSERVATION ABORTED BY OPERATOR");
            return;
        }

        observing = true;
        captureSignalIndex = -1;
        captureWallSeconds = 0f;
        captureIntegrationSeconds = 0f;
        captureLostLockSeconds = 0f;
        capturedPulseCount = 0;
        pulseWasActive = false;
        lastObservationMessage = "OBSERVATION ARMED // SEEK A STABLE FIX";
    }

    private void CancelObservation(string message)
    {
        observing = false;
        captureSignalIndex = -1;
        captureWallSeconds = 0f;
        captureIntegrationSeconds = 0f;
        captureLostLockSeconds = 0f;
        capturedPulseCount = 0;
        pulseWasActive = false;
        lastObservationMessage = message;
    }

    private void UpdateObservation(float deltaTime)
    {
        if (!observing)
        {
            return;
        }

        captureWallSeconds += deltaTime;
        if (captureWallSeconds >= CaptureTimeoutSeconds)
        {
            CancelObservation("NO VALID FIX // TUNE, CENTER, AND RETRY");
            return;
        }

        bool hasCandidate = FindBestPotentialSignal(out int candidateIndex, out float potentialExcess)
            && potentialExcess >= requiredSignalExcess;
        bool dishIsStable = dishController.IsSettled;
        if (!hasCandidate || !dishIsStable)
        {
            captureLostLockSeconds += deltaTime;
            pulseWasActive = false;
            if (captureLostLockSeconds >= LockGraceSeconds)
            {
                captureSignalIndex = -1;
                captureIntegrationSeconds = 0f;
                capturedPulseCount = 0;
                lastObservationMessage = dishController.IsScanning
                    ? "WAITING FOR SWEEP TO STOP // NO DIRECTIONAL FIX"
                    : "WAITING FOR STABLE FIX // TUNE AND CENTER THE PEAK";
            }
            return;
        }

        captureLostLockSeconds = 0f;
        if (captureSignalIndex != candidateIndex)
        {
            BeginDirectionalFix(candidateIndex);
        }
        else if (GetDirectionDifference(captureAnchorAzimuth, captureAnchorElevation, dishController.AzimuthDegrees, dishController.ElevationDegrees) > DirectionConsistencyToleranceDegrees)
        {
            BeginDirectionalFix(candidateIndex);
        }

        captureIntegrationSeconds += deltaTime;
        SignalDefinition source = SignalSources[captureSignalIndex];
        bool pulseIsActive = source.Kind == SignalKind.Pulsar && IsPulseCurrentlyVisible(source);
        if (pulseIsActive && !pulseWasActive)
        {
            capturedPulseCount++;
        }
        pulseWasActive = pulseIsActive;

        float requiredDuration = source.Kind == SignalKind.Pulsar ? PulseObservationDurationSeconds : ObservationDurationSeconds;
        if (source.Kind == SignalKind.Pulsar)
        {
            lastObservationMessage = $"PULSE TRAIN INTEGRATING // {capturedPulseCount} BURSTS";
            if (captureIntegrationSeconds >= requiredDuration && capturedPulseCount >= 2)
            {
                CompleteObservation(captureSignalIndex);
            }
        }
        else
        {
            lastObservationMessage = $"DIRECTIONAL FIX HOLD // {captureIntegrationSeconds:0.0} / {requiredDuration:0.0} SEC";
            if (captureIntegrationSeconds >= requiredDuration)
            {
                CompleteObservation(captureSignalIndex);
            }
        }
    }

    private void BeginDirectionalFix(int signalIndex)
    {
        captureSignalIndex = signalIndex;
        captureIntegrationSeconds = 0f;
        capturedPulseCount = 0;
        pulseWasActive = false;
        captureAnchorAzimuth = dishController.AzimuthDegrees;
        captureAnchorElevation = dishController.ElevationDegrees;
        lastObservationMessage = "DIRECTIONAL FIX ACQUIRED // HOLD POSITION";
    }

    private void CompleteObservation(int signalIndex)
    {
        SignalDefinition source = SignalSources[signalIndex];
        string timestamp = GetSessionTimestamp();
        float confidence = Mathf.Clamp(76f + receiverGainDb * 0.45f + captureIntegrationSeconds * 1.2f, 0f, 99f);
        FieldNote existingNote = FindFieldNote("signal:" + source.Kind);
        if (existingNote == null)
        {
            existingNote = new FieldNote
            {
                SourceKey = "signal:" + source.Kind,
                Name = $"RF SOURCE {signalIndex + 1:00}",
                Signature = source.Signature,
                FrequencyMHz = tunedFrequencyMHz,
                AzimuthDegrees = captureAnchorAzimuth,
                ElevationDegrees = captureAnchorElevation,
                IntegrationSeconds = captureIntegrationSeconds,
                PulseCount = capturedPulseCount,
                Timestamp = timestamp,
                Confidence = confidence,
                ObservationCount = 1
            };
            fieldNotebook.Add(existingNote);
            lastObservationMessage = $"RF SOURCE {signalIndex + 1:00} LOGGED // CLASSIFICATION OPEN";
        }
        else
        {
            existingNote.ObservationCount++;
            existingNote.Confidence = Mathf.Max(existingNote.Confidence, confidence);
            existingNote.IntegrationSeconds = captureIntegrationSeconds;
            existingNote.PulseCount = Mathf.Max(existingNote.PulseCount, capturedPulseCount);
            existingNote.Timestamp = timestamp;
            existingNote.FrequencyMHz = tunedFrequencyMHz;
            existingNote.AzimuthDegrees = captureAnchorAzimuth;
            existingNote.ElevationDegrees = captureAnchorElevation;
            lastObservationMessage = $"REPEAT FIX CONFIRMED // RF SOURCE {signalIndex + 1:00}";
        }

        SaveNotebook();
        observing = false;
        captureSignalIndex = -1;
        captureLostLockSeconds = 0f;
        pulseWasActive = false;
    }

    private FieldNote FindFieldNote(string sourceKey)
    {
        for (int index = 0; index < fieldNotebook.Count; index++)
        {
            if (fieldNotebook[index].SourceKey == sourceKey)
            {
                return fieldNotebook[index];
            }
        }

        return null;
    }

    private FieldNote FindFieldNoteByName(string targetName)
    {
        for (int index = 0; index < fieldNotebook.Count; index++)
        {
            if (string.Equals(fieldNotebook[index].Name, targetName, System.StringComparison.OrdinalIgnoreCase))
            {
                return fieldNotebook[index];
            }
        }

        return null;
    }

    private void EnsureNotebookLoaded()
    {
        if (notebookLoaded)
        {
            return;
        }

        notebookLoaded = true;
        fieldNotebook.Clear();
        string savedJson = PlayerPrefs.GetString(NotebookSaveKey, string.Empty);
        if (string.IsNullOrWhiteSpace(savedJson))
        {
            return;
        }

        try
        {
            NotebookSaveData saveData = JsonUtility.FromJson<NotebookSaveData>(savedJson);
            if (saveData == null || saveData.version != NotebookSaveVersion || saveData.items == null)
            {
                return;
            }

            for (int itemIndex = 0; itemIndex < saveData.items.Count; itemIndex++)
            {
                FieldNoteSaveData savedNote = saveData.items[itemIndex];
                if (savedNote == null || string.IsNullOrWhiteSpace(savedNote.sourceKey) || string.IsNullOrWhiteSpace(savedNote.name))
                {
                    continue;
                }

                if (FindFieldNote(savedNote.sourceKey) != null)
                {
                    continue;
                }

                FieldNote note = new FieldNote
                {
                    SourceKey = LimitText(savedNote.sourceKey.Trim(), 120),
                    Name = LimitText(savedNote.name.Trim(), 80),
                    Signature = LimitText(savedNote.signature ?? string.Empty, 120),
                    FrequencyMHz = IsFinite(savedNote.frequencyMHz) ? Mathf.Clamp(savedNote.frequencyMHz, MinimumFrequencyMHz, MaximumFrequencyMHz) : 0f,
                    AzimuthDegrees = IsFinite(savedNote.azimuthDegrees) ? Mathf.Repeat(savedNote.azimuthDegrees, 360f) : 0f,
                    ElevationDegrees = IsFinite(savedNote.elevationDegrees) ? Mathf.Clamp(savedNote.elevationDegrees, 0f, 90f) : 0f,
                    IntegrationSeconds = IsFinite(savedNote.integrationSeconds) ? Mathf.Max(0f, savedNote.integrationSeconds) : 0f,
                    PulseCount = Mathf.Max(0, savedNote.pulseCount),
                    Timestamp = LimitText(savedNote.timestamp ?? string.Empty, 16),
                    Confidence = IsFinite(savedNote.confidence) ? Mathf.Clamp(savedNote.confidence, 0f, 100f) : 0f,
                    ObservationCount = Mathf.Max(0, savedNote.observationCount),
                    ResearchNotes = LimitText(savedNote.researchNotes ?? string.Empty, MaximumResearchTextLength),
                    Hypothesis = LimitText(savedNote.hypothesis ?? string.Empty, MaximumResearchTextLength),
                    Classification = NormalizeClassification(savedNote.classification),
                    OpticalObservations = new List<string>()
                };

                if (savedNote.opticalObservations != null)
                {
                    for (int observationIndex = 0; observationIndex < savedNote.opticalObservations.Count; observationIndex++)
                    {
                        string opticalDetail = savedNote.opticalObservations[observationIndex];
                        if (!string.IsNullOrWhiteSpace(opticalDetail))
                        {
                            note.OpticalObservations.Add(LimitText(opticalDetail.Trim(), MaximumOpticalDetailLength));
                        }
                    }
                }

                fieldNotebook.Add(note);
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"FARFIELD notebook save could not be loaded and was ignored: {exception.Message}", this);
        }
    }

    private void SaveNotebook()
    {
        EnsureNotebookLoaded();
        NotebookSaveData saveData = new NotebookSaveData { version = NotebookSaveVersion };
        for (int itemIndex = 0; itemIndex < fieldNotebook.Count; itemIndex++)
        {
            FieldNote note = fieldNotebook[itemIndex];
            FieldNoteSaveData savedNote = new FieldNoteSaveData
            {
                sourceKey = note.SourceKey,
                name = note.Name,
                signature = note.Signature,
                frequencyMHz = note.FrequencyMHz,
                azimuthDegrees = note.AzimuthDegrees,
                elevationDegrees = note.ElevationDegrees,
                integrationSeconds = note.IntegrationSeconds,
                pulseCount = note.PulseCount,
                timestamp = note.Timestamp,
                confidence = note.Confidence,
                observationCount = note.ObservationCount,
                researchNotes = note.ResearchNotes,
                hypothesis = note.Hypothesis,
                classification = note.Classification,
                opticalObservations = new List<string>(note.OpticalObservations)
            };
            saveData.items.Add(savedNote);
        }

        try
        {
            PlayerPrefs.SetString(NotebookSaveKey, JsonUtility.ToJson(saveData));
            PlayerPrefs.Save();
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"FARFIELD notebook save could not be written: {exception.Message}", this);
        }
    }

    private static string NormalizeClassification(string classification)
    {
        if (!string.IsNullOrWhiteSpace(classification))
        {
            for (int optionIndex = 0; optionIndex < ClassificationOptions.Length; optionIndex++)
            {
                if (ClassificationOptions[optionIndex] == classification)
                {
                    return classification;
                }
            }
        }

        return ClassificationOptions[0];
    }

    private static string LimitText(string text, int maximumLength)
    {
        if (string.IsNullOrEmpty(text) || maximumLength <= 0)
        {
            return string.Empty;
        }

        return text.Length <= maximumLength ? text : text.Substring(0, maximumLength);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string GetSessionTimestamp()
    {
        return $"{Mathf.FloorToInt(Time.timeSinceLevelLoad / 60f):00}:{Mathf.FloorToInt(Time.timeSinceLevelLoad % 60f):00}";
    }

    private bool IsPulseCurrentlyVisible(SignalDefinition source)
    {
        float angularResponse = GetAngularResponse(source, dishController.AzimuthDegrees, dishController.ElevationDegrees);
        float frequencyResponse = GetFrequencyResponse(source, tunedFrequencyMHz);
        float signalExcess = GetSignalAmplitude(source, tunedFrequencyMHz, true) * angularResponse * frequencyResponse * GetGainMultiplier();
        return signalExcess >= requiredSignalExcess;
    }

    private void UpdateMeasurement()
    {
        if (dishController == null)
        {
            liveNoiseFloor = 0f;
            liveSignalExcess = 0f;
            liveSignalStrength = 0f;
            liveSignalIndex = -1;
            requiredSignalExcess = 0f;
            return;
        }

        float strongestExcess = 0f;
        liveSignalIndex = -1;
        for (int index = 0; index < SignalSources.Length; index++)
        {
            SignalDefinition source = SignalSources[index];
            float signalExcess = GetSignalAmplitude(source, tunedFrequencyMHz, true)
                * GetAngularResponse(source, dishController.AzimuthDegrees, dishController.ElevationDegrees)
                * GetFrequencyResponse(source, tunedFrequencyMHz)
                * GetGainMultiplier();
            if (signalExcess > strongestExcess)
            {
                strongestExcess = signalExcess;
                liveSignalIndex = index;
            }
        }

        requiredSignalExcess = GetRequiredSignalExcess();
        liveNoiseFloor = GetNoiseFloor(tunedFrequencyMHz);
        liveSignalExcess = strongestExcess;
        liveSignalStrength = Mathf.Clamp(liveNoiseFloor + liveSignalExcess, 0f, 100f);
        if (liveSignalExcess < requiredSignalExcess)
        {
            liveSignalIndex = -1;
        }

        float minimumSpectrumFrequency = Mathf.Max(MinimumFrequencyMHz, tunedFrequencyMHz - SpectrumHalfSpanMHz);
        float maximumSpectrumFrequency = Mathf.Min(MaximumFrequencyMHz, tunedFrequencyMHz + SpectrumHalfSpanMHz);
        for (int sampleIndex = 0; sampleIndex < SpectrumBinCount; sampleIndex++)
        {
            float fraction = sampleIndex / (float)(SpectrumBinCount - 1);
            float sampleFrequency = Mathf.Lerp(minimumSpectrumFrequency, maximumSpectrumFrequency, fraction);
            float sampleExcess = GetSignalExcessAtFrequency(sampleFrequency);
            spectrumSamples[sampleIndex] = Mathf.Clamp(GetNoiseFloor(sampleFrequency) + sampleExcess, 0f, 100f);
        }
    }

    private float GetSignalExcessAtFrequency(float frequencyMHz)
    {
        float strongestExcess = 0f;
        for (int index = 0; index < SignalSources.Length; index++)
        {
            SignalDefinition source = SignalSources[index];
            float signalExcess = GetSignalAmplitude(source, frequencyMHz, true)
                * GetAngularResponse(source, dishController.AzimuthDegrees, dishController.ElevationDegrees)
                * GetFrequencyResponse(source, frequencyMHz)
                * GetGainMultiplier();
            strongestExcess = Mathf.Max(strongestExcess, signalExcess);
        }

        return strongestExcess;
    }

    private bool FindBestPotentialSignal(out int bestSignalIndex, out float bestSignalExcess)
    {
        bestSignalIndex = -1;
        bestSignalExcess = 0f;
        if (dishController == null)
        {
            return false;
        }

        for (int index = 0; index < SignalSources.Length; index++)
        {
            SignalDefinition source = SignalSources[index];
            float signalExcess = source.PeakAmplitude
                * GetAngularResponse(source, dishController.AzimuthDegrees, dishController.ElevationDegrees)
                * GetFrequencyResponse(source, tunedFrequencyMHz)
                * GetGainMultiplier();
            if (signalExcess > bestSignalExcess)
            {
                bestSignalExcess = signalExcess;
                bestSignalIndex = index;
            }
        }

        return bestSignalIndex >= 0;
    }

    private float GetSignalAmplitude(SignalDefinition source, float frequencyMHz, bool includeModulation)
    {
        if (!includeModulation)
        {
            return source.PeakAmplitude;
        }

        float time = Time.unscaledTime;
        switch (source.Kind)
        {
            case SignalKind.HydrogenLine:
                return source.PeakAmplitude * (0.88f + 0.12f * Mathf.Sin(time * 0.31f));
            case SignalKind.Pulsar:
                float pulsePhase = Mathf.Repeat(time, PulsePeriodSeconds);
                return source.PeakAmplitude * (pulsePhase < PulseOnDurationSeconds ? 1f : 0.12f);
            case SignalKind.DriftingCarrier:
                float driftAmplitude = 0.82f + 0.18f * Mathf.Sin(time * 0.23f);
                return source.PeakAmplitude * driftAmplitude;
            default:
                return 0f;
        }
    }

    private float GetFrequencyResponse(SignalDefinition source, float frequencyMHz)
    {
        float centerFrequency = source.CenterFrequencyMHz;
        if (source.Kind == SignalKind.DriftingCarrier)
        {
            centerFrequency += Mathf.Sin(Time.unscaledTime * 0.42f + 0.7f) * 0.11f;
        }

        return GetFrequencyResponse(source, frequencyMHz, centerFrequency);
    }

    private static float GetFrequencyResponse(SignalDefinition source, float frequencyMHz, float centerFrequencyMHz)
    {
        float offset = (frequencyMHz - centerFrequencyMHz) / source.FrequencyWidthMHz;
        return Mathf.Exp(-0.5f * offset * offset);
    }

    private static float GetAngularResponse(SignalDefinition source, float azimuthDegrees, float elevationDegrees)
    {
        float azimuthError = Mathf.DeltaAngle(azimuthDegrees, source.AzimuthDegrees);
        float elevationError = elevationDegrees - source.ElevationDegrees;
        float angularError = Mathf.Sqrt(azimuthError * azimuthError + elevationError * elevationError);
        float normalizedError = angularError / BeamSigmaDegrees;
        return Mathf.Exp(-0.5f * normalizedError * normalizedError);
    }

    private float GetNoiseFloor(float frequencyMHz)
    {
        float receiverNoise = 9.5f + receiverGainDb * 0.17f;
        float slowNoiseDrift = (Mathf.Sin(Time.unscaledTime * 1.37f + frequencyMHz * 0.11f) + 1f) * 0.9f;
        return receiverNoise + slowNoiseDrift;
    }

    private float GetRequiredSignalExcess()
    {
        float sensitivityThreshold = Mathf.Lerp(30f, 11f, receiverGainDb / MaximumGainDb);
        float overloadPenalty = Mathf.Max(0f, receiverGainDb - 28f) * 0.8f;
        return sensitivityThreshold + overloadPenalty;
    }

    private float GetGainMultiplier()
    {
        return 1f + receiverGainDb / 18f;
    }

    private static float GetDirectionDifference(float firstAzimuth, float firstElevation, float secondAzimuth, float secondElevation)
    {
        float azimuthDifference = Mathf.DeltaAngle(firstAzimuth, secondAzimuth);
        float elevationDifference = firstElevation - secondElevation;
        return Mathf.Sqrt(azimuthDifference * azimuthDifference + elevationDifference * elevationDifference);
    }

    private void CreateScreenReadout()
    {
        GameObject displayObject = new GameObject("Console Readout Text");
        displayObject.transform.SetParent(transform, false);
        displayObject.transform.localPosition = new Vector3(0f, 0f, -0.56f);
        displayObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        Vector3 parentScale = transform.localScale;
        displayObject.transform.localScale = new Vector3(
            parentScale.x != 0f ? 1f / parentScale.x : 1f,
            parentScale.y != 0f ? 1f / parentScale.y : 1f,
            parentScale.z != 0f ? 1f / parentScale.z : 1f);

        screenReadout = displayObject.AddComponent<TextMesh>();
        screenReadout.anchor = TextAnchor.MiddleCenter;
        screenReadout.alignment = TextAlignment.Center;
        screenReadout.fontSize = 28;
        screenReadout.characterSize = 0.07f;
        screenReadout.color = new Color(0.43f, 0.96f, 1f);
        screenReadout.text = formattedReadout;
    }

    private void RefreshReadout()
    {
        if (dishController == null)
        {
            formattedReadout = "ARRAY CONSOLE\nNO DISH LINK\nCHECK SYSTEM";
        }
        else
        {
            string signalState = liveSignalIndex >= 0 ? "SIGNAL PEAK / DIRECTIONAL RETURN" : "NO DOMINANT PEAK / SEARCHING";
            string operationState = observing ? "OBSERVATION INTEGRATING" : dishController.IsScanning ? "AZIMUTH SWEEP ACTIVE" : isOperating ? "RECEIVER READY" : "RECEIVER STANDBY";
            formattedReadout = $"FARFIELD // SPECTRAL SURVEY\n{tunedFrequencyMHz:0000.000} MHz  GAIN {receiverGainDb:00} dB\nAZ {dishController.AzimuthDegrees:000.0}  EL {dishController.ElevationDegrees:00.0}  RX {liveSignalStrength:00.0}\n{operationState}\n{signalState}";
        }

        if (screenReadout != null)
        {
            screenReadout.text = formattedReadout;
            screenReadout.color = liveSignalIndex >= 0 || observing
                ? new Color(1f, 0.71f, 0.3f)
                : new Color(0.43f, 0.96f, 1f);
        }
    }

    private void SetNotebookVisible(bool visible)
    {
        if (notebookVisible == visible)
        {
            return;
        }

        notebookVisible = visible;
        notebookTextFieldFocused = false;
        if (visible)
        {
            previousCursorLockState = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            notebookCursorStateCaptured = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        if (notebookCursorStateCaptured)
        {
            Cursor.lockState = previousCursorLockState;
            Cursor.visible = previousCursorVisible;
            notebookCursorStateCaptured = false;
        }
    }

    private void SelectNotebookEntry(int direction)
    {
        if (fieldNotebook.Count == 0)
        {
            selectedNotebookIndex = 0;
            return;
        }

        selectedNotebookIndex = (selectedNotebookIndex + direction + fieldNotebook.Count) % fieldNotebook.Count;
        notebookDetailScrollPosition = Vector2.zero;
    }

    private void ChangeClassification(FieldNote note, int direction)
    {
        int currentIndex = 0;
        string currentClassification = NormalizeClassification(note.Classification);
        for (int optionIndex = 0; optionIndex < ClassificationOptions.Length; optionIndex++)
        {
            if (ClassificationOptions[optionIndex] == currentClassification)
            {
                currentIndex = optionIndex;
                break;
            }
        }

        int nextIndex = (currentIndex + direction + ClassificationOptions.Length) % ClassificationOptions.Length;
        note.Classification = ClassificationOptions[nextIndex];
        SaveNotebook();
    }

    private void OnDisable()
    {
        if (notebookVisible)
        {
            SetNotebookVisible(false);
        }
    }

    private void OnGUI()
    {
        if (!isOperating)
        {
            return;
        }

        EnsureGuiStyles();
        Rect panelRect = new Rect(Screen.width - PanelWidth - 22f, Screen.height - PanelHeight - 22f, PanelWidth, PanelHeight);
        GUI.Box(panelRect, GUIContent.none);
        GUI.Label(new Rect(panelRect.x + 16f, panelRect.y + 10f, PanelWidth - 32f, 25f), "FARFIELD // SIGNAL INVESTIGATION", headingStyle);
        string tabLabel = notebookVisible
            ? $"FIELD NOTEBOOK   //   {fieldNotebook.Count} ENTRIES"
            : $"LIVE RECEIVER   {fieldNotebook.Count} LOGGED   |   N FIELD NOTEBOOK";
        float tabWidth = notebookVisible ? PanelWidth - 158f : PanelWidth - 32f;
        GUI.Label(new Rect(panelRect.x + 16f, panelRect.y + 36f, tabWidth, 20f), tabLabel, smallStyle);

        if (notebookVisible)
        {
            if (GUI.Button(new Rect(panelRect.xMax - 136f, panelRect.y + 34f, 120f, 24f), "LIVE RECEIVER"))
            {
                SetNotebookVisible(false);
            }
            DrawNotebook(panelRect);
        }
        else
        {
            DrawReceiver(panelRect);
        }
    }

    private void DrawReceiver(Rect panelRect)
    {
        string scanState = dishController.IsScanning ? "AZIMUTH SWEEP" : dishController.IsSettled ? "ANTENNA STABLE" : "ANTENNA SLEWING";
        string liveState = liveSignalIndex >= 0
            ? $"{SignalSources[liveSignalIndex].Signature} // LIVE RETURN"
            : captureSignalIndex >= 0 && SignalSources[captureSignalIndex].Kind == SignalKind.Pulsar
                ? "PULSE TRAIN // BETWEEN BURSTS"
                : "NO DOMINANT PEAK // TUNE OR SLEW FOR A RETURN";
        GUI.Label(new Rect(panelRect.x + 16f, panelRect.y + 61f, PanelWidth - 32f, 37f),
            $"{tunedFrequencyMHz:0000.000} MHz   |   GAIN {receiverGainDb:00.0} dB   |   RX {liveSignalStrength:00.0} dB\nAZ {dishController.AzimuthDegrees:000.0}°   EL {dishController.ElevationDegrees:00.0}°   |   {scanState}", bodyStyle);

        GUI.Label(new Rect(panelRect.x + 16f, panelRect.y + 101f, PanelWidth - 32f, 18f), "LIVE SPECTRUM  //  ±0.80 MHz AROUND TUNED CENTER", smallStyle);
        Rect graphRect = new Rect(panelRect.x + 16f, panelRect.y + 120f, PanelWidth - 32f, 101f);
        DrawSpectrum(graphRect);

        GUI.Label(new Rect(panelRect.x + 16f, panelRect.y + 226f, PanelWidth - 32f, 20f), liveState, bodyStyle);
        GUI.Label(new Rect(panelRect.x + 16f, panelRect.y + 248f, PanelWidth - 32f, 17f),
            $"SENSITIVITY THRESHOLD {requiredSignalExcess:00.0} dB   |   PEAK EXCESS {liveSignalExcess:00.0} dB", smallStyle);
        Rect strengthRect = new Rect(panelRect.x + 16f, panelRect.y + 267f, PanelWidth - 32f, 10f);
        DrawMeter(strengthRect, Mathf.Clamp01(liveSignalStrength / 100f), new Color(0.23f, 0.79f, 0.91f));

        string observationState = GetObservationStatus();
        GUI.Label(new Rect(panelRect.x + 16f, panelRect.y + 283f, PanelWidth - 32f, 32f), observationState, bodyStyle);
        Rect observationRect = new Rect(panelRect.x + 16f, panelRect.y + 318f, PanelWidth - 32f, 10f);
        DrawMeter(observationRect, GetObservationProgress(), observing ? new Color(1f, 0.62f, 0.22f) : new Color(0.29f, 0.75f, 0.58f));

        GUI.Label(new Rect(panelRect.x + 16f, panelRect.y + 337f, PanelWidth - 32f, 84f),
            "A / D AZIMUTH     W / S ELEVATION     Q SWEEP ON / OFF\n← / → FINE TUNE     PGUP / PGDN 100 MHz     ↑ / ↓ GAIN\nR START / CANCEL OBSERVATION     N FIELD NOTEBOOK     ESC EXIT\nHold a directional peak steady; pulse records require two bursts.", smallStyle);
    }

    private void DrawSpectrum(Rect graphRect)
    {
        GUI.Box(graphRect, GUIContent.none);
        Rect plotRect = new Rect(graphRect.x + 8f, graphRect.y + 7f, graphRect.width - 16f, graphRect.height - 25f);
        Color originalColor = GUI.color;
        GUI.color = new Color(0.23f, 0.39f, 0.45f, 0.42f);
        for (int gridLine = 1; gridLine <= 3; gridLine++)
        {
            float y = plotRect.y + plotRect.height * gridLine * 0.25f;
            GUI.DrawTexture(new Rect(plotRect.x, y, plotRect.width, 1f), Texture2D.whiteTexture);
        }

        GUI.color = new Color(0.27f, 0.82f, 0.88f, 0.92f);
        float barWidth = plotRect.width / SpectrumBinCount;
        for (int sampleIndex = 0; sampleIndex < SpectrumBinCount; sampleIndex++)
        {
            float normalizedLevel = Mathf.Clamp01(spectrumSamples[sampleIndex] / 100f);
            float barHeight = Mathf.Max(1.5f, normalizedLevel * plotRect.height);
            GUI.DrawTexture(new Rect(plotRect.x + sampleIndex * barWidth + 1f, plotRect.yMax - barHeight, Mathf.Max(1f, barWidth - 2f), barHeight), Texture2D.whiteTexture);
        }

        GUI.color = originalColor;
        GUI.Label(new Rect(graphRect.x + 8f, graphRect.yMax - 17f, graphRect.width * 0.5f, 15f), $"{Mathf.Max(MinimumFrequencyMHz, tunedFrequencyMHz - SpectrumHalfSpanMHz):0.00} MHz", smallStyle);
        GUI.Label(new Rect(graphRect.x + graphRect.width * 0.5f, graphRect.yMax - 17f, graphRect.width * 0.5f - 8f, 15f), $"{Mathf.Min(MaximumFrequencyMHz, tunedFrequencyMHz + SpectrumHalfSpanMHz):0.00} MHz", rightSmallStyle);
    }

    private void DrawNotebook(Rect panelRect)
    {
        notebookTextFieldFocused = false;
        GUI.Label(new Rect(panelRect.x + 16f, panelRect.y + 62f, PanelWidth - 32f, 24f),
            "RESEARCH LOG // Select a source, record your own notes, and keep interpretation separate from measurement.", smallStyle);

        Rect entryListRect = new Rect(panelRect.x + 14f, panelRect.y + 89f, 174f, 318f);
        GUI.Box(entryListRect, GUIContent.none);
        GUI.Label(new Rect(entryListRect.x + 9f, entryListRect.y + 7f, entryListRect.width - 18f, 18f), "DISCOVERIES", headingStyle);
        Rect entryViewport = new Rect(entryListRect.x + 7f, entryListRect.y + 31f, entryListRect.width - 14f, entryListRect.height - 38f);
        float entryContentHeight = Mathf.Max(entryViewport.height, fieldNotebook.Count * 43f);
        notebookListScrollPosition = GUI.BeginScrollView(entryViewport, notebookListScrollPosition,
            new Rect(0f, 0f, entryViewport.width - 14f, entryContentHeight));
        if (fieldNotebook.Count == 0)
        {
            GUI.Label(new Rect(5f, 7f, entryViewport.width - 28f, 72f), "No discoveries yet.\nLog an RF detection or optical finding to create the first entry.", smallStyle);
        }
        else
        {
            for (int noteIndex = 0; noteIndex < fieldNotebook.Count; noteIndex++)
            {
                FieldNote note = fieldNotebook[noteIndex];
                string entryType = note.SourceKey.StartsWith("optical:") ? "OPTICAL" : "RADIO";
                string entryLabel = $"{entryType}  {note.Name}\n{NormalizeClassification(note.Classification)}";
                if (GUI.Button(new Rect(3f, noteIndex * 43f, entryViewport.width - 20f, 39f), entryLabel))
                {
                    selectedNotebookIndex = noteIndex;
                    notebookDetailScrollPosition = Vector2.zero;
                }
            }
        }
        GUI.EndScrollView();

        Rect detailRect = new Rect(panelRect.x + 197f, panelRect.y + 89f, PanelWidth - 211f, 318f);
        GUI.Box(detailRect, GUIContent.none);
        if (fieldNotebook.Count == 0)
        {
            selectedNotebookIndex = 0;
            GUI.Label(new Rect(detailRect.x + 12f, detailRect.y + 12f, detailRect.width - 24f, 76f),
                "Your notebook stores each confirmed radio fix and every optical field observation.\n\nWhen a source is detected, its measured signature is recorded as evidence, not a conclusion.", bodyStyle);
        }
        else
        {
            selectedNotebookIndex = Mathf.Clamp(selectedNotebookIndex, 0, fieldNotebook.Count - 1);
            FieldNote note = fieldNotebook[selectedNotebookIndex];
            float contentWidth = detailRect.width - 32f;
            notebookDetailScrollPosition = GUI.BeginScrollView(
                new Rect(detailRect.x + 5f, detailRect.y + 5f, detailRect.width - 10f, detailRect.height - 10f),
                notebookDetailScrollPosition,
                new Rect(0f, 0f, contentWidth, 397f));

            GUI.Label(new Rect(7f, 4f, contentWidth - 14f, 21f), $"{note.Name}  //  {note.SourceKey.StartsWith("optical:") ? "OPTICAL ENTRY" : "RF SOURCE"}", headingStyle);
            string measurementLine = note.FrequencyMHz > 0f
                ? $"{note.FrequencyMHz:0000.000} MHz  |  AZ {note.AzimuthDegrees:000.0}°  EL {note.ElevationDegrees:00.0}°  |  {note.ObservationCount} fixes"
                : $"{note.OpticalObservations.Count} optical observations  |  last logged {note.Timestamp}";
            GUI.Label(new Rect(7f, 26f, contentWidth - 14f, 18f), measurementLine, smallStyle);
            GUI.Label(new Rect(7f, 45f, contentWidth - 14f, 19f),
                note.FrequencyMHz > 0f ? $"MEASURED SIGNATURE: {note.Signature}" : "MEASURED SIGNATURE: optical field observation", smallStyle);

            GUI.Label(new Rect(7f, 66f, contentWidth - 14f, 18f), "OPTICAL FINDINGS", smallStyle);
            string opticalSummary = GetOpticalObservationSummary(note);
            GUI.Label(new Rect(7f, 84f, contentWidth - 14f, 47f), opticalSummary, smallStyle);

            GUI.Label(new Rect(7f, 135f, contentWidth - 14f, 18f), "RESEARCH NOTES  //  editable", smallStyle);
            GUI.SetNextControlName(ResearchTextControlName);
            string updatedResearch = GUI.TextArea(new Rect(7f, 153f, contentWidth - 14f, 49f), note.ResearchNotes ?? string.Empty, notebookTextAreaStyle);
            if (!string.Equals(updatedResearch, note.ResearchNotes, System.StringComparison.Ordinal))
            {
                note.ResearchNotes = LimitText(updatedResearch, MaximumResearchTextLength);
                SaveNotebook();
            }

            GUI.Label(new Rect(7f, 206f, contentWidth - 14f, 18f), "HYPOTHESIS / OPEN QUESTIONS  //  editable", smallStyle);
            GUI.SetNextControlName(HypothesisTextControlName);
            string updatedHypothesis = GUI.TextArea(new Rect(7f, 224f, contentWidth - 14f, 49f), note.Hypothesis ?? string.Empty, notebookTextAreaStyle);
            if (!string.Equals(updatedHypothesis, note.Hypothesis, System.StringComparison.Ordinal))
            {
                note.Hypothesis = LimitText(updatedHypothesis, MaximumResearchTextLength);
                SaveNotebook();
            }

            string focusedControl = GUI.GetNameOfFocusedControl();
            notebookTextFieldFocused = focusedControl == ResearchTextControlName || focusedControl == HypothesisTextControlName;
            GUI.Label(new Rect(7f, 277f, contentWidth - 14f, 18f), "PLAYER CLASSIFICATION  //  choose an interpretation", smallStyle);
            if (GUI.Button(new Rect(7f, 298f, 35f, 25f), "<"))
            {
                ChangeClassification(note, -1);
            }
            GUI.Label(new Rect(46f, 299f, contentWidth - 92f, 24f), NormalizeClassification(note.Classification), bodyStyle);
            if (GUI.Button(new Rect(contentWidth - 42f, 298f, 35f, 25f), ">"))
            {
                ChangeClassification(note, 1);
            }

            string confidenceLine = note.FrequencyMHz > 0f
                ? $"FIX CONFIDENCE {note.Confidence:00}%  |  INTEGRATION {note.IntegrationSeconds:0.0}s  |  OPTICAL {note.OpticalObservations.Count}  |  {note.Timestamp}"
                : $"OPTICAL ENTRY  |  {note.Timestamp}";
            GUI.Label(new Rect(7f, 328f, contentWidth - 14f, 32f), confidenceLine, smallStyle);
            GUI.EndScrollView();
        }

        GUI.Label(new Rect(panelRect.x + 16f, panelRect.y + 410f, PanelWidth - 32f, 22f),
            "CLICK ENTRY TO SELECT   |   [ / ] PREVIOUS / NEXT   |   CLICK TEXT TO EDIT   |   N OR LIVE RECEIVER TO RETURN", smallStyle);
    }

    private string GetOpticalObservationSummary(FieldNote note)
    {
        if (note.OpticalObservations == null || note.OpticalObservations.Count == 0)
        {
            return "No optical findings recorded for this entry.";
        }

        int firstVisibleObservation = Mathf.Max(0, note.OpticalObservations.Count - MaximumDisplayedOpticalObservations);
        string summary = string.Empty;
        for (int observationIndex = firstVisibleObservation; observationIndex < note.OpticalObservations.Count; observationIndex++)
        {
            if (!string.IsNullOrEmpty(summary))
            {
                summary += "\n";
            }
            summary += "• " + note.OpticalObservations[observationIndex];
        }

        return summary;
    }

    private string GetObservationStatus()
    {
        if (!observing)
        {
            return lastObservationMessage;
        }

        if (captureSignalIndex >= 0 && SignalSources[captureSignalIndex].Kind == SignalKind.Pulsar)
        {
            return $"OBSERVATION ACTIVE // PULSE TRAIN {captureIntegrationSeconds:0.0}/{PulseObservationDurationSeconds:0.0}s   BURSTS {capturedPulseCount}/2";
        }

        if (captureSignalIndex >= 0)
        {
            return $"OBSERVATION ACTIVE // {captureIntegrationSeconds:0.0}/{ObservationDurationSeconds:0.0}s STABLE INTEGRATION";
        }

        return captureWallSeconds > 0.1f
            ? $"OBSERVATION ACTIVE // WAITING FOR LOCK ({captureWallSeconds:0.0}s ELAPSED)"
            : "OBSERVATION ARMED // CENTER A SIGNAL AND HOLD POSITION";
    }

    private float GetObservationProgress()
    {
        if (!observing || captureSignalIndex < 0)
        {
            return 0f;
        }

        SignalDefinition source = SignalSources[captureSignalIndex];
        float requiredDuration = source.Kind == SignalKind.Pulsar ? PulseObservationDurationSeconds : ObservationDurationSeconds;
        return Mathf.Clamp01(captureIntegrationSeconds / requiredDuration);
    }

    private static void DrawMeter(Rect meterRect, float fraction, Color fillColor)
    {
        Color originalColor = GUI.color;
        GUI.color = new Color(0.06f, 0.1f, 0.13f, 0.95f);
        GUI.DrawTexture(meterRect, Texture2D.whiteTexture);
        GUI.color = fillColor;
        GUI.DrawTexture(new Rect(meterRect.x, meterRect.y, meterRect.width * Mathf.Clamp01(fraction), meterRect.height), Texture2D.whiteTexture);
        GUI.color = originalColor;
    }

    private GUIStyle rightSmallStyle;

    private void EnsureGuiStyles()
    {
        if (guiStylesReady || GUI.skin == null)
        {
            return;
        }

        headingStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.48f, 0.91f, 1f) }
        };
        bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            wordWrap = true,
            normal = { textColor = new Color(0.85f, 0.92f, 0.97f) }
        };
        smallStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            wordWrap = true,
            normal = { textColor = new Color(0.73f, 0.83f, 0.87f) }
        };
        notebookTextAreaStyle = new GUIStyle(GUI.skin.textArea)
        {
            fontSize = 11,
            wordWrap = true,
            normal = { textColor = new Color(0.88f, 0.94f, 0.97f) }
        };
        rightSmallStyle = new GUIStyle(smallStyle)
        {
            alignment = TextAnchor.UpperRight
        };
        guiStylesReady = true;
    }
}
