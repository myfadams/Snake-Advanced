using UnityEngine;

/// <summary>
/// Attach to any dangerous obstacle, hazard block, or damaging entity.
/// When any body segment of the snake comes into contact with this hazard,
/// it triggers the snake's body-damage mechanic (destroying that segment and
/// reducing the Head's value accordingly).
/// The Head is immune to normal body damage.
/// </summary>
public class Hazard : MonoBehaviour
{
    [Header("Hazard Settings")]
    [Tooltip("If true, this hazard GameObject is destroyed or deactivated after dealing damage to a body segment.")]
    [SerializeField] private bool destroyOnHit = false;

    [Tooltip("Cooldown in seconds before this hazard can damage another segment.")]
    [SerializeField] private float damageCooldown = 0.5f;

    private float lastDamageTime = -999f;

    private void OnTriggerEnter(Collider other)
    {
        TryDamageSegment(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryDamageSegment(collision.gameObject);
    }

    private void TryDamageSegment(GameObject targetObject)
    {
        if (Time.time < lastDamageTime + damageCooldown)
            return;

        // Find the SnakeGrow manager on the target or its parents
        SnakeGrow snake = targetObject.GetComponentInParent<SnakeGrow>();
        if (snake == null)
            return;

        // Identify the specific segment transform that collided
        Transform hitTransform = targetObject.transform;

        // If targetObject is a child of the segment (e.g. TextMeshPro child), find the root segment
        body bodyComponent = targetObject.GetComponentInParent<body>();
        if (bodyComponent != null)
        {
            hitTransform = bodyComponent.transform;
        }

        // Attempt to deal body damage
        bool damaged = snake.TakeBodyDamage(hitTransform);
        if (damaged)
        {
            lastDamageTime = Time.time;

            if (destroyOnHit)
            {
                Destroy(gameObject);
            }
        }
    }
}
