using System;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Pool-managed projectile that supports two movement modes:
///
///   1. HOMING    – While <see cref="_homingTimer"/> &lt; <see cref="homingDuration"/>,
///                  rotates toward the target's chassis centre each frame before
///                  translating forward.  When the timer expires the heading locks
///                  and the round continues as a ballistic shell.
///
///   2. BALLISTIC – Moves in a fixed straight line (original behaviour, and the
///                  automatic fallback when no homing target is assigned).
///
/// DAMAGE ARCHITECTURE
///   • <see cref="IDamageable"/> is resolved via <c>GetComponentInParent</c> so
///     that hitting ANY child collider on a multi-collider vehicle still reaches
///     the <see cref="HealthComponent"/> that lives on the root GameObject.
///   • An <em>owner root</em> reference (<see cref="_ownerRoot"/>) is stored when
///     the weapon calls <see cref="SetOwner"/>.  The projectile skips damage if the
///     hit root matches the owner root, completely preventing self-damage
///     regardless of layer configuration.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class Projectile : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector fields
    // -------------------------------------------------------------------------

    [Header("Motion")]
    [Tooltip("Forward travel speed in metres per second.")]
    [SerializeField] private float projectileSpeed = 18f;

    [Tooltip("Maximum lifetime before the projectile is returned to the pool.")]
    [SerializeField] private float lifetime = 4f;

    [Header("Homing")]
    [Tooltip("Enable homing behaviour when a target has been assigned via SetTarget().")]
    [SerializeField] private bool isHoming = false;

    [Tooltip("Degrees per second the projectile can rotate toward its target while homing.")]
    [SerializeField] private float homingTurnRate = 180f;

    [Tooltip("Seconds of active homing before the projectile locks its current heading " +
             "and travels in a straight line for the rest of its lifetime.")]
    [SerializeField] private float homingDuration = 1.5f;

    [Header("Combat")]
    [Tooltip("Damage applied to the first IDamageable hit.")]
    [SerializeField] private int damage = 10;

    [Header("Layer Filtering")]
    [Tooltip("Layers to ignore on trigger enter (e.g. arena boundary triggers). " +
             "For self-damage prevention, rely on SetOwner() instead of this mask so " +
             "that same-layer enemies still take damage from each other.")]
    [SerializeField] private LayerMask ignoreLayers;

    // -------------------------------------------------------------------------
    // Private runtime state
    // -------------------------------------------------------------------------

    /// <summary>Pool reference used to release this instance without a spawner reference.</summary>
    private IObjectPool<Projectile> _pool;

    /// <summary>Cached transform for performance (avoids repeated property lookup).</summary>
    private Transform _transform;

    /// <summary>Seconds elapsed since this projectile was last retrieved from the pool.</summary>
    private float _activeTimer;

    /// <summary>Seconds spent actively homing this activation cycle.</summary>
    private float _homingTimer;

    /// <summary>Guard flag preventing a double-release to the pool in the same frame.</summary>
    private bool _isReturned;

    /// <summary>
    /// The root transform of the entity that fired this projectile.
    /// Any collider whose <c>transform.root</c> matches this reference is treated
    /// as the owner and is unconditionally skipped, preventing self-damage.
    /// Cleared on pool return.
    /// </summary>
    private Transform _ownerRoot;

    /// <summary>
    /// Optional homing target assigned by the weapon system.
    /// Null when no target is available or after the projectile is returned to the pool.
    /// </summary>
    private Transform _homingTarget;

    /// <summary>
    /// Runtime damage multiplier forwarded by <see cref="VehicleWeapon"/> each time
    /// this projectile is retrieved from the pool.  The base <see cref="damage"/> field
    /// (set in the Inspector) is multiplied by this value on impact.
    /// Reset to 1 on pool return so stale power-up states never carry over.
    /// </summary>
    private float _damageMultiplier = 1f;

    // -------------------------------------------------------------------------
    // Unity messages
    // -------------------------------------------------------------------------

    private void Awake()
    {
        _transform = transform;

        // Safety net: a projectile collider must be a trigger so it does not
        // physically deflect off other Rigidbodies.  Set this in the prefab to
        // suppress the log.
        Collider col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning("[Projectile] Collider is not set as a Trigger. " +
                             "Enabling isTrigger automatically.", this);
            col.isTrigger = true;
        }
    }

    private void Update()
    {
        Fly();
        CheckLifetime();
    }

    // -------------------------------------------------------------------------
    // Pool lifecycle API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stores the pool reference so the projectile can release itself.
    /// Must be called exactly once after instantiation.
    /// </summary>
    public void SetPool(IObjectPool<Projectile> pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
    }

    /// <summary>
    /// Called by the pool's <c>actionOnGet</c> delegate each time this instance
    /// is retrieved.  Resets ALL per-activation state so a reused projectile
    /// behaves identically to a freshly created one.
    /// </summary>
    public void OnGetFromPool()
    {
        _isReturned  = false;
        _activeTimer = 0f;
        _homingTimer = 0f;
        gameObject.SetActive(true);
    }

    /// <summary>
    /// Called by the pool's <c>actionOnRelease</c> delegate.
    /// Clears all external references so pooled objects cannot retain
    /// stale pointers to destroyed GameObjects.
    /// </summary>
    public void OnReturnToPool()
    {
        ClearTarget();
        ClearOwner();
        _damageMultiplier = 1f;   // Reset so the next user starts from the base value.
        gameObject.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Owner API  (called by VehicleWeapon before activation)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stores the root transform of the entity that fired this round.
    /// OnTriggerEnter will skip any collider whose root matches this transform,
    /// making self-damage impossible regardless of layer setup.
    /// </summary>
    /// <param name="ownerRoot">
    /// The <c>transform.root</c> of the firing vehicle.  Pass <c>null</c> to
    /// disable the owner check (not recommended for gameplay projectiles).
    /// </param>
    public void SetOwner(Transform ownerRoot)
    {
        _ownerRoot = ownerRoot;
    }

    /// <summary>Clears the owner reference. Called automatically on pool return.</summary>
    public void ClearOwner()
    {
        _ownerRoot = null;
    }

    // -------------------------------------------------------------------------
    // Damage Multiplier API  (called by VehicleWeapon before activation)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sets the runtime damage scale applied on top of the base <see cref="damage"/>.
    /// Called by <see cref="VehicleWeapon.OnGetProjectile"/> immediately before
    /// the projectile is activated.  The value is reset to 1 on pool return.
    /// </summary>
    /// <param name="multiplier">Damage scale factor. Clamped to a minimum of 0.1.</param>
    public void SetDamageMultiplier(float multiplier)
    {
        _damageMultiplier = Mathf.Max(0.1f, multiplier);
    }

    // -------------------------------------------------------------------------
    // Homing API  (called by VehicleWeapon before activation)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Assigns a homing target.  If <see cref="isHoming"/> is <c>false</c> the
    /// reference is stored but never used — the projectile still flies ballistically.
    /// </summary>
    public void SetTarget(Transform target)
    {
        _homingTarget = target;
    }

    /// <summary>Clears the homing target. Called automatically on pool return.</summary>
    public void ClearTarget()
    {
        _homingTarget = null;
    }

    // -------------------------------------------------------------------------
    // Movement
    // -------------------------------------------------------------------------

    /// <summary>
    /// Steers and translates the projectile each frame.
    ///
    /// HOMING phase (while <see cref="isHoming"/> is true, a target exists, and
    /// <see cref="_homingTimer"/> &lt; <see cref="homingDuration"/>):
    ///   – Rotate toward <c>target.position + Vector3.up * 0.5f</c> using
    ///     <see cref="Quaternion.RotateTowards"/> (smooth, frame-rate independent).
    ///   – The +0.5 m offset targets the chassis centre rather than the root pivot
    ///     which may sit at ground level.
    ///
    /// BALLISTIC phase (homing expired or no target):
    ///   – Heading is frozen; the projectile translates forward in a straight line.
    /// </summary>
    private void Fly()
    {
        bool canHome = isHoming
                       && _homingTarget != null
                       && _homingTimer < homingDuration;

        if (canHome)
        {
            _homingTimer += Time.deltaTime;

            Vector3 aimPoint  = _homingTarget.position + Vector3.up * 0.5f;
            Vector3 direction = (aimPoint - _transform.position).normalized;

            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion desiredRotation = Quaternion.LookRotation(direction);
                _transform.rotation = Quaternion.RotateTowards(
                    _transform.rotation,
                    desiredRotation,
                    homingTurnRate * Time.deltaTime);
            }
        }

        // Always translate along the current forward axis so the round keeps
        // moving even while rotating during the homing phase.
        _transform.Translate(Vector3.forward * (projectileSpeed * Time.deltaTime), Space.Self);
    }

    private void CheckLifetime()
    {
        _activeTimer += Time.deltaTime;
        if (_activeTimer >= lifetime)
        {
            ReturnToPool();
        }
    }

    // -------------------------------------------------------------------------
    // Collision & damage
    // -------------------------------------------------------------------------

    private void OnTriggerEnter(Collider other)
    {
        // ── STEP 1: Entry confirmation ────────────────────────────────────────
        // Prints full identity of the hit collider: name, layer, tag, parent, root.
        // If the projectile is physically passing through the player without
        // triggering this log at all, the problem is Physics (layer matrix / Is Trigger).
        // If this log fires but damage doesn't, the problem is in the guards or search below.
        Debug.Log($"🚀 [PROJECTILE] Trigger hit: '{other.gameObject.name}' " +
                  $"| Layer: {LayerMask.LayerToName(other.gameObject.layer)} ({other.gameObject.layer}) " +
                  $"| Tag: '{other.gameObject.tag}' " +
                  $"| Parent: '{(other.transform.parent != null ? other.transform.parent.name : "NO PARENT")}' " +
                  $"| Root: '{other.transform.root.name}'");

        // ── STEP 2: Layer filter ──────────────────────────────────────────────
        if (((1 << other.gameObject.layer) & ignoreLayers.value) != 0)
        {
            Debug.Log($"🔇 [PROJECTILE] Skipped — layer mask filtered out: " +
                      $"'{other.gameObject.name}' (Layer index {other.gameObject.layer})");
            return;
        }

        // ── STEP 3: Self-damage guard ─────────────────────────────────────────
        if (_ownerRoot != null && other.transform.root == _ownerRoot)
        {
            Debug.Log($"🛡️ [PROJECTILE] Skipped — hit belongs to owner root: '{_ownerRoot.name}'");
            return;
        }

        // ── STEP 3b: Player-identity diagnostic ───────────────────────────────
        // If the hit object is not tagged "Player", log its full identity so we
        // can tell whether the projectile is reaching the player at all, or is
        // only ever hitting walls / other enemies.
        if (!other.gameObject.CompareTag("Player"))
        {
            Debug.Log($"🔵 [DIAGNOSTIC] Hit object is NOT Player. " +
                      $"Name: '{other.name}' " +
                      $"| Tag: '{other.gameObject.tag}' " +
                      $"| Layer: {LayerMask.LayerToName(other.gameObject.layer)} ({other.gameObject.layer})");
        }

        // ── STEP 4a: Primary search — GetComponentInParent ────────────────────
        // Walks UP the hierarchy from the hit child collider.
        // This is the expected path: HealthComponent on root, collider on child.
        IDamageable target = other.GetComponentInParent<IDamageable>();

        // ── STEP 4b: Fallback search — GetComponent on the hit object itself ──
        // Catches the edge case where the collider and HealthComponent share the
        // exact same GameObject (GetComponentInParent already covers this, but
        // making it explicit rules out any subtle hierarchy ambiguity).
        if (target == null)
        {
            target = other.GetComponent<IDamageable>();
            if (target != null)
            {
                Debug.Log($"🔍 [PROJECTILE] IDamageable found via GetComponent (same GameObject) " +
                          $"on: '{other.gameObject.name}'");
            }
        }

        // ── STEP 5: Apply damage or emit a full root-component diagnostic ─────
        if (target != null)
        {
            int scaledDamage = Mathf.RoundToInt(damage * _damageMultiplier);
            Debug.Log($"🩸 [PROJECTILE] Successfully dealt {scaledDamage} damage (base {damage} × {_damageMultiplier:F2}) to: {target}");
            target.TakeDamage(scaledDamage);
        }
        else
        {
            // Both searches returned null. Dump every component on the root so
            // we can see whether IDamageable / HealthComponent is present at all.
            // A missing entry or a broken script reference (null) will be visible here.
            Transform root = other.transform.root;
            Component[] allComponents = root.GetComponents<Component>();

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"⚠️ [PROJECTILE] No IDamageable found after both searches.");
            sb.AppendLine($"   Hit object : '{other.gameObject.name}' " +
                          $"| Tag: '{other.gameObject.tag}' " +
                          $"| Layer: {LayerMask.LayerToName(other.gameObject.layer)} ({other.gameObject.layer})");
            sb.AppendLine($"   Root object: '{root.name}' " +
                          $"| Root Tag: '{root.tag}' " +
                          $"| {allComponents.Length} components on root");
            foreach (Component c in allComponents)
            {
                // A null entry means a missing/broken MonoBehaviour in the Inspector.
                sb.AppendLine(c != null
                    ? $"     • {c.GetType().FullName}"
                    : "     • ⛔ MISSING SCRIPT — check Inspector for broken references");
            }
            sb.AppendLine("   ── Likely causes ─────────────────────────────────────────────");
            sb.AppendLine("   1. HealthComponent is NOT on the root or any ancestor — move it up.");
            sb.AppendLine("   2. IDamageable is defined in a different assembly/namespace than HealthComponent.");
            sb.AppendLine("   3. HealthComponent has a compile error and was silently not loaded by Unity.");
            sb.AppendLine("   4. transform.root is resolving to an unexpected object (check the hierarchy).");
            Debug.LogWarning(sb.ToString());
        }

        // Return to pool whether damage was applied or not (wall hit, etc.).
        ReturnToPool();
    }

    // -------------------------------------------------------------------------
    // Pool return
    // -------------------------------------------------------------------------

    private void ReturnToPool()
    {
        if (_isReturned) return;
        _isReturned = true;

        if (_pool == null)
        {
            Destroy(gameObject);
            return;
        }

        _pool.Release(this);
    }
}