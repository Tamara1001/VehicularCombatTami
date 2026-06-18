using UnityEngine;

/// <summary>
/// Hace que la torreta del enemigo mire siempre al jugador, 
/// permitiendo que el chasis dispare mientras maneja de costado.
/// </summary>
public class EnemyTurretAim : MonoBehaviour
{
    [SerializeField] private float rotationSpeed = 120f;
    private Transform _target;

    private void Start()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) _target = player.transform;
    }

    private void Update()
    {
        if (_target == null) return;

        // Calculamos la dirección hacia el jugador
        Vector3 direction = _target.position - transform.position;

        if (direction.sqrMagnitude > 0.01f)
        {
            // Usamos transform.parent.up para que la torreta no se rompa si el auto está en una rampa lunar
            Quaternion targetRotation = Quaternion.LookRotation(direction, transform.parent.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }
}