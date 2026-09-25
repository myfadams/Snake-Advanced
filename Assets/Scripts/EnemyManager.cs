using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to: Environment (or Environment GameObject)
/// Spawns and manages enemies dynamically across the floor just like PickupManager spawns pickups.
/// Features:
/// - Strict live enemy cap (maxEnemiesAlive) so the game never spawns too many enemies.
/// - Varied idle behaviors: enemies roll distinct passive and active (roaming) durations (e.g. Lurkers, Roamers, Balanced).
/// - Off-screen despawn: enemies staying continuously off-screen for a while automatically despawn.
/// - Forward-cone spawn bias ahead of player heading.
/// - Floor surface snapping and obstacle clearance checks (avoiding rocks, trees, bushes, walls).
/// - Safe spacing from other enemies, pickups, and the player.
/// - Progression-aware or random head values (2, 4, 8, 16).
/// - Automatic recycling of distant off-screen enemies left far behind the player.
/// </summary>
[DisallowMultipleComponent]
public class EnemyManager : MonoBehaviour
{
    public static EnemyManager Instance { get; private set; }

    [Header("Enemy Prefab")]
    [Tooltip("The Enemy prefab to instantiate (must contain EnemiesLogic or have it in children).")]
    [SerializeField] private GameObject enemyPrefab;

    [Header("Max Enemies Allowed Alive (Strict Cap)")]
    [Tooltip("Strict maximum number of enemies allowed to be alive in the scene at any time. Spawning will NEVER exceed this number.")]
    [SerializeField] private int maxEnemiesAlive = 4;

    [Tooltip("Minimum number of active enemies in the play area. If count drops below this, spawning refills immediately up to this count.")]
    [SerializeField] private int minEnemiesAlive = 2;

    [Tooltip("Initial delay in seconds after game start before the first enemy spawns.")]
    [SerializeField] private float initialSpawnDelay = 1.5f;

    [Tooltip("Periodic delay between spawn checks when below maxEnemiesAlive.")]
    [SerializeField] private float spawnCheckInterval = 3f;

    [Header("Idle & Roam Duration Variations")]
    [Tooltip("If enabled, each spawned enemy is assigned different passive and active (roam) durations (e.g. Lurkers stay passive longer, Roamers stay active longer).")]
    [SerializeField] private bool varyIdleDurations = true;

    [Tooltip("Passive duration range for Lurker enemies (longer passive, shorter active).")]
    [SerializeField] private Vector2 lurkerPassiveRange = new Vector2(7f, 12f);
    [Tooltip("Roam duration range for Lurker enemies.")]
    [SerializeField] private Vector2 lurkerRoamRange = new Vector2(2f, 4f);

    [Tooltip("Passive duration range for Roamer enemies (shorter passive, longer active).")]
    [SerializeField] private Vector2 roamerPassiveRange = new Vector2(1.5f, 3f);
    [Tooltip("Roam duration range for Roamer enemies.")]
    [SerializeField] private Vector2 roamerRoamRange = new Vector2(8f, 15f);

    [Tooltip("Passive duration range for Balanced enemies.")]
    [SerializeField] private Vector2 balancedPassiveRange = new Vector2(3f, 6f);
    [Tooltip("Roam duration range for Balanced enemies.")]
    [SerializeField] private Vector2 balancedRoamRange = new Vector2(4f, 8f);

    [Header("Off-Screen Despawn Settings")]
    [Tooltip("Seconds an enemy can remain continuously off-screen before it despawns.")]
    [SerializeField] private float enemyMaxTimeOutOfView = 7f;

    [Tooltip("Recycles off-screen enemies left this far behind the player (in meters) to free up spawn capacity ahead.")]
    [SerializeField] private float maxDistanceBehindPlayer = 8f;

    [Header("Player Reference")]
    [Tooltip("The player/snake transform enemies should spawn near. Automatically finds Player if left empty.")]
    [SerializeField] private Transform player;

    [Header("Spawn Distance & Forward Bias")]
    [Tooltip("Closest an enemy is allowed to spawn to the player (prevents enemies appearing right in the player's face).")]
    [SerializeField] private float minSpawnDistanceFromPlayer = 4.5f;

    [Tooltip("Farthest an enemy is allowed to spawn from the player.")]
    [SerializeField] private float maxSpawnDistanceFromPlayer = 12f;

    [Tooltip("Chance that a spawn is placed in the forward cone ahead of the snake (e.g. 0.85 = 85% in front).")]
    [Range(0f, 1f)]
    [SerializeField] private float forwardBiasChance = 0.85f;

    [Tooltip("Maximum angle (in degrees) to left or right of forward heading for forward-biased spawns.")]
    [Range(15f, 90f)]
    [SerializeField] private float forwardConeAngle = 55f;

    [Header("Floor & Height Rules")]
    [Tooltip("FloorManager reference used to check active floor bounds. If unassigned, finds FloorManager in scene.")]
    [SerializeField] private FloorManager floorManager;

    [Tooltip("World Y position of the floor surface (synced with PickupManager if present).")]
    [SerializeField] private float floorHeight = 0f;

    [Tooltip("How far above the floor surface enemies should spawn.")]
    [SerializeField] private float heightAboveFloor = 0.2f;

    [Tooltip("Fallback anchor point (Y is ignored), used only if Player is not assigned.")]
    [SerializeField] private Vector3 floorBoundsCenter = Vector3.zero;

    [Tooltip("Width (X) and depth (Z) around the player within which spawns are clamped.")]
    [SerializeField] private Vector3 floorBoundsSize = new Vector3(45f, 0f, 45f);

    [Tooltip("If enabled, performs downward raycast to snap accurately to ground/floor collider elevation.")]
    [SerializeField] private bool raycastToFloorSurface = true;

    [Header("Spawn Spacing & Rules")]
    [Tooltip("Minimum horizontal distance required between two enemies to prevent crowding.")]
    [SerializeField] private float minDistanceBetweenEnemies = 4.0f;

    [Tooltip("Minimum distance from active pickups to avoid overlapping.")]
    [SerializeField] private float minDistanceFromPickups = 1.2f;

    [Tooltip("How many candidate positions to test per enemy before giving up for this cycle.")]
    [SerializeField] private int maxSpawnAttempts = 35;

    [Header("Camera Reference")]
    [Tooltip("Camera used to check what is currently on screen. Leave empty to use Camera.main.")]
    [SerializeField] private Camera gameplayCamera;

    [Header("Head Value Selection")]
    [Tooltip("If true, selects enemy head values influenced by player's current head value tier (just like PickupManager); if false, picks uniformly from Possible Head Values.")]
    [SerializeField] private bool useProgressionDistribution = true;

    [Tooltip("Supported head values when spawning enemies (e.g. 2, 4, 8, 16).")]
    [SerializeField] private int[] possibleHeadValues = new int[] { 2, 4, 8, 16 };

    [Tooltip("Probability distributions per player Head tier if progression is enabled.")]
    [SerializeField] private List<HeadTierDistribution> tierDistributions = new List<HeadTierDistribution>
    {
        new HeadTierDistribution(2, 60f, 30f, 10f, 0f),
        new HeadTierDistribution(4, 40f, 40f, 20f, 0f),
        new HeadTierDistribution(8, 25f, 35f, 25f, 15f),
        new HeadTierDistribution(16, 15f, 30f, 30f, 25f)
    };

    [Header("Optional Initial Body Segments")]
    [Tooltip("If enabled, newly spawned enemies will occasionally start with body segments already attached.")]
    [SerializeField] private bool spawnWithInitialSegments = false;

    [Tooltip("Range of initial body segments to add when spawnWithInitialSegments is true.")]
    [SerializeField] private Vector2Int initialSegmentsRange = new Vector2Int(0, 2);

    // Active state
    private readonly List<EnemiesLogic> activeEnemies = new List<EnemiesLogic>();
    private PlayerMovement playerMovement;
    private SnakeGrow playerSnake;
    private float spawnTimer = 0f;
    private bool hasStartedSpawning = false;

    public IReadOnlyList<EnemiesLogic> ActiveEnemies => activeEnemies;
    public int ActiveEnemyCount => GetTotalAliveEnemiesCount();
    public int MaxEnemiesAlive { get => maxEnemiesAlive; set => maxEnemiesAlive = Mathf.Max(1, value); }
    public int MinEnemiesAlive { get => minEnemiesAlive; set => minEnemiesAlive = Mathf.Clamp(value, 0, maxEnemiesAlive); }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        AutoResolveReferences();
        FindInitialSceneEnemies();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        AutoResolveReferences();
        FindInitialSceneEnemies();
        spawnTimer = initialSpawnDelay;
    }

    private void OnValidate()
    {
        maxEnemiesAlive = Mathf.Max(1, maxEnemiesAlive);
        minEnemiesAlive = Mathf.Clamp(minEnemiesAlive, 0, maxEnemiesAlive);

        if (maxSpawnDistanceFromPlayer < minSpawnDistanceFromPlayer)
        {
            maxSpawnDistanceFromPlayer = minSpawnDistanceFromPlayer;
        }

        minDistanceBetweenEnemies = Mathf.Max(1f, minDistanceBetweenEnemies);
        minDistanceFromPickups = Mathf.Max(0.5f, minDistanceFromPickups);
        enemyMaxTimeOutOfView = Mathf.Max(2f, enemyMaxTimeOutOfView);
    }

    private void AutoResolveReferences()
    {
        if (enemyPrefab == null)
        {
            enemyPrefab = Resources.Load<GameObject>("Prefabs/Ememies");
        }

        if (player == null)
        {
            if (SnakeGrow.Instance != null)
            {
                playerSnake = SnakeGrow.Instance;
                player = playerSnake.transform;
            }
            else
            {
                GameObject pObj = GameObject.FindWithTag("Player");
                if (pObj != null)
                {
                    player = pObj.transform;
                    playerSnake = pObj.GetComponent<SnakeGrow>();
                }
            }
        }

        if (player != null && playerMovement == null)
        {
            playerMovement = player.GetComponent<PlayerMovement>() ?? player.GetComponentInChildren<PlayerMovement>();
            if (playerSnake == null)
            {
                playerSnake = player.GetComponent<SnakeGrow>() ?? player.GetComponentInChildren<SnakeGrow>();
            }
        }

        if (floorManager == null)
        {
            floorManager = GetComponent<FloorManager>() ?? FindObjectOfType<FloorManager>();
        }

        if (PickupManager.Instance != null)
        {
            floorHeight = PickupManager.Instance.FloorHeight;
            heightAboveFloor = PickupManager.Instance.HeightAboveFloor;
            floorBoundsSize = PickupManager.Instance.FloorBoundsSize;
            if (gameplayCamera == null)
            {
                gameplayCamera = PickupManager.Instance.GameplayCamera;
            }
        }

        if (gameplayCamera == null)
        {
            gameplayCamera = Camera.main;
        }
    }

    private void FindInitialSceneEnemies()
    {
        EnemiesLogic[] sceneEnemies = FindObjectsOfType<EnemiesLogic>();
        foreach (var enemy in sceneEnemies)
        {
            if (enemy != null && !activeEnemies.Contains(enemy))
            {
                activeEnemies.Add(enemy);
            }
        }
    }

    /// <summary>
    /// Returns the exact count of living enemies in the scene, cleaning up null references.
    /// </summary>
    public int GetTotalAliveEnemiesCount()
    {
        activeEnemies.RemoveAll(e => e == null || e.gameObject == null);
        return activeEnemies.Count;
    }

    private void Update()
    {
        // 1. Purge null/destroyed enemies (defeated in combat or despawned off-screen)
        activeEnemies.RemoveAll(e => e == null || e.gameObject == null);

        // 2. Recycle distant enemies left behind player
        RecycleEnemiesLeftBehind();

        int currentAlive = activeEnemies.Count;

        // 3. Strict max cap enforcement: If already at or above max allowed, NEVER spawn!
        if (currentAlive >= maxEnemiesAlive)
        {
            return;
        }

        // 4. Initial spawn delay
        if (!hasStartedSpawning)
        {
            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f)
            {
                hasStartedSpawning = true;
                RefillEnemies(minEnemiesAlive);
                spawnTimer = spawnCheckInterval;
            }
            return;
        }

        // 5. Maintain minimum density immediately up to minEnemiesAlive
        if (currentAlive < minEnemiesAlive)
        {
            int needed = minEnemiesAlive - currentAlive;
            for (int i = 0; i < needed; i++)
            {
                if (activeEnemies.Count >= maxEnemiesAlive) break;
                if (!SpawnEnemy()) break;
            }
        }
        // 6. Periodic check to top up towards maxEnemiesAlive
        else if (currentAlive < maxEnemiesAlive)
        {
            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f)
            {
                spawnTimer = spawnCheckInterval;
                SpawnEnemy();
            }
        }
    }

    /// <summary>
    /// Checks for active enemies that have been left far behind the player and are no longer
    /// visible, destroying them so their capacity can be used to spawn ahead of the player.
    /// </summary>
    private void RecycleEnemiesLeftBehind()
    {
        if (player == null || activeEnemies.Count <= minEnemiesAlive) return;

        Vector3 playerPos = player.position;
        Vector3 forward = GetPlayerForward();
        Camera cam = gameplayCamera != null ? gameplayCamera : Camera.main;

        for (int i = activeEnemies.Count - 1; i >= 0; i--)
        {
            EnemiesLogic enemy = activeEnemies[i];
            if (enemy == null || enemy.Head == null)
            {
                activeEnemies.RemoveAt(i);
                continue;
            }

            Vector3 toEnemy = enemy.Head.position - playerPos;
            toEnemy.y = 0f;

            float dot = Vector3.Dot(toEnemy, forward);

            // If enemy is more than maxDistanceBehindPlayer behind, or more than 1.6x max distance away
            if (dot < -maxDistanceBehindPlayer || toEnemy.sqrMagnitude > maxSpawnDistanceFromPlayer * maxSpawnDistanceFromPlayer * 2.5f)
            {
                bool isVisible = cam != null && IsPointVisibleToCamera(enemy.Head.position, cam);
                if (!isVisible)
                {
                    Destroy(enemy.gameObject);
                    activeEnemies.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>
    /// Rolls varying passive and active durations for a newly spawned enemy.
    /// </summary>
    public void GetVaryingDurations(out Vector2 passiveRange, out Vector2 roamRange, out string archetypeLog)
    {
        if (!varyIdleDurations)
        {
            passiveRange = balancedPassiveRange;
            roamRange = balancedRoamRange;
            archetypeLog = "Default";
            return;
        }

        float roll = Random.value;
        if (roll < 0.35f)
        {
            // Lurker: Long passive duration, short active roam
            float pMin = Random.Range(lurkerPassiveRange.x, lurkerPassiveRange.x + 1.5f);
            float pMax = Random.Range(pMin + 2f, lurkerPassiveRange.y + 2f);
            float rMin = Random.Range(lurkerRoamRange.x, lurkerRoamRange.y);
            float rMax = rMin + Random.Range(1.5f, 2.5f);
            passiveRange = new Vector2(pMin, pMax);
            roamRange = new Vector2(rMin, rMax);
            archetypeLog = "Lurker (Long Passive, Short Roam)";
        }
        else if (roll < 0.70f)
        {
            // Roamer: Short passive duration, long active roam
            float pMin = Random.Range(roamerPassiveRange.x, roamerPassiveRange.y);
            float pMax = pMin + Random.Range(1f, 2f);
            float rMin = Random.Range(roamerRoamRange.x, roamerRoamRange.x + 2f);
            float rMax = Random.Range(rMin + 2f, roamerRoamRange.y + 2f);
            passiveRange = new Vector2(pMin, pMax);
            roamRange = new Vector2(rMin, rMax);
            archetypeLog = "Roamer (Short Passive, Long Roam)";
        }
        else
        {
            // Balanced
            float pMin = Random.Range(balancedPassiveRange.x, balancedPassiveRange.y);
            float pMax = pMin + Random.Range(1.5f, 3f);
            float rMin = Random.Range(balancedRoamRange.x, balancedRoamRange.y);
            float rMax = rMin + Random.Range(1.5f, 3f);
            passiveRange = new Vector2(pMin, pMax);
            roamRange = new Vector2(rMin, rMax);
            archetypeLog = "Balanced";
        }
    }

    /// <summary>
    /// Attempts to spawn an enemy at a valid floor position with random/progression head values and varying durations.
    /// Strictly guarantees the live enemy count never exceeds maxEnemiesAlive.
    /// </summary>
    public bool SpawnEnemy()
    {
        // 1. Strict cap check
        if (GetTotalAliveEnemiesCount() >= maxEnemiesAlive)
        {
            return false;
        }

        if (enemyPrefab == null)
        {
            Debug.LogWarning("EnemyManager: Enemy Prefab is not assigned in Inspector.");
            return false;
        }

        if (!TryGetValidSpawnPosition(out Vector3 spawnPosition))
        {
            return false;
        }

        Quaternion spawnRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        GameObject enemyObj = Instantiate(enemyPrefab, spawnPosition, spawnRotation);

        EnemiesLogic logic = enemyObj.GetComponent<EnemiesLogic>() ?? enemyObj.GetComponentInChildren<EnemiesLogic>();
        if (logic != null)
        {
            int headVal = GetRandomHeadValue();

            // Vary passive and active roam durations per enemy
            GetVaryingDurations(out Vector2 passiveRange, out Vector2 roamRange, out string archetype);

            Camera cam = gameplayCamera != null ? gameplayCamera : Camera.main;
            logic.Initialize(headVal, passiveRange, roamRange, cam, enemyMaxTimeOutOfView);

            // Optional: attach initial body segments if enabled
            if (spawnWithInitialSegments)
            {
                int segCount = Random.Range(initialSegmentsRange.x, initialSegmentsRange.y + 1);
                for (int s = 0; s < segCount && logic.TotalCubeCount < logic.MaxCubes; s++)
                {
                    int segVal = GetRandomHeadValue();
                    logic.EnemyGrow(segVal);
                }
            }

            activeEnemies.Add(logic);
            IgnoreCollisionsWithPowerUps(logic);
            return true;
        }

        Debug.LogWarning("EnemyManager: Spawned object missing EnemiesLogic component.");
        return false;
    }

    private void RefillEnemies(int targetCount)
    {
        targetCount = Mathf.Min(targetCount, maxEnemiesAlive);
        int amountToSpawn = targetCount - activeEnemies.Count;
        for (int i = 0; i < amountToSpawn; i++)
        {
            if (activeEnemies.Count >= maxEnemiesAlive) break;
            if (!SpawnEnemy()) break;
        }
    }

    /// <summary>
    /// Finds a guaranteed valid spawn position strictly ON an active floor tile.
    /// Prioritizes active floors from FloorManager ahead of the player within view.
    /// </summary>
    private bool TryGetValidSpawnPosition(out Vector3 result)
    {
        Camera cam = gameplayCamera != null ? gameplayCamera : Camera.main;
        Vector3 anchor = player != null ? player.position : floorBoundsCenter;
        Vector3 forward = GetPlayerForward();

        // ---------------------------------------------------------
        // STRATEGY A: Direct Selection from Active Floor Tiles (100% Guaranteed on the Floor!)
        // ---------------------------------------------------------
        if (floorManager != null && floorManager.ActiveTileCount > 0)
        {
            var activeFloors = floorManager.GetActiveFloors();
            List<Transform> candidateFloors = new List<Transform>();
            List<Transform> forwardFloors = new List<Transform>();

            // Clamp max distance to the actual spawn radius of FloorManager so we NEVER pick beyond existing tiles!
            float maxEffectiveDist = Mathf.Min(maxSpawnDistanceFromPlayer, floorManager.SpawnRadius + 1.5f);

            foreach (var floor in activeFloors)
            {
                if (floor == null) continue;

                float dist = Vector3.Distance(floor.position, anchor);
                if (dist >= minSpawnDistanceFromPlayer && dist <= maxEffectiveDist)
                {
                    candidateFloors.Add(floor);

                    Vector3 toFloor = floor.position - anchor;
                    toFloor.y = 0f;
                    if (Vector3.Angle(forward, toFloor) <= forwardConeAngle)
                    {
                        forwardFloors.Add(floor);
                    }
                }
            }

            // Fallback: if no floor met min distance (e.g. few tiles), consider closer floors >= 2.5m
            if (candidateFloors.Count == 0)
            {
                foreach (var floor in activeFloors)
                {
                    if (floor == null) continue;
                    float dist = Vector3.Distance(floor.position, anchor);
                    if (dist >= 2.5f && dist <= maxEffectiveDist)
                    {
                        candidateFloors.Add(floor);
                    }
                }
            }

            if (candidateFloors.Count > 0)
            {
                Vector3 fallbackPos = Vector3.zero;
                bool hasFallback = false;

                for (int attempt = 0; attempt < maxSpawnAttempts; attempt++)
                {
                    // Choose floor (forward biased)
                    Transform chosenFloor;
                    if (forwardFloors.Count > 0 && Random.value < forwardBiasChance)
                    {
                        chosenFloor = forwardFloors[Random.Range(0, forwardFloors.Count)];
                    }
                    else
                    {
                        chosenFloor = candidateFloors[Random.Range(0, candidateFloors.Count)];
                    }

                    // Use FloorManager's exact placement logic (strictly within tile bounds, no prop overlap, no obstacle overlap)
                    if (!floorManager.FindValidFloorPosition(chosenFloor, 0.45f, out Vector2 localPos, out Vector3 worldPos))
                    {
                        continue;
                    }

                    // Raycast down to find top surface of ground collider
                    RaycastHit[] hits = Physics.RaycastAll(new Vector3(worldPos.x, chosenFloor.position.y + 4f, worldPos.z), Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
                    float groundY = chosenFloor.position.y;
                    bool groundConfirmed = false;
                    for (int h = 0; h < hits.Length; h++)
                    {
                        if (hits[h].collider != null && FloorManager.IsGroundOrFloor(hits[h].collider))
                        {
                            groundY = hits[h].point.y;
                            groundConfirmed = true;
                            break;
                        }
                    }

                    if (!groundConfirmed)
                    {
                        // No floor collider directly under this candidate - reject!
                        continue;
                    }

                    worldPos.y = groundY + heightAboveFloor;

                    if (!IsFarEnoughFromOtherEnemies(worldPos) ||
                        !IsFarEnoughFromPlayer(worldPos) ||
                        !IsFarEnoughFromPickups(worldPos) ||
                        !IsFarEnoughFromPowerUps(worldPos) ||
                        IsPositionBlockedBySceneObject(worldPos))
                    {
                        continue;
                    }

                    if (!hasFallback)
                    {
                        fallbackPos = worldPos;
                        hasFallback = true;
                    }

                    if (cam == null || IsPointVisibleToCamera(worldPos, cam))
                    {
                        result = worldPos;
                        return true;
                    }
                }

                if (hasFallback)
                {
                    result = fallbackPos;
                    return true;
                }
            }
        }

        // ---------------------------------------------------------
        // STRATEGY B: Raycast Fallback (Used if FloorManager is absent or tiles not ready)
        // ---------------------------------------------------------
        for (int attempt = 0; attempt < maxSpawnAttempts; attempt++)
        {
            if (!TryGenerateCandidatePosition(out Vector3 candidate))
            {
                continue;
            }

            if (!IsFarEnoughFromOtherEnemies(candidate) ||
                !IsFarEnoughFromPlayer(candidate) ||
                !IsFarEnoughFromPickups(candidate) ||
                !IsFarEnoughFromPowerUps(candidate) ||
                IsPositionBlockedBySceneObject(candidate))
            {
                continue;
            }

            if (cam == null || IsPointVisibleToCamera(candidate, cam))
            {
                result = candidate;
                return true;
            }
        }

        result = Vector3.zero;
        return false;
    }

    /// <summary>
    /// Generates a candidate position on the floor using downward raycasting against confirmed ground colliders.
    /// Clamped within floor bounds, and rejects any position not supported by a solid floor.
    /// </summary>
    private bool TryGenerateCandidatePosition(out Vector3 candidate)
    {
        Vector3 anchor = player != null ? player.position : floorBoundsCenter;
        Vector3 forward = GetPlayerForward();

        float maxDist = floorManager != null ? Mathf.Min(maxSpawnDistanceFromPlayer, floorManager.SpawnRadius + 1f) : maxSpawnDistanceFromPlayer;
        float distance = Random.Range(minSpawnDistanceFromPlayer, maxDist);

        Vector2 direction2D;
        if (Random.value < forwardBiasChance)
        {
            float baseAngle = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float angleOffset = Random.Range(-forwardConeAngle, forwardConeAngle);
            float angleRad = (baseAngle + angleOffset) * Mathf.Deg2Rad;
            direction2D = new Vector2(Mathf.Sin(angleRad), Mathf.Cos(angleRad)) * distance;
        }
        else
        {
            direction2D = Random.insideUnitCircle.normalized * distance;
        }

        float x = anchor.x + direction2D.x;
        float z = anchor.z + direction2D.y;

        float minX = anchor.x - floorBoundsSize.x * 0.5f;
        float maxX = anchor.x + floorBoundsSize.x * 0.5f;
        float minZ = anchor.z - floorBoundsSize.z * 0.5f;
        float maxZ = anchor.z + floorBoundsSize.z * 0.5f;

        x = Mathf.Clamp(x, minX, maxX);
        z = Mathf.Clamp(z, minZ, maxZ);

        // Raycast down to find a REAL ground collider
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, 20f, z), Vector3.down, 35f, ~0, QueryTriggerInteraction.Ignore);
        bool foundFloor = false;
        float groundY = floorHeight;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider != null && FloorManager.IsGroundOrFloor(hits[i].collider))
            {
                groundY = hits[i].point.y;
                foundFloor = true;
                break;
            }
        }

        // MUST confirm a real floor collider is beneath! If not, REJECT!
        if (!foundFloor)
        {
            candidate = Vector3.zero;
            return false;
        }

        candidate = new Vector3(x, groundY + heightAboveFloor, z);
        return true;
    }

    private bool IsPositionBlockedBySceneObject(Vector3 candidate)
    {
        Collider[] hits = Physics.OverlapSphere(candidate, 0.8f, ~0, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0) return false;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i];
            if (col == null || col.isTrigger) continue;

            string colName = col.name.ToLowerInvariant();
            if (colName.StartsWith("ground") || colName.StartsWith("floor")) continue;
            if (col.CompareTag("Ground")) continue;

            // Blocked by an obstacle, rock, tree, bush, or wall
            return true;
        }

        return false;
    }

    private bool IsFarEnoughFromOtherEnemies(Vector3 candidate)
    {
        for (int i = 0; i < activeEnemies.Count; i++)
        {
            if (activeEnemies[i] == null || activeEnemies[i].Head == null) continue;
            Vector3 pos = activeEnemies[i].Head.position;
            float distSq = (new Vector2(candidate.x, candidate.z) - new Vector2(pos.x, pos.z)).sqrMagnitude;
            if (distSq < minDistanceBetweenEnemies * minDistanceBetweenEnemies)
            {
                return false;
            }
        }
        return true;
    }

    private bool IsFarEnoughFromPlayer(Vector3 candidate)
    {
        if (player == null) return true;
        Vector3 pos = player.position;
        float distSq = (new Vector2(candidate.x, candidate.z) - new Vector2(pos.x, pos.z)).sqrMagnitude;
        return distSq >= minSpawnDistanceFromPlayer * minSpawnDistanceFromPlayer;
    }

    private bool IsFarEnoughFromPickups(Vector3 candidate)
    {
        if (PickupManager.Instance != null)
        {
            var pickups = PickupManager.Instance.ActivePickups;
            if (pickups != null)
            {
                for (int i = 0; i < pickups.Count; i++)
                {
                    if (pickups[i] == null) continue;
                    Vector3 pos = pickups[i].transform.position;
                    float distSq = (new Vector2(candidate.x, candidate.z) - new Vector2(pos.x, pos.z)).sqrMagnitude;
                    if (distSq < minDistanceFromPickups * minDistanceFromPickups)
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    private bool IsFarEnoughFromPowerUps(Vector3 candidate)
    {
        if (PowerUpManager.Instance != null && PowerUpManager.Instance.ActivePowerUps != null)
        {
            var powerUps = PowerUpManager.Instance.ActivePowerUps;
            for (int i = 0; i < powerUps.Count; i++)
            {
                if (powerUps[i] == null) continue;
                Vector3 pos = powerUps[i].transform.position;
                float distSq = (new Vector2(candidate.x, candidate.z) - new Vector2(pos.x, pos.z)).sqrMagnitude;
                if (distSq < minDistanceFromPickups * minDistanceFromPickups)
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Configures PhysX to ignore all collisions between the specified enemy's colliders
    /// and any active power-up collectibles in the scene.
    /// </summary>
    public static void IgnoreCollisionsWithPowerUps(EnemiesLogic enemy)
    {
        if (enemy == null) return;
        Collider[] enemyColliders = enemy.GetComponentsInChildren<Collider>(true);
        PowerUp[] allPowerUps = FindObjectsOfType<PowerUp>();
        for (int p = 0; p < allPowerUps.Length; p++)
        {
            if (allPowerUps[p] == null) continue;
            Collider[] puColliders = allPowerUps[p].GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < puColliders.Length; i++)
            {
                if (puColliders[i] == null) continue;
                for (int j = 0; j < enemyColliders.Length; j++)
                {
                    if (enemyColliders[j] == null) continue;
                    Physics.IgnoreCollision(puColliders[i], enemyColliders[j], true);
                }
            }
        }
    }

    private bool IsPointVisibleToCamera(Vector3 point, Camera cam)
    {
        if (cam == null) return true;
        Vector3 viewportPoint = cam.WorldToViewportPoint(point);
        return viewportPoint.z > 0f
            && viewportPoint.x >= 0f && viewportPoint.x <= 1f
            && viewportPoint.y >= 0f && viewportPoint.y <= 1f;
    }

    private Vector3 GetPlayerForward()
    {
        if (playerMovement != null && playerMovement.Head != null)
        {
            Vector3 fwd = playerMovement.Head.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.001f) return fwd.normalized;
        }

        if (player != null)
        {
            Vector3 fwd = player.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.001f) return fwd.normalized;
        }

        return Vector3.forward;
    }

    /// <summary>
    /// Chooses a random head value using progression distributions or uniform selection.
    /// </summary>
    public int GetRandomHeadValue()
    {
        if (possibleHeadValues == null || possibleHeadValues.Length == 0)
        {
            possibleHeadValues = new int[] { 2, 4, 8, 16 };
        }

        if (!useProgressionDistribution || tierDistributions == null || tierDistributions.Count == 0)
        {
            return possibleHeadValues[Random.Range(0, possibleHeadValues.Length)];
        }

        int playerHeadVal = GetPlayerHeadValue();
        HeadTierDistribution tier = GetDistributionForHead(playerHeadVal);

        List<float> weights = new List<float>(possibleHeadValues.Length);
        float totalWeight = 0f;

        for (int i = 0; i < possibleHeadValues.Length; i++)
        {
            float w = tier.GetWeight(possibleHeadValues[i]);
            weights.Add(w);
            totalWeight += w;
        }

        if (totalWeight <= 0.0001f)
        {
            return possibleHeadValues[Random.Range(0, possibleHeadValues.Length)];
        }

        float roll = Random.value * totalWeight;
        float cumulative = 0f;

        for (int i = 0; i < possibleHeadValues.Length; i++)
        {
            cumulative += weights[i];
            if (roll <= cumulative)
            {
                return possibleHeadValues[i];
            }
        }

        return possibleHeadValues[0];
    }

    private HeadTierDistribution GetDistributionForHead(int headValue)
    {
        if (tierDistributions == null || tierDistributions.Count == 0)
        {
            return new HeadTierDistribution(headValue, 60f, 30f, 10f, 0f);
        }

        HeadTierDistribution bestMatch = tierDistributions[0];
        foreach (HeadTierDistribution tier in tierDistributions)
        {
            if (headValue >= tier.headValue)
            {
                bestMatch = tier;
            }
        }

        return bestMatch;
    }

    private int GetPlayerHeadValue()
    {
        if (playerSnake != null && playerSnake.HeadSegment != null)
        {
            body b = playerSnake.HeadSegment.GetComponent<body>();
            if (b != null) return b.Value;
        }

        if (playerMovement != null && playerMovement.Head != null)
        {
            body b = playerMovement.Head.GetComponent<body>();
            if (b != null) return b.Value;
        }

        return 2;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 anchor = player != null ? player.position : floorBoundsCenter;

        Gizmos.color = Color.red;
        Vector3 boundsCenter = new Vector3(anchor.x, floorHeight, anchor.z);
        Vector3 boundsSize = new Vector3(floorBoundsSize.x, 0.05f, floorBoundsSize.z);
        Gizmos.DrawWireCube(boundsCenter, boundsSize);

        Gizmos.color = Color.magenta;
        DrawWireCircle(anchor, minSpawnDistanceFromPlayer);
        DrawWireCircle(anchor, maxSpawnDistanceFromPlayer);
    }

    private void DrawWireCircle(Vector3 center, float radius, int segments = 32)
    {
        float angleStep = 360f / segments;
        Vector3 prevPoint = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = angleStep * i * Mathf.Deg2Rad;
            Vector3 nextPoint = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }
    }
}
