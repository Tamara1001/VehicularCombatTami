// ==============================================================
// EnemyShooter.cs
// --------------------------------------------------------------
// PURPOSE:
//   Concrete enemy that maintains an optimal combat distance from
//   the player and fires via an independently-aimed turret. The
//   vehicle chassis steers to hold a safe orbit range; the turret
//   rotates on its own Y-axis to track and fire at the player
//   completely independently of the chassis direction.
//
// FSM FLOW:
//   Patrol ──(detect player)──► Chase ──(enter range)──► Attack
//              ◄──(player lost)──            ◄──(player escaped)──
//
// ATTACK STATE DETAIL:
//   ┌──────────────────────────────────────────────────────────┐
//   │  ATTACK — two parallel systems running every frame:      │
//   │                                                          │
//   │  1. CHASSIS MOVEMENT (FixedUpdate / OnAttackUpdate)      │
//   │     • If too close → drive to orbit point (behind+side)  │
//   │     • If in range  → hold position / light orbit         │
//   │     • If too far   → transition to Chase                 │
//   │                                                          │
//   │  2. TURRET AIMING (OnAttackUpdate / Update)              │
//   │     • Rotates turret Transform on local Y axis toward     │
//   │       Player using RotateTowards (max degrees/sec)       │
//   │     • Fires via VehicleWeapon.TryFireAI() when aligned   │
//   │       within a configurable aim tolerance angle          │
//   └──────────────────────────────────────────────────────────┘
//
// ORBIT / KEEP-DISTANCE DESIGN:
//   Rather than stopping dead (which looks robotic), the Shooter
//   continuously recalculates an orbit destination:
//     orbitPoint = Player.position
//                + (-Player-to-Shooter direction) * optimalRange
//   This keeps it at the desired radius while circling slightly
//   as the player moves, creating organic-looking evasion.
//   If the chassis is closer than minimumSafeDistance, it steers
//   away immediately using RequestNavPathRefresh().
//
// TURRET INDEPENDENCE:
//   The turret is a separate child Transform (turretPivot).
//   It is rotated purely in world space toward Player via
//   Quaternion.RotateTowards, with no coupling to Rb.MoveRotation.
//   The vehicle chassis may be facing anywhere — the turret always
//   looks at the player. This is the key design requirement.
// ==============================================================

using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A ranged enemy vehicle that maintains a safe distance from the player
/// and fires via an independently-aimed child turret Transform.
/// </summary>
public sealed class EnemyShooter : EnemyVehicleBase
{
    // ----------------------------------------------------------
    // INSPECTOR — TURRET
    // ----------------------------------------------------------

    [Header("Shooter — Turret")]
    [Tooltip("The child Transform that represents the rotatable turret pivot. " +
             "Only its Y rotation is driven by this script; attach your turret " +
             "mesh as a child of this Transform.")]
    [SerializeField] private Transform turretPivot;

    [Tooltip("Maximum degrees per second the turret can rotate to track the player.")]
    [SerializeField] private float turretRotationSpeed = 120f;

    [Tooltip("The turret will only fire when it is aligned within this angle (degrees) " +
             "of the player. Prevents shooting in the completely wrong direction.")]
    [SerializeField] private float aimToleranceDegrees = 8f;

    // ----------------------------------------------------------
    // INSPECTOR — WEAPON
    // ----------------------------------------------------------

    [Header("Shooter — Weapon")]
    [Tooltip("The VehicleWeapon component on this enemy that handles projectile pooling. " +
             "Must have a firePoint Transform child assigned in its Inspector.")]
    [SerializeField] private VehicleWeapon weapon;

    [Tooltip("Time in seconds between fire attempts. " +
             "Lower than VehicleWeapon.fireRate is pointless but harmless.")]
    [SerializeField] private float aiFireInterval = 0.6f;

    // ----------------------------------------------------------
    // INSPECTOR — RANGE MANAGEMENT
    // ----------------------------------------------------------

    [Header("Shooter — Range Management")]
    [Tooltip("The Shooter targets this distance from the player during Attack state. " +
             "NavMesh orbit is recalculated to maintain this radius.")]
    [SerializeField] private float optimalCombatRange = 18f;

    [Tooltip("If the player gets closer than this, the Shooter immediately steers away, " +
             "overriding any other movement. Must be less than optimalCombatRange.")]
    [SerializeField] private float minimumSafeDistance = 10f;

    [Tooltip("How frequently (in seconds) the orbit destination is recalculated " +
             "during Attack state. Lower = more reactive; higher = cheaper.")]
    [SerializeField] private float orbitRecalculateInterval = 0.5f;

    [Header("Shooter — Chase")]
    [Tooltip("The Shooter enters Attack state at this range (overrides base attackRange). " +
             "Should be ≥ optimalCombatRange so it never overshoots into minimum range.")]
    [SerializeField] private float shooterAttackRange = 22f;

    [Tooltip("The Shooter transitions back to Chase if player exceeds this range.")]
    [SerializeField] private float shooterLoseAttackRange = 28f;

    // ----------------------------------------------------------
    // PRIVATE STATE
    // ----------------------------------------------------------

    /// <summary>Timer tracking when the AI last attempted to fire.</summary>
    private float _lastAiFireTime;

    /// <summary>Timer tracking orbit destination recalculation.</summary>
    private float _orbitRecalculateTimer;

    /// <summary>
    /// Cached world-space orbit point. The chassis navigates to this
    /// point each orbit interval; the turret aims at Player directly.
    /// </summary>
    private Vector3 _currentOrbitPoint;

    /// <summary>
    /// True while the turret is rotating and within aim tolerance.
    /// Used to gate firing without a per-frame angle calculation.
    /// </summary>
    private bool _isTurretAimed;

    // ----------------------------------------------------------
    // AWAKE — VALIDATION
    // ----------------------------------------------------------

    protected override void Awake()
    {
        base.Awake();
        ValidateReferences();
    }

    /// <summary>
    /// Logs clear, actionable errors for missing serialised references
    /// rather than letting a NullReferenceException surface at runtime.
    /// </summary>
    private void ValidateReferences()
    {
        if (turretPivot == null)
        {
            Debug.LogError($"[{name}] EnemyShooter: 'Turret Pivot' is not assigned. " +
                           "Drag the turret child Transform into the Inspector slot.", this);
        }

        if (weapon == null)
        {
            Debug.LogError($"[{name}] EnemyShooter: 'Weapon' is not assigned. " +
                           "Drag the VehicleWeapon component into the Inspector slot.", this);
        }

        if (minimumSafeDistance >= optimalCombatRange)
        {
            Debug.LogWarning($"[{name}] EnemyShooter: minimumSafeDistance ({minimumSafeDistance}) " +
                             $"should be less than optimalCombatRange ({optimalCombatRange}).", this);
        }
    }

    // ----------------------------------------------------------
    // RANGE OVERRIDES
    // ----------------------------------------------------------

    /// <summary>
    /// The Shooter enters Attack state when the player is within
    /// shooterAttackRange (larger than base, so it stops chasing
    /// before overshooting into the player's face).
    /// </summary>
    protected override float GetAttackRange() => shooterAttackRange;

    // ----------------------------------------------------------
    // LOCOMOTION GATE OVERRIDE [PATCH-2a]
    // ----------------------------------------------------------

    /// <summary>
    /// [PATCH-2a] Opt into NavMesh locomotion during Attack state so
    /// the chassis continues orbiting at the optimal combat range
    /// while the turret handles independent aiming and firing.
    ///
    /// Without this override the base FixedUpdate only runs locomotion
    /// in Patrol and Chase, which would freeze the Shooter in place the
    /// moment it enters Attack — it would fire accurately but never reposition.
    /// </summary>
    protected override bool ShouldUseNavMeshLocomotion()
    {
        return CurrentState == EnemyState.Chase
            || CurrentState == EnemyState.Patrol
            || CurrentState == EnemyState.Attack;
    }

    // ----------------------------------------------------------
    // FSM HOOKS — ATTACK STATE ENTRY / EXIT
    // ----------------------------------------------------------

    protected override void OnStateEntered(EnemyState newState)
    {
        base.OnStateEntered(newState);

        if (newState == EnemyState.Attack)
        {
            // Reset timers so orbit and fire are recalculated immediately.
            _orbitRecalculateTimer = orbitRecalculateInterval; // Force immediate recalc.
            _lastAiFireTime = -aiFireInterval;                 // Allow immediate first shot.
            _isTurretAimed = false;

            // Calculate an initial orbit point right away.
            if (Player != null)
            {
                _currentOrbitPoint = CalculateOrbitPoint();
            }
        }
    }

    protected override void OnStateExited(EnemyState exitedState)
    {
        base.OnStateExited(exitedState);

        if (exitedState == EnemyState.Attack)
        {
            _isTurretAimed = false;
        }
    }

    // ----------------------------------------------------------
    // FSM HOOKS — UPDATE
    // ----------------------------------------------------------

    /// <summary>
    /// Called every frame while in Attack state.
    /// Manages both chassis orbit movement and turret aiming/firing.
    /// These are separated into private methods for clarity.
    /// </summary>
    protected override void OnAttackUpdate()
    {
        if (Player == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, Player.position);

        // ── TRANSITION CHECK ─────────────────────────────────────
        // The Shooter uses a custom, wider lose-range before
        // returning to Chase (avoids micro-transitions at range boundary).
        if (distanceToPlayer > shooterLoseAttackRange)
        {
            TransitionTo(EnemyState.Chase);
            return;
        }

        // ── CHASSIS — ORBIT MOVEMENT ──────────────────────────────
        UpdateOrbitMovement(distanceToPlayer);

        // ── TURRET — INDEPENDENT AIMING ───────────────────────────
        UpdateTurretAim();

        // ── WEAPON — FIRE WHEN AIMED ──────────────────────────────
        TryAIFire();
    }

    // ----------------------------------------------------------
    // CHASSIS ORBIT MOVEMENT
    // ----------------------------------------------------------

    /// <summary>
    /// Steers the vehicle chassis to maintain optimal combat distance.
    /// If the player is too close, the orbit point is calculated to
    /// push the vehicle away immediately. If in range, it circulates
    /// around the player using a recalculated offset point.
    /// The actual physics execution happens in FixedUpdate via the
    /// base DriveTowardsNextCorner() — this method only manages
    /// where that path is pointed.
    /// </summary>
    private void UpdateOrbitMovement(float distanceToPlayer)
    {
        _orbitRecalculateTimer += Time.deltaTime;

        // Recalculate orbit destination at the configured interval,
        // or immediately if we're dangerously close to the player.
        bool tooClose = distanceToPlayer < minimumSafeDistance;
        bool intervalElapsed = _orbitRecalculateTimer >= orbitRecalculateInterval;

        if (tooClose || intervalElapsed)
        {
            _orbitRecalculateTimer = 0f;
            _currentOrbitPoint = CalculateOrbitPoint();

            // Push the new destination into the base class nav path.
            // RequestNavPathRefresh() was added to the base in v2 for
            // exactly this use case — controlled external path override.
            RequestNavPathRefresh(_currentOrbitPoint);
        }
    }

    /// <summary>
    /// Calculates the world-space point the chassis should navigate to
    /// in order to maintain the optimal orbit range around the player.
    ///
    /// The orbit point is placed on the NavMesh at <see cref="optimalCombatRange"/>
    /// distance from the player, in the direction away from this vehicle.
    /// This naturally causes the vehicle to circle as the player moves,
    /// rather than always approaching from the same angle.
    /// </summary>
    private Vector3 CalculateOrbitPoint()
    {
        // Direction from player to this vehicle — used to place orbit
        // point "behind" the vehicle's current position relative to the player.
        Vector3 awayFromPlayer = (transform.position - Player.position).normalized;

        // Add a small perpendicular offset to encourage circling rather
        // than direct back-and-forth oscillation on the same axis.
        Vector3 perpendicular = Vector3.Cross(awayFromPlayer, Vector3.up).normalized;
        Vector3 orbitDirection = (awayFromPlayer + perpendicular * 0.4f).normalized;

        Vector3 candidatePoint = Player.position + orbitDirection * optimalCombatRange;
        candidatePoint.y = transform.position.y; // Keep on the same horizontal plane.

        // Snap to NavMesh surface so the path calculator always gets a valid point.
        NavMeshHit hit;
        if (NavMesh.SamplePosition(candidatePoint, out hit, optimalCombatRange * 0.5f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        // Fallback: return the raw candidate if no NavMesh point is found nearby.
        // This is safe — NavMesh.CalculatePath will silently fail and the
        // vehicle will hold its last path, which is acceptable behaviour.
        return candidatePoint;
    }

    // ----------------------------------------------------------
    // TURRET INDEPENDENT AIMING
    // ----------------------------------------------------------

    /// <summary>
    /// Rotates the turret pivot Transform toward the player's position
    /// independently of the vehicle chassis direction.
    ///
    /// DESIGN INTENT:
    ///   The vehicle body faces its travel direction (controlled by
    ///   Rigidbody.MoveRotation in the base class). The turret pivot
    ///   is a CHILD Transform whose local Y rotation is driven here
    ///   separately. This decoupling is the key visual requirement:
    ///   the gun always faces the player even when the chassis does not.
    ///
    /// IMPLEMENTATION NOTE:
    ///   We rotate in world space (Space.World) to avoid parent rotation
    ///   accumulation. We compute the target rotation from world vectors
    ///   and apply via Quaternion.RotateTowards for a hard speed cap.
    /// </summary>
    private void UpdateTurretAim()
    {
        if (turretPivot == null || Player == null) return;

        // Direction from turret to player, in full 3-D world space.
        // We intentionally do NOT flatten this to the XZ plane because on
        // uneven lunar terrain (ramps, crater rims) the height difference
        // between turret and player is real and should influence aiming.
        Vector3 directionToPlayer = (Player.position - turretPivot.position);

        if (directionToPlayer.sqrMagnitude < 0.001f) return;

        // Normalise once; used for both LookRotation and angle check below.
        Vector3 directionNormalised = directionToPlayer.normalized;

        // [PATCH-2b] Use transform.up instead of Vector3.up as the upward
        // reference for the turret's LookRotation.
        //
        // WHY: When the chassis sits on a sloped ramp or crater wall its
        // local up-axis tilts with the terrain (physics constraints only
        // freeze X/Z ROTATION, not the perceived up-vector relative to the
        // world). Using Vector3.up (world-up) when the vehicle is tilted
        // produces a jarring snap because the computed forward and up vectors
        // are no longer orthogonal to the turret base. transform.up is always
        // perpendicular to the turret's mounting surface, giving a smooth,
        // contextually correct rotation on any terrain angle.
        Quaternion targetRotation = Quaternion.LookRotation(directionNormalised, transform.up);

        // Rotate at most turretRotationSpeed degrees this frame.
        turretPivot.rotation = Quaternion.RotateTowards(
            turretPivot.rotation,
            targetRotation,
            turretRotationSpeed * Time.deltaTime
        );

        // [PATCH-2c] Aim tolerance check via Vector3.Angle instead of
        // Quaternion.Angle.
        //
        // WHY: Quaternion.Angle measures the total angular difference
        // between two full orientations (including roll and pitch), so
        // even a tiny terrain-induced roll on the turret can push the
        // Quaternion angle above aimToleranceDegrees and prevent firing
        // even when the turret barrel is visually pointing directly at
        // the player. Vector3.Angle compares only the forward vectors —
        // the single axis we actually care about for projectile accuracy.
        float aimAngle = Vector3.Angle(turretPivot.forward, directionNormalised);
        _isTurretAimed = aimAngle <= aimToleranceDegrees;
    }

    // ----------------------------------------------------------
    // WEAPON FIRING
    // ----------------------------------------------------------

    /// <summary>
    /// Attempts to fire the weapon if the turret is aimed within
    /// tolerance and the AI fire interval has elapsed.
    ///
    /// Uses <see cref="VehicleWeapon.TryFireAI"/> — the public
    /// AI entry point added to VehicleWeapon in v2 — which internally
    /// delegates to the same TryFire() logic the player uses.
    /// This keeps a single fire-rate code path and respects ammo/reload.
    /// </summary>
    private void TryAIFire()
    {
        if (weapon == null) return;
        if (!_isTurretAimed) return;
        if (Time.time < _lastAiFireTime + aiFireInterval) return;

        _lastAiFireTime = Time.time;
        weapon.TryFireAI();
    }

    // ----------------------------------------------------------
    // GIZMOS — Turret Aim & Orbit Visualisation
    // ----------------------------------------------------------

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Optimal combat range — green ring.
        Gizmos.color = new Color(0f, 1f, 0.4f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, optimalCombatRange);

        // Minimum safe distance — orange ring.
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, minimumSafeDistance);

        // Lose-attack range — grey ring.
        Gizmos.color = Color.grey;
        Gizmos.DrawWireSphere(transform.position, shooterLoseAttackRange);

        // Orbit destination — magenta sphere.
        if (Application.isPlaying && CurrentState == EnemyState.Attack)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(_currentOrbitPoint, 0.6f);
            Gizmos.DrawLine(transform.position, _currentOrbitPoint);

            // Turret aim line.
            if (turretPivot != null)
            {
                Gizmos.color = _isTurretAimed ? Color.green : Color.red;
                Gizmos.DrawLine(turretPivot.position,
                    turretPivot.position + turretPivot.forward * 5f);
            }
        }
    }
#endif
}
