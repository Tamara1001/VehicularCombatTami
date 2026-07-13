using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.InputSystem;

/// <summary>
/// Manages firing, ammo, reloading, and projectile pooling for a single vehicle
/// weapon mount.  Shared by the player (Input System callbacks) and the AI
/// (<see cref="TryFire"/> / <see cref="SetAITarget"/>).
///
/// OWNERSHIP MODEL
///   Every projectile spawned by this weapon receives two references:
///     1. <see cref="Projectile.SetOwner"/>  – the root transform of this vehicle,
///        so the round can skip self-damage collisions unconditionally.
///     2. <see cref="Projectile.SetTarget"/> – the optional AI homing target
///        (null for player-owned weapons; projectile flies ballistically).
///   Both are set atomically inside <see cref="OnGetProjectile"/> before the
///   projectile's first <c>Update</c> runs.
/// </summary>
public sealed class VehicleWeapon : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector fields
    // -------------------------------------------------------------------------

    [Header("Firing")]
    [Tooltip("The transform from which projectiles spawn (muzzle point).")]
    [SerializeField] private Transform firePoint;

    [Tooltip("Projectile prefab managed by the internal object pool.")]
    [SerializeField] private Projectile projectilePrefab;

    [Tooltip("Minimum seconds between consecutive shots.")]
    [SerializeField] private float fireRate = 0.25f;

    [Header("Ammo & Reloading")]
    [Tooltip("Maximum rounds in a full magazine.")]
    [SerializeField] private int maxAmmo = 20;

    [Tooltip("Seconds required to complete a reload.")]
    [SerializeField] private float reloadTime = 1.5f;

    [Header("Object Pool Settings")]
    [Tooltip("Initial pool capacity (pre-allocated instances).")]
    [SerializeField] private int poolDefaultCapacity = 20;

    [Tooltip("Hard upper limit on simultaneous live projectile instances.")]
    [SerializeField] private int poolMaxSize = 40;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private IObjectPool<Projectile> _projectilePool;
    private float _lastFireTime = float.NegativeInfinity;
    private int   _currentAmmo;
    private bool  _isReloading;

    /// <summary>
    /// The root transform of the vehicle that owns this weapon.
    /// Resolved once in <see cref="Awake"/> and passed to every spawned
    /// projectile so the round can ignore its own shooter's colliders.
    /// </summary>
    private Transform _ownerRoot;

    /// <summary>
    /// Optional AI homing target forwarded to every spawned projectile.
    /// Null for player-owned weapons; the projectile flies ballistically.
    /// </summary>
    private Transform _aiTarget;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raised whenever ammo count changes.
    /// Payload: (currentAmmo, maxAmmo).  currentAmmo == -1 signals "Reloading…".
    /// </summary>
    public event Action<int, int> OnAmmoChanged;

    /// <summary>World-space muzzle transform; read by the AI for angle checks.</summary>
    public Transform FirePoint => firePoint;

    /// <summary>
    /// Assigns the AI homing target forwarded to every subsequently spawned
    /// projectile.  Pass <c>null</c> to clear (e.g. when the target is destroyed).
    /// </summary>
    public void SetAITarget(Transform target)
    {
        _aiTarget = target;
    }

    // -------------------------------------------------------------------------
    // Unity messages
    // -------------------------------------------------------------------------

    private void Awake()
    {
        // Cache the root transform once.  Using transform.root means this works
        // correctly whether VehicleWeapon sits on the root or on a child turret.
        _ownerRoot = transform.root;

        CreatePool();
        _currentAmmo = maxAmmo;
    }

    private void Start()
    {
        // Broadcast initial ammo so the HUD displays "20 / 20" immediately.
        OnAmmoChanged?.Invoke(_currentAmmo, maxAmmo);
    }

    private void OnDestroy()
    {
        _projectilePool?.Clear();
    }

    // -------------------------------------------------------------------------
    // Input System callbacks (player only)
    // -------------------------------------------------------------------------

    /// <summary>Wired to the Fire action via the Player Input component.</summary>
    public void RespondToFireInput(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            TryFire();
        }
    }

    /// <summary>Wired to the Reload action via the Player Input component.</summary>
    public void RespondToReloadInput(InputAction.CallbackContext context)
    {
        if (context.performed && !_isReloading && _currentAmmo < maxAmmo)
        {
            StartCoroutine(ReloadRoutine());
        }
    }

    // -------------------------------------------------------------------------
    // Shared fire logic (player & AI)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts to fire one round, respecting fire rate, ammo count, and reload
    /// state.  Safe to call every frame from the AI — redundant calls are ignored.
    /// </summary>
    public void TryFire()
    {
        if (_isReloading) return;
        if (Time.time < _lastFireTime + fireRate) return;

        if (_currentAmmo <= 0)
        {
            StartCoroutine(ReloadRoutine());
            return;
        }

        _lastFireTime = Time.time;
        _currentAmmo--;
        OnAmmoChanged?.Invoke(_currentAmmo, maxAmmo);

        FireProjectile();

        if (_currentAmmo <= 0)
        {
            StartCoroutine(ReloadRoutine());
        }
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    private IEnumerator ReloadRoutine()
    {
        _isReloading = true;
        OnAmmoChanged?.Invoke(-1, maxAmmo);   // -1 == "Reloading…" signal for HUD

        yield return new WaitForSeconds(reloadTime);

        _currentAmmo = maxAmmo;
        _isReloading = false;
        OnAmmoChanged?.Invoke(_currentAmmo, maxAmmo);
    }

    private void FireProjectile()
    {
        // DIAGNOSTIC: Confirm the fire event reaches the pool.
        Debug.Log($"🔫 [WEAPON] Firing projectile from: {firePoint.name}");
        _projectilePool.Get();
    }

    // -------------------------------------------------------------------------
    // Object pool callbacks
    // -------------------------------------------------------------------------

    private void CreatePool()
    {
        _projectilePool = new ObjectPool<Projectile>(
            createFunc:      CreateProjectile,
            actionOnGet:     OnGetProjectile,
            actionOnRelease: OnReleaseProjectile,
            actionOnDestroy: OnDestroyProjectile,
            collectionCheck: true,
            defaultCapacity: poolDefaultCapacity,
            maxSize:         poolMaxSize
        );
    }

    private Projectile CreateProjectile()
    {
        Projectile instance = Instantiate(projectilePrefab, firePoint.position, firePoint.rotation);
        instance.SetPool(_projectilePool);
        instance.gameObject.SetActive(false);
        return instance;
    }

    private void OnGetProjectile(Projectile projectile)
    {
        // 1. Position and orient at the muzzle before the projectile activates.
        projectile.transform.SetPositionAndRotation(firePoint.position, firePoint.rotation);

        // 2. Tell the projectile which root to treat as its owner so that it
        //    never damages the vehicle that fired it, regardless of layer setup.
        projectile.SetOwner(_ownerRoot);

        // 3. Forward the AI homing target (null for player weapons — projectile
        //    will fly ballistically).
        projectile.SetTarget(_aiTarget);

        // DIAGNOSTIC: Confirm whether a homing target was passed to this round.
        if (_aiTarget != null)
        {
            Debug.Log($"🎯 [WEAPON] Target assigned to projectile: {_aiTarget.name}");
        }

        // 4. Reset per-activation state and enable the GameObject.
        projectile.OnGetFromPool();
    }

    private void OnReleaseProjectile(Projectile projectile)
    {
        projectile.OnReturnToPool();
    }

    private void OnDestroyProjectile(Projectile projectile)
    {
        if (projectile != null) Destroy(projectile.gameObject);
    }
}