using UnityEngine;
using UnityEngine.UI;
using TMPro; // Requerido para manejar los textos profesionales

public class VehicleHUD : MonoBehaviour
{
    [Header("Data Sources")]
    [SerializeField] private HealthComponent vehicleHealth;
    [SerializeField] private VehicleResourceComponent vehicleResources;
    [SerializeField] private VehicleWeapon vehicleWeapon; // La nueva fuente de datos

    [Header("Bar Fill Images")]
    [SerializeField] private Image healthBarFill;
    [SerializeField] private Image boostBarFill;

    [Header("Text Elements")]
    [Tooltip("El texto en la UI que mostrará las balas (TextMeshPro).")]
    [SerializeField] private TextMeshProUGUI ammoText;

    private void OnEnable()
    {
        if (vehicleHealth != null)
        {
            vehicleHealth.OnHealthChanged += UpdateHealthBar;
            UpdateHealthBar(vehicleHealth.GetNormalizedHealth());
        }

        if (vehicleResources != null)
        {
            vehicleResources.OnBoostChanged += UpdateBoostBar;
            UpdateBoostBar(vehicleResources.GetNormalizedBoost());
        }

        if (vehicleWeapon != null)
        {
            // Nos suscribimos al nuevo evento de munición
            vehicleWeapon.OnAmmoChanged += UpdateAmmoText;
        }
    }

    private void OnDisable()
    {
        if (vehicleHealth != null) vehicleHealth.OnHealthChanged -= UpdateHealthBar;
        if (vehicleResources != null) vehicleResources.OnBoostChanged -= UpdateBoostBar;
        if (vehicleWeapon != null) vehicleWeapon.OnAmmoChanged -= UpdateAmmoText;
    }

    private void UpdateHealthBar(float normalized)
    {
        if (healthBarFill != null) healthBarFill.fillAmount = normalized;
    }

    private void UpdateBoostBar(float normalized)
    {
        if (boostBarFill != null) boostBarFill.fillAmount = normalized;
    }

    private void UpdateAmmoText(int current, int max)
    {
        if (ammoText == null) return;

        // El script del arma envía un -1 cuando está ejecutando la corrutina de recarga
        if (current < 0)
        {
            ammoText.text = "RECARGANDO...";
            ammoText.color = Color.yellow;
        }
        else
        {
            ammoText.text = $"{current} / {max}";
            // Cambiar a color rojo si quedan 5 balas o menos, sino blanco
            ammoText.color = current <= 5 ? Color.red : Color.white;
        }
    }
}