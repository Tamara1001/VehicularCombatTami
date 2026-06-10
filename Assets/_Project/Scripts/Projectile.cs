using System;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Proyectil balístico gestionado por un ObjectPool.
/// Se mueve en línea recta y aplica daño a cualquier IDamageable que toque.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class Projectile : MonoBehaviour
{
    [Header("Motion")]
    [Tooltip("Velocidad de viaje del proyectil hacia adelante.")]
    [SerializeField] private float projectileSpeed = 18f;

    [Tooltip("Tiempo máximo de vida antes de volver al Pool.")]
    [SerializeField] private float lifetime = 4f;

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

    private void Update()
    {
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
        gameObject.SetActive(true);
    }

    public void OnReturnToPool()
    {
        gameObject.SetActive(false);
    }

    // --- LÓGICA DE MOVIMIENTO ---

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