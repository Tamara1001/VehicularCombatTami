using UnityEngine;
using UnityEngine.InputSystem;

public class TurretAim : MonoBehaviour
{
    [Tooltip("La cámara principal del juego.")]
    [SerializeField] private Camera mainCamera;

    [Tooltip("Capas contra las que choca el raycast. ¡Asegurate de incluir a los Enemigos y al Piso!")]
    [SerializeField] private LayerMask aimMask;

    private void Update()
    {
        AimWithMouse();
    }

    private void AimWithMouse()
    {
        if (mainCamera == null) return;

        // Leemos la posición del mouse en pantalla
        Vector2 mousePos = Mouse.current.position.ReadValue();

        // Tiramos un rayo desde la cámara hacia el mundo 3D
        Ray ray = mainCamera.ScreenPointToRay(mousePos);

        // Chocamos contra la máscara de apuntado
        if (Physics.Raycast(ray, out RaycastHit hitInfo, 1000f, aimMask))
        {
            // Apuntamos la torreta EXACTAMENTE al punto de impacto 3D.
            // Ahora sí rotará hacia arriba o hacia abajo según dónde esté el mouse.
            transform.LookAt(hitInfo.point);
        }
    }
}