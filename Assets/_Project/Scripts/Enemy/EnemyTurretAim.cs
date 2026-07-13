using UnityEngine;

/// <summary>
/// Rotates the enemy turret to face a predicted intercept point instead of the
/// player's current position, accounting for the player's linear velocity so
/// that shots lead a moving target.
/// The turret remains parallel to lunar ramps by using <c>transform.parent.up</c>
/// as the LookRotation up-vector.
/// </summary>
public class EnemyTurretAim : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Degrees per second the turret can rotate.")]
    private float rotationSpeed = 120f;

    [SerializeField]
    [Tooltip("Speed of the projectile fired by this turret (m/s). " +
             "Must match the weapon's ballistic speed for accurate prediction.")]
    private float projectileSpeed = 18f;

    private Transform _target;
    private Rigidbody _targetRigidbody;

    private void Start()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            _target = player.transform;
            _targetRigidbody = player.GetComponent<Rigidbody>();
        }
    }

    private void Update()
    {
        if (_target == null) return;

        // --- Predictive Aiming ---
        // Offset up by 0.5 m to target the chassis centre, not the pivot root.
        Vector3 aimOrigin = transform.position;
        Vector3 targetCenter = _target.position + Vector3.up * 0.5f;

        float distanceToTarget = Vector3.Distance(aimOrigin, targetCenter);

        // First-order intercept: estimate where the target will be when the
        // bullet arrives, based on its current linear velocity.
        float timeToReach = (projectileSpeed > 0f)
            ? distanceToTarget / projectileSpeed
            : 0f;

        Vector3 targetVelocity = (_targetRigidbody != null)
            ? _targetRigidbody.linearVelocity
            : Vector3.zero;

        Vector3 predictedPosition = targetCenter + targetVelocity * timeToReach;

        // --- Rotation ---
        Vector3 direction = predictedPosition - aimOrigin;

        if (direction.sqrMagnitude > 0.01f)
        {
            // Use transform.parent.up so the turret stays flush with the
            // vehicle body even when driving on lunar ramps.
            Quaternion targetRotation = Quaternion.LookRotation(direction, transform.parent.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime);
        }
    }
}