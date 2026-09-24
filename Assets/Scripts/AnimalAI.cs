using UnityEngine;
using ithappy.Animals_FREE;

[RequireComponent(typeof(CreatureMover))]
public class AnimalAI : MonoBehaviour
{
    public enum State
    {
        Idle,
        Wander,
        Flee
    }

    [Header("State")]
    public State currentState = State.Idle;

    [Header("Speeds (Inspector Config)")]
    [Tooltip("Speeds are primarily driven by CreatureMover settings, but isRun controls walk vs run state.")]
    public float walkSpeed = 1f;
    public float fleeSpeed = 4f;

    [Header("Detection")]
    [Tooltip("Distance at which the animal notices the player and begins fleeing. Kept close so the player can approach.")]
    public float detectionRadius = 5f;
    [Tooltip("Distance the player must be away before the animal feels safe to stop fleeing.")]
    public float fleeEscapeRadius = 10f;

    [Header("Idle")]
    public float minIdleTime = 2f;
    public float maxIdleTime = 6f;

    [Header("Wander")]
    public float minWanderDistance = 1f;
    public float maxWanderDistance = 2.5f;
    public float wanderRadius = 4f;
    public float stopDistance = 0.5f;

    [Header("Despawning")]
    public float despawnDelay = 10f;

    private CreatureMover creatureMover;
    private Transform playerTransform;
    private Camera mainCamera;
    
    private float stateTimer;
    private Vector3 currentDestination;
    private Vector3 initialSpawnPosition;
    
    private float despawnTimer = 0f;
    private AnimalSpawner spawner;

    public void Initialize(AnimalSpawner spawner, Transform player)
    {
        this.spawner = spawner;
        this.playerTransform = player;
        this.initialSpawnPosition = transform.position;
    }

    private void Awake()
    {
        creatureMover = GetComponent<CreatureMover>();
        mainCamera = Camera.main;
    }

    private void Start()
    {
        EnterIdle();
    }

    private void Update()
    {
        HandleDespawnLogic();

        switch (currentState)
        {
            case State.Idle:
                UpdateIdle();
                break;
            case State.Wander:
                UpdateWander();
                break;
            case State.Flee:
                UpdateFlee();
                break;
        }
    }

    private void HandleDespawnLogic()
    {
        if (mainCamera == null)
            return;

        Vector3 viewportPoint = mainCamera.WorldToViewportPoint(transform.position);
        // Add a small margin so it despawns when comfortably off screen
        bool inFrustum = viewportPoint.z > 0 && viewportPoint.x > -0.2f && viewportPoint.x < 1.2f && viewportPoint.y > -0.2f && viewportPoint.y < 1.2f;
        
        if (!inFrustum)
        {
            despawnTimer += Time.deltaTime;
            if (despawnTimer >= despawnDelay)
            {
                if (spawner != null)
                {
                    spawner.DespawnAnimal(this);
                }
                else
                {
                    Destroy(gameObject);
                }
            }
        }
        else
        {
            despawnTimer = 0f;
        }
    }

    private void EnterIdle()
    {
        currentState = State.Idle;
        // Stop moving
        creatureMover.SetInput(Vector2.zero, transform.position + transform.forward, false, false);
        stateTimer = Random.Range(minIdleTime, maxIdleTime);
    }

    private void UpdateIdle()
    {
        CheckFleeCondition();
        if (currentState == State.Flee) return;

        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            EnterWander();
        }
    }

    private void EnterWander()
    {
        currentState = State.Wander;
        FindNewWanderDestination();
    }

    private void FindNewWanderDestination()
    {
        const int maxAttempts = 10;
        for (int i = 0; i < maxAttempts; i++)
        {
            float dist = Random.Range(minWanderDistance, maxWanderDistance);
            Vector2 randomDir = Random.insideUnitCircle.normalized * dist;
            Vector3 candidatePos = transform.position + new Vector3(randomDir.x, 0, randomDir.y);
            
            // Keep constrained around the initial spawn position
            if (Vector3.Distance(candidatePos, initialSpawnPosition) > wanderRadius)
            {
                Vector3 dirToCenter = (initialSpawnPosition - transform.position).normalized;
                candidatePos = transform.position + dirToCenter * dist;
            }

            // Downward raycast to verify there is a floor underneath this point
            if (Physics.Raycast(candidatePos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 6f))
            {
                if (FloorManager.IsGroundOrFloor(hit.collider))
                {
                    currentDestination = hit.point;
                    return;
                }
            }
        }

        // If no safe floor spot found, remain at current position
        currentDestination = transform.position;
        EnterIdle();
    }

    private void UpdateWander()
    {
        CheckFleeCondition();
        if (currentState == State.Flee) return;

        float distance = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), new Vector3(currentDestination.x, 0, currentDestination.z));
        
        if (distance <= stopDistance)
        {
            EnterIdle();
        }
        else
        {
            // Move forwards toward target
            creatureMover.SetInput(new Vector2(0, 1), currentDestination, false, false);
        }
    }

    private void CheckFleeCondition()
    {
        if (playerTransform == null) return;

        float dist = Vector3.Distance(transform.position, playerTransform.position);
        if (dist < detectionRadius)
        {
            EnterFlee();
        }
    }

    private void EnterFlee()
    {
        currentState = State.Flee;
    }

    private void UpdateFlee()
    {
        if (playerTransform == null)
        {
            EnterIdle();
            return;
        }

        float dist = Vector3.Distance(transform.position, playerTransform.position);
        if (dist > fleeEscapeRadius)
        {
            EnterIdle();
            return;
        }

        Vector3 dirAway = (transform.position - playerTransform.position).normalized;
        dirAway.y = 0f;

        // Try direct flee first, then flank angles so the animal doesn't run off the floor edge
        Vector3 bestDestination = transform.position;
        bool foundSafeFloor = false;

        float[] angles = new float[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f };
        for (int i = 0; i < angles.Length; i++)
        {
            Vector3 testDir = Quaternion.Euler(0f, angles[i], 0f) * dirAway;
            Vector3 testPos = transform.position + testDir * 3f;

            if (Physics.Raycast(testPos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 6f))
            {
                if (FloorManager.IsGroundOrFloor(hit.collider))
                {
                    bestDestination = hit.point;
                    foundSafeFloor = true;
                    break;
                }
            }
        }

        if (foundSafeFloor)
        {
            currentDestination = bestDestination;
            creatureMover.SetInput(new Vector2(0, 1), currentDestination, true, false);
        }
        else
        {
            // At the boundary of the floor with nowhere safe to run: stop at edge
            creatureMover.SetInput(Vector2.zero, transform.position + dirAway, false, false);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, fleeEscapeRadius);

        Gizmos.color = Color.green;
        if (Application.isPlaying)
        {
            Gizmos.DrawWireSphere(initialSpawnPosition, wanderRadius);
            Gizmos.DrawSphere(currentDestination, 0.5f);
        }
    }
}
