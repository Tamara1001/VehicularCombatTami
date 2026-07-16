using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody), typeof(VehicleResourceComponent))]
public sealed class ArcadeVehicleController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float acceleration = 35f;
    [SerializeField] private float maximumForwardSpeed = 18f;
    [SerializeField] private float maximumReverseSpeed = 8f;
    [SerializeField] private float turnSpeed = 120f;
    [SerializeField] private float lateralFriction = 8f;

    [Header("Brake")]
    [SerializeField] private float brakeForce = 18f;

    [Header("Nitro (Boost)")]
    [SerializeField] private float nitroSpeedMultiplier = 1.6f;
    [SerializeField] private float nitroAccelMultiplier = 2f;
    [SerializeField]
    [Tooltip("Costo de energía por segundo al usar Nitro.")]
    private float nitroCostPerSecond = 35f;

    [Header("Lunar Mechanics")]
    [SerializeField] private float jumpForce = 15f;
    [SerializeField] private float lunarGravity = -1.62f;
    [SerializeField] private float earthGravity = -9.81f;
    [SerializeField]
    [Tooltip("Costo de energía por segundo al usar Gravedad Terrestre.")]
    private float earthGravityCostPerSecond = 10f;

    [Header("Stabilization")]
    [Tooltip("Qué tan rápido se endereza el auto en el aire o se adapta a las rampas.")]
    [SerializeField] private float stabilizationSpeed = 5f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundRadius = 0.3f;
    [SerializeField] private LayerMask groundLayer;

    private Rigidbody _rigidbody;
    private VehicleResourceComponent _resourceComponent;
    private Vector2 _movementInput;
    private bool _isBraking;
    private bool _isNitroPressed;

    private bool _isLunarGravity = true;
    private float _currentGravity;

    // -------------------------------------------------------------------------
    // Power-Up Multipliers
    // These are written by a future PowerUpHandler and read each physics tick.
    // They stack cleanly on top of Nitro and all existing movement math.
    // Clamped to 0.1f so the vehicle can never have zero or negative core stats.
    // -------------------------------------------------------------------------

    private float _speedMultiplier       = 1f;
    private float _accelerationMultiplier = 1f;
    private float _turnSpeedMultiplier   = 1f;

    /// <summary>Scales <c>maximumForwardSpeed</c> (and the Nitro boosted cap). Default: 1.</summary>
    public float SpeedMultiplier
    {
        get => _speedMultiplier;
        set => _speedMultiplier = Mathf.Max(0.1f, value);
    }

    /// <summary>Scales the base <c>acceleration</c> force (and the Nitro boosted force). Default: 1.</summary>
    public float AccelerationMultiplier
    {
        get => _accelerationMultiplier;
        set => _accelerationMultiplier = Mathf.Max(0.1f, value);
    }

    /// <summary>Scales <c>turnSpeed</c> degrees-per-second. Default: 1.</summary>
    public float TurnSpeedMultiplier
    {
        get => _turnSpeedMultiplier;
        set => _turnSpeedMultiplier = Mathf.Max(0.1f, value);
    }

    private void Awake()
    {
        _rigidbody         = GetComponent<Rigidbody>();
        _resourceComponent = GetComponent<VehicleResourceComponent>();
        _rigidbody.useGravity = false;
        _currentGravity    = lunarGravity;

        // -------------------------------------------------------
        // Auto-discovery: if groundCheck was not assigned in the
        // Inspector (or the prefab link broke at runtime), search
        // the vehicle's own hierarchy for a child named "GroundCheck".
        // This makes the component resilient to missing assignments
        // without requiring the designer to re-wire the prefab.
        // -------------------------------------------------------
        if (groundCheck == null)
        {
            groundCheck = transform.Find("GroundCheck");

            if (groundCheck != null)
            {
                Debug.Log("[ArcadeVehicleController] groundCheck was unassigned — " +
                          $"auto-discovered '{groundCheck.name}' in the hierarchy.", this);
            }
            else
            {
                // Single warning only: FixedUpdate will not throw because
                // ApplyStabilization() guards every access to groundCheck.
                Debug.LogWarning("[ArcadeVehicleController] groundCheck is null and could not be " +
                                 "found as a child named 'GroundCheck'. Ground detection will be " +
                                 "skipped. Add a 'GroundCheck' child Transform to the vehicle prefab.", this);
            }
        }
    }

    private void FixedUpdate()
    {
        ManageGravityState();
        bool isActuallyBoosting = ManageNitroState();

        ApplyCustomGravity();
        ApplyAcceleration(isActuallyBoosting);
        ApplyTurning();
        ApplyLateralFriction();

        if (_isBraking) ApplyBrake();

        ApplyStabilization();
    }

    // --- INPUTS ---
    public void RespondToMoveInput(InputAction.CallbackContext context) => _movementInput = context.ReadValue<Vector2>();
    public void RespondToBrakeInput(InputAction.CallbackContext context) => _isBraking = context.ReadValueAsButton();
    public void RespondToBoostInput(InputAction.CallbackContext context) => _isNitroPressed = context.ReadValueAsButton();

    public void RespondToJumpInput(InputAction.CallbackContext context)
    {
        if (context.performed && IsGrounded())
        {
            _rigidbody.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        }
    }

    public void RespondToGravityToggle(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            _isLunarGravity = !_isLunarGravity;
            _currentGravity = _isLunarGravity ? lunarGravity : earthGravity;
        }
    }

    // --- PUENTE PARA LA IA ---
    public float CurrentSpeed
    {
        get
        {
            Vector3 velocity = _rigidbody.linearVelocity;
            velocity.y = 0f;
            return velocity.magnitude;
        }
    }

    public void SetAIInput(float steering, float throttle, bool brake)
    {
        _movementInput = new Vector2(steering, throttle);
        _isBraking = brake;
    }

    public void ClearAIInput()
    {
        _movementInput = Vector2.zero;
        _isBraking = false;
    }

    // --- LÓGICA DE ENERGÍA ---
    private void ManageGravityState()
    {
        if (!_isLunarGravity)
        {
            if (!_resourceComponent.TryConsumeContinuous(earthGravityCostPerSecond))
            {
                _isLunarGravity = true;
                _currentGravity = lunarGravity;
                Debug.Log("Gravedad forzada a Lunar: ¡Falta de energía!");
            }
        }
    }

    private bool ManageNitroState()
    {
        if (_isNitroPressed && _movementInput.y > 0)
        {
            return _resourceComponent.TryConsumeContinuous(nitroCostPerSecond);
        }
        return false;
    }

    // --- FÍSICAS MODIFICADAS ---
    private void ApplyAcceleration(bool isBoosting)
    {
        // Apply power-up multipliers first, then layer Nitro on top.
        float currentAccel    = (isBoosting ? acceleration * nitroAccelMultiplier : acceleration)
                                * AccelerationMultiplier;
        float currentMaxSpeed = (isBoosting ? maximumForwardSpeed * nitroSpeedMultiplier : maximumForwardSpeed)
                                * SpeedMultiplier;

        float forwardSpeed  = Vector3.Dot(_rigidbody.linearVelocity, transform.forward);
        float verticalInput = _movementInput.y;

        if (verticalInput > 0f && forwardSpeed >= currentMaxSpeed) return;
        if (verticalInput < 0f && forwardSpeed <= -maximumReverseSpeed) return;

        Vector3 force = transform.forward * verticalInput * currentAccel;
        _rigidbody.AddForce(force, ForceMode.Acceleration);
    }

    private void ApplyTurning()
    {
        float forwardSpeed   = Vector3.Dot(_rigidbody.linearVelocity, transform.forward);
        float speedFactor    = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / maximumForwardSpeed);
        // TurnSpeedMultiplier scales the effective turn speed for power-up effects.
        float rotationAmount = _movementInput.x * (turnSpeed * TurnSpeedMultiplier) * speedFactor * Time.fixedDeltaTime;

        Quaternion rotation = Quaternion.Euler(0f, rotationAmount, 0f);
        _rigidbody.MoveRotation(_rigidbody.rotation * rotation);
    }

    private void ApplyLateralFriction()
    {
        Vector3 localVelocity = transform.InverseTransformDirection(_rigidbody.linearVelocity);
        localVelocity.x = Mathf.Lerp(localVelocity.x, 0f, lateralFriction * Time.fixedDeltaTime);
        _rigidbody.linearVelocity = transform.TransformDirection(localVelocity);
    }

    private void ApplyBrake()
    {
        _rigidbody.linearVelocity = Vector3.Lerp(_rigidbody.linearVelocity, Vector3.zero, brakeForce * Time.fixedDeltaTime);
    }

    private void ApplyCustomGravity()
    {
        _rigidbody.AddForce(Vector3.up * _currentGravity, ForceMode.Acceleration);
    }

    private void ApplyStabilization()
    {
        Vector3 targetUp = Vector3.up;

        // Guard: only run the slope-normal raycast when groundCheck exists AND
        // the vehicle is confirmed grounded. Separating these two conditions
        // prevents the NullReferenceException that occurred because IsGrounded()
        // returns 'true' when groundCheck is null (safe fallback), which caused
        // the right-hand side of the original '&&' to evaluate groundCheck.position
        // even with a null reference — crashing FixedUpdate 50+ times per second.
        if (groundCheck != null && IsGrounded())
        {
            if (Physics.Raycast(groundCheck.position, -transform.up,
                                out RaycastHit hit, groundRadius + 0.5f, groundLayer))
            {
                targetUp = hit.normal;
            }
        }
        // else: groundCheck is missing → keep targetUp = Vector3.up so the
        // vehicle still self-levels in the air without any raycast dependency.

        Vector3 projectedForward = Vector3.ProjectOnPlane(transform.forward, targetUp).normalized;

        if (projectedForward.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(projectedForward, targetUp);
            _rigidbody.MoveRotation(Quaternion.Slerp(_rigidbody.rotation, targetRotation, stabilizationSpeed * Time.fixedDeltaTime));
        }
    }

    private bool IsGrounded()
    {
        if (groundCheck == null) return true;
        return Physics.CheckSphere(groundCheck.position, groundRadius, groundLayer);
    }
}