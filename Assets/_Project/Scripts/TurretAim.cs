using UnityEngine;
using UnityEngine.InputSystem;

public class TurretAim : MonoBehaviour
{
    [Tooltip("La cámara principal del juego.")]
    [SerializeField] private Camera mainCamera;

    [Tooltip("A qué altura del mundo apunta el raycast (para que no apunte al fondo de la luna).")]
    [SerializeField] private LayerMask groundMask;

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

        // Si el rayo golpea el piso (o una caja invisible a la altura del arma)
        if (Physics.Raycast(ray, out RaycastHit hitInfo, 1000f, groundMask))
        {
            // Apuntamos la torreta hacia ese punto de impacto.
            // Opcional: Anular el eje Y para que la torreta no apunte al piso
            Vector3 targetPoint = hitInfo.point;
            targetPoint.y = transform.position.y;

            transform.LookAt(targetPoint);
        }
    }
}