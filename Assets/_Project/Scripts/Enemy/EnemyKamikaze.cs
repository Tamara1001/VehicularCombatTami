// ==============================================================
// EnemyKamikaze.cs
// --------------------------------------------------------------
// PURPOSE:
//   Concrete enemy type that inherits from EnemyVehicleBase and
//   implements the "Mechanical Bull" attack pattern:
//
//   ┌─────────────────────────────────────────────────────────┐
//   │  PHASE 1 — TELEGRAPH  │  Stop → Rotate to face player  │
//   │  PHASE 2 — CHARGE     │  Lock steering, launch forward  │
//   │  PHASE 3 — CONSEQUENCE│  Hit player → damage + bounce  │
//   │                       │  Hit wall   → stun (punish)    │
//   └─────────────────────────────────────────────────────────┘
//
// DESIGN PHILOSOPHY ("Mechanical Bull"):
//   • Commitment  — Once the charge begins, the vehicle cannot
//                   steer. The player must dodge; the Kamikaze
//                   must commit.
//   • Telegraphing — A visible anticipation window (full stop +
//                   rotation) gives the player a fair warning
//                   and creates readable moment-to-moment play.
//
// ARCHITECTURE (how it fits EnemyVehicleBase):
//   • Inherits all Patrol, Chase, and Stun logic for free.
//   • Overrides OnStateEntered/OnStateExited(Attack) to manage
//     the coroutine lifecycle cleanly without touching base state.
//   • Overrides ShouldUseNavMeshLocomotion() → false during
//     Attack so the base FixedUpdate doesn't fight the charge.
//   • Uses base.SuppressAttackDetection to keep ownership of the
//     Attack state for the full coroutine duration.
//   • Uses base.EnterStunned() to trigger the punishment path.
//   • Uses base.Health to apply self-damage without a local ref.
//   • Uses base.Rb and base.Player (protected properties).
//
// MODIFICATIONS REQUIRED IN EnemyVehicleBase (already present v2/v3):
//   ✔ SuppressAttackDetection  (v2)
//   ✔ Health property          (v2)
//   ✔ ShouldUseNavMeshLocomotion() virtual  (v3 PATCH-1a)
//   ✔ Rb / Player / Agent protected properties
//   ✔ EnterStunned() public method
//   ✔ TransitionTo() protected method
// ==============================================================

using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A kamikaze enemy that telegraphs its intent, then launches a
/// committed, straight-line charge at the player.
/// Inherits patrol/chase/stun from <see cref="EnemyVehicleBase"/>.
/// </summary>
public sealed class EnemyKamikaze : EnemyVehicleBase
{
    // ----------------------------------------------------------
    // INSPECTOR — TELEGRAPH (Phase 1)
    // ----------------------------------------------------------

    [Header("Kamikaze — Phase 1: Telegraph")]

    [Tooltip("Time (seconds) the vehicle spends stopped and rotating to face " +
             "the player before launching the charge. Increase for more dread.")]
    [SerializeField] private float telegraphDuration = 0.75f;

    [Tooltip("How fast (degrees/second) the vehicle rotates in place to face " +
             "the player during the telegraph phase.")]
    [SerializeField] private float telegraphTurnSpeed = 120f;

    [Tooltip("Fired the moment Phase 1 begins. Hook this up in the Inspector " +
             "to enable red headlights, a warning VFX, audio cue, etc.")]
    [SerializeField] public UnityEvent OnTelegraphStart;

    // ----------------------------------------------------------
    // INSPECTOR — CHARGE (Phase 2)
    // ----------------------------------------------------------

    [Header("Kamikaze — Phase 2: Charge")]

    [Tooltip("The instant velocity (m/s) injected directly into the Rigidbody " +
             "the moment the charge is launched. This bypasses the normal " +
             "acceleration ramp — the vehicle leaps to full speed immediately.")]
    [SerializeField] private float chargeVelocity = 28f;

    [Tooltip("Maximum duration of the charge (seconds) before the attack is " +
             "considered a miss and the enemy transitions back to Chase. " +
             "Acts as a safety valve if no collision is ever detected.")]
    [SerializeField] private float chargeTimeoutDuration = 3f;

    // ----------------------------------------------------------
    // INSPECTOR — COLLISION (Phase 3)
    // ----------------------------------------------------------

    [Header("Kamikaze — Phase 3: Collision")]

    [Tooltip("Damage applied to the IDamageable component found in the " +
             "collision hierarchy on a successful ram hit.")]
    [SerializeField] private int ramDamage = 40;

    [Tooltip("Force (Newtons, Impulse mode) applied to this Rigidbody in the " +
             "opposite direction of the hit — the 'bounce-back' after a ram.")]
    [SerializeField] private float bounceBackForce = 18f;

    [Tooltip("LayerMask for solid environment geometry (walls, terrain, barriers). " +
             "Collisions with layers in this mask that do NOT have an IDamageable " +
             "are treated as a missed charge and trigger EnterStunned().")]
    [SerializeField] private LayerMask environmentLayerMask;

    // ----------------------------------------------------------
    // PRIVATE — COROUTINE & PHASE STATE
    // ----------------------------------------------------------

    /// <summary>
    /// Reference to the running attack coroutine so we can cancel
    /// it cleanly if the state is exited before it finishes
    /// (e.g. the enemy dies mid-charge).
    /// </summary>
    private Coroutine _attackCoroutine;

    /// <summary>
    /// True while the Phase 2 charge is actively running.
    /// Checked in OnCollisionEnter to gate damage/stun logic so
    /// accidental bumps during Patrol/Chase have no effect.
    /// </summary>
    private bool _isCharging;

    /// <summary>
    /// Latched direction of the charge (unit vector, Y=0).
    /// Set once at Phase 2 launch and never changed — this is
    /// what enforces the straight-line commitment mechanic.
    /// </summary>
    private Vector3 _chargeDirection;

    // ----------------------------------------------------------
    // EnemyVehicleBase — LOCOMOTION GATE OVERRIDE [PATCH-1a]
    // ----------------------------------------------------------

    /// <summary>
    /// Disables the base NavMesh locomotion pipeline during Attack so
    /// DriveTowardsNextCorner() does not interfere with the charge.
    /// The base class friction-only Stunned path is unaffected.
    /// </summary>
    protected override bool ShouldUseNavMeshLocomotion()
    {
        // NavMesh locomotion runs normally for Patrol and Chase.
        // During Attack the coroutine owns all movement via Rigidbody.
        return CurrentState == EnemyState.Chase || CurrentState == EnemyState.Patrol;
    }

    // ----------------------------------------------------------
    // EnemyVehicleBase — FSM ENTRY / EXIT HOOKS
    // ----------------------------------------------------------

    /// <summary>
    /// Called by the base class FSM every time a new state is entered.
    /// We intercept Attack here to launch the three-phase coroutine.
    /// </summary>
    protected override void OnStateEntered(EnemyState newState)
    {
        base.OnStateEntered(newState);

        if (newState == EnemyState.Attack)
        {
            // Claim ownership of the Attack state so the base class won't
            // auto-transition back to Chase while the coroutine is running.
            SuppressAttackDetection = true;

            // Launch the three-phase sequence.
            _attackCoroutine = StartCoroutine(AttackSequenceRoutine());
        }
    }

    /// <summary>
    /// Called by the base class FSM immediately before leaving a state.
    /// We use this to guarantee the coroutine is always stopped and all
    /// flags are reset, regardless of how the exit was triggered.
    /// </summary>
    protected override void OnStateExited(EnemyState exitedState)
    {
        base.OnStateExited(exitedState);

        if (exitedState == EnemyState.Attack)
        {
            // Release ownership so the base class resumes normal detection.
            SuppressAttackDetection = false;

            // Clear the charging flag so OnCollisionEnter becomes a no-op.
            _isCharging = false;

            // Stop the coroutine if it is still running (e.g. death mid-charge).
            if (_attackCoroutine != null)
            {
                StopCoroutine(_attackCoroutine);
                _attackCoroutine = null;
            }

            // FIX — Momentum bleed-over (QA Issue #2):
            // Wipe the horizontal (XZ) velocity so the 28 m/s charge speed
            // does not carry over into Chase and fling the vehicle off the map.
            // The Y component is deliberately preserved so we don't fight
            // gravity or interrupt a fall that is already in progress.
            Rb.linearVelocity = new Vector3(0f, Rb.linearVelocity.y, 0f);
        }
    }

    // ----------------------------------------------------------
    // ATTACK SEQUENCE COROUTINE
    // ----------------------------------------------------------

    /// <summary>
    /// The three-phase attack sequence. Runs as a coroutine so each
    /// phase can cleanly wait for time or conditions without polluting
    /// Update() or OnAttackUpdate() with complex state flags.
    /// </summary>
    private IEnumerator AttackSequenceRoutine()
    {
        // ── PHASE 1: TELEGRAPH ────────────────────────────────────────────
        // Bring the vehicle to a complete stop, then rotate in place to
        // face the player for telegraphDuration seconds.

        // Kill current velocity immediately — the vehicle freezes in place.
        Rb.linearVelocity = Vector3.zero;
        Rb.angularVelocity = Vector3.zero;

        // Notify subscribers (Inspector hook → red headlights, VFX, SFX, etc.)
        OnTelegraphStart?.Invoke();

        float telegraphTimer = 0f;

        while (telegraphTimer < telegraphDuration)
        {
            // WaitForFixedUpdate keeps time accumulation and Rigidbody writes
            // (MoveRotation, linearVelocity) in sync with the physics step.
            // Using Time.fixedDeltaTime here is consistent with that cadence.
            telegraphTimer += Time.fixedDeltaTime;

            // Rotate smoothly towards the player in local Y only.
            // Null-guard: Player is set by the base class; can be null if
            // the player is destroyed while telegraphing.
            if (Player != null)
            {
                Vector3 dirToPlayer = (Player.position - transform.position);
                dirToPlayer.y = 0f;

                if (dirToPlayer.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dirToPlayer.normalized);
                    // MoveRotation keeps the Rigidbody in sync with the physics engine.
                    Rb.MoveRotation(Quaternion.RotateTowards(
                        Rb.rotation,
                        targetRot,
                        telegraphTurnSpeed * Time.fixedDeltaTime
                    ));
                }
            }

            // Keep the vehicle dead-stopped during the telegraph window.
            Rb.linearVelocity = Vector3.zero;

            yield return new WaitForFixedUpdate(); // Sync with physics step.
        }

        // ── PHASE 2: CHARGE ───────────────────────────────────────────────
        // Latch the charge direction (current facing, Y neutralised) and
        // inject the full charge velocity. No further steering is applied.

        // Capture the forward vector at launch time. This is the "commitment"
        // — from this point on, the direction is immutable.
        _chargeDirection = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;

        // Inject velocity directly — bypasses AddForce ramp-up for an
        // instantaneous, dramatic lunge. Y component is kept neutral (no
        // artificial lift) so gravity still governs vertical movement.
        Rb.linearVelocity = _chargeDirection * chargeVelocity;

        // Arm the collision handler — only now will OnCollisionEnter act.
        _isCharging = true;

        // Wait until the collision handler sets _isCharging = false (success
        // or miss) OR the safety timeout expires (charge flew off into space).
        float chargeTimer = 0f;

        while (_isCharging && chargeTimer < chargeTimeoutDuration)
        {
            // WaitForFixedUpdate guarantees the velocity write below is
            // consumed by the same physics step that moves the Rigidbody.
            // yield return null (Update cadence) could write velocity AFTER
            // the physics step has already resolved, causing a one-frame lag
            // where the injected speed is invisible to collision detection.
            chargeTimer += Time.fixedDeltaTime;

            // Re-apply the latched velocity every physics step so lateral
            // friction in the base class doesn't bleed off charge speed
            // before impact. Only XZ is forced — Y is left to gravity.
            Vector3 current = Rb.linearVelocity;
            current.x = _chargeDirection.x * chargeVelocity;
            current.z = _chargeDirection.z * chargeVelocity;
            Rb.linearVelocity = current;

            yield return new WaitForFixedUpdate();
        }

        // ── CHARGE TIMED OUT (Phase 3 — missed, no collision) ─────────────
        // If we exit the while loop because the timer expired (not because
        // a collision resolved the attack), treat it as a controlled miss
        // and return to Chase so the enemy can re-engage.
        if (_isCharging)
        {
            _isCharging = false;
            TransitionTo(EnemyState.Chase);
        }

        // Null the reference — coroutine has completed naturally.
        _attackCoroutine = null;
    }

    // ----------------------------------------------------------
    // PHASE 3 — COLLISION HANDLER
    // ----------------------------------------------------------

    /// <summary>
    /// Called by Unity for every physics collision.
    /// Gated by <see cref="_isCharging"/> so accidental bumps during
    /// Patrol or Chase are silently ignored.
    /// </summary>
    private void OnCollisionEnter(Collision collision)
    {
        // Only react during the active charge window.
        if (!_isCharging) return;

        // Self-collision guard: multi-collider rigs can generate internal
        // contacts between child colliders — skip anything on our own hierarchy.
        if (collision.transform.root == transform.root) return;

        // ── HIT: Does this object (or a parent) implement IDamageable? ──────
        IDamageable damageable = collision.gameObject.GetComponentInParent<IDamageable>();

        if (damageable != null)
        {
            // ── PATH A: Successful ram hit ───────────────────────────────────
            // Apply damage to the target.
            damageable.TakeDamage(ramDamage);

            // FIX — Lunar Physics Bounce (QA Issue #1):
            // Zero the Rigidbody velocity FIRST so the bounce impulse is not
            // added on top of the existing charge momentum. Without this, the
            // combined force (28 m/s charge + 18 N impulse) under lunar gravity
            // (-1.62) is enough to launch the vehicle clean off the map.
            Rb.linearVelocity = Vector3.zero;

            // Enforce a strictly horizontal (XZ-plane) bounce vector.
            // _chargeDirection is already Y=0 from Phase 2, but we re-flatten
            // and renormalise here as a defensive guarantee — a future change
            // to charge launch code should not silently reintroduce a Y component.
            Vector3 horizontalBounce = new Vector3(-_chargeDirection.x, 0f, -_chargeDirection.z).normalized;
            Rb.AddForce(horizontalBounce * bounceBackForce, ForceMode.Impulse);

            // Disarm the charging flag so this method becomes a no-op for any
            // subsequent contacts in the same frame.
            _isCharging = false;

            // Return to Chase so the enemy can reposition and charge again.
            TransitionTo(EnemyState.Chase);
        }
        else
        {
            // ── PATH B: Missed — hit environment geometry ────────────────────
            // Only punish collisions with solid environment layers.
            // This avoids the "instant stun on touching the floor" problem that
            // would occur if the layer mask is not set — only intentional wall
            // strikes trigger the stun.
            int collisionLayer = 1 << collision.gameObject.layer;
            bool isEnvironment = (environmentLayerMask.value & collisionLayer) != 0;

            if (isEnvironment)
            {
                // Disarm the charge before transitioning so OnCollisionEnter
                // becomes a no-op for any duplicate contacts this frame.
                _isCharging = false;

                // Punish the miss with a timed stun — the player now has a window
                // to counter-attack while the Kamikaze is disabled.
                // EnterStunned() handles the FSM transition and timer internally.
                EnterStunned();
            }
            // Non-environment, non-damageable contacts (other enemies, props that
            // are not tagged as environment) are silently ignored — the charge
            // continues through them.
        }
    }

    // ----------------------------------------------------------
    // EDITOR VISUALISATION
    // ----------------------------------------------------------

#if UNITY_EDITOR
    /// <summary>
    /// Draws the charge direction arrow and attack-range gizmos in
    /// the Scene view. Rendered on selection to avoid visual clutter.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        // Draw the active charge direction if a charge is in progress.
        if (_isCharging && _chargeDirection.sqrMagnitude > 0.01f)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, _chargeDirection * 4f);
            Gizmos.DrawSphere(transform.position + _chargeDirection * 4f, 0.3f);
        }

        // Draw the telegraph rotation arc to visualise the maximum
        // turn speed relative to the telegraphDuration.
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.6f); // orange
        float maxTurnDegrees = telegraphTurnSpeed * telegraphDuration;
        int segments = 24;
        float step = maxTurnDegrees / segments;
        Vector3 prevLeft  = transform.position +
                            Quaternion.Euler(0f, -maxTurnDegrees * 0.5f, 0f) * transform.forward * 3f;
        for (int i = 1; i <= segments; i++)
        {
            float angle = -maxTurnDegrees * 0.5f + step * i;
            Vector3 nextLeft = transform.position +
                               Quaternion.Euler(0f, angle, 0f) * transform.forward * 3f;
            Gizmos.DrawLine(prevLeft, nextLeft);
            prevLeft = nextLeft;
        }
    }
#endif
}
