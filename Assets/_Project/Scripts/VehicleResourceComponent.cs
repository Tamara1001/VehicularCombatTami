using System;
using UnityEngine;

/// <summary>
/// Gestiona el recurso secundario del vehículo: el Nitro (Boost).
/// El nitro se recarga automáticamente con el tiempo y puede ser consumido para acelerar.
/// </summary>
public sealed class VehicleResourceComponent : MonoBehaviour
{
    [Header("Boost Settings")]
    [Tooltip("Cantidad máxima de energía para el nitro.")]
    [Min(1)]
    [SerializeField] private int _maxBoost = 100;

    [Tooltip("Puntos de nitro que se regeneran por segundo.")]
    [Min(0f)]
    [SerializeField] private float _boostRegenPerSecond = 10f;

    private float _currentBoost;

    /// <summary>
    /// Evento que se dispara cada vez que el valor del nitro cambia.
    /// Pasa un float normalizado [0, 1] ideal para las barras de la UI.
    /// </summary>
    public event Action<float> OnBoostChanged;

    public int CurrentBoost => (int)_currentBoost;
    public int MaxBoost => _maxBoost;

    private void Awake()
    {
        _currentBoost = _maxBoost; // Empezamos con el tanque lleno
    }

    private void Start()
    {
        OnBoostChanged?.Invoke(GetNormalizedBoost());
    }

    private void Update()
    {
        RegenerateBoost();
    }

    /// <summary>
    /// Intenta consumir la cantidad especificada de nitro.
    /// Retorna true si fue exitoso (y resta el valor), false si no alcanza la energía.
    /// </summary>
    public bool TryConsumeBoost(int amount)
    {
        if (amount <= 0) return false;
        if (_currentBoost < amount) return false;

        _currentBoost -= amount;
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