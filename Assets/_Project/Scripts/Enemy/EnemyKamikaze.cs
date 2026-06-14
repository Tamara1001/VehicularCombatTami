// ==============================================================
// EnemyKamikaze.cs
// --------------------------------------------------------------
// PURPOSE:
//   Concrete enemy that charges into the player using a
//   high-speed physics dash. Each successful ram costs 50% of
//   its maximum health (two rams = self-destruction). After the
//   dash the vehicle overshoots naturally via Rigidbody momentum
//   before transitioning back to Chase for the next approach.
//
// FSM FLOW:
//   Patrol ──(detect player)──► Chase ──(enter range)──► Attack
//                                  ▲                        │
//                                  └──(post-ram cooldown)───┘
//
// ATTACK STATE DETAIL:
//   ┌─────────────────────────────────────────────────────────┐
//   │  ATTACK sub-phases (managed by RamCoroutine):           │
//   │  1. WINDUP  – Brief freeze, snapshot player position    │
//   │  2. DASH    – High-force impulse toward snapshot point  │
//   │  3. OVERSHOOT – Physics carries momentum; no correction │
//   │  4. RECOVERY – Decelerate, apply self-damage, return    │
//   └─────────────────────────────────────────────────────────┘
//
// SELF-DAMAGE DESIGN:
//   Uses Health.TakeDamage(amount) where amount is calculated
//   as 50% of Health.MaxHealth at the moment of impact.
//   This means the Kamikaze is always exactly 2 rams from death
//   regardless of external damage taken (keeps it predictable
//   for designers). Health is applied on ram execution, not on
//   collision, so the vehicle always pays the cost — even if
//   the player dodges. This is intentional: the ram itself is
//   destructive (engine burnout), not the impact.
//
// MOMENTUM / OVERSHOOT DESIGN:
//   The Rigidbody impulse during DASH is intentionally large.
//   During OVERSHOOT, lateral friction is disabled (SuppressLateralFriction)
//   so the vehicle carries its full velocity vector forward.
//   Natural drag + the recovery deceleration bleed it off
//   within a designer-tunable window, then Chase resumes.
// ==============================================================

using System.Collections;
using UnityEngine;

/// <summary>
/// A suicide-charge enemy vehicle that rams the player with a physics
/// impulse, paying 50% of its maximum health per ram. Two rams = death.
/// </summary>
public sealed class EnemyKamikaze : EnemyVehicleBase
{
    // ----------------------------------------------------------
    // INSPECTOR — RAM ATTACK TUNING
    // ----------------------------------------------------------

    [Header("Kamikaze — Ram Attack")]
    [Tooltip("Initial freeze duration before the dash launches. " +
             "Creates a readable telegraph for the player.")]
    [SerializeField] private float windupDuration = 0.4f;

    [Tooltip("Magnitude of the Impulse force applied to the Rigidbody at dash launch. " +
             "ForceMode.Impulse — applied in a single frame regardless of mass.")]
    [SerializeField] private float dashImpulseForce = 28f;

    [Tooltip("How long the ram physics state is maintained (overshoot window). " +
             "During this time no corrections are applied — pure momentum.")]
    [SerializeField] private float overshootDuration = 0.8f;

    [Tooltip("Deceleration lerp speed applied after the overshoot window expires.")]
    [SerializeField] private float recoveryDecelerationSpeed = 5f;

    [Tooltip("The vehicle transitions back to Chase once speed drops below this threshold.")]
    [SerializeField] private float recoverySpeedThreshold = 2f;

    [Tooltip("[PATCH-3] Maximum time (seconds) the Recovery phase may run before forcibly " +
             "exiting, even if Rigidbody.linearVelocity never drops below recoverySpeedThreshold. " +
             "Guards against the while-loop hanging indefinitely when a wall collision instantly " +
             "zeroes velocity in an unexpected physics state.")]
    [SerializeField] private float recoveryMaxTimeout = 2f;

    [Header("Kamikaze — Chase")]
    [Tooltip("Speed multiplier applied on top of base maximumChaseSpeed while chasing.")]
    [SerializeField] private float chaseSpeedMultiplier = 1.4f;

    [Header("Kamikaze — Gizmos")]
    [Tooltip("Colour of the ram direction arrow drawn in the Scene view.")]
    [SerializeField] private Color gizmoRamColor = new Color(1f, 0.4f, 0f, 0.9f);

    // ----------------------------------------------------------
    // PRIVATE STATE
    // ----------------------------------------------------------

    /// <summary>
    /// World-space snapshot of Player.position at the moment the dash
    /// launches. The Kamikaze charges at this fixed point even if the
    /// player moves — this prevents homing behaviour mid-dash and makes
    /// the attack dodgeable by a reactive player.
    /// </summary>
    private Vector3 _ramTargetPosition;

    /// <summary>
    /// Guard that prevents a second RamCoroutine from starting while
    /// one is already running (e.g. if TransitionTo(Attack) fires twice).
    /// </summary>
    private bool _isRamming;

    /// <summary>Running ram coroutine reference for clean cancellation.</summary>
    private Coroutine _ramCoroutine;

    // ----------------------------------------------------------
    // AWAKE
    // ----------------------------------------------------------

    protected override void Awake()
    {
        base.Awake();

        // Boost the chase speed multiplier so this enemy feels more
        // aggressive than a Shooter approaching cautiously.
        maximumChaseSpeed *= chaseSpeedMultiplier;
    }

    // ----------------------------------------------------------
    // FSM HOOKS — ATTACK STATE
    // ----------------------------------------------------------

    /// <summary>
    /// When entering Attack state, suppress the base auto-detection
    /// and launch the ram coroutine. The base class will NOT transition
    /// away from Attack until SuppressAttackDetection is cleared.
    /// </summary>
    protected override void OnStateEntered(EnemyState newState)
    {
        base.OnStateEntered(newState);

        if (newState == EnemyState.Attack)
        {
            // Take ownership: block base auto-exit from Attack state.
            // This prevents a mid-dash range check from interrupting
            // the ram at the worst possible moment (during windup).
            SuppressAttackDetection = true;
            _isRamming = false;
        }
    }

    protected override void OnStateExited(EnemyState exitedState)
    {
        base.OnStateExited(exitedState);

        if (exitedState == EnemyState.Attack)
        {
            // Return detection control to the base class.
            SuppressAttackDetection = false;

            // Safety: cancel any orphaned coroutine if something
            // (e.g. an external Stun) forces us out early.
            if (_ramCoroutine != null)
            {
                StopCoroutine(_ramCoroutine);
                _ramCoroutine = null;
            }
            _isRamming = false;
        }
    }

    /// <summary>
    /// Called every Update() frame while in Attack state.
    /// Launches the ram coroutine exactly once per attack entry.
    /// All actual ram logic lives in RamCoroutine to keep the
    /// update hook clean and avoid per-frame state duplication.
    /// </summary>
    protected override void OnAttackUpdate()
    {
        // Guard: only start one coroutine per attack entry.
        if (_isRamming) return;

        _isRamming = true;
        _ramCoroutine = StartCoroutine(RamCoroutine());
    }

    // ----------------------------------------------------------
    // RAM COROUTINE — CORE ATTACK LOGIC
    // ----------------------------------------------------------

    /// <summary>
    /// The complete lifecycle of a single ram attack, split into
    /// four sequential phases: Windup → Dash → Overshoot → Recovery.
    /// </summary>
    private IEnumerator RamCoroutine()
    {
        // ── PHASE 1: WINDUP ─────────────────────────────────────
        // Snapshot the player's position and briefly freeze the vehicle.
        // The freeze is a gameplay telegraph: a skilled player will see
        // the Kamikaze stop and can attempt to dodge the incoming dash.
        if (Player != null)
        {
            _ramTargetPosition = Player.position;
        }
        else
        {
            // No player found — abort and return to patrol.
            TransitionTo(EnemyState.Patrol);
            yield break;
        }

        // Zero out existing velocity for a clean, predictable launch angle.
        Rb.linearVelocity = Vector3.zero;

        // Rotate to face the target snapshot before the impulse fires.
        Vector3 directionToTarget = (_ramTargetPosition - transform.position).normalized;
        directionToTarget.y = 0f;
        if (directionToTarget.sqrMagnitude > 0.001f)
        {
            Rb.MoveRotation(Quaternion.LookRotation(directionToTarget));
        }

        yield return new WaitForSeconds(windupDuration);

        // ── PHASE 2: DASH ────────────────────────────────────────
        // Apply a single-frame impulse in the vehicle's forward direction.
        // We use the vehicle's current forward (set by the windup rotation)
        // rather than recalculating the direction to avoid a homing effect
        // if the player moved during windup.
        Rb.AddForce(transform.forward * dashImpulseForce, ForceMode.Impulse);

        // Apply self-damage immediately upon dash execution.
        // Design intent: the ram IS the self-damage event (engine burnout),
        // not the collision. This guarantees predictable 2-ram death
        // regardless of whether the hit connects.
        ApplyRamSelfDamage();

        // ── PHASE 3: OVERSHOOT ───────────────────────────────────
        // Let physics carry the full momentum for the overshoot window.
        // Lateral friction is NOT applied during this phase so the
        // vehicle maintains its full velocity vector (no correction).
        // The base FixedUpdate does not apply lateral friction in
        // Attack state, so no extra code is needed here — the impulse
        // velocity is simply preserved by the physics engine.
        yield return new WaitForSeconds(overshootDuration);

        // ── PHASE 4: RECOVERY ────────────────────────────────────
        // Gradually decelerate until speed drops below the threshold,
        // then transition back to Chase to recalculate a new approach.
        //
        // [PATCH-3] Maximum timeout guard.
        // PROBLEM: If a wall collision or a flat physics surface instantly
        // zeroes the Rigidbody velocity mid-loop, linearVelocity.magnitude
        // is already 0 and the condition is satisfied on the first frame —
        // BUT there are edge-case physics states (e.g. continuous contact
        // with a static wall applying a persistent counter-force) where the
        // magnitude oscillates just above recoverySpeedThreshold forever.
        // SOLUTION: Track elapsed time inside the loop and break out after
        // recoveryMaxTimeout seconds regardless of the speed reading.
        // Both exit conditions are always safe: the vehicle is simply done.
        float recoveryElapsed = 0f;
        while (Rb.linearVelocity.magnitude > recoverySpeedThreshold
               && recoveryElapsed < recoveryMaxTimeout)
        {
            Rb.linearVelocity = Vector3.Lerp(
                Rb.linearVelocity,
                Vector3.zero,
                recoveryDecelerationSpeed * Time.deltaTime
            );
            recoveryElapsed += Time.deltaTime;
            yield return null; // Wait one frame between deceleration steps.
        }

        // Log if timeout was the exit condition (useful for tuning).
        if (recoveryElapsed >= recoveryMaxTimeout)
        {
            Debug.LogWarning($"[{name}] Kamikaze Recovery timed out after {recoveryMaxTimeout}s. " +
                             "Consider increasing recoveryMaxTimeout or decreasing recoveryDecelerationSpeed.",
                             this);
        }

        _ramCoroutine = null;
        _isRamming = false;

        // Return to Chase — the base class will re-evaluate distance
        // and re-enter Attack if still within range, creating the
        // "circle → dash → circle → dash" loop.
        TransitionTo(EnemyState.Chase);
    }

    // ----------------------------------------------------------
    // SELF-DAMAGE LOGIC
    // ----------------------------------------------------------

    /// <summary>
    /// Deducts exactly 50% of MaxHealth from this vehicle via the
    /// decoupled HealthComponent. Two calls = death.
    ///
    /// Uses <see cref="EnemyVehicleBase.Health"/> (the protected property
    /// added in v2) so no additional GetComponent call is needed here.
    /// The HealthComponent's own OnDied event handles destruction —
    /// this method has no knowledge of that flow.
    /// </summary>
    private void ApplyRamSelfDamage()
    {
        if (Health == null) return;

        // Calculate 50% of MaxHealth as an integer damage amount.
        // Mathf.CeilToInt ensures we never round down to 0 on edge cases.
        int selfDamageAmount = Mathf.CeilToInt(Health.MaxHealth * 0.5f);
        Health.TakeDamage(selfDamageAmount);

        Debug.Log($"[{name}] Ram executed. Self-damage applied: {selfDamageAmount} " +
                  $"(50% of {Health.MaxHealth}). Remaining HP: {Health.CurrentHealth}", this);
    }

    // ----------------------------------------------------------
    // GIZMOS — Ram Direction Visualisation
    // ----------------------------------------------------------

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Draw the snapshotted ram target during Play mode.
        if (Application.isPlaying && CurrentState == EnemyState.Attack)
        {
            Gizmos.color = gizmoRamColor;
            Gizmos.DrawLine(transform.position, _ramTargetPosition);
            Gizmos.DrawSphere(_ramTargetPosition, 0.5f);

            // Draw the dash impulse direction arrow.
            Vector3 arrowEnd = transform.position + transform.forward * dashImpulseForce * 0.15f;
            Gizmos.DrawLine(transform.position, arrowEnd);
        }
    }
#endif
}
