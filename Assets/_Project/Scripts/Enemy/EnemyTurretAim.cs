using UnityEngine;

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

        Vector3 targetCenter = _target.position + Vector3.up * 0.5f;
        Vector3 direction = targetCenter - transform.position;

        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction, transform.parent.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }
}