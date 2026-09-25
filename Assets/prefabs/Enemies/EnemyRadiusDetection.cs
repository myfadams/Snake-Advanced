using UnityEngine;

/// <summary>
/// Attached to the child "collider" GameObject under the Enemy Head.
/// Holds the trigger SphereCollider that represents the player detection radius.
/// When the player enters or exits this radius, it notifies EnemiesLogic to update pursuit.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class EnemyRadiusDetection : MonoBehaviour
{
    [Tooltip("Reference to the EnemiesLogic component on the enemy root. If null, automatically resolves in parent.")]
    [SerializeField] private EnemiesLogic enemyLogic;

    private SphereCollider sphereCollider;

    public SphereCollider SphereCollider
    {
        get
        {
            if (sphereCollider == null)
            {
                sphereCollider = GetComponent<SphereCollider>();
            }
            return sphereCollider;
        }
    }

    private void Awake()
    {
        InitializeCollider();
        ResolveEnemyLogic();
    }

    private void Start()
    {
        InitializeCollider();
        ResolveEnemyLogic();
    }

    public void InitializeCollider()
    {
        if (sphereCollider == null)
        {
            sphereCollider = GetComponent<SphereCollider>();
            if (sphereCollider == null)
            {
                sphereCollider = gameObject.AddComponent<SphereCollider>();
            }
        }

        sphereCollider.isTrigger = true;
        sphereCollider.center = Vector3.zero;

        if (enemyLogic != null)
        {
            SetRadius(enemyLogic.DetectionRadius);
        }
    }

    public void ResolveEnemyLogic()
    {
        if (enemyLogic == null)
        {
            enemyLogic = GetComponentInParent<EnemiesLogic>();
        }

        if (enemyLogic != null && sphereCollider != null)
        {
            SetRadius(enemyLogic.DetectionRadius);
        }
    }

    public void SetEnemyLogic(EnemiesLogic logic)
    {
        enemyLogic = logic;
        if (enemyLogic != null && sphereCollider != null)
        {
            SetRadius(enemyLogic.DetectionRadius);
        }
    }

    /// <summary>
    /// Adjusts the SphereCollider's local radius so its world-space radius
    /// exactly equals the specified world radius, taking hierarchy lossy scale into account.
    /// </summary>
    public void SetRadius(float worldRadius)
    {
        if (sphereCollider == null)
        {
            sphereCollider = GetComponent<SphereCollider>();
        }
        if (sphereCollider == null) return;

        float scale = transform.lossyScale.x;
        if (scale < 0.001f) scale = 1f;

        sphereCollider.radius = worldRadius / scale;
    }

    private void Update()
    {
        // Continuously ensure radius stays synchronized with enemyLogic.DetectionRadius
        if (enemyLogic != null && sphereCollider != null)
        {
            SetRadius(enemyLogic.DetectionRadius);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;

        // Player entered detection radius
        if (other.CompareTag("SnakeHead") || other.CompareTag("SnakeBody") || other.GetComponentInParent<SnakeGrow>() != null)
        {
            if (enemyLogic == null)
            {
                ResolveEnemyLogic();
            }

            if (enemyLogic != null)
            {
                enemyLogic.OnPlayerEnteredRadius();
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other == null) return;

        // Player exited detection radius
        if (other.CompareTag("SnakeHead") || other.CompareTag("SnakeBody") || other.GetComponentInParent<SnakeGrow>() != null)
        {
            if (enemyLogic == null)
            {
                ResolveEnemyLogic();
            }

            if (enemyLogic != null)
            {
                enemyLogic.OnPlayerExitedRadius();
            }
        }
    }
}
