using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Controls Enemy AI behavior in Snake-Advanced:
/// 1. IDLE BEHAVIOR:
///    - Single-cube state (length 1): Alternates between a pickup-like passive state (hovering, rotating) and roaming to consume pickups.
///      If it consumes a pickup and grows (length >= 2), it immediately stops behaving like a pickup and becomes a normal enemy snake.
///    - Multi-cube state (length >= 2): Roams around normally to find and consume pickups.
///    - If reduced back to a single cube, it returns to pickup-like idle behavior.
/// 2. PURSUIT & PERSISTENCE:
///    - Detects player within detectionRadius and starts chasing.
///    - If player leaves radius, continues chasing for 10 seconds before returning to idle. Re-entering resets the timer.
/// 3. MUTUAL CONSUMPTION & HEAD PROMOTION:
///    - Consumes player/enemy body segments if Head Value > Target Body Value (dissolves target segment, adds to attacker body, reconnects remaining victim body).
///    - Consumes player/enemy head if Head Value > Target Head Value (dissolves target head, promotes next victim cube to NEW HEAD, adds value to attacker body).
///    - Victim dies / defeated only when no cubes remain.
/// </summary>
public class EnemiesLogic : MonoBehaviour
{
    [Header("Enemy Settings")]
    [Tooltip("Movement speed of the enemy snake.")]
    [SerializeField] private float enemySpeed = 3f;

    [Tooltip("Rotation turning speed of the enemy head.")]
    [SerializeField] private float rotationSpeed = 4f;

    [Tooltip("Detection radius to spot the player and initiate chase.")]
    [SerializeField] private float detectionRadius = 8f;

    [Tooltip("Duration in seconds the enemy will persist chasing the player after the player leaves detection radius.")]
    [SerializeField] private float pursuitDuration = 10f;

    [Header("Single-Cube Idle Settings")]
    [Tooltip("Hover height amplitude when idling in single-cube passive mode.")]
    [SerializeField] private float hoverHeight = 0.08f;

    [Tooltip("Hover bobbing speed when idling in single-cube passive mode.")]
    [SerializeField] private float hoverSpeed = 2f;

    [Tooltip("Min and max seconds to stay in passive pickup mode during single-cube idle.")]
    [SerializeField] private Vector2 passiveDurationRange = new Vector2(3f, 7f);

    [Tooltip("Min and max seconds to roam for pickups during single-cube idle.")]
    [SerializeField] private Vector2 roamDurationRange = new Vector2(4f, 8f);

    [Header("Off-Screen Despawn")]
    [Tooltip("If enabled, despawns this enemy if it stays continuously out of camera view for maxTimeOutOfView seconds.")]
    [SerializeField] private bool despawnWhenOutOfView = true;

    [Tooltip("Seconds the enemy can stay continuously outside camera view before despawning.")]
    [SerializeField] private float maxTimeOutOfView = 7f;

    [Tooltip("Camera used to check visibility. If left empty, uses Camera.main.")]
    [SerializeField] private Camera gameplayCamera;

    [Header("Body & Growth")]
    [Tooltip("Maximum allowed cubes for this enemy (head + body segments). When reached, the enemy stops seeking pickups and roams freely.")]
    [SerializeField] private int maxCubes = 5;

    [Tooltip("Body segment prefab to instantiate when enemy grows. If unassigned, will load from Resources/Prefabs.")]
    [SerializeField] private GameObject bodyPrefab;

    [Tooltip("Distance gap between neighbouring body segments.")]
    [SerializeField] private float segmentSpacing = 0.35f;

    [Header("Obstacle Avoidance")]
    [Tooltip("Enable raycast-based obstacle avoidance so enemy steers around trees, rocks, bushes, and walls.")]
    [SerializeField] private bool useObstacleAvoidance = true;

    [Tooltip("Raycast distance to detect obstacles ahead.")]
    [SerializeField] private float obstacleAvoidanceDistance = 3.0f;

    [Tooltip("Weight of the avoidance steering force relative to target direction.")]
    [Range(0.5f, 5f)]
    [SerializeField] private float obstacleAvoidanceWeight = 2.5f;

    [Header("Audio & Effects")]
    [SerializeField] private AudioClip eatSoundClip;

    private static readonly int[] PossibleStartValues = { 2, 4, 8, 16 };

    // Internal State
    private Transform head;
    private readonly List<Transform> bodySegments = new List<Transform>();
    private readonly List<Vector3> pathHistory = new List<Vector3>();

    private Transform playerTransform;
    private SnakeGrow playerSnake;

    private bool isChasingPlayer = false;
    private float pursuitTimer = 0f;
    private bool wasPlayerOutsideRadius = false;
    private bool initializedPursuitCheck = false;
    private float combatCooldownTimer = 0f;

    // Single-cube idle sub-state
    private bool isPickupPassive = true;
    private float idleTimer = 0f;
    private float timeOutOfView = 0f;
    private Vector3 spawnPosition;
    private EnemyRadiusDetection radiusDetection;

    public float DetectionRadius => detectionRadius;
    public SphereCollider DetectionCollider => radiusDetection != null ? radiusDetection.SphereCollider : null;

    public int HeadValue
    {
        get
        {
            if (head != null)
            {
                body b = head.GetComponent<body>();
                if (b != null) return b.Value;
            }
            return 2;
        }
    }

    public int TotalCubeCount => 1 + bodySegments.Count;
    public int MaxCubes => maxCubes;
    public bool IsAtMaxCubes => TotalCubeCount >= maxCubes;
    public Transform Head => head;
    public IReadOnlyList<Transform> BodySegments => bodySegments;

    public Vector2 PassiveDurationRange { get => passiveDurationRange; set => passiveDurationRange = value; }
    public Vector2 RoamDurationRange { get => roamDurationRange; set => roamDurationRange = value; }
    public bool DespawnWhenOutOfView { get => despawnWhenOutOfView; set => despawnWhenOutOfView = value; }
    public float MaxTimeOutOfView { get => maxTimeOutOfView; set => maxTimeOutOfView = value; }
    public Camera GameplayCamera { get => gameplayCamera; set => gameplayCamera = value; }

    public void SetDurations(Vector2 passiveRange, Vector2 roamRange)
    {
        passiveDurationRange = passiveRange;
        roamDurationRange = roamRange;
        idleTimer = isPickupPassive
            ? Random.Range(passiveDurationRange.x, passiveDurationRange.y)
            : Random.Range(roamDurationRange.x, roamDurationRange.y);
    }

    private void Awake()
    {
        ResolveHeadAndComponents();

        // Enforce tag safety:
        // The root GameObject holds the detection trigger sphere collider.
        // It must NEVER be tagged SnakeEnemyHead or SnakeEnemyBody so physics won't confuse the detection sphere with a cube!
        if (gameObject.CompareTag("SnakeEnemyHead") || gameObject.CompareTag("SnakeEnemyBody"))
        {
            gameObject.tag = "Untagged";
        }

        if (head != null)
        {
            if (head != transform)
            {
                head.tag = "SnakeEnemyHead";
            }

            spawnPosition = head.position;

            body b = head.GetComponent<body>();
            if (b == null)
            {
                b = head.gameObject.AddComponent<body>();
            }

            if (b.Value <= 0)
            {
                int startVal = PossibleStartValues[Random.Range(0, PossibleStartValues.Length)];
                b.SetValue(startVal);
            }
        }

        // Setup radius detection on the "collider" child under head
        SetupRadiusDetection();

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    /// <summary>
    /// Initializes this enemy's head value, durations, camera, and updates visuals.
    /// Called by EnemyManager upon spawning.
    /// </summary>
    public void Initialize(int headValue, Vector2? customPassiveRange = null, Vector2? customRoamRange = null, Camera cam = null, float? customMaxTimeOutOfView = null)
    {
        ResolveHeadAndComponents();
        if (head != null)
        {
            spawnPosition = head.position;

            body b = head.GetComponent<body>();
            if (b == null)
            {
                b = head.gameObject.AddComponent<body>();
            }
            b.SetValue(headValue);
        }

        if (customPassiveRange.HasValue && customRoamRange.HasValue)
        {
            SetDurations(customPassiveRange.Value, customRoamRange.Value);
        }

        if (cam != null)
        {
            gameplayCamera = cam;
        }

        if (customMaxTimeOutOfView.HasValue)
        {
            maxTimeOutOfView = customMaxTimeOutOfView.Value;
        }

        LocatePlayer();
    }

    /// <summary>
    /// Locates or creates the "collider" child GameObject under the head,
    /// ensures EnemyRadiusDetection is attached, and configures the trigger SphereCollider.
    /// </summary>
    public void SetupRadiusDetection()
    {
        ResolveHeadAndComponents();
        if (head == null) return;

        // Clean up any old SphereCollider that was mistakenly on the root object
        SphereCollider rootSphere = GetComponent<SphereCollider>();
        if (rootSphere != null)
        {
            Destroy(rootSphere);
        }

        // Look for the "collider" / "Collider" GameObject under head
        radiusDetection = head.GetComponentInChildren<EnemyRadiusDetection>();
        if (radiusDetection == null)
        {
            Transform colChild = null;
            for (int i = 0; i < head.childCount; i++)
            {
                Transform child = head.GetChild(i);
                if (string.Equals(child.name, "collider", System.StringComparison.OrdinalIgnoreCase))
                {
                    colChild = child;
                    break;
                }
            }

            if (colChild == null)
            {
                GameObject colObj = new GameObject("collider");
                colObj.transform.SetParent(head, false);
                colChild = colObj.transform;
            }

            radiusDetection = colChild.GetComponent<EnemyRadiusDetection>();
            if (radiusDetection == null)
            {
                radiusDetection = colChild.gameObject.AddComponent<EnemyRadiusDetection>();
            }
        }

        radiusDetection.SetEnemyLogic(this);
        radiusDetection.InitializeCollider();
    }

    private void Start()
    {
        ResolveHeadAndComponents();
        SetupRadiusDetection();
        LocatePlayer();

        // Seed initial idle timer
        idleTimer = Random.Range(passiveDurationRange.x, passiveDurationRange.y);
        isPickupPassive = true;

        // Ensure bodyPrefab is resolved
        if (bodyPrefab == null && SnakeGrow.Instance != null)
        {
            bodyPrefab = SnakeGrow.Instance.BodyPrefab;
        }
    }

    private void ResolveHeadAndComponents()
    {
        if (head == null)
        {
            Transform h = transform.Find("head") ?? transform.Find("Head");
            if (h == null && transform.childCount > 0)
            {
                h = transform.GetChild(0);
            }
            head = h != null ? h : transform;
        }
    }

    private void LocatePlayer()
    {
        if (SnakeGrow.Instance != null)
        {
            playerSnake = SnakeGrow.Instance;
            playerTransform = playerSnake.transform;
        }
        else
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                playerTransform = playerObj.transform;
                playerSnake = playerObj.GetComponent<SnakeGrow>();
            }
        }
    }

    private void Update()
    {
        if (head == null) return;

        if (combatCooldownTimer > 0f)
        {
            combatCooldownTimer -= Time.deltaTime;
        }

        LocatePlayer();
        UpdatePursuitState();
        ExecuteBehavior();
        RecordHistoryAndMoveBody();
        HandleOutOfViewDespawn();
    }

    /// <summary>
    /// Tracks continuous time spent outside the camera viewport.
    /// If off-screen continuously for maxTimeOutOfView seconds, despawns the enemy cleanly.
    /// Resets the instant the enemy re-enters view, engages in pursuit, or approaches the player.
    /// </summary>
    private void HandleOutOfViewDespawn()
    {
        if (!despawnWhenOutOfView) return;

        // Never despawn if actively chasing the player!
        if (isChasingPlayer)
        {
            timeOutOfView = 0f;
            return;
        }

        // Never despawn if close to the player
        if (playerTransform != null && Vector3.Distance(head.position, playerTransform.position) <= detectionRadius + 3f)
        {
            timeOutOfView = 0f;
            return;
        }

        Camera cam = gameplayCamera != null ? gameplayCamera : Camera.main;
        if (cam == null) return;

        if (IsPointVisibleToCamera(head.position, cam))
        {
            timeOutOfView = 0f;
            return;
        }

        timeOutOfView += Time.deltaTime;
        if (timeOutOfView >= maxTimeOutOfView)
        {
            // Clean silent despawn when staying off-screen too long
            Destroy(gameObject);
        }
    }

    private bool IsPointVisibleToCamera(Vector3 point, Camera cam)
    {
        Vector3 viewportPoint = cam.WorldToViewportPoint(point);
        return viewportPoint.z > 0f
            && viewportPoint.x >= 0f && viewportPoint.x <= 1f
            && viewportPoint.y >= 0f && viewportPoint.y <= 1f;
    }

    /// <summary>
    /// Evaluates player distance, detection radius, and the 10-second pursuit timer persistence.
    /// Ensures pursuit is not triggered instantly on game start if player spawns inside radius.
    /// </summary>
    private void UpdatePursuitState()
    {
        Transform pHead = (playerSnake != null && playerSnake.HeadSegment != null) ? playerSnake.HeadSegment : playerTransform;

        if (pHead != null && (playerSnake == null || !playerSnake.IsDying))
        {
            float distToPlayer = Vector3.Distance(head.position, pHead.position);

            if (!initializedPursuitCheck)
            {
                initializedPursuitCheck = true;
                // On initial spawn, require player to be outside radius first before chase triggers
                wasPlayerOutsideRadius = (distToPlayer > detectionRadius);
            }

            if (distToPlayer > detectionRadius)
            {
                wasPlayerOutsideRadius = true;
            }

            if (distToPlayer <= detectionRadius)
            {
                // Only start chasing if player entered radius during gameplay (was outside), or if player approaches within close range (<= 3.6m)
                if (wasPlayerOutsideRadius || distToPlayer <= detectionRadius * 0.45f)
                {
                    isChasingPlayer = true;
                    pursuitTimer = pursuitDuration;
                    wasPlayerOutsideRadius = false;
                }
            }
            else if (isChasingPlayer)
            {
                // Player outside detection radius -> continue chasing for 10 seconds
                pursuitTimer -= Time.deltaTime;
                if (pursuitTimer <= 0f)
                {
                    // 10 seconds expired outside radius -> give up chase and return to idle
                    isChasingPlayer = false;
                    pursuitTimer = 0f;
                    wasPlayerOutsideRadius = true;
                }
            }
        }
        else if (isChasingPlayer)
        {
            pursuitTimer -= Time.deltaTime;
            if (pursuitTimer <= 0f)
            {
                isChasingPlayer = false;
                wasPlayerOutsideRadius = true;
            }
        }
    }

    /// <summary>
    /// Executes active state behavior: Chasing Player vs Single-Cube Idle vs Multi-Cube Roaming.
    /// </summary>
    private void ExecuteBehavior()
    {
        if (isChasingPlayer)
        {
            // Chase Player
            Transform pHead = (playerSnake != null && playerSnake.HeadSegment != null) ? playerSnake.HeadSegment : playerTransform;
            if (pHead != null)
            {
                MoveAndSteerToward(pHead.position);
            }
        }
        else
        {
            // Idle Behavior based on length
            if (TotalCubeCount == 1)
            {
                // Single-Cube Idle Behavior: Alternate between passive pickup-like state and roaming for pickups
                idleTimer -= Time.deltaTime;
                if (idleTimer <= 0f)
                {
                    isPickupPassive = !isPickupPassive;
                    idleTimer = isPickupPassive
                        ? Random.Range(passiveDurationRange.x, passiveDurationRange.y)
                        : Random.Range(roamDurationRange.x, roamDurationRange.y);
                }

                if (isPickupPassive)
                {
                    // Pickup-like passive state: Hover bobbing and rotating in place
                    float newY = spawnPosition.y + Mathf.Sin(Time.time * hoverSpeed) * hoverHeight;
                    Vector3 pos = head.position;
                    pos.y = newY;
                    head.position = pos;

                    head.Rotate(Vector3.up, rotationSpeed * 30f * Time.deltaTime, Space.World);
                }
                else
                {
                    // Pickup-like roaming state: Search for and consume pickups
                    RoamForPickups();
                }
            }
            else if (TotalCubeCount >= maxCubes)
            {
                // At 5 cubes (max capacity), stop searching for pickups and just roam
                RoamFreely();
            }
            else
            {
                // Multi-Cube Idle Behavior (< 5 cubes): Move around normally and look for pickups
                RoamForPickups();
            }
        }
    }

    private void RoamForPickups()
    {
        if (TotalCubeCount >= maxCubes)
        {
            RoamFreely();
            return;
        }

        Pickup nearestPickup = FindNearestPickup();
        if (nearestPickup != null)
        {
            if (Vector3.Distance(head.position, nearestPickup.transform.position) <= 0.6f)
            {
                int pVal = nearestPickup.Value;
                EnemyGrow(pVal);
                Destroy(nearestPickup.gameObject);
                return;
            }

            MoveAndSteerToward(nearestPickup.transform.position);
        }
        else
        {
            RoamFreely();
        }
    }

    /// <summary>
    /// Roams peacefully across the arena without seeking pickups, applying natural meandering
    /// and raycast-based obstacle avoidance (walls, rocks, trees, bushes, hazards).
    /// </summary>
    private void RoamFreely()
    {
        // Organic meandering path using Perlin noise offset per instance + gentle serpentine motion
        float wanderTurn = (Mathf.PerlinNoise(GetInstanceID() * 0.1f + 17.5f, Time.time * 0.25f) - 0.5f) * 2f;
        Vector3 roamForward = Quaternion.Euler(0f, wanderTurn * 40f, 0f) * head.forward;
        Vector3 targetPos = head.position + roamForward * 3f + head.right * Mathf.Sin(Time.time * 1.0f) * 1.2f;
        MoveAndSteerToward(targetPos);
    }

    private void MoveAndSteerToward(Vector3 targetWorldPos)
    {
        Vector3 targetDir = targetWorldPos - head.position;
        targetDir.y = 0f;

        Vector3 avoidanceSteer = GetObstacleAvoidanceSteer(head.forward);

        Vector3 finalDir = targetDir.normalized;
        if (avoidanceSteer.sqrMagnitude > 0.01f)
        {
            finalDir = (targetDir.normalized + avoidanceSteer * obstacleAvoidanceWeight).normalized;
            finalDir.y = 0f;
        }

        if (finalDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(finalDir);
            head.rotation = Quaternion.Slerp(head.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        head.position += head.forward * enemySpeed * Time.deltaTime;
    }

    /// <summary>
    /// Uses multi-raycast fan ahead of the enemy head to detect static obstacles (trees, bushes, rocks, walls, hazards)
    /// and calculates a smooth avoidance steering direction vector.
    /// </summary>
    private Vector3 GetObstacleAvoidanceSteer(Vector3 currentForward)
    {
        if (!useObstacleAvoidance || head == null) return Vector3.zero;

        Vector3 rayOrigin = head.position + Vector3.up * 0.2f;
        Vector3 avoidanceSteer = Vector3.zero;

        float[] rayAngles = { 0f, -25f, 25f, -50f, 50f };

        foreach (float angle in rayAngles)
        {
            Vector3 rayDir = Quaternion.Euler(0, angle, 0) * currentForward;
            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, rayDir, obstacleAvoidanceDistance);

            foreach (var hit in hits)
            {
                if (hit.collider == null || hit.collider.isTrigger) continue;
                if (IsEnemySegment(hit.transform)) continue;
                if (IsPlayerSegment(hit.transform)) continue;
                if (hit.collider.GetComponent<Pickup>() != null || hit.collider.GetComponentInParent<Pickup>() != null) continue;

                // Hit a valid obstacle (Tree, Bush, Wall, Rock, Hazard...)
                float weight = 1f - (hit.distance / obstacleAvoidanceDistance);
                Vector3 reflectOrNormalDir = hit.normal;
                reflectOrNormalDir.y = 0f;
                if (reflectOrNormalDir.sqrMagnitude > 0.001f) reflectOrNormalDir.Normalize();

                // Steer away from hit normal
                avoidanceSteer += reflectOrNormalDir * weight;

                // Also steer sideways away from ray angle
                if (angle < 0) avoidanceSteer += head.right * weight; // Left ray hit -> steer right
                else if (angle > 0) avoidanceSteer -= head.right * weight; // Right ray hit -> steer left
                else avoidanceSteer += (Vector3.Dot(hit.normal, head.right) >= 0 ? head.right : -head.right) * weight;

                break; // Only consider closest obstacle hit per ray angle
            }
        }

        avoidanceSteer.y = 0f;
        return avoidanceSteer;
    }

    private bool IsEnemySegment(Transform t)
    {
        if (t == null) return false;
        if (t == transform || t == head || t.IsChildOf(transform) || t.IsChildOf(head)) return true;
        for (int i = 0; i < bodySegments.Count; i++)
        {
            if (bodySegments[i] != null && (t == bodySegments[i] || t.IsChildOf(bodySegments[i]))) return true;
        }
        return false;
    }

    private bool IsPlayerSegment(Transform t)
    {
        if (t == null) return false;
        if (playerSnake != null)
        {
            if (t == playerTransform || t.IsChildOf(playerTransform)) return true;
            if (playerSnake.HeadSegment != null && (t == playerSnake.HeadSegment || t.IsChildOf(playerSnake.HeadSegment))) return true;
            var segs = playerSnake.Segments;
            for (int i = 0; i < segs.Count; i++)
            {
                if (segs[i] != null && (t == segs[i] || t.IsChildOf(segs[i]))) return true;
            }
        }
        return false;
    }

    private Pickup FindNearestPickup()
    {
        Pickup[] pickups = FindObjectsOfType<Pickup>();
        Pickup nearest = null;
        float minSqDist = float.MaxValue;

        for (int i = 0; i < pickups.Length; i++)
        {
            if (pickups[i] == null || !pickups[i].gameObject.activeInHierarchy) continue;

            float sqDist = (pickups[i].transform.position - head.position).sqrMagnitude;
            if (sqDist < minSqDist)
            {
                minSqDist = sqDist;
                nearest = pickups[i];
            }
        }

        return nearest;
    }

    private void RecordHistoryAndMoveBody()
    {
        if (head == null) return;

        Vector3 headPos = head.position;
        if (pathHistory.Count == 0 || Vector3.Distance(headPos, pathHistory[pathHistory.Count - 1]) >= 0.05f)
        {
            pathHistory.Add(headPos);
        }

        // Trim path history
        float requiredLength = bodySegments.Count * segmentSpacing + 1f;
        float totalDist = 0f;
        Vector3 prev = headPos;

        for (int i = pathHistory.Count - 1; i >= 0; i--)
        {
            totalDist += Vector3.Distance(prev, pathHistory[i]);
            prev = pathHistory[i];
            if (totalDist > requiredLength && i > 0)
            {
                pathHistory.RemoveRange(0, i);
                break;
            }
        }

        // Position trailing body segments along recorded path history
        for (int i = 0; i < bodySegments.Count; i++)
        {
            Transform seg = bodySegments[i];
            if (seg == null) continue;

            float targetDistance = (i + 1) * segmentSpacing;
            Vector3 targetPos = GetPointAtHistoryDistance(targetDistance);

            Vector3 facing = (i == 0 ? head.position : bodySegments[i - 1].position) - targetPos;
            facing.y = 0f;

            if (facing.sqrMagnitude > 0.0001f)
            {
                seg.rotation = Quaternion.LookRotation(facing.normalized);
            }

            seg.position = targetPos;
        }
    }

    private Vector3 GetPointAtHistoryDistance(float distance)
    {
        if (pathHistory.Count == 0) return head.position;

        Vector3 previousPoint = head.position;
        float distanceCovered = 0f;

        for (int i = pathHistory.Count - 1; i >= 0; i--)
        {
            Vector3 currentPoint = pathHistory[i];
            float length = Vector3.Distance(previousPoint, currentPoint);

            if (distanceCovered + length >= distance)
            {
                float remaining = distance - distanceCovered;
                float t = length > 0.0001f ? remaining / length : 0f;
                return Vector3.Lerp(previousPoint, currentPoint, t);
            }

            distanceCovered += length;
            previousPoint = currentPoint;
        }

        return previousPoint;
    }

    #region Growth, Merges & Defeat

    /// <summary>
    /// Adds a new body segment of specified value to the enemy.
    /// Immediately disables single-cube pickup behavior and triggers body merge checks.
    /// Enforces max cube capacity (cannot grow beyond maxCubes).
    /// </summary>
    public void EnemyGrow(int value)
    {
        if (TotalCubeCount >= maxCubes)
        {
            return;
        }

        if (bodyPrefab == null)
        {
            bodyPrefab = SnakeGrow.Instance != null ? SnakeGrow.Instance.BodyPrefab : null;
        }

        if (bodyPrefab == null)
        {
            Debug.LogWarning("EnemiesLogic: no bodyPrefab assigned - cannot add body segment.");
            return;
        }

        GameObject newSegObj = Instantiate(bodyPrefab, transform);
        newSegObj.tag = "SnakeEnemyBody";
        Transform newSeg = newSegObj.transform;

        body b = newSeg.GetComponent<body>();
        if (b == null)
        {
            b = newSeg.gameObject.AddComponent<body>();
        }
        b.SetValue(value);

        bodySegments.Add(newSeg);

        // Immediately stop behaving like a single-cube pickup
        isPickupPassive = false;

        // Check for adjacent merges among enemy body segments
        CheckEnemyBodyMerges();
    }

    private void CheckEnemyBodyMerges()
    {
        for (int i = 0; i < bodySegments.Count - 1; i++)
        {
            body b1 = bodySegments[i] != null ? bodySegments[i].GetComponent<body>() : null;
            body b2 = bodySegments[i + 1] != null ? bodySegments[i + 1].GetComponent<body>() : null;

            if (b1 != null && b2 != null && b1.Value == b2.Value)
            {
                b1.SetValue(b1.Value * 2);

                Transform rear = bodySegments[i + 1];
                bodySegments.RemoveAt(i + 1);

                if (rear != null)
                {
                    SnakeGrow.AnimateSegmentDissolveAndDestroyObject(rear.gameObject, 0.4f);
                }

                CheckEnemyBodyMerges();
                break;
            }
        }
    }

    /// <summary>
    /// Promotes the next body segment to become the new Head of the enemy
    /// when the current head is consumed by a higher-value player.
    /// Returns true if enemy survived (new head promoted), false if enemy defeated (no cubes left).
    /// </summary>
    public bool PromoteNewEnemyHead()
    {
        if (bodySegments.Count == 0)
        {
            Debug.Log($"[EnemyAI] Enemy '{name}' head consumed with no remaining body segments -> Defeated!");
            DestroyEnemy();
            return false;
        }

        Transform oldHead = head;
        Transform newHead = bodySegments[0];
        bodySegments.RemoveAt(0);

        Debug.Log($"[EnemyAI] Enemy head consumed! Promoting '{newHead.name}' to new Enemy Head.");

        // 1. Immediately disable all colliders on old head so it cannot trigger duplicate hits while dissolving
        if (oldHead != null)
        {
            Collider[] cols = oldHead.GetComponentsInChildren<Collider>();
            foreach (var c in cols) if (c != null) c.enabled = false;

            oldHead.SetParent(null);
            SnakeGrow.AnimateSegmentDissolveAndDestroyObject(oldHead.gameObject, 0.6f);
        }

        // 2. Update head reference
        head = newHead;
        if (head != null)
        {
            head.tag = "SnakeEnemyHead";
        }

        // 3. Ensure new head has body component
        if (head.GetComponent<body>() == null)
        {
            head.gameObject.AddComponent<body>();
        }

        // 4. Attach/reparent radius detection collider to the new head
        if (radiusDetection != null && radiusDetection.transform != null)
        {
            radiusDetection.transform.SetParent(head, false);
            radiusDetection.transform.localPosition = Vector3.zero;
            radiusDetection.SetEnemyLogic(this);
            radiusDetection.InitializeCollider();
        }
        else
        {
            SetupRadiusDetection();
        }

        // 5. If reduced back to 1 cube total, re-enable pickup-like idle behavior capability
        if (TotalCubeCount == 1)
        {
            isPickupPassive = true;
            idleTimer = Random.Range(passiveDurationRange.x, passiveDurationRange.y);
        }

        return true;
    }

    /// <summary>
    /// Removes a body segment from the enemy when consumed by a higher-value player.
    /// Returns true if segment was removed.
    /// </summary>
    public bool RemoveEnemyBodySegment(Transform segment)
    {
        if (segment == null || !bodySegments.Contains(segment)) return false;

        // Immediately disable all colliders on removed segment so physics will not trigger again
        Collider[] cols = segment.GetComponentsInChildren<Collider>();
        foreach (var c in cols) if (c != null) c.enabled = false;

        bodySegments.Remove(segment);
        segment.SetParent(null);
        SnakeGrow.AnimateSegmentDissolveAndDestroyObject(segment.gameObject, 0.6f);

        if (TotalCubeCount == 1)
        {
            isPickupPassive = true;
            idleTimer = Random.Range(passiveDurationRange.x, passiveDurationRange.y);
        }

        return true;
    }

    public void DestroyEnemy()
    {
        if (head != null)
        {
            Collider[] headCols = head.GetComponentsInChildren<Collider>();
            foreach (var c in headCols) if (c != null) c.enabled = false;

            head.SetParent(null);
            SnakeGrow.AnimateSegmentDissolveAndDestroyObject(head.gameObject, 0.6f);
        }

        foreach (var seg in bodySegments)
        {
            if (seg != null)
            {
                Collider[] segCols = seg.GetComponentsInChildren<Collider>();
                foreach (var c in segCols) if (c != null) c.enabled = false;

                seg.SetParent(null);
                SnakeGrow.AnimateSegmentDissolveAndDestroyObject(seg.gameObject, 0.6f);
            }
        }
        bodySegments.Clear();

        Destroy(gameObject, 0.65f);
    }

    #endregion

    #region Collision & Mutual Combat

    /// <summary>
    /// Called by EnemyRadiusDetection when the player enters the detection radius trigger.
    /// Initiates pursuit and resets pursuit persistence timer.
    /// NEVER triggers combat consumption.
    /// </summary>
    public void OnPlayerEnteredRadius()
    {
        isChasingPlayer = true;
        pursuitTimer = pursuitDuration;
        wasPlayerOutsideRadius = false;
    }

    /// <summary>
    /// Called by EnemyRadiusDetection when the player exits the detection radius trigger.
    /// Marks the player as outside so pursuit countdown will begin.
    /// </summary>
    public void OnPlayerExitedRadius()
    {
        wasPlayerOutsideRadius = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;

        // Fallback in case trigger bubbles up to root Rigidbody
        if (other.CompareTag("SnakeHead") || other.CompareTag("SnakeBody") || other.GetComponentInParent<SnakeGrow>() != null)
        {
            OnPlayerEnteredRadius();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other == null) return;

        // Fallback in case trigger bubbles up to root Rigidbody
        if (other.CompareTag("SnakeHead") || other.CompareTag("SnakeBody") || other.GetComponentInParent<SnakeGrow>() != null)
        {
            OnPlayerExitedRadius();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null || collision.gameObject == null) return;
        HandleCombatCollision(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        if (collision == null || collision.gameObject == null) return;
        HandleCombatCollision(collision);
    }

    public void HandleCombatCollision(Collision collision)
    {
        if (collision == null || combatCooldownTimer > 0f) return;
        if (head == null) return;

        // 1. Solid pickup collection (if pickup has solid collider)
        Pickup pickup = collision.gameObject.GetComponent<Pickup>() ?? collision.gameObject.GetComponentInParent<Pickup>();
        if (pickup != null)
        {
            if (TotalCubeCount >= maxCubes) return;

            float distToHead = Vector3.Distance(head.position, pickup.transform.position);
            if (distToHead <= 0.6f)
            {
                int pVal = pickup.Value;
                EnemyGrow(pVal);
                Destroy(pickup.gameObject);
            }
            return;
        }

        // 2. Locate Player
        SnakeGrow player = collision.gameObject.GetComponentInParent<SnakeGrow>() ?? SnakeGrow.Instance;
        if (player == null) player = FindObjectOfType<SnakeGrow>();
        if (player == null || player.IsDying || player.HeadSegment == null) return;

        // 3. Identify physical colliders involved from contact points
        Collider enemyCol = null;
        Collider playerCol = null;

        List<Collider> involved = new List<Collider>();
        if (collision.collider != null) involved.Add(collision.collider);
        for (int i = 0; i < collision.contactCount; i++)
        {
            var contact = collision.GetContact(i);
            if (contact.thisCollider != null && !involved.Contains(contact.thisCollider))
                involved.Add(contact.thisCollider);
            if (contact.otherCollider != null && !involved.Contains(contact.otherCollider))
                involved.Add(contact.otherCollider);
        }

        foreach (var col in involved)
        {
            if (col == null || col.isTrigger) continue;

            // Check if collider belongs to this enemy
            if (col.CompareTag("SnakeEnemyHead") || col.CompareTag("SnakeEnemyBody") ||
                col.transform == head || col.transform.IsChildOf(head) ||
                IsEnemySegment(col.transform))
            {
                if (enemyCol == null || col.CompareTag("SnakeEnemyHead") || col.transform == head)
                {
                    enemyCol = col;
                }
            }
            // Check if collider belongs to the player
            else if (col.CompareTag("SnakeHead") || col.CompareTag("SnakeBody") ||
                     col.transform == player.HeadSegment || col.transform.IsChildOf(player.HeadSegment) ||
                     IsPlayerSegment(col.transform))
            {
                if (playerCol == null || col.CompareTag("SnakeHead") || col.transform == player.HeadSegment)
                {
                    playerCol = col;
                }
            }
        }

        // Fallback for collider resolution if contact points were unpopulated
        if (enemyCol == null || playerCol == null)
        {
            if (collision.collider != null && !collision.collider.isTrigger)
            {
                if (collision.collider.CompareTag("SnakeEnemyHead") || collision.collider.CompareTag("SnakeEnemyBody") || IsEnemySegment(collision.collider.transform))
                {
                    enemyCol = collision.collider;
                    playerCol = player.HeadSegment != null ? player.HeadSegment.GetComponentInChildren<Collider>() : null;
                }
                else if (collision.collider.CompareTag("SnakeHead") || collision.collider.CompareTag("SnakeBody") || IsPlayerSegment(collision.collider.transform))
                {
                    playerCol = collision.collider;
                    enemyCol = head != null ? head.GetComponentInChildren<Collider>() : null;
                }
            }
        }

        // If either collider is missing or is a trigger collider, do not resolve combat
        if (enemyCol == null || playerCol == null || enemyCol.isTrigger || playerCol.isTrigger)
        {
            return;
        }

        // Determine if enemy collider is Head vs Body segment
        bool isEnemyHead = (enemyCol.CompareTag("SnakeEnemyHead") || enemyCol.transform == head || enemyCol.transform.IsChildOf(head));
        Transform hitEnemyBody = null;

        if (!isEnemyHead)
        {
            for (int i = 0; i < bodySegments.Count; i++)
            {
                if (bodySegments[i] != null && (enemyCol.transform == bodySegments[i] || enemyCol.transform.IsChildOf(bodySegments[i])))
                {
                    hitEnemyBody = bodySegments[i];
                    break;
                }
            }

            if (hitEnemyBody == null && (enemyCol.CompareTag("SnakeEnemyBody") || enemyCol.GetComponent<body>() != null))
            {
                float minD = float.MaxValue;
                for (int i = 0; i < bodySegments.Count; i++)
                {
                    if (bodySegments[i] != null)
                    {
                        float d = Vector3.Distance(enemyCol.transform.position, bodySegments[i].position);
                        if (d < minD)
                        {
                            minD = d;
                            hitEnemyBody = bodySegments[i];
                        }
                    }
                }
            }
        }

        // Determine if player collider is Head vs Body segment
        bool isPlayerHead = (playerCol.CompareTag("SnakeHead") ||
                             playerCol.transform == player.HeadSegment ||
                             playerCol.transform.IsChildOf(player.HeadSegment) ||
                             (player.Segments.Count > 0 && (playerCol.transform == player.Segments[0] || playerCol.transform.IsChildOf(player.Segments[0]))));

        Transform hitPlayerBody = null;
        if (!isPlayerHead)
        {
            var pSegs = player.Segments;
            for (int i = 1; i < pSegs.Count; i++)
            {
                if (pSegs[i] != null && (playerCol.transform == pSegs[i] || playerCol.transform.IsChildOf(pSegs[i])))
                {
                    hitPlayerBody = pSegs[i];
                    break;
                }
            }

            if (hitPlayerBody == null && (playerCol.CompareTag("SnakeBody") || playerCol.GetComponent<body>() != null))
            {
                float minD = float.MaxValue;
                for (int i = 1; i < pSegs.Count; i++)
                {
                    if (pSegs[i] != null)
                    {
                        float d = Vector3.Distance(playerCol.transform.position, pSegs[i].position);
                        if (d < minD)
                        {
                            minD = d;
                            hitPlayerBody = pSegs[i];
                        }
                    }
                }
            }
        }

        // Body-to-body collisions do not consume either snake
        if (!isPlayerHead && !isEnemyHead)
        {
            return;
        }

        int pHeadVal = player.HeadValue;
        int eHeadVal = HeadValue;

        // CASE 1: Player Head touches Enemy Head
        if (isPlayerHead && isEnemyHead)
        {
            combatCooldownTimer = 0.4f;

            if (pHeadVal >= eHeadVal)
            {
                Debug.Log($"[Combat] Player Head [{pHeadVal}] touched & consumed Enemy Head [{eHeadVal}]!");
                int consumedVal = eHeadVal;
                bool enemySurvived = PromoteNewEnemyHead();
                player.Grow(consumedVal);
            }
            else
            {
                Debug.Log($"[Combat] Enemy Head [{eHeadVal}] touched & consumed Player Head [{pHeadVal}]!");
                int consumedVal = pHeadVal;
                bool playerSurvived = player.PromoteNewHead(consumedVal);
                EnemyGrow(consumedVal);
            }
            return;
        }

        // CASE 2: Player Head touches Enemy Body Segment
        if (isPlayerHead && hitEnemyBody != null)
        {
            body eBody = hitEnemyBody.GetComponent<body>() ?? hitEnemyBody.GetComponentInChildren<body>();
            int eBodyVal = eBody != null ? eBody.Value : 2;

            if (pHeadVal >= eBodyVal)
            {
                Debug.Log($"[Combat] Player Head [{pHeadVal}] touched & consumed Enemy Body Segment [{eBodyVal}]!");
                combatCooldownTimer = 0.4f;
                bool removed = RemoveEnemyBodySegment(hitEnemyBody);
                if (removed)
                {
                    player.Grow(eBodyVal);
                }
            }
            else
            {
                Debug.Log($"[Combat] Player Head [{pHeadVal}] cannot consume stronger Enemy Body Segment [{eBodyVal}].");
            }
            return;
        }

        // CASE 3: Enemy Head touches Player Body Segment
        if (isEnemyHead && hitPlayerBody != null)
        {
            body pBody = hitPlayerBody.GetComponent<body>() ?? hitPlayerBody.GetComponentInChildren<body>();
            int pBodyVal = pBody != null ? pBody.Value : 2;

            if (eHeadVal > pBodyVal)
            {
                Debug.Log($"[Combat] Enemy Head [{eHeadVal}] touched & consumed Player Body Segment [{pBodyVal}]!");
                combatCooldownTimer = 0.4f;
                bool damaged = player.TakeBodyDamage(hitPlayerBody);
                if (damaged)
                {
                    EnemyGrow(pBodyVal);
                }
            }
            else
            {
                Debug.Log($"[Combat] Enemy Head [{eHeadVal}] cannot consume stronger Player Body Segment [{pBodyVal}].");
            }
            return;
        }
    }

    public void HandleCombatCollision(GameObject otherObj)
    {
        if (otherObj == null || head == null) return;

        // Pickup Collection
        Pickup pickup = otherObj.GetComponent<Pickup>() ?? otherObj.GetComponentInParent<Pickup>();
        if (pickup != null)
        {
            if (TotalCubeCount >= maxCubes) return;

            float distToHead = Vector3.Distance(head.position, pickup.transform.position);
            if (distToHead <= 0.6f)
            {
                int pVal = pickup.Value;
                EnemyGrow(pVal);
                Destroy(pickup.gameObject);
            }
        }
    }

    #endregion
}
