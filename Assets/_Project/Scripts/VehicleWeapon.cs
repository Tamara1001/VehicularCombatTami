using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.InputSystem; // Añadido para el Input

/// <summary>
/// Adaptación del sistema Object Pool para el vehículo lunar.
/// Gestiona la recarga y la instancia de proyectiles hacia adelante.
/// </summary>
public sealed class VehicleWeapon : MonoBehaviour
{
    [Header("Firing")]
    [SerializeField] private Transform firePoint;
    [SerializeField] private Projectile projectilePrefab;
    [SerializeField] private float fireRate = 0.25f;

    [Header("Object Pool Settings")]
    [SerializeField] private int poolDefaultCapacity = 10;
    [SerializeField] private int poolMaxSize = 30;

    private IObjectPool<Projectile> _projectilePool;
    private float _lastFireTime = float.NegativeInfinity;

    private void Awake()
    {
        CreatePool();
    }

    private void OnDestroy()
    {
        _projectilePool?.Clear();
    }

    // --- NUEVO MÉTODO PARA EL INPUT SYSTEM ---
    /// <summary>
    /// Conectá este método en el PlayerInput (Events -> Vehicle -> Fire)
    /// </summary>
    public void RespondToFireInput(InputAction.CallbackContext context)
    {
        // Solo disparar cuando se presiona el botón, o mantener disparando si es un arma automática.
        // Para que se sienta bien en un shooter, podés cambiar esto si querés mantener apretado.
        if (context.performed)
        {
            TryFire();
        }
    }

    private void TryFire()
    {
        if (Time.time < _lastFireTime + fireRate) return;

        _lastFireTime = Time.time;
        FireProjectile();
    }

    private void FireProjectile()
    {
        _projectilePool.Get();
    }

    // --- MÉTODOS DEL POOL ORIGINIAL ---
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