using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>New Input System first-person movement, mouse look, and focused console interaction.</summary>
[RequireComponent(typeof(CharacterController))]
public sealed class FirstPersonController : MonoBehaviour
{
    private const float WalkSpeedMetersPerSecond = 4.8f;
    private const float RunSpeedMetersPerSecond = 7.6f;
    private const float JumpSpeedMetersPerSecond = 6.4f;
    private const float GravityMetersPerSecondSquared = 22f;
    private const float InteractionDistanceMeters = 4.2f;
    private const float MouseSensitivity = 0.085f;
    private const float MinimumPitchDegrees = -82f;
    private const float MaximumPitchDegrees = 82f;

    private CharacterController characterController;
    private Camera playerCamera;
    private ConsoleOperator focusedConsole;
    private ConsoleOperator operatingConsole;
    private float verticalVelocity;
    private float cameraPitch;
    private GUIStyle hudStyle;
    private GUIStyle promptStyle;
    private bool guiStylesReady;
    private bool initialized;

    /// <summary>Connects the runtime camera and prepares New Input System controls.</summary>
    public void Initialize(Camera generatedCamera)
    {
        playerCamera = generatedCamera;
        characterController = GetComponent<CharacterController>();
        initialized = playerCamera != null && characterController != null;
        LockCursor();
    }

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    private void Update()
    {
        if (!initialized || characterController == null || playerCamera == null)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (operatingConsole != null)
        {
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                operatingConsole.ExitOperation();
                operatingConsole = null;
                return;
            }

            if (!operatingConsole.IsOperating)
            {
                operatingConsole = null;
            }
            else
            {
                return;
            }
        }

        if (keyboard == null)
        {
            focusedConsole = null;
            ApplyGravityOnly();
            return;
        }

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                UnlockCursor();
            }
            else
            {
                LockCursor();
            }
        }
        else if (Cursor.lockState != CursorLockMode.Locked && mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            LockCursor();
        }

        if (Cursor.lockState == CursorLockMode.Locked && mouse != null)
        {
            ApplyMouseLook(mouse);
        }

        UpdateConsoleFocus();
        if (focusedConsole != null && keyboard.eKey.wasPressedThisFrame && focusedConsole.TryBeginOperation())
        {
            operatingConsole = focusedConsole;
            focusedConsole = null;
            return;
        }

        MoveCharacter(keyboard);
    }

    private void ApplyMouseLook(Mouse mouse)
    {
        Vector2 mouseDelta = mouse.delta.ReadValue();
        transform.Rotate(Vector3.up, mouseDelta.x * MouseSensitivity, Space.Self);
        cameraPitch = Mathf.Clamp(cameraPitch - mouseDelta.y * MouseSensitivity, MinimumPitchDegrees, MaximumPitchDegrees);
        playerCamera.transform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
    }

    private void UpdateConsoleFocus()
    {
        focusedConsole = null;
        Ray focusRay = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (!Physics.Raycast(focusRay, out RaycastHit hit, InteractionDistanceMeters, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        focusedConsole = hit.collider.GetComponentInParent<ConsoleOperator>();
    }

    private void MoveCharacter(Keyboard keyboard)
    {
        Vector3 inputDirection = Vector3.zero;
        if (keyboard.wKey.isPressed)
        {
            inputDirection.z += 1f;
        }
        if (keyboard.sKey.isPressed)
        {
            inputDirection.z -= 1f;
        }
        if (keyboard.dKey.isPressed)
        {
            inputDirection.x += 1f;
        }
        if (keyboard.aKey.isPressed)
        {
            inputDirection.x -= 1f;
        }

        inputDirection = Vector3.ClampMagnitude(inputDirection, 1f);
        Vector3 worldDirection = transform.TransformDirection(inputDirection);
        float movementSpeed = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed
            ? RunSpeedMetersPerSecond
            : WalkSpeedMetersPerSecond;

        if (characterController.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        if (characterController.isGrounded && keyboard.spaceKey.wasPressedThisFrame)
        {
            verticalVelocity = JumpSpeedMetersPerSecond;
        }

        verticalVelocity -= GravityMetersPerSecondSquared * Time.deltaTime;
        Vector3 velocity = worldDirection * movementSpeed;
        velocity.y = verticalVelocity;
        characterController.Move(velocity * Time.deltaTime);
    }

    private void ApplyGravityOnly()
    {
        if (characterController.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        verticalVelocity -= GravityMetersPerSecondSquared * Time.deltaTime;
        characterController.Move(Vector3.up * (verticalVelocity * Time.deltaTime));
    }

    private void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnDisable()
    {
        if (initialized)
        {
            UnlockCursor();
        }
    }

    private void OnGUI()
    {
        if (!initialized || operatingConsole != null)
        {
            return;
        }

        EnsureGuiStyles();
        GUI.Box(new Rect(20f, 20f, 360f, 99f), GUIContent.none);
        GUI.Label(new Rect(34f, 29f, 336f, 76f), "FARFIELD // REMOTE ARRAY\nWASD MOVE   SHIFT RUN   SPACE JUMP\nMOUSE LOOK   E OPERATE CONSOLE   ESC CURSOR", hudStyle);

        float centerX = Screen.width * 0.5f;
        float centerY = Screen.height * 0.5f;
        GUI.DrawTexture(new Rect(centerX - 1f, centerY - 1f, 2f, 2f), Texture2D.whiteTexture);

        if (focusedConsole != null)
        {
            GUI.Box(new Rect(centerX - 142f, centerY + 24f, 284f, 36f), GUIContent.none);
            GUI.Label(new Rect(centerX - 132f, centerY + 31f, 264f, 24f), "E   OPERATE ARRAY CONSOLE", promptStyle);
        }

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            GUI.Label(new Rect(Screen.width * 0.5f - 130f, Screen.height - 72f, 260f, 32f), "CLICK TO CAPTURE MOUSE", promptStyle);
        }
    }

    private void EnsureGuiStyles()
    {
        if (guiStylesReady || GUI.skin == null)
        {
            return;
        }

        hudStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
            normal = { textColor = new Color(0.84f, 0.91f, 0.96f) }
        };
        promptStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.54f, 0.95f, 1f) }
        };
        guiStylesReady = true;
    }
}
