using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ArcadeVehicleController – Lunar Rover / Arcade Vehicle
/// --------------------------------------------------------
/// Handles arcade-style vehicular movement for a 3D lunar rover.
///
/// Key design decisions:
///  • Rigidbody.useGravity is disabled; gravity is applied manually each
///    FixedUpdate so we can hot-swap between Lunar and Earth gravity.
///  • Acceleration is applied via AddForce in the vehicle's forward direction.
///  • Steering is applied via AddTorque on the world-up axis so the rover
///    rotates independently of the camera.
///  • The gravity toggle is edge-triggered (fires once per button press)
///    using the InputAction "started" callback, not polled every frame.
///  • All Rigidbody writes happen inside FixedUpdate to stay in sync with
///    the physics step.
///
/// Input Setup (Inspector):
///  • moveAction   → InputActionReference for the "Move" action  (Vector2)
///  • gravityToggleAction → InputActionReference for gravity toggle (Button)
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ArcadeVehicleController : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-exposed variables
    // -------------------------------------------------------------------------

    [Header("Movement Settings")]
    [Tooltip("Forward / backward thrust force in Newtons.")]
    [SerializeField] private float accelerationSpeed = 20f;

    [Tooltip("Rotational torque applied when steering left / right.")]
    [SerializeField] private float turnSpeed = 80f;

    [Header("Gravity Settings")]
    [Tooltip("Lunar surface gravity (m/s²). Keep negative (downward). Moon = -1.62.")]
    [SerializeField] private float lunarGravity = -1.62f;

    [Tooltip("Earth surface gravity (m/s²). Keep negative (downward). Earth = -9.81.")]
    [SerializeField] private float earthGravity = -9.81f;

    [Header("Input Actions")]
    [Tooltip("Reference to the 'Move' InputAction (Value / Vector2). " +
             "Bind WASD or Arrow Keys as a 2D Composite.")]
    [SerializeField] private InputActionReference moveAction;

    [Tooltip("Reference to the gravity-toggle InputAction (Button). " +
             "Bind to <Keyboard>/space (or any other key).")]
    [SerializeField] private InputActionReference gravityToggleAction;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private Rigidbody _rb;

    /// <summary>True = using Lunar gravity, False = using Earth gravity.</summary>
    private bool _isLunarGravity = true;

    /// <summary>Cached active gravity value, updated when toggled.</summary>
    private float _currentGravity;

    /// <summary>Raw WASD input read in Update, consumed in FixedUpdate.</summary>
    private Vector2 _moveInput;

    // -------------------------------------------------------------------------
    // Unity Messages
    // -------------------------------------------------------------------------

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();

        // Disable Unity's built-in gravity – we apply our own each FixedUpdate.
        _rb.useGravity = false;

        // Start on Lunar gravity.
        _currentGravity = lunarGravity;
    }

    private void OnEnable()
    {
        // Enable the actions so they start receiving input.
        moveAction.action.Enable();
        gravityToggleAction.action.Enable();

        // Subscribe to the gravity toggle using the "started" phase.
        // "started" fires once on the first frame the button is pressed,
        // which gives us a clean single-toggle behaviour.
        gravityToggleAction.action.started += OnGravityToggle;
    }

    private void OnDisable()
    {
        // Always unsubscribe to prevent memory leaks / ghost callbacks.
        gravityToggleAction.action.started -= OnGravityToggle;

        // Disable the actions when the component is disabled.
        moveAction.action.Disable();
        gravityToggleAction.action.Disable();
    }

    private void Update()
    {
        // Read the raw 2-axis input every frame.
        // X = left/right (steering), Y = forward/backward (throttle).
        _moveInput = moveAction.action.ReadValue<Vector2>();
    }

    private void FixedUpdate()
    {
        ApplyCustomGravity();
        ApplyAcceleration();
        ApplySteering();
    }

    // -------------------------------------------------------------------------
    // Input Callbacks
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called once each time the gravity-toggle button is pressed.
    /// Flips the active gravity mode and logs the change to the Console.
    /// </summary>
    private void OnGravityToggle(InputAction.CallbackContext ctx)
    {
        _isLunarGravity = !_isLunarGravity;
        _currentGravity = _isLunarGravity ? lunarGravity : earthGravity;

        Debug.Log($"[ArcadeVehicleController] Gravity switched to: " +
                  $"{(_isLunarGravity ? "Lunar" : "Earth")} ({_currentGravity} m/s²)");
    }

    // -------------------------------------------------------------------------
    // Physics Helpers (called from FixedUpdate)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Manually applies downward gravitational acceleration each physics step.
    /// Equivalent to: velocity.y += gravity * Time.fixedDeltaTime
    /// We use AddForce with ForceMode.Acceleration so mass does NOT affect the
    /// fall rate (every object falls at the same rate regardless of mass, which
    /// matches real gravity and feels better for arcade physics).
    /// </summary>
    private void ApplyCustomGravity()
    {
        // Vector3.up * _currentGravity gives a downward vector because _currentGravity is negative.
        _rb.AddForce(Vector3.up * _currentGravity, ForceMode.Acceleration);
    }

    /// <summary>
    /// Pushes the rover forward / backward along its local Z axis.
    /// _moveInput.y is +1 when W is held, -1 when S is held.
    /// ForceMode.Force respects mass and drag for a natural feeling response.
    /// </summary>
    private void ApplyAcceleration()
    {
        if (Mathf.Approximately(_moveInput.y, 0f)) return;

        Vector3 thrustForce = transform.forward * (_moveInput.y * accelerationSpeed);
        _rb.AddForce(thrustForce, ForceMode.Force);
    }

    /// <summary>
    /// Rotates the rover around the world Y axis.
    /// We scale turn input by the rover's current forward speed so the rover
    /// only steers meaningfully when it is actually moving — this prevents the
    /// common "spinning in place" artefact with pure torque-based steering.
    ///
    /// _moveInput.x is +1 when D is held (turn right), -1 when A is held (turn left).
    /// ForceMode.Force applies torque respecting the Rigidbody's angular drag.
    /// </summary>
    private void ApplySteering()
    {
        if (Mathf.Approximately(_moveInput.x, 0f)) return;

        // Scale steering by forward velocity so we don't spin in place.
        float forwardSpeed = Vector3.Dot(_rb.linearVelocity, transform.forward);

        // Use Mathf.Sign on forwardSpeed so reversing still steers correctly.
        float speedFactor = Mathf.Clamp(Mathf.Abs(forwardSpeed), 0.1f, 1f);

        // Determine steer direction relative to driving direction.
        float steerDirection = _moveInput.x * Mathf.Sign(forwardSpeed == 0f ? 1f : forwardSpeed);

        Vector3 torque = Vector3.up * (steerDirection * turnSpeed * speedFactor);
        _rb.AddTorque(torque, ForceMode.Force);
    }

    // -------------------------------------------------------------------------
    // Public Read-Only API (useful for HUD / debug display)
    // -------------------------------------------------------------------------

    /// <summary>Returns true if Lunar gravity is currently active.</summary>
    public bool IsLunarGravity => _isLunarGravity;

    /// <summary>Returns the currently active gravity value.</summary>
    public float CurrentGravity => _currentGravity;
}
