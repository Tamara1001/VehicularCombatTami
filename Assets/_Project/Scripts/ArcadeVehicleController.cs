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

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _resourceComponent = GetComponent<VehicleResourceComponent>();
        _rigidbody.useGravity = false;
        _currentGravity = lunarGravity;
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
        float currentAccel = isBoosting ? acceleration * nitroAccelMultiplier : acceleration;
        float currentMaxSpeed = isBoosting ? maximumForwardSpeed * nitroSpeedMultiplier : maximumForwardSpeed;

        float forwardSpeed = Vector3.Dot(_rigidbody.linearVelocity, transform.forward);
        float verticalInput = _movementInput.y;

        if (verticalInput > 0f && forwardSpeed >= currentMaxSpeed) return;
        if (verticalInput < 0f && forwardSpeed <= -maximumReverseSpeed) return;

        Vector3 force = transform.forward * verticalInput * currentAccel;
        _rigidbody.AddForce(force, ForceMode.Acceleration);
    }

    private void ApplyTurning()
    {
        float forwardSpeed = Vector3.Dot(_rigidbody.linearVelocity, transform.forward);
        float speedFactor = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / maximumForwardSpeed);
        float rotationAmount = _movementInput.x * turnSpeed * speedFactor * Time.fixedDeltaTime;

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

        if (IsGrounded() && Physics.Raycast(groundCheck.position, -transform.up, out RaycastHit hit, groundRadius + 0.5f, groundLayer))
        {
            targetUp = hit.normal;
        }

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