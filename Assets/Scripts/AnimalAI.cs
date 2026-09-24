using UnityEngine;
using ithappy.Animals_FREE;

[RequireComponent(typeof(CreatureMover))]
[RequireComponent(typeof(CharacterController))]
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

    [Header("Speeds & Steering")]
    [Tooltip("Speed when wandering peacefully.")]
    public float walkSpeed = 1.2f;

    [Tooltip("Speed when fleeing from the player snake.")]
    public float fleeSpeed = 3.5f;

    [Tooltip("Rotation turning speed in degrees per second for responsive obstacle steering.")]
    public float rotateSpeed = 270f;

    [Header("Obstacle Avoidance Feeler Settings")]
    [Tooltip("Distance ahead to scan for obstacles while wandering.")]
    public float lookAheadDistance = 1.8f;

    [Tooltip("Distance ahead to scan for obstacles while fleeing.")]
    public float fleeLookAheadDistance = 2.8f;

    [Tooltip("Extra clearance buffer added to the animal's physical collision radius.")]
    public float obstacleClearanceBuffer = 0.15f;

    [Tooltip("Seconds with minimal movement before considering the animal stuck and triggering recovery.")]
    public float stuckCheckDuration = 0.4f;

    [Header("Detection & Fleeing")]
    [Tooltip("Distance at which the animal notices the player and begins fleeing.")]
    public float detectionRadius = 5f;

    [Tooltip("Distance the player must be away before the animal feels safe to stop fleeing.")]
    public float fleeEscapeRadius = 10f;

    [Header("Idle")]
    public float minIdleTime = 2f;
    public float maxIdleTime = 5f;

    [Header("Wander")]
    public float minWanderDistance = 1.2f;
    public float maxWanderDistance = 2.8f;
    public float wanderRadius = 4f;
    public float stopDistance = 0.4f;

    [Header("Despawning")]
    public float despawnDelay = 10f;

    private CreatureMover creatureMover;
    private CharacterController characterController;
    private Transform playerTransform;
    private Camera mainCamera;

    private float stateTimer;
    private Vector3 currentDestination;
    private Vector3 initialSpawnPosition;

    private float despawnTimer = 0f;
    private AnimalSpawner spawner;

    // Avoidance & stuck state
    private float bodyRadius = 0.35f;
    private float sensorHeight = 0.35f;
    private Vector3 lastPosition;
    private float stuckTimer = 0f;

    private bool hasObstacleContact = false;
    private float lastObstacleHitTime = -999f;
    private Vector3 lastObstacleNormal = Vector3.up;

    public void Initialize(AnimalSpawner spawner, Transform player)
    {
        this.spawner = spawner;
        this.playerTransform = player;
        this.initialSpawnPosition = transform.position;
    }

    private void Awake()
    {
        creatureMover = GetComponent<CreatureMover>();
        characterController = GetComponent<CharacterController>();
        mainCamera = Camera.main;

        // Ensure human player input scripts from ithappy demo are removed from AI creatures
        MovePlayerInput playerInput = GetComponent<MovePlayerInput>();
        if (playerInput != null)
        {
            playerInput.enabled = false;
            Destroy(playerInput);
        }

        // Measure actual world-space collision radius and center height
        if (characterController != null)
        {
            bodyRadius = characterController.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
            sensorHeight = Mathf.Max(0.2f, characterController.center.y * transform.lossyScale.y);
        }
        else
        {
            bodyRadius = 0.35f;
            sensorHeight = 0.35f;
        }

        // Configure creature mover with robust AI settings
        if (creatureMover != null)
        {
            creatureMover.SpaceMode = Space.Self;
            creatureMover.WalkSpeed = walkSpeed;
            creatureMover.RunSpeed = fleeSpeed;
            creatureMover.RotateSpeed = rotateSpeed;
        }
    }

    private void Start()
    {
        lastPosition = transform.position;
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
        {
            mainCamera = Camera.main;
            if (mainCamera == null) return;
        }

        // Never despawn animals that are close to the player or on active nearby floor tiles
        float distToPlayer = playerTransform != null 
            ? Vector3.Distance(transform.position, playerTransform.position) 
            : 0f;

        float keepAliveDistance = (spawner != null && spawner.FloorManager != null) 
            ? spawner.FloorManager.SpawnRadius 
            : 15f;

        if (distToPlayer <= keepAliveDistance)
        {
            despawnTimer = 0f;
            return;
        }

        Vector3 viewportPoint = mainCamera.WorldToViewportPoint(transform.position);
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

    #region State Machine

    private void EnterIdle()
    {
        currentState = State.Idle;
        stuckTimer = 0f;
        hasObstacleContact = false;

        if (creatureMover != null)
        {
            creatureMover.Stop();
            creatureMover.ForceIdleAnimation();
        }

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
        stuckTimer = 0f;
        hasObstacleContact = false;
        FindNewWanderDestination();
    }

    private void UpdateWander()
    {
        CheckFleeCondition();
        if (currentState == State.Flee) return;

        float distance = Vector3.Distance(
            new Vector3(transform.position.x, 0, transform.position.z),
            new Vector3(currentDestination.x, 0, currentDestination.z)
        );

        if (distance <= stopDistance)
        {
            EnterIdle();
            return;
        }

        Vector3 desiredDir = currentDestination - transform.position;
        desiredDir.y = 0f;

        if (desiredDir.sqrMagnitude < 0.001f)
        {
            EnterIdle();
            return;
        }
        desiredDir.Normalize();

        // Real-time dynamic obstacle avoidance feeler check
        GetAvoidanceSteering(desiredDir, lookAheadDistance, false, out Vector3 moveDir);

        if (creatureMover != null)
        {
            creatureMover.MoveInDirection(moveDir, false);
        }

        // Stuck detection
        CheckStuckCondition(State.Wander);
    }

    private void CheckFleeCondition()
    {
        if (playerTransform == null) return;

        Vector3 threatPos = GetClosestThreatPoint();
        float dist = Vector3.Distance(transform.position, threatPos);
        if (dist < detectionRadius)
        {
            EnterFlee();
        }
    }

    private void EnterFlee()
    {
        currentState = State.Flee;
        stuckTimer = 0f;
    }

    private void UpdateFlee()
    {
        if (playerTransform == null)
        {
            EnterIdle();
            return;
        }

        Vector3 threatPos = GetClosestThreatPoint();
        float distToThreat = Vector3.Distance(transform.position, threatPos);

        if (distToThreat > fleeEscapeRadius)
        {
            EnterIdle();
            return;
        }

        Vector3 dirAway = transform.position - threatPos;
        dirAway.y = 0f;
        if (dirAway.sqrMagnitude < 0.001f)
        {
            dirAway = transform.forward;
        }
        dirAway.Normalize();

        // Evaluate candidate flee directions using smart obstacle clearance & threat distance scoring
        Vector3 bestFleeDir = FindBestFleeDirection(dirAway);

        if (bestFleeDir == Vector3.zero)
        {
            // At the boundary or surrounded by obstacles: stop safely instead of ramming into walls
            if (creatureMover != null) creatureMover.Stop();
            return;
        }

        // Apply real-time dynamic obstacle steering feelers
        GetAvoidanceSteering(bestFleeDir, fleeLookAheadDistance, true, out Vector3 finalMoveDir);

        if (creatureMover != null)
        {
            creatureMover.MoveInDirection(finalMoveDir, true);
        }

        // Stuck detection
        CheckStuckCondition(State.Flee);
    }

    #endregion

    #region Obstacle Avoidance & Path Checking

    /// <summary>
    /// Determines whether a collider represents a physical obstacle to this animal.
    /// Ground/floor and the animal itself are excluded.
    /// Props, hazards, walls, snake head/body, and other animals are recognized as obstacles.
    /// </summary>
    public bool IsObstacle(Collider col)
    {
        if (col == null) return false;
        if (col == characterController || col.transform.root == transform.root) return false;
        if (FloorManager.IsGroundOrFloor(col)) return false;

        // Non-hazard triggers like pickups can be stepped past, but hazards, snake segments,
        // and other animals with trigger colliders must be strictly avoided.
        if (col.isTrigger)
        {
            if (col.GetComponent<Hazard>() != null || col.GetComponentInParent<Hazard>() != null) return true;
            if (col.CompareTag("Player") || col.GetComponentInParent<SnakeGrow>() != null) return true;
            if (col.GetComponent<AnimalAI>() != null || col.GetComponentInParent<AnimalAI>() != null) return true;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if solid walkable ground exists directly under the specified world position.
    /// </summary>
    public bool IsFloorSolid(Vector3 worldPos)
    {
        Vector3 rayOrigin = new Vector3(worldPos.x, worldPos.y + 1.5f, worldPos.z);
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 4f))
        {
            return FloorManager.IsGroundOrFloor(hit.collider);
        }
        return false;
    }

    /// <summary>
    /// Checks whether a full sphere corridor from start to end is completely clear of obstacles.
    /// </summary>
    public bool IsPathClear(Vector3 start, Vector3 end, float radius, out RaycastHit blockingHit)
    {
        blockingHit = default;
        Vector3 diff = end - start;
        float dist = diff.magnitude;
        if (dist < 0.01f) return true;
        Vector3 dir = diff / dist;

        RaycastHit[] hits = Physics.SphereCastAll(start, radius, dir, dist, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            if (IsObstacle(hits[i].collider))
            {
                blockingHit = hits[i];
                return false;
            }
        }

        // Verify floor continuity
        if (!IsFloorSolid(end)) return false;
        if (!IsFloorSolid(start + dir * (dist * 0.5f))) return false;

        return true;
    }

    /// <summary>
    /// Checks if a candidate position is obstructed by any existing scene prop or obstacle.
    /// </summary>
    private bool IsPositionBlockedByObstacle(Vector3 position, float radius)
    {
        Collider[] hits = Physics.OverlapSphere(position + Vector3.up * sensorHeight, radius, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            if (IsObstacle(hits[i]))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if a direction is blocked by any obstacle or missing floor.
    /// </summary>
    private bool IsDirectionBlocked(Vector3 dir, float distance)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return false;
        dir.Normalize();

        Vector3 sensorOrigin = transform.position + Vector3.up * sensorHeight;
        float checkRadius = bodyRadius + obstacleClearanceBuffer;

        RaycastHit[] hits = Physics.SphereCastAll(sensorOrigin, checkRadius, dir, distance, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            if (IsObstacle(hits[i].collider))
            {
                return true;
            }
        }

        if (!IsFloorSolid(transform.position + dir * distance))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Dynamic whisker feeler sensor: casts forward along desiredDir.
    /// If an obstacle or floor drop-off is ahead, tests lateral deflection angles to find
    /// a smooth, clear avoidance direction.
    /// </summary>
    private bool GetAvoidanceSteering(Vector3 desiredDir, float lookAhead, bool isFleeing, out Vector3 steeredDir)
    {
        desiredDir.y = 0f;
        if (desiredDir.sqrMagnitude < 0.001f)
        {
            steeredDir = desiredDir;
            return false;
        }
        desiredDir.Normalize();

        Vector3 sensorOrigin = transform.position + Vector3.up * sensorHeight;
        float checkRadius = bodyRadius + obstacleClearanceBuffer;

        // 1. Check direct line ahead
        bool directBlocked = false;
        RaycastHit[] hits = Physics.SphereCastAll(sensorOrigin, checkRadius, desiredDir, lookAhead, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            if (IsObstacle(hits[i].collider))
            {
                directBlocked = true;
                break;
            }
        }

        if (!directBlocked && !IsFloorSolid(transform.position + desiredDir * lookAhead))
        {
            directBlocked = true;
        }

        // If direct path is unobstructed, proceed without steering deflection
        if (!directBlocked)
        {
            steeredDir = desiredDir;
            return false;
        }

        // 2. Direct path is obstructed: evaluate alternative lateral avoidance angles
        float[] angles = new float[] { 30f, -30f, 55f, -55f, 80f, -80f, 105f, -105f, 135f, -135f, 160f, -160f };

        // If recently touched an obstacle, prioritize turning away from the obstacle normal
        if (hasObstacleContact && (Time.time - lastObstacleHitTime < 0.8f))
        {
            float side = Vector3.Dot(lastObstacleNormal, transform.right);
            if (side > 0.05f)
            {
                angles = new float[] { 30f, 55f, 80f, 105f, 135f, 160f, -30f, -55f, -80f, -105f, -135f, -160f };
            }
            else if (side < -0.05f)
            {
                angles = new float[] { -30f, -55f, -80f, -105f, -135f, -160f, 30f, 55f, 80f, 105f, 135f, 160f };
            }
        }

        Vector3 bestDir = Vector3.zero;
        float bestScore = float.MinValue;

        for (int i = 0; i < angles.Length; i++)
        {
            Vector3 testDir = Quaternion.Euler(0f, angles[i], 0f) * desiredDir;

            bool blocked = false;
            RaycastHit[] testHits = Physics.SphereCastAll(sensorOrigin, checkRadius, testDir, lookAhead, ~0, QueryTriggerInteraction.Collide);
            for (int j = 0; j < testHits.Length; j++)
            {
                if (IsObstacle(testHits[j].collider))
                {
                    blocked = true;
                    break;
                }
            }

            if (blocked) continue;

            // Ensure solid floor ahead along this test direction
            if (!IsFloorSolid(transform.position + testDir * (lookAhead * 0.5f)) ||
                !IsFloorSolid(transform.position + testDir * lookAhead))
            {
                continue;
            }

            // Score candidate angle
            float score = 100f - Mathf.Abs(angles[i]);

            if (isFleeing && playerTransform != null)
            {
                Vector3 futurePos = transform.position + testDir * lookAhead;
                float distToThreat = Vector3.Distance(futurePos, GetClosestThreatPoint());
                score += distToThreat * 8f;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestDir = testDir;

                // In peaceful wander, accept the smallest clear angle immediately
                if (!isFleeing)
                {
                    break;
                }
            }
        }

        if (bestDir != Vector3.zero)
        {
            steeredDir = bestDir;
            return true;
        }

        // If completely cornered/boxed in, turn back
        steeredDir = -transform.forward;
        return true;
    }

    /// <summary>
    /// Evaluates multiple fan angles away from the threat, verifying obstacle clearance
    /// and solid floor support to find the safest flee direction.
    /// </summary>
    private Vector3 FindBestFleeDirection(Vector3 dirAway)
    {
        float[] candidateAngles = new float[] { 0f, 25f, -25f, 50f, -50f, 75f, -75f, 100f, -100f, 125f, -125f, 150f, -150f };

        Vector3 bestDir = Vector3.zero;
        float bestScore = float.MinValue;

        Vector3 sensorOrigin = transform.position + Vector3.up * sensorHeight;
        float checkRadius = bodyRadius + obstacleClearanceBuffer;

        for (int i = 0; i < candidateAngles.Length; i++)
        {
            Vector3 testDir = Quaternion.Euler(0f, candidateAngles[i], 0f) * dirAway;

            // 1. Raycast/Spherecast along testDir to check for obstacles
            bool hasObstacle = false;
            float clearanceDist = fleeLookAheadDistance;

            RaycastHit[] hits = Physics.SphereCastAll(sensorOrigin, checkRadius, testDir, fleeLookAheadDistance, ~0, QueryTriggerInteraction.Collide);
            for (int h = 0; h < hits.Length; h++)
            {
                if (IsObstacle(hits[h].collider))
                {
                    if (hits[h].distance < 1.0f)
                    {
                        hasObstacle = true;
                        break;
                    }
                    else if (hits[h].distance < clearanceDist)
                    {
                        clearanceDist = hits[h].distance;
                    }
                }
            }

            if (hasObstacle) continue;

            // 2. Check floor support along the path and ahead
            Vector3 midPoint = transform.position + testDir * (fleeLookAheadDistance * 0.5f);
            Vector3 farPoint = transform.position + testDir * fleeLookAheadDistance;

            if (!IsFloorSolid(midPoint) || !IsFloorSolid(farPoint))
            {
                continue;
            }

            // 3. Score candidate
            Vector3 threatPos = GetClosestThreatPoint();
            float distFromThreat = Vector3.Distance(farPoint, threatPos);

            float score = (distFromThreat * 4.0f) + (clearanceDist * 2.0f) - (Mathf.Abs(candidateAngles[i]) * 0.05f);

            if (score > bestScore)
            {
                bestScore = score;
                bestDir = testDir;
            }
        }

        return bestDir;
    }

    /// <summary>
    /// Finds a safe wander destination on solid ground with complete line-of-sight clearance
    /// free from obstacles, rocks, trees, hazards, or floor edges.
    /// </summary>
    private void FindNewWanderDestination()
    {
        const int maxAttempts = 16;
        Vector3 sensorOrigin = transform.position + Vector3.up * sensorHeight;
        float checkRadius = bodyRadius + obstacleClearanceBuffer;

        for (int i = 0; i < maxAttempts; i++)
        {
            float dist = Random.Range(minWanderDistance, maxWanderDistance);
            Vector2 randomDir = Random.insideUnitCircle.normalized * dist;
            Vector3 candidatePos = transform.position + new Vector3(randomDir.x, 0, randomDir.y);

            // Constrain candidate within wander radius of initial spawn position
            if (Vector3.Distance(candidatePos, initialSpawnPosition) > wanderRadius)
            {
                Vector3 dirToCenter = (initialSpawnPosition - transform.position).normalized;
                candidatePos = transform.position + dirToCenter * dist;
            }

            // 1. Verify solid floor under candidate point
            if (Physics.Raycast(candidatePos + Vector3.up * 2f, Vector3.down, out RaycastHit groundHit, 6f))
            {
                if (!FloorManager.IsGroundOrFloor(groundHit.collider))
                {
                    continue;
                }

                Vector3 targetPoint = groundHit.point;

                // 2. Check no obstacle is sitting at destination point
                if (IsPositionBlockedByObstacle(targetPoint, checkRadius))
                {
                    continue;
                }

                // 3. Complete line-of-sight clearance check from current position to targetPoint
                Vector3 targetSensorPoint = targetPoint + Vector3.up * sensorHeight;
                if (!IsPathClear(sensorOrigin, targetSensorPoint, checkRadius, out _))
                {
                    continue;
                }

                // 4. Do not wander towards player snake if within detection range
                if (playerTransform != null)
                {
                    float distToSnake = Vector3.Distance(targetPoint, GetClosestThreatPoint());
                    if (distToSnake < detectionRadius * 0.85f)
                    {
                        continue;
                    }
                }

                // Safe and verified destination accepted
                currentDestination = targetPoint;
                stuckTimer = 0f;
                return;
            }
        }

        // If no safe spot found, remain at current position and idle
        currentDestination = transform.position;
        EnterIdle();
    }

    #endregion

    #region Stuck Detection & Collision Deflection

    private void CheckStuckCondition(State state)
    {
        float distTraveled = Vector3.Distance(
            new Vector3(transform.position.x, 0, transform.position.z),
            new Vector3(lastPosition.x, 0, lastPosition.z)
        );

        lastPosition = transform.position;

        if (distTraveled < 0.04f * Time.deltaTime)
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer >= stuckCheckDuration)
            {
                stuckTimer = 0f;

                if (state == State.Wander)
                {
                    EnterIdle();
                }
                else if (state == State.Flee)
                {
                    // Jammed against an obstacle while fleeing: execute lateral evasive maneuver
                    Vector3 threatPoint = GetClosestThreatPoint();
                    Vector3 awayFromThreat = (transform.position - threatPoint).normalized;
                    awayFromThreat.y = 0f;

                    Vector3 leftFlank = Quaternion.Euler(0f, -90f, 0f) * awayFromThreat;
                    Vector3 rightFlank = Quaternion.Euler(0f, 90f, 0f) * awayFromThreat;

                    bool leftClear = !IsDirectionBlocked(leftFlank, 1.5f);
                    bool rightClear = !IsDirectionBlocked(rightFlank, 1.5f);

                    Vector3 escapeDir = leftClear ? leftFlank : (rightClear ? rightFlank : -transform.forward);

                    if (creatureMover != null)
                    {
                        creatureMover.MoveInDirection(escapeDir, true);
                    }
                }
            }
        }
        else
        {
            stuckTimer = Mathf.Max(0f, stuckTimer - Time.deltaTime * 2f);
        }
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (IsObstacle(hit.collider))
        {
            hasObstacleContact = true;
            lastObstacleHitTime = Time.time;
            lastObstacleNormal = hit.normal;

            // In wander mode, abort destination if continuously colliding against an obstacle
            if (currentState == State.Wander && (Time.time - lastObstacleHitTime > 0.35f))
            {
                EnterIdle();
            }
        }
    }

    /// <summary>
    /// Returns the closest point of the snake (checking head and all trailing body segments)
    /// so the animal flees away from the nearest part of the snake without running into coils.
    /// </summary>
    private Vector3 GetClosestThreatPoint()
    {
        if (playerTransform == null) return transform.position;

        Vector3 closest = playerTransform.position;
        float minDistSqr = (transform.position - closest).sqrMagnitude;

        body[] segments = playerTransform.GetComponentsInChildren<body>();
        if (segments != null)
        {
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == null) continue;
                float dSqr = (transform.position - segments[i].transform.position).sqrMagnitude;
                if (dSqr < minDistSqr)
                {
                    minDistSqr = dSqr;
                    closest = segments[i].transform.position;
                }
            }
        }

        return closest;
    }

    #endregion

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
            Gizmos.DrawSphere(currentDestination, 0.25f);
            Gizmos.DrawLine(transform.position, currentDestination);

            Vector3 sensorOrigin = transform.position + Vector3.up * sensorHeight;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(sensorOrigin, bodyRadius + obstacleClearanceBuffer);
        }
    }
}
