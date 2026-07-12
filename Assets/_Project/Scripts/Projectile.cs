using System;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Ballistic projectile managed by an ObjectPool.
/// Supports an optional homing phase that steers toward a target for a
/// configurable duration before continuing in a straight line.
/// Applies damage to any IDamageable on trigger contact.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class Projectile : MonoBehaviour
{
    [Header("Motion")]
    [Tooltip("Forward travel speed of the projectile.")]
    [SerializeField] private float projectileSpeed = 18f;

    [Tooltip("Maximum lifetime before returning to the Pool.")]
    [SerializeField] private float lifetime = 4f;

    [Header("Homing")]
    [Tooltip("When true the projectile steers toward its assigned target for homingDuration seconds.")]
    [SerializeField] private bool isHoming = false;

    [Tooltip("Seconds the projectile actively homes before flying straight.")]
    [SerializeField] private float homingDuration = 3f;

    [Tooltip("Degrees per second the projectile can turn while homing.")]
    [SerializeField] private float homingTurnSpeed = 90f;

    [Header("Combat Settings")]
    [Tooltip("Daño que aplica el proyectil al impactar.")]
    [SerializeField] private int damage = 10;

    [Header("Layer Filtering")]
    [Tooltip("Capas a ignorar (Ej: Asignar la capa 'Player' para que el auto no se dispare a sí mismo).")]
    [SerializeField] private LayerMask ignoreLayers;

    private IObjectPool<Projectile> _pool;
    private float _activeTimer;
    private Transform _transform;
    private bool _isReturned;
    private Transform _homingTarget;

    private void Awake()
    {
        _transform = transform;

        // Validación de seguridad para asegurar que sea Trigger
        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning("[Projectile] El Collider no está configurado como Trigger. Activando por código...", this);
            col.isTrigger = true;
        }
    }

    // --- PUBLIC HOMING API ---

    /// <summary>
    /// Assigns a target for the homing phase. Call this immediately after
    /// retrieving the projectile from the pool (before it moves).
    /// Passing null disables homing for this flight.
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        _homingTarget = newTarget;
    }

    private void Update()
    {
        if (isHoming && _activeTimer < homingDuration && _homingTarget != null)
        {
            SteerTowardsTarget();
        }

        MoveForward();
        CheckLifetime();
    }

    // --- MÉTODOS DEL POOL ---

    public void SetPool(IObjectPool<Projectile> pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
    }

    public void OnGetFromPool()
    {
        _isReturned = false;
        _activeTimer = 0f;
        _homingTarget = null;   // Caller sets target via SetTarget() after retrieval.
        gameObject.SetActive(true);
    }

    public void OnReturnToPool()
    {
        _homingTarget = null;   // Release reference so the target GC can collect.
        gameObject.SetActive(false);
    }

    // --- MOVEMENT ---

    /// <summary>
    /// Rotates the projectile toward the homing target's chassis centre
    /// (offset 0.5 m up) at homingTurnSpeed degrees per second.
    /// Called only during the active homing window.
    /// </summary>
    private void SteerTowardsTarget()
    {
        Vector3 targetCenter = _homingTarget.position + Vector3.up * 0.5f;
        Vector3 directionToTarget = targetCenter - _transform.position;

        if (directionToTarget.sqrMagnitude < 0.001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
        _transform.rotation = Quaternion.RotateTowards(
            _transform.rotation,
            targetRotation,
            homingTurnSpeed * Time.deltaTime);
    }

    private void MoveForward()
    {
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

    // --- SISTEMA DE DAÑO Y COLISIONES ---

    private void OnTriggerEnter(Collider other)
    {
        // Ignoramos las capas filtradas (Ej: El jugador o los límites de la arena)
        if (((1 << other.gameObject.layer) & ignoreLayers.value) != 0) return;

        // Si el objeto tocado tiene la interfaz IDamageable, le aplicamos daño
        if (other.TryGetComponent<IDamageable>(out IDamageable target))
        {
            target.TakeDamage(damage);
        }

        // Ya sea que haya golpeado a un enemigo o a una pared, el proyectil vuelve al Pool.
        ReturnToPool();
    }

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