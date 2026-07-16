using System;
using UnityEngine;

/// <summary>
/// Controls an arcade combat vehicle using pursuit, attack,
/// repositioning, obstacle avoidance and recovery behaviors.
/// </summary>
public sealed class CombatVehicleAI : MonoBehaviour
{
    /// <summary>
    /// Determines the high-level combat personality of this enemy vehicle.
    /// </summary>
    public enum EnemyType
    {
        /// <summary>Orbits the player and fires its weapon.</summary>
        Shooter,
        /// <summary>Charges directly at the player to ram it.</summary>
        Kamikaze
    }

    private enum VehicleAIState
    {
        Pursue,
        Attack,
        Reposition,
        Recover
    }

    [Header("Enemy Type")]
    [SerializeField]
    [Tooltip("Shooter orbits the player and fires. Kamikaze charges directly to ram.")]
    private EnemyType enemyType = EnemyType.Shooter;

    [Header("Kamikaze Settings")]
    [SerializeField]
    [Tooltip("Throttle multiplier applied during the Kamikaze charge. Values above 1 allow it to exceed the normal speed cap.")]
    private float kamikazeChargeThrottle = 1.5f;

    [SerializeField]
    [Tooltip("Damage dealt to the target's IDamageable component on a successful ram collision.")]
    private int ramDamage = 20;

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
    [Tooltip("Maximum flat (horizontal-plane) angle between FirePoint.forward and the target " +
             "before the weapon is allowed to fire. 30° gives the turret rotation a comfortable " +
             "window while keeping shots reasonably on-target.")]
    private float maximumFireAngle = 30f;

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

    // ----------------------------------------------------------
    // HEALTH / DEATH
    // ----------------------------------------------------------

    /// <summary>
    /// Fired when this vehicle's health reaches zero.
    /// WinConditionManager and any VFX/audio listeners subscribe here.
    /// </summary>
    public event Action OnEnemyDied;

    /// <summary>Reference to the HealthComponent on this GameObject.</summary>
    private HealthComponent _health;

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

        // Subscribe to the health system so this AI responds to death
        // through the same pipeline as EnemyVehicleBase.
        _health = GetComponent<HealthComponent>();
        if (_health != null)
        {
            _health.OnDied += OnHealthDepleted;
        }
        else
        {
            Debug.LogError($"[{name}] CombatVehicleAI requires a HealthComponent. "
                         + "Add one to this GameObject.", this);
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

        // Forward the player transform to the weapon so that homing projectiles
        // spawned by this enemy automatically chase the correct target.
        // For Kamikaze enemies vehicleWeapon may be null — the null-check is safe.
        if (vehicleWeapon != null)
        {
            vehicleWeapon.SetAITarget(target);
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
        switch (enemyType)
        {
            case EnemyType.Shooter:
                ExecuteShooterAttack();
                break;

            case EnemyType.Kamikaze:
                ExecuteKamikazeAttack();
                break;
        }
    }

    /// <summary>
    /// Shooter attack: calculates an orbital position offset to the side of the
    /// player and drives towards it while attempting to fire.
    /// </summary>
    private void ExecuteShooterAttack()
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

    /// <summary>
    /// Kamikaze attack: ignores orbital math entirely and charges straight at the
    /// player using an aggressive throttle multiplier. Never fires its weapon.
    /// </summary>
    private void ExecuteKamikazeAttack()
    {
        DriveTowards(target.position, kamikazeChargeThrottle);
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

        // Project both vectors onto the horizontal plane so that vertical tilt
        // from ramps or homing arcs does not inflate the measured angle and
        // accidentally prevent the weapon from firing.
        Vector3 directionToTarget = target.position - vehicleWeapon.FirePoint.position;
        directionToTarget.y = 0f;

        Vector3 flatFireForward = vehicleWeapon.FirePoint.forward;
        flatFireForward.y = 0f;

        // Guard against degenerate zero-length vectors (e.g. turret directly
        // above/below the target on a steep ramp).
        if (flatFireForward.sqrMagnitude < 0.001f || directionToTarget.sqrMagnitude < 0.001f)
        {
            return;
        }

        float angleToTarget = Vector3.Angle(flatFireForward, directionToTarget);

        if (angleToTarget <= maximumFireAngle && HasLineOfSight())
        {
            vehicleWeapon.TryFire();
        }
    }

    /// <summary>
    /// Handles Kamikaze ram damage on physical collision.
    /// Only applies damage when this vehicle is in the Attack state and is
    /// configured as a Kamikaze, preventing accidental damage during pursuit
    /// or recovery phases.
    /// </summary>
    private void OnCollisionEnter(Collision collision)
    {
        // DIAGNOSTIC: Log every collision so we can confirm physics events are firing.
        Debug.Log($"💥 [KAMIKAZE] Collision detected with: {collision.gameObject.name}");

        // Only the Kamikaze type deals ram damage.
        if (enemyType != EnemyType.Kamikaze) return;

        // Only deal damage during the attack charge, not during pursuit or recovery.
        if (currentState != VehicleAIState.Attack) return;

        // Self-damage guard: skip any collider that belongs to this vehicle's own
        // hierarchy.  Comparing transform.root handles multi-collider rigs where the
        // contact point may be a child collider rather than the root itself.
        if (collision.transform.root == transform.root) return;

        // Walk up the collision hierarchy to find the IDamageable implementation on
        // the root GameObject (HealthComponent).  This ensures the hit registers
        // correctly on vehicles that distribute physics across multiple child
        // colliders (wheels, body panels, etc.).
        IDamageable target = collision.gameObject.GetComponentInParent<IDamageable>();
        if (target != null)
        {
            Debug.Log($"🩸 [KAMIKAZE] Applying damage to: {target}");
            target.TakeDamage(ramDamage);
        }
        else
        {
            Debug.LogWarning("⚠️ [KAMIKAZE] Collision detected but no IDamageable found in parent.");
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
        // Gate on states where the vehicle is actively expected to be moving.
        // This avoids false positives during Reposition (which can legitimately
        // crawl) and eliminates the old lastThrottleInput gate that obstacle
        // avoidance would suppress by clamping the throttle to 0.25f.
        bool isActiveMovementState = currentState == VehicleAIState.Pursue ||
                                     currentState == VehicleAIState.Attack;

        bool isAlmostStopped = vehicleController.CurrentSpeed < stuckSpeedThreshold;

        if (isActiveMovementState && isAlmostStopped)
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
            orbitDirection = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        }
        if (newState == VehicleAIState.Recover)
        {
            recoveryDirection = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            recoveryTimer = recoveryDuration;
        }
    }

    private void ApplyVehicleInput(float steering, float throttle, bool brake)
    {
        vehicleController.SetAIInput(steering, throttle, brake);
    }

    private void StopVehicle()
    {
        if (vehicleController != null)
        {
            vehicleController.SetAIInput(0f, 0f, true);
        }
    }

    // ----------------------------------------------------------
    // DEATH HANDLER
    // ----------------------------------------------------------

    /// <summary>
    /// Invoked by HealthComponent.OnDied.
    /// Order of operations mirrors EnemyVehicleBase.OnHealthDepleted:
    ///   1. Stop all vehicle movement immediately.
    ///   2. Notify external listeners (WinConditionManager, VFX, etc.)
    ///      while the GameObject still exists.
    ///   3. Destroy the GameObject — Unity will call OnDestroy, which
    ///      cleanly unsubscribes from HealthComponent.OnDied.
    /// </summary>
    private void OnHealthDepleted()
    {
        // 1. Kill movement immediately.
        StopVehicle();

        // 2. Notify all external listeners synchronously.
        OnEnemyDied?.Invoke();

        // 3. Remove the GameObject from the arena.
        Debug.Log($"[{name}] CombatVehicleAI destroyed after health depleted.", this);
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // Prevent dangling event subscriptions if the object is destroyed
        // by any other means (scene reload, editor stop, etc.).
        if (_health != null)
        {
            _health.OnDied -= OnHealthDepleted;
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