// ==============================================================
// EnemyVehicleBase.cs
// --------------------------------------------------------------
// PURPOSE:
//   Abstract base class for all enemy vehicle AI in the game.
//   Provides a hybrid movement system (NavMesh pathfinding +
//   Rigidbody physics execution) and a Finite State Machine
//   foundation that child classes specialise via overrides.
//
// ARCHITECTURE OVERVIEW:
//   ┌─────────────────────────────────────────────────────────┐
//   │                  EnemyVehicleBase                        │
//   │  ┌─────────────────────┐  ┌──────────────────────────┐  │
//   │  │  FSM (enum + switch) │  │  Hybrid Movement System  │  │
//   │  │  - Patrol            │  │  - NavMeshPath (routing)  │  │
//   │  │  - Chase             │  │  - Rigidbody (execution)  │  │
//   │  │  - Attack            │  │  - Lateral Friction        │  │
//   │  │  - Stunned           │  └──────────────────────────┘  │
//   │  └─────────────────────┘                                 │
//   │            ▲  Transitions fire events (Observer)         │
//   │            │                                             │
//   │  ┌─────────────────────┐                                 │
//   │  │  HealthComponent     │ (subscribed via event, not     │
//   │  │  (decoupled, event)  │  direct field access)          │
//   │  └─────────────────────┘                                 │
//   └─────────────────────────────────────────────────────────┘
//
// SOLID COMPLIANCE:
//   S - Each concern (movement, FSM, targeting) is isolated.
//   O - Child classes extend behaviour via virtual overrides.
//   L - All concrete enemies are substitutable for the base type.
//   I - Depends on IDamageable, not any concrete health class.
//   D - Targeting uses Transform (abstract), not a named class.
//
// MODIFICATIONS v2 (backward-compatible, no breaking changes):
//   1. Health exposed as protected property (HealthComponent):
//      Child classes (e.g. EnemyKamikaze) can call TakeDamage()
//      for self-damage without requiring their own GetComponent.
//   2. SuppressAttackDetection flag:
//      Allows child classes to own the Attack state for a fixed
//      duration (e.g. ram dash) without the base class auto-
//      transitioning back to Chase mid-action.
//   3. RequestNavPathRefresh(Vector3 destination):
//      Exposes a controlled way for children to force a path
//      recalculation to a custom destination (e.g. Shooter orbit
//      point) without touching private nav path state directly.
//
// MODIFICATIONS v3 (backward-compatible, surgical patches):
//   [PATCH-1a] ShouldUseNavMeshLocomotion() virtual method:
//      Replaces the hardcoded (Chase || Patrol) gate in FixedUpdate.
//      Child classes override to opt into NavMesh locomotion in
//      additional states (e.g. EnemyShooter during Attack orbiting).
//   [PATCH-1b] NavMesh.SamplePosition failsafe in RefreshNavPath
//      and RequestNavPathRefresh:
//      Before calling NavMesh.CalculatePath, the vehicle's current
//      position is snapped to the nearest NavMesh surface within a
//      5-unit radius. This prevents pathfinding failures when a
//      Rigidbody collision nudges the vehicle slightly off the baked
//      NavMesh surface (common on ramps and crater edges).
//
// CHILD CLASS RESPONSIBILITIES:
//   Override the following virtual methods to specialise behavior:
//     • OnPatrolUpdate()   – custom patrol waypoint logic
//     • OnChaseUpdate()    – optional chase tweaks (e.g. flanking)
//     • OnAttackUpdate()   – REQUIRED: implement the attack pattern
//     • OnStunnedUpdate()  – optional stun effects (sparks, etc.)
//     • OnStateEntered()   – react to any state entry
//     • OnStateExited()    – clean up on state exit
//     • GetChaseDetectionRadius() – per-enemy tunable range
//     • GetAttackRange()   – per-enemy tunable range
// ==============================================================

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Defines the states of the Enemy Finite State Machine.
/// Kept as a top-level enum for readability and serialisation
/// in the Inspector (visible in derived class editors).
/// </summary>
public enum EnemyState
{
    /// <summary>Wandering; no target detected.</summary>
    Patrol,

    /// <summary>Target detected; closing in.</summary>
    Chase,

    /// <summary>Within attack range; executing attack behaviour.</summary>
    Attack,

    /// <summary>Temporarily disabled (e.g., hit by EMP).</summary>
    Stunned
}

/// <summary>
/// Abstract foundation for all enemy vehicle AI.
/// Handles physics-driven movement along NavMesh paths
/// and drives a Finite State Machine whose leaf states
/// child classes customise via protected virtual methods.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(NavMeshAgent), typeof(HealthComponent))]
public abstract class EnemyVehicleBase : MonoBehaviour
{
    // ----------------------------------------------------------
    // INSPECTOR — MOVEMENT TUNING
    // Mirror the ArcadeVehicleController parameter naming for
    // design-team consistency when tuning in the Inspector.
    // ----------------------------------------------------------

    [Header("Movement — Acceleration")]
    [Tooltip("Forward thrust force applied via Rigidbody.AddForce.")]
    [SerializeField] protected float acceleration = 25f;

    [Tooltip("Maximum speed the vehicle may reach while chasing.")]
    [SerializeField] protected float maximumChaseSpeed = 14f;

    [Tooltip("Maximum speed the vehicle may reach while patrolling.")]
    [SerializeField] protected float maximumPatrolSpeed = 7f;

    [Header("Movement — Steering")]
    [Tooltip("Degrees per second the vehicle can rotate to face the next NavMesh corner.")]
    [SerializeField] protected float turnSpeed = 90f;

    [Tooltip("Lateral drag coefficient; prevents ice-skating. Higher = grippier.")]
    [SerializeField] protected float lateralFriction = 6f;

    [Header("Movement — Gravity")]
    [Tooltip("Custom downward force; keep consistent with ArcadeVehicleController.")]
    [SerializeField] private float customGravity = -1.62f;      // Lunar default, mirrors player

    [Header("FSM — Detection")]
    [Tooltip("Radius at which this enemy detects the player and begins chasing.")]
    [SerializeField] private float baseChaseRadius = 20f;

    [Tooltip("Radius at which this enemy enters Attack state.")]
    [SerializeField] private float baseAttackRange = 8f;

    [Tooltip("If player leaves this radius, enemy returns to Patrol.")]
    [SerializeField] private float loseTargetRadius = 35f;

    [Header("FSM — Patrol")]
    [Tooltip("Radius around the spawn point used to pick random patrol waypoints.")]
    [SerializeField] private float patrolRadius = 15f;

    [Tooltip("How long the enemy idles at each patrol waypoint before picking the next.")]
    [SerializeField] private float patrolIdleDuration = 2f;

    // ----------------------------------------------------------
    // INSPECTOR — STUN
    // ----------------------------------------------------------

    [Header("Stun")]
    [Tooltip("Stun duration in seconds when EnterStunned() is called externally.")]
    [SerializeField] private float stunDuration = 3f;

    // ----------------------------------------------------------
    // PROTECTED REFERENCES
    // Exposed to child classes so they can read (not replace)
    // core components without requiring GetComponent calls.
    // ----------------------------------------------------------

    /// <summary>The player's Transform. Set by FindPlayer() at Awake.</summary>
    protected Transform Player { get; private set; }

    /// <summary>Physics body used for all force-based movement.</summary>
    protected Rigidbody Rb { get; private set; }

    /// <summary>Used ONLY to calculate NavMesh paths; movement is via Rigidbody.</summary>
    protected NavMeshAgent Agent { get; private set; }

    /// <summary>Current active state of the FSM. Read-only to children; use TransitionTo().</summary>
    protected EnemyState CurrentState { get; private set; } = EnemyState.Patrol;

    // ----------------------------------------------------------
    // PROTECTED — HEALTH COMPONENT (v2 addition)
    // Exposed as a protected property so child classes can call
    // TakeDamage() for self-damage (e.g. Kamikaze ramming cost)
    // without needing their own GetComponent or breaking the
    // decoupling contract. Read-only reference; children cannot
    // replace the component, only interact via its public API.
    // ----------------------------------------------------------

    /// <summary>
    /// Reference to the HealthComponent on this GameObject.
    /// Use Health.TakeDamage(amount) to apply self-damage.
    /// Do NOT cache a separate GetComponent reference in child classes.
    /// </summary>
    protected HealthComponent Health { get; private set; }

    // ----------------------------------------------------------
    // PROTECTED — FSM CONTROL FLAGS (v2 addition)
    // ----------------------------------------------------------

    /// <summary>
    /// When true, the base class will NOT automatically transition
    /// from Attack back to Chase if the player leaves attack range.
    /// Set this flag in OnStateEntered(Attack) and clear it in
    /// OnStateExited(Attack) to give child classes full ownership
    /// of the Attack state for a fixed-duration action (e.g. a
    /// Kamikaze ram dash that must run to completion regardless of
    /// the player's position during the dash).
    /// </summary>
    protected bool SuppressAttackDetection { get; set; } = false;

    // ----------------------------------------------------------
    // PRIVATE STATE
    // ----------------------------------------------------------

    /// <summary>Reused NavMeshPath to avoid per-frame GC allocations.</summary>
    private NavMeshPath _navPath;

    /// <summary>Index into _navPath.corners pointing at the current steering target.</summary>
    private int _currentCornerIndex;

    /// <summary>World-space spawn position; used as patrol centre.</summary>
    private Vector3 _spawnPosition;

    /// <summary>Tracks whether the patrol coroutine is currently waiting at a waypoint.</summary>
    private bool _isPatrolWaiting;

    /// <summary>Running stun timer coroutine reference so it can be cancelled early.</summary>
    private Coroutine _stunCoroutine;

    // ----------------------------------------------------------
    // PUBLIC EVENTS (Observer Pattern)
    // External systems (VFX, audio, UI) subscribe to these
    // without coupling to the enemy's internal logic.
    // ----------------------------------------------------------

    /// <summary>
    /// Fired every time the FSM transitions to a new state.
    /// Parameters: (previous state, new state).
    /// </summary>
    public event Action<EnemyState, EnemyState> OnStateChanged;

    /// <summary>
    /// Fired when the enemy dies (relayed from HealthComponent.OnDied).
    /// Listeners (GameManager, VFX spawner, etc.) handle destruction.
    /// </summary>
    public event Action OnEnemyDied;

    // ----------------------------------------------------------
    // UNITY LIFECYCLE
    // ----------------------------------------------------------

    /// <summary>
    /// Caches component references, configures the NavMeshAgent for
    /// path-only mode, and subscribes to the HealthComponent.
    /// </summary>
    protected virtual void Awake()
    {
        Rb = GetComponent<Rigidbody>();
        Agent = GetComponent<NavMeshAgent>();
        Health = GetComponent<HealthComponent>();
        _navPath = new NavMeshPath();
        _spawnPosition = transform.position;

        ConfigureAgent();
        ConfigureRigidbody();
        SubscribeToHealth();
        FindPlayer();
    }

    /// <summary>Begins Patrol state once all components are initialised.</summary>
    protected virtual void Start()
    {
        TransitionTo(EnemyState.Patrol);
    }

    /// <summary>
    /// Drives the FSM tick. Each state dispatches to a virtual handler
    /// that child classes override for specialised behaviour.
    /// </summary>
    private void Update()
    {
        switch (CurrentState)
        {
            case EnemyState.Patrol:
                HandlePatrolDetection();
                OnPatrolUpdate();
                break;

            case EnemyState.Chase:
                HandleChaseDetection();
                OnChaseUpdate();
                break;

            case EnemyState.Attack:
                // Only run auto-range detection when a child hasn't
                // taken full ownership of the Attack state.
                if (!SuppressAttackDetection)
                {
                    HandleAttackDetection();
                }
                OnAttackUpdate();
                break;

            case EnemyState.Stunned:
                OnStunnedUpdate();
                break;
        }
    }

    /// <summary>
    /// All physics-driven movement is executed here, decoupled from
    /// game-logic updates in Update(). Called at a fixed timestep.
    /// </summary>
    private void FixedUpdate()
    {
        ApplyCustomGravity();

        // [PATCH-1a] Route locomotion through the virtual gate instead of a
        // hardcoded state list. Child classes (e.g. EnemyShooter) override
        // ShouldUseNavMeshLocomotion() to return true during Attack so their
        // orbit movement still runs through the same NavMesh pipeline.
        if (ShouldUseNavMeshLocomotion())
        {
            RefreshNavPath();
            DriveTowardsNextCorner();
            ApplyLateralFriction();
        }
        else if (CurrentState == EnemyState.Stunned)
        {
            // Friction-only during stun: bleed off existing velocity.
            ApplyLateralFriction();
        }
    }

    // ----------------------------------------------------------
    // LOCOMOTION GATE [PATCH-1a]
    // ----------------------------------------------------------

    /// <summary>
    /// Returns true when the base FixedUpdate should run the full
    /// NavMesh locomotion pipeline (RefreshNavPath + DriveTowardsNextCorner
    /// + ApplyLateralFriction).
    ///
    /// Base implementation allows locomotion in Patrol and Chase only.
    /// Override in child classes to include additional states — for example,
    /// EnemyShooter returns true for Attack as well so the chassis continues
    /// orbiting while the turret fires.
    ///
    /// IMPORTANT: returning true here enables full locomotion. The Stun
    /// friction-only fallback in FixedUpdate is NOT gated by this method
    /// and always runs when the state is Stunned regardless of this value.
    /// </summary>
    /// <returns>True if the NavMesh locomotion pipeline should run this frame.</returns>
    protected virtual bool ShouldUseNavMeshLocomotion()
    {
        return CurrentState == EnemyState.Chase || CurrentState == EnemyState.Patrol;
    }

    private void OnDestroy()
    {
        // Prevent memory leaks from dangling event subscriptions.
        if (Health != null)
        {
            Health.OnDied -= OnHealthDepleted;
        }
    }

    // ----------------------------------------------------------
    // FSM — TRANSITION CONTROLLER
    // Single point of truth for all state changes. This enforces
    // the Open/Closed Principle: adding a new state only requires
    // adding a case here and new virtual methods below.
    // ----------------------------------------------------------

    /// <summary>
    /// Transitions the FSM to <paramref name="newState"/>, firing
    /// <see cref="OnStateChanged"/> and calling the enter/exit hooks.
    /// Child classes should NOT set <see cref="CurrentState"/> directly;
    /// always call this method to keep transitions auditable.
    /// </summary>
    /// <param name="newState">The state to transition into.</param>
    protected void TransitionTo(EnemyState newState)
    {
        if (newState == CurrentState) return;

        EnemyState previousState = CurrentState;

        // Notify child + external listeners that a state was exited.
        OnStateExited(previousState);

        CurrentState = newState;

        // Reset the nav path so a fresh route is calculated immediately.
        _navPath.ClearCorners();
        _currentCornerIndex = 0;

        // Notify child + external listeners that a new state was entered.
        OnStateEntered(newState);
        OnStateChanged?.Invoke(previousState, newState);

        Debug.Log($"[{name}] FSM: {previousState} → {newState}", this);
    }

    // ----------------------------------------------------------
    // FSM — DETECTION LOGIC (per state)
    // These run every Update() frame in their respective states
    // and call TransitionTo() when conditions are met.
    // ----------------------------------------------------------

    private void HandlePatrolDetection()
    {
        if (Player == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, Player.position);

        if (distanceToPlayer <= GetChaseDetectionRadius())
        {
            TransitionTo(EnemyState.Chase);
        }
    }

    private void HandleChaseDetection()
    {
        if (Player == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, Player.position);

        if (distanceToPlayer <= GetAttackRange())
        {
            TransitionTo(EnemyState.Attack);
        }
        else if (distanceToPlayer > loseTargetRadius)
        {
            TransitionTo(EnemyState.Patrol);
        }
    }

    private void HandleAttackDetection()
    {
        if (Player == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, Player.position);

        // Player escaped attack range → resume chase.
        if (distanceToPlayer > GetAttackRange())
        {
            TransitionTo(EnemyState.Chase);
        }
    }

    // ----------------------------------------------------------
    // FSM — VIRTUAL STATE UPDATE HOOKS
    // Child classes override these to inject state-specific logic
    // without touching the base transition system (OCP).
    // ----------------------------------------------------------

    /// <summary>
    /// Called every frame while in Patrol state.
    /// Base implementation drives the vehicle to random waypoints.
    /// Override to implement patrol path-following or guarding logic.
    /// </summary>
    protected virtual void OnPatrolUpdate()
    {
        if (_isPatrolWaiting || _navPath.corners.Length > 0) return;

        // Pick a new random waypoint and immediately start moving.
        StartCoroutine(PatrolToRandomWaypoint());
    }

    /// <summary>
    /// Called every frame while in Chase state.
    /// Base implementation continuously recalculates path to Player.
    /// Override for flanking, ambush routing, or formation logic.
    /// </summary>
    protected virtual void OnChaseUpdate()
    {
        // Navigation is handled in FixedUpdate via DriveTowardsNextCorner().
        // This hook exists for children to add non-physics chase logic.
    }

    /// <summary>
    /// Called every frame while in Attack state.
    /// Base implementation is intentionally empty — child classes MUST
    /// override this to define their attack pattern (shooting, ramming, etc.).
    /// </summary>
    protected virtual void OnAttackUpdate() { }

    /// <summary>
    /// Called every frame while in Stunned state.
    /// Override to add visual feedback (sparks, flickering, etc.).
    /// </summary>
    protected virtual void OnStunnedUpdate() { }

    /// <summary>
    /// Called once immediately after transitioning INTO <paramref name="newState"/>.
    /// Override to play entry animations, sounds, or reset sub-state variables.
    /// Always call base.OnStateEntered(newState) if you override.
    /// </summary>
    protected virtual void OnStateEntered(EnemyState newState) { }

    /// <summary>
    /// Called once immediately BEFORE transitioning OUT OF <paramref name="exitedState"/>.
    /// Override to clean up timers, animations, or cached data.
    /// Always call base.OnStateExited(exitedState) if you override.
    /// </summary>
    protected virtual void OnStateExited(EnemyState exitedState) { }

    // ----------------------------------------------------------
    // FSM — RANGE PROVIDERS (virtual, child-overridable)
    // Returning values from virtual methods instead of exposing
    // raw serialised fields lets child classes dynamically scale
    // ranges (e.g., a Shooter with a wider detection cone).
    // ----------------------------------------------------------

    /// <summary>
    /// Returns the radius at which this enemy transitions from Patrol
    /// to Chase. Override per enemy type for fine-grained tuning.
    /// </summary>
    protected virtual float GetChaseDetectionRadius() => baseChaseRadius;

    /// <summary>
    /// Returns the radius at which this enemy transitions from Chase
    /// to Attack. Override per enemy type for fine-grained tuning.
    /// </summary>
    protected virtual float GetAttackRange() => baseAttackRange;

    // ----------------------------------------------------------
    // STUN — PUBLIC API
    // Called externally (e.g., by a weapon with an EMP effect).
    // Decoupled: callers do not need to know the FSM internals.
    // ----------------------------------------------------------

    /// <summary>
    /// Interrupts the current behaviour and enters Stunned state
    /// for <see cref="stunDuration"/> seconds, then resumes Chase.
    /// Safe to call while already stunned (resets the timer).
    /// </summary>
    public void EnterStunned()
    {
        if (_stunCoroutine != null)
            StopCoroutine(_stunCoroutine);

        _stunCoroutine = StartCoroutine(StunRoutine());
    }

    private IEnumerator StunRoutine()
    {
        TransitionTo(EnemyState.Stunned);
        yield return new WaitForSeconds(stunDuration);
        _stunCoroutine = null;
        TransitionTo(EnemyState.Chase);
    }

    // ----------------------------------------------------------
    // HYBRID MOVEMENT SYSTEM
    // NavMeshPath is used ONLY to get world-space corner waypoints.
    // The NavMeshAgent is intentionally set to updatePosition=false
    // and updateRotation=false so Rigidbody owns all locomotion.
    // ----------------------------------------------------------

    /// <summary>
    /// Recalculates the NavMesh path to the appropriate destination
    /// depending on the current FSM state. Path is only refreshed
    /// when the previous path is exhausted (no per-frame allocations).
    /// </summary>
    private void RefreshNavPath()
    {
        // Only recalculate when we've consumed all corners.
        bool pathExhausted = _currentCornerIndex >= _navPath.corners.Length;
        if (!pathExhausted) return;

        Vector3 destination = CurrentState == EnemyState.Chase && Player != null
            ? Player.position
            : GetPatrolDestination();

        // [PATCH-1b] NavMesh surface failsafe.
        // A Rigidbody collision on a ramp or crater edge can push the vehicle
        // slightly above or below the baked NavMesh surface. NavMesh.CalculatePath
        // called with an off-surface origin returns false and the vehicle stops.
        // SamplePosition snaps the origin to the nearest valid surface point
        // within a 5-unit search radius before attempting path calculation.
        Vector3 sampledOrigin = transform.position;
        NavMeshHit originHit;
        if (NavMesh.SamplePosition(transform.position, out originHit, 5f, NavMesh.AllAreas))
        {
            sampledOrigin = originHit.position;
        }

        if (NavMesh.CalculatePath(sampledOrigin, destination, NavMesh.AllAreas, _navPath))
        {
            _currentCornerIndex = 0;
        }
    }

    /// <summary>
    /// Forces an immediate recalculation of the NavMesh path to the
    /// specified <paramref name="destination"/>, discarding the current path.
    /// Used by child classes that navigate to a custom point rather than
    /// directly to the player (e.g. EnemyShooter orbit position).
    /// </summary>
    /// <param name="destination">World-space target position on the NavMesh.</param>
    protected void RequestNavPathRefresh(Vector3 destination)
    {
        // [PATCH-1b] Same NavMesh surface failsafe applied to the child-facing
        // path refresh so EnemyShooter orbit paths are equally robust.
        Vector3 sampledOrigin = transform.position;
        NavMeshHit originHit;
        if (NavMesh.SamplePosition(transform.position, out originHit, 5f, NavMesh.AllAreas))
        {
            sampledOrigin = originHit.position;
        }

        if (NavMesh.CalculatePath(sampledOrigin, destination, NavMesh.AllAreas, _navPath))
        {
            _currentCornerIndex = 0;
        }
    }

    /// <summary>
    /// Steers the Rigidbody towards the next corner of the current NavMesh path.
    /// Uses AddForce for acceleration and MoveRotation for steering, mirroring
    /// the ArcadeVehicleController approach for physical consistency.
    /// </summary>
    private void DriveTowardsNextCorner()
    {
        if (_navPath.corners.Length == 0 || _currentCornerIndex >= _navPath.corners.Length)
            return;

        Vector3 targetCorner = _navPath.corners[_currentCornerIndex];
        Vector3 directionToCorner = (targetCorner - transform.position).normalized;

        // --- STEERING: Rotate towards the next corner ---
        // Project direction onto the horizontal plane to avoid tilting on slopes.
        Vector3 flatDirection = new Vector3(directionToCorner.x, 0f, directionToCorner.z).normalized;
        if (flatDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(flatDirection);
            Quaternion newRotation = Quaternion.RotateTowards(
                Rb.rotation,
                targetRotation,
                turnSpeed * Time.fixedDeltaTime
            );
            Rb.MoveRotation(newRotation);
        }

        // --- ACCELERATION: Apply forward force if not at speed cap ---
        float maxSpeed = CurrentState == EnemyState.Patrol ? maximumPatrolSpeed : maximumChaseSpeed;
        float forwardSpeed = Vector3.Dot(Rb.linearVelocity, transform.forward);

        if (forwardSpeed < maxSpeed)
        {
            // Align force with the vehicle's forward axis (not path direction)
            // so the vehicle cannot strafe — it must turn first, then accelerate.
            Rb.AddForce(transform.forward * acceleration, ForceMode.Acceleration);
        }

        // --- CORNER ADVANCE: Move to the next corner when close enough ---
        float horizontalDistance = Vector3.Distance(
            new Vector3(transform.position.x, 0f, transform.position.z),
            new Vector3(targetCorner.x, 0f, targetCorner.z)
        );

        if (horizontalDistance < 1.5f)
        {
            _currentCornerIndex++;
        }
    }

    /// <summary>
    /// Cancels lateral (sideways) velocity by Lerping the local X velocity
    /// to zero. Prevents the vehicle from sliding sideways like on ice.
    /// Mirrors the ArcadeVehicleController.ApplyLateralFriction() method.
    /// </summary>
    protected void ApplyLateralFriction()
    {
        Vector3 localVelocity = transform.InverseTransformDirection(Rb.linearVelocity);
        localVelocity.x = Mathf.Lerp(localVelocity.x, 0f, lateralFriction * Time.fixedDeltaTime);
        Rb.linearVelocity = transform.TransformDirection(localVelocity);
    }

    /// <summary>
    /// Applies a constant custom gravity force, bypassing Unity's
    /// physics gravity so we can use the same lunar gravity as the player.
    /// </summary>
    private void ApplyCustomGravity()
    {
        Rb.AddForce(Vector3.up * customGravity, ForceMode.Acceleration);
    }

    // ----------------------------------------------------------
    // PATROL HELPERS
    // ----------------------------------------------------------

    /// <summary>
    /// Coroutine that picks a random NavMesh waypoint within
    /// <see cref="patrolRadius"/>, waits at the destination, then
    /// clears the path so <see cref="OnPatrolUpdate"/> picks a new one.
    /// </summary>
    private IEnumerator PatrolToRandomWaypoint()
    {
        _isPatrolWaiting = true;

        Vector3 randomPoint = _spawnPosition + UnityEngine.Random.insideUnitSphere * patrolRadius;
        randomPoint.y = _spawnPosition.y;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomPoint, out hit, patrolRadius, NavMesh.AllAreas))
        {
            NavMesh.CalculatePath(transform.position, hit.position, NavMesh.AllAreas, _navPath);
            _currentCornerIndex = 0;
        }

        // Wait until the path is fully consumed (arrived at waypoint).
        yield return new WaitUntil(() => _currentCornerIndex >= _navPath.corners.Length);

        // Idle at the waypoint before choosing the next one.
        yield return new WaitForSeconds(patrolIdleDuration);

        _navPath.ClearCorners();
        _isPatrolWaiting = false;
    }

    /// <summary>
    /// Returns the next patrol destination. Override this if child
    /// classes use a fixed waypoint list instead of random sampling.
    /// </summary>
    protected virtual Vector3 GetPatrolDestination() => _spawnPosition;

    // ----------------------------------------------------------
    // HEALTH — DECOUPLED LISTENER
    // The base class subscribes to HealthComponent events.
    // It does NOT implement IDamageable itself; it delegates fully.
    // ----------------------------------------------------------

    /// <summary>
    /// Subscribes to HealthComponent events. If HealthComponent is
    /// missing, logs a clear error rather than throwing a silent NPE.
    /// </summary>
    private void SubscribeToHealth()
    {
        if (Health == null)
        {
            Debug.LogError($"[{name}] EnemyVehicleBase requires a HealthComponent. " +
                           "Add one to this GameObject.", this);
            return;
        }

        Health.OnDied += OnHealthDepleted;
    }

    /// <summary>
    /// Invoked by HealthComponent.OnDied. Fires the public
    /// OnEnemyDied event so listeners (GameManager, VFX spawner,
    /// etc.) can react. This class does NOT destroy itself.
    /// </summary>
    private void OnHealthDepleted()
    {
        // Disable FSM and physics so the dead vehicle doesn't keep moving.
        CurrentState = EnemyState.Stunned; // Bypass TransitionTo to avoid event noise.
        Rb.linearVelocity = Vector3.zero;
        enabled = false;

        OnEnemyDied?.Invoke();
    }

    // ----------------------------------------------------------
    // COMPONENT CONFIGURATION
    // ----------------------------------------------------------

    /// <summary>
    /// Configures the NavMeshAgent to act as a path calculator only.
    /// updatePosition and updateRotation are disabled so the Rigidbody
    /// is the sole authority on the vehicle's Transform.
    /// </summary>
    private void ConfigureAgent()
    {
        Agent.updatePosition = false;   // Rigidbody owns position.
        Agent.updateRotation = false;   // Rigidbody owns rotation.
        Agent.updateUpAxis   = false;   // Prevent agent from fighting gravity.
        Agent.isStopped      = true;    // Agent is path-only; never moves itself.
    }

    /// <summary>
    /// Configures the Rigidbody for an arcade vehicle feel.
    /// Gravity is disabled in favour of ApplyCustomGravity().
    /// </summary>
    private void ConfigureRigidbody()
    {
        Rb.useGravity = false;
        Rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    // ----------------------------------------------------------
    // TARGETING
    // ----------------------------------------------------------

    /// <summary>
    /// Locates the player by the "Player" tag at startup.
    /// Using tag-based lookup at Awake avoids a hard class dependency.
    /// Replace with a GameManager.Instance.PlayerTransform reference
    /// once a GameManager is available for O(1) access.
    /// </summary>
    private void FindPlayer()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            Player = playerObject.transform;
        }
        else
        {
            Debug.LogWarning($"[{name}] No GameObject with tag 'Player' found in scene. " +
                             "Enemy will remain in Patrol state.", this);
        }
    }

    // ----------------------------------------------------------
    // GIZMOS — Editor Visualisation
    // Draws detection radii in the Scene view for designers.
    // Only compiled into the editor build.
    // ----------------------------------------------------------

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Chase detection radius — yellow.
        Gizmos.color = new Color(1f, 1f, 0f, 0.25f);
        Gizmos.DrawSphere(transform.position, baseChaseRadius);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, baseChaseRadius);

        // Attack range — red.
        Gizmos.color = new Color(1f, 0f, 0f, 0.2f);
        Gizmos.DrawSphere(transform.position, baseAttackRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, baseAttackRange);

        // Lose-target radius — grey.
        Gizmos.color = Color.grey;
        Gizmos.DrawWireSphere(transform.position, loseTargetRadius);

        // Patrol radius — cyan.
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(_spawnPosition == Vector3.zero ? transform.position : _spawnPosition, patrolRadius);

        // Draw current NavMesh path corners if available.
        if (_navPath != null && _navPath.corners.Length > 1)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < _navPath.corners.Length - 1; i++)
            {
                Gizmos.DrawLine(_navPath.corners[i], _navPath.corners[i + 1]);
                Gizmos.DrawSphere(_navPath.corners[i], 0.2f);
            }
        }
    }
#endif
}
