using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.InputSystem;

public sealed class VehicleWeapon : MonoBehaviour
{
    [Header("Firing")]
    [SerializeField] private Transform firePoint;
    [SerializeField] private Projectile projectilePrefab;
    [SerializeField] private float fireRate = 0.25f;

    [Header("Ammo & Reloading")]
    [SerializeField] private int maxAmmo = 20;
    [SerializeField] private float reloadTime = 1.5f;

    [Header("Object Pool Settings")]
    [SerializeField] private int poolDefaultCapacity = 20;
    [SerializeField] private int poolMaxSize = 40;

    private IObjectPool<Projectile> _projectilePool;
    private float _lastFireTime = float.NegativeInfinity;

    private int _currentAmmo;
    private bool _isReloading;

    /// <summary>
    /// Evento para actualizar el texto del HUD. 
    /// Pasa la munición actual y la máxima. Si envía -1, significa que está recargando.
    /// </summary>
    public event Action<int, int> OnAmmoChanged;

    // Propiedad pública para que la IA sepa desde dónde sale el disparo
    public Transform FirePoint => firePoint;

    // Target assigned by the AI for homing projectiles.
    // Null when fired by the player (homing is handled per-projectile).
    private Transform _currentTarget;

    /// <summary>
    /// Sets the homing target used by the next AI-fired projectile.
    /// Call this before TryFire(). Passing null clears the target so the
    /// projectile flies straight (player-fired shots are always straight).
    /// </summary>
    public void SetAITarget(Transform target)
    {
        _currentTarget = target;
    }

    private void Awake()
    {
        CreatePool();
        _currentAmmo = maxAmmo;
    }

    private void Start()
    {
        // Disparamos el evento al iniciar para que el HUD muestre "20 / 20"
        OnAmmoChanged?.Invoke(_currentAmmo, maxAmmo);
    }

    private void OnDestroy()
    {
        _projectilePool?.Clear();
    }

    // --- INPUTS (Solo para el Jugador) ---
    public void RespondToFireInput(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            TryFire();
        }
    }

    public void RespondToReloadInput(InputAction.CallbackContext context)
    {
        // Recarga manual (por ejemplo, con la tecla R)
        if (context.performed && !_isReloading && _currentAmmo < maxAmmo)
        {
            StartCoroutine(ReloadRoutine());
        }
    }

    // --- LÓGICA DE DISPARO (Compartida por Jugador e IA) ---
    public void TryFire()
    {
        if (_isReloading) return;
        if (Time.time < _lastFireTime + fireRate) return;

        if (_currentAmmo <= 0)
        {
            // Auto-recarga si se quedó sin balas e intenta disparar
            StartCoroutine(ReloadRoutine());
            return;
        }

        _lastFireTime = Time.time;
        _currentAmmo--;

        OnAmmoChanged?.Invoke(_currentAmmo, maxAmmo);
        FireProjectile();

        // Opcional: Auto-recarga inmediata si la bala que acaba de salir era la última
        if (_currentAmmo <= 0)
        {
            StartCoroutine(ReloadRoutine());
        }
    }

    private IEnumerator ReloadRoutine()
    {
        _isReloading = true;

        // Enviamos -1 para indicarle a la UI que muestre "Recargando..."
        OnAmmoChanged?.Invoke(-1, maxAmmo);

        yield return new WaitForSeconds(reloadTime);

        _currentAmmo = maxAmmo;
        _isReloading = false;

        // Actualizamos la UI con el cargador lleno
        OnAmmoChanged?.Invoke(_currentAmmo, maxAmmo);
    }

    private void FireProjectile()
    {
        // Get() calls OnGetProjectile which positions and activates the projectile.
        // We then pass the current AI target so homing projectiles know who to chase.
        Projectile projectile = _projectilePool.Get();
        projectile.SetTarget(_currentTarget);
    }

    // --- MÉTODOS DEL POOL ---
    private void CreatePool()
    {
        _projectilePool = new ObjectPool<Projectile>(
            createFunc: CreateProjectile,
            actionOnGet: OnGetProjectile,
            actionOnRelease: OnReleaseProjectile,
            actionOnDestroy: OnDestroyProjectile,
            collectionCheck: true,
            defaultCapacity: poolDefaultCapacity,
            maxSize: poolMaxSize
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
        projectile.transform.SetPositionAndRotation(firePoint.position, firePoint.rotation);
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