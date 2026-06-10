using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controls an arcade-style combat vehicle using Rigidbody movement.
/// Modificado para incluir mecánicas de gravedad lunar y salto.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class ArcadeVehicleController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField]
    [Tooltip("Forward acceleration applied while pressing vertical input.")]
    private float acceleration = 35f;

    [SerializeField]
    [Tooltip("Maximum forward speed.")]
    private float maximumForwardSpeed = 18f;

    [SerializeField]
    [Tooltip("Maximum reverse speed.")]
    private float maximumReverseSpeed = 8f;

    [SerializeField]
    [Tooltip("Vehicle turn speed.")]
    private float turnSpeed = 120f;

    [SerializeField]
    [Tooltip("How strongly sideways velocity is reduced.")]
    private float lateralFriction = 8f;

    [Header("Brake")]
    [SerializeField]
    [Tooltip("Brake force applied while pressing the brake input.")]
    private float brakeForce = 18f;

    [Header("Lunar Mechanics")]
    [SerializeField] private float jumpForce = 15f;
    [SerializeField] private float lunarGravity = -1.62f;
    [SerializeField] private float earthGravity = -9.81f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundRadius = 0.3f;
    [SerializeField] private LayerMask groundLayer;

    private Rigidbody _rigidbody;
    private Vector2 _movementInput;
    private bool _isBraking;

    private bool _isLunarGravity = true;
    private float _currentGravity;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _rigidbody.useGravity = false;
        _currentGravity = lunarGravity;
    }

    private void FixedUpdate()
    {
        ApplyCustomGravity();
        ApplyAcceleration();
        ApplyTurning();
        ApplyLateralFriction();

        if (_isBraking)
        {
            ApplyBrake();
        }
    }

    public void RespondToMoveInput(InputAction.CallbackContext context)
    {
        _movementInput = context.ReadValue<Vector2>();
    }

    public void RespondToBrakeInput(InputAction.CallbackContext context)
    {
        _isBraking = context.ReadValueAsButton();
    }

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
            Debug.Log("Gravedad actual: " + (_isLunarGravity ? "Lunar" : "Terrestre"));
        }
    }

    private void ApplyAcceleration()
    {
        float forwardSpeed = Vector3.Dot(_rigidbody.linearVelocity, transform.forward);
        float verticalInput = _movementInput.y;

        if (verticalInput > 0f && forwardSpeed >= maximumForwardSpeed) return;
        if (verticalInput < 0f && forwardSpeed <= -maximumReverseSpeed) return;

        Vector3 force = transform.forward * verticalInput * acceleration;
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

    private bool IsGrounded()
    {
        if (groundCheck == null) return true;
        return Physics.CheckSphere(groundCheck.position, groundRadius, groundLayer);
    }
}