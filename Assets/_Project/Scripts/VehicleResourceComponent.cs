using System;
using UnityEngine;

/// <summary>
/// Gestiona el recurso secundario del vehículo: el Nitro (Boost) y Gravedad Terrestre.
/// </summary>
public sealed class VehicleResourceComponent : MonoBehaviour
{
    [Header("Boost Settings")]
    [Tooltip("Cantidad máxima de energía.")]
    [Min(1)][SerializeField] private int _maxBoost = 100;

    [Tooltip("Puntos de energía que se regeneran por segundo.")]
    [Min(0f)][SerializeField] private float _boostRegenPerSecond = 10f;

    [Tooltip("Tiempo a esperar antes de regenerar tras gastar energía.")]
    [Min(0f)][SerializeField] private float _regenDelay = 0.5f;

    private float _currentBoost;
    private float _timeSinceLastConsumption;

    public event Action<float> OnBoostChanged;

    public int CurrentBoost => (int)_currentBoost;
    public int MaxBoost => _maxBoost;

    private void Awake()
    {
        _currentBoost = _maxBoost;
    }

    private void Start()
    {
        OnBoostChanged?.Invoke(GetNormalizedBoost());
    }

    private void Update()
    {
        _timeSinceLastConsumption += Time.deltaTime;

        // Solo regenera si pasó el tiempo de delay
        if (_timeSinceLastConsumption >= _regenDelay)
        {
            RegenerateBoost();
        }
    }

    /// <summary>
    /// Intenta consumir energía de forma continua (ideal para Update/FixedUpdate).
    /// </summary>
    public bool TryConsumeContinuous(float costPerSecond)
    {
        float costThisFrame = costPerSecond * Time.deltaTime;

        if (_currentBoost < costThisFrame) return false; // No hay suficiente energía

        _currentBoost -= costThisFrame;
        _timeSinceLastConsumption = 0f; // Reiniciamos el temporizador de regeneración
        OnBoostChanged?.Invoke(GetNormalizedBoost());
        return true;
    }

    public float GetNormalizedBoost()
    {
        if (_maxBoost <= 0) return 0f;
        return Mathf.Clamp01(_currentBoost / _maxBoost);
    }

    private void RegenerateBoost()
    {
        if (_currentBoost >= _maxBoost) return;

        float previous = _currentBoost;
        _currentBoost = Mathf.Clamp(_currentBoost + _boostRegenPerSecond * Time.deltaTime, 0f, _maxBoost);

        if (!Mathf.Approximately(_currentBoost, previous))
        {
            OnBoostChanged?.Invoke(GetNormalizedBoost());
        }
    }
}