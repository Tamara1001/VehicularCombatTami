using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Conecta los componentes de Vida y Nitro del vehículo con las barras visuales en la UI.
/// Purely reactive — subscribes to events and routes normalized values to bars.
/// </summary>
public class VehicleHUD : MonoBehaviour
{
    [Header("Data Sources")]
    [Tooltip("El componente HealthComponent en el vehículo.")]
    [SerializeField] private HealthComponent vehicleHealth;

    [Tooltip("El componente VehicleResourceComponent en el vehículo.")]
    [SerializeField] private VehicleResourceComponent vehicleResources;

    [Header("Bar Fill Images (Image Type = Filled)")]
    [Tooltip("Imagen de la barra de Vida (Image Type: Filled).")]
    [SerializeField] private Image healthBarFill;

    [Tooltip("Imagen de la barra de Nitro/Boost (Image Type: Filled).")]
    [SerializeField] private Image boostBarFill;

    private void OnEnable()
    {
        // Suscribir eventos de salud
        if (vehicleHealth != null)
        {
            vehicleHealth.OnHealthChanged += UpdateHealthBar;
            UpdateHealthBar(vehicleHealth.GetNormalizedHealth()); // Sincronización inicial
        }
        else
        {
            Debug.LogWarning("[VehicleHUD] vehicleHealth no está asignado.", this);
        }

        // Suscribir eventos de recursos (Nitro)
        if (vehicleResources != null)
        {
            vehicleResources.OnBoostChanged += UpdateBoostBar;
            UpdateBoostBar(vehicleResources.GetNormalizedBoost()); // Sincronización inicial
        }
        else
        {
            Debug.LogWarning("[VehicleHUD] vehicleResources no está asignado.", this);
        }
    }

    private void OnDisable()
    {
        if (vehicleHealth != null)
            vehicleHealth.OnHealthChanged -= UpdateHealthBar;

        if (vehicleResources != null)
            vehicleResources.OnBoostChanged -= UpdateBoostBar;
    }

    private void UpdateHealthBar(float normalized)
    {
        if (healthBarFill != null)
            healthBarFill.fillAmount = normalized;
    }

    private void UpdateBoostBar(float normalized)
    {
        if (boostBarFill != null)
            boostBarFill.fillAmount = normalized;
    }
}