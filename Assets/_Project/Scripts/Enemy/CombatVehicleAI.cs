using UnityEngine;

/// <summary>
/// Controls an arcade combat vehicle using pursuit, attack,
/// repositioning, obstacle avoidance and recovery behaviors.
/// </summary>
public sealed class CombatVehicleAI : MonoBehaviour
{
    private enum VehicleAIState
    {
        Pursue,
        Attack,
        Reposition,
        Recover
    }

    [Header("References")]
    [SerializeField]
    [Tooltip("Arcade vehicle controller used by the enemy.")]
    private ArcadeVehicleController vehicleController;

    [SerializeField]
    [Tooltip("Weapon used by the enemy vehicle.")]
    private VehicleWeapon vehicleWeapon;

    [SerializeField]
    [Tooltip("Transform of the vehicle controlled by the player.")]
    private Transform target;

    [SerializeField]
    [Tooltip("Origin used by obstacle detection sensors.")]
    private Transform sensorOrigin;

    [Header("Combat")]
    [SerializeField]
    [Tooltip("Distance below which the enemy moves away from the player.")]
    private float minimumAttackDistance = 7f;

    [SerializeField]
    [Tooltip("Distance the enemy attempts to maintain while attacking.")]
    private float preferredAttackDistance = 14f;

    [SerializeField]
    [Tooltip("Maximum distance from which the enemy can attack.")]
    private float maximumAttackDistance = 24f;

    [SerializeField]
    [Tooltip("Maximum angle allowed before firing.")]
    private float maximumFireAngle = 8f;

    [SerializeField]
    [Tooltip("Lateral distance used when circling the player.")]
    private float attackSideOffset = 6f;

    [SerializeField]
    [Tooltip("Layers considered when checking line of sight.")]
    private LayerMask lineOfSightMask;

    [Header("Navigation")]
    [SerializeField]
    [Tooltip("Angle used to normalize steering input.")]
    private float steeringNormalizationAngle = 45f;

    [Header("Obstacle Detection")]
    [SerializeField]
    [Tooltip("Layers detected as obstacles.")]
    private LayerMask obstacleMask;

    [SerializeField]
    [Tooltip("Maximum distance of the obstacle sensors.")]
    private float sensorDistance = 5f;

    [SerializeField]
    [Tooltip("Horizontal angle of the side sensors.")]
    private float sideSensorAngle = 30f;

    [SerializeField]
    [Tooltip("Influence of obstacle avoidance over steering.")]
    private float avoidanceStrength = 1.25f;

    [Header("Recovery")]
    [SerializeField]
    [Tooltip("Speed below which the vehicle may be considered stuck.")]
    private float stuckSpeedThreshold = 0.5f;

    [SerializeField]
    [Tooltip("Time almost stationary before starting recovery.")]
    private float stuckDetectionTime = 1.5f;

    [SerializeField]
    [Tooltip("Duration of the reverse recovery maneuver.")]
    private float recoveryDuration = 1.25f;

    [Header("Debug")]
    [SerializeField]
    [Tooltip("Current state displayed for debugging.")]
    private VehicleAIState currentState;

    private float orbitDirection = 1f;
    private float recoveryDirection = 1f;
    private float recoveryTimer;
    private float stuckTimer;
    private float lastThrottleInput;

    private void Awake()
    {
        if (vehicleController == null)
        {
            vehicleController = GetComponent<ArcadeVehicleController>();
        }
        if (vehicleWeapon == null)
        {
            vehicleWeapon = GetComponent<VehicleWeapon>();
        }
        if (sensorOrigin == null)
        {
            sensorOrigin = transform.Find("SensorOrigin");
        }
    }

    private void Start()
    {
        if (target == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                target = playerObject.transform;
            }
        }
        currentState = VehicleAIState.Pursue;
    }

    private void FixedUpdate()
    {
        if (vehicleController == null || target == null)
        {
            StopVehicle();
            return;
        }

        if (currentState == VehicleAIState.Recover)
        {
            ExecuteRecovery();
            return;
        }

        UpdateStuckDetection();
        SelectState();
        ExecuteCurrentState();
    }

    private void SelectState()
    {
        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        if (distanceToTarget < minimumAttackDistance)
        {
            ChangeState(VehicleAIState.Reposition);
            return;
        }

        if (distanceToTarget <= maximumAttackDistance && HasLineOfSight())
        {
            ChangeState(VehicleAIState.Attack);
            return;
        }

        ChangeState(VehicleAIState.Pursue);
    }

    private void ExecuteCurrentState()
    {
        switch (currentState)
        {
            case VehicleAIState.Pursue:
                ExecutePursuit();
                break;
            case VehicleAIState.Attack:
                ExecuteAttack();
                break;
            case VehicleAIState.Reposition:
                ExecuteReposition();
                break;
            case VehicleAIState.Recover:
                ExecuteRecovery();
                break;
        }
    }

    private void ExecutePursuit()
    {
        DriveTowards(target.position, 1f);
    }

    private void ExecuteAttack()
    {
        Vector3 directionAwayFromTarget = transform.position - target.position;
        directionAwayFromTarget.y = 0f;

        if (directionAwayFromTarget.sqrMagnitude < 0.01f)
        {
            directionAwayFromTarget = -target.forward;
        }
        directionAwayFromTarget.Normalize();

        Vector3 sideDirection = Vector3.Cross(Vector3.up, directionAwayFromTarget) * orbitDirection;

        Vector3 attackPosition = target.position +
                                 directionAwayFromTarget * preferredAttackDistance +
                                 sideDirection * attackSideOffset;

        DriveTowards(attackPosition, 0.65f);
        TryFireAtTarget();
    }

    private void ExecuteReposition()
    {
        Vector3 directionAwayFromTarget = transform.position - target.position;
        directionAwayFromTarget.y = 0f;

        if (directionAwayFromTarget.sqrMagnitude < 0.01f)
        {
            directionAwayFromTarget = target.forward;
        }
        directionAwayFromTarget.Normalize();

        Vector3 sideDirection = Vector3.Cross(Vector3.up, directionAwayFromTarget) * orbitDirection;

        Vector3 repositionPosition = target.position +
                                     directionAwayFromTarget * preferredAttackDistance +
                                     sideDirection * attackSideOffset;

        DriveTowards(repositionPosition, 1f);
    }

    private void ExecuteRecovery()
    {
        recoveryTimer -= Time.fixedDeltaTime;

        ApplyVehicleInput(recoveryDirection, -0.8f, false);

        if (recoveryTimer <= 0f)
        {
            stuckTimer = 0f;
            ChangeState(VehicleAIState.Pursue);
        }
    }

    private void DriveTowards(Vector3 destination, float desiredThrottle)
    {
        Vector3 directionToDestination = destination - transform.position;
        directionToDestination.y = 0f;

        if (directionToDestination.sqrMagnitude < 0.25f)
        {
            ApplyVehicleInput(0f, 0f, true);
            return;
        }
        directionToDestination.Normalize();

        float angleToDestination = Vector3.SignedAngle(transform.forward, directionToDestination, Vector3.up);

        float destinationSteering = Mathf.Clamp(angleToDestination / steeringNormalizationAngle, -1f, 1f);

        float obstacleSteering = CalculateObstacleAvoidance(out float throttleMultiplier);

        float finalSteering = Mathf.Clamp(destinationSteering + obstacleSteering * avoidanceStrength, -1f, 1f);

        float turnIntensity = Mathf.Abs(finalSteering);
        float turnThrottleMultiplier = Mathf.Lerp(1f, 0.3f, turnIntensity);

        float finalThrottle = desiredThrottle * turnThrottleMultiplier * throttleMultiplier;

        bool shouldBrake = Mathf.Abs(angleToDestination) > 80f && vehicleController.CurrentSpeed > 5f;
        if (shouldBrake)
        {
            finalThrottle = 0f;
        }

        ApplyVehicleInput(finalSteering, finalThrottle, shouldBrake);
    }

    private float CalculateObstacleAvoidance(out float throttleMultiplier)
    {
        throttleMultiplier = 1f;
        if (sensorOrigin == null) return 0f;

        Vector3 origin = sensorOrigin.position;

        Vector3 leftDirection = Quaternion.Euler(0f, -sideSensorAngle, 0f) * transform.forward;
        Vector3 rightDirection = Quaternion.Euler(0f, sideSensorAngle, 0f) * transform.forward;

        bool obstacleInFront = Physics.Raycast(origin, transform.forward, sensorDistance, obstacleMask, QueryTriggerInteraction.Ignore);
        bool obstacleOnLeft = Physics.Raycast(origin, leftDirection, sensorDistance, obstacleMask, QueryTriggerInteraction.Ignore);
        bool obstacleOnRight = Physics.Raycast(origin, rightDirection, sensorDistance, obstacleMask, QueryTriggerInteraction.Ignore);

        float steering = 0f;

        if (obstacleOnLeft) steering += 1f;
        if (obstacleOnRight) steering -= 1f;

        if (obstacleInFront)
        {
            throttleMultiplier = 0.25f;
            if (Mathf.Abs(steering) < 0.1f)
            {
                steering = recoveryDirection;
            }
        }

        return steering;
    }

    private void TryFireAtTarget()
    {
        if (vehicleWeapon == null || vehicleWeapon.FirePoint == null)
        {
            return;
        }

        Vector3 directionToTarget = target.position - vehicleWeapon.FirePoint.position;
        directionToTarget.y = 0f;

        float angleToTarget = Vector3.Angle(vehicleWeapon.FirePoint.forward, directionToTarget);

        if (angleToTarget <= maximumFireAngle && HasLineOfSight())
        {
            vehicleWeapon.TryFire();
        }
    }

    private bool HasLineOfSight()
    {
        if (vehicleWeapon == null || vehicleWeapon.FirePoint == null)
        {
            return false;
        }

        Vector3 origin = vehicleWeapon.FirePoint.position;
        Vector3 targetPosition = target.position + Vector3.up * 0.5f;
        Vector3 direction = targetPosition - origin;
        float distance = direction.magnitude;

        bool detectedObject = Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance, lineOfSightMask, QueryTriggerInteraction.Ignore);

        if (!detectedObject) return false;

        return hit.transform.root == target.root;
    }

    private void UpdateStuckDetection()
    {
        bool isTryingToMove = Mathf.Abs(lastThrottleInput) > 0.5f;
        bool isAlmostStopped = vehicleController.CurrentSpeed < stuckSpeedThreshold;

        if (isTryingToMove && isAlmostStopped)
        {
            stuckTimer += Time.fixedDeltaTime;
        }
        else
        {
            stuckTimer = 0f;
        }

        if (stuckTimer >= stuckDetectionTime)
        {
            ChangeState(VehicleAIState.Recover);
        }
    }

    private void ChangeState(VehicleAIState newState)
    {
        if (currentState == newState) return;
        currentState = newState;

        if (newState == VehicleAIState.Reposition)
        {
            orbitDirection = Random.value < 0.5f ? -1f : 1f;
        }
        if (newState == VehicleAIState.Recover)
        {
            recoveryDirection = Random.value < 0.5f ? -1f : 1f;
            recoveryTimer = recoveryDuration;
        }
    }

    private void ApplyVehicleInput(float steering, float throttle, bool brake)
    {
        lastThrottleInput = throttle;
        vehicleController.SetAIInput(steering, throttle, brake);
    }

    private void StopVehicle()
    {
        lastThrottleInput = 0f;
        if (vehicleController != null)
        {
            vehicleController.SetAIInput(0f, 0f, true);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (sensorOrigin == null) return;

        Vector3 origin = sensorOrigin.position;
        Vector3 leftDirection = Quaternion.Euler(0f, -sideSensorAngle, 0f) * transform.forward;
        Vector3 rightDirection = Quaternion.Euler(0f, sideSensorAngle, 0f) * transform.forward;

        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(origin, transform.forward * sensorDistance);
        Gizmos.DrawRay(origin, leftDirection * sensorDistance);
        Gizmos.DrawRay(origin, rightDirection * sensorDistance);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, minimumAttackDistance);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, maximumAttackDistance);
    }
#endif
}