using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to: Environment/PowerUpManager (or GameManager / Scene Controller).
/// Spawns and dynamically configures Power-Up collectibles (Magnet, Rearrange, Speed)
/// across valid floor areas, matching the exact spawn rules, floor detection, and density
/// mechanics of PickupManager.
/// </summary>
public class PowerUpManager : MonoBehaviour
{
    public static PowerUpManager Instance { get; private set; }

    [Header("Power-Up Prefabs")]
    [Tooltip("Magnet Power-Up prefab (e.g. Box 1 with PowerUp component set to Magnet).")]
    [SerializeField] private PowerUp magnetPrefab;

    [Tooltip("Rearrange Power-Up prefab (e.g. Box 2 with PowerUp component set to Rearrange).")]
    [SerializeField] private PowerUp rearrangePrefab;

    [Tooltip("Speed Power-Up prefab (e.g. Box 3 with PowerUp component set to Speed).")]
    [SerializeField] private PowerUp speedPrefab;

    [Tooltip("Optional list of extra or custom power-up prefabs to include in the random spawn pool.")]
    [SerializeField] private List<PowerUp> additionalPrefabs = new List<PowerUp>();

    [Header("Density & Spawn Timing")]
    [Tooltip("Maximum number of active power-ups allowed in the scene at one time.")]
    [SerializeField] private int maxActivePowerUps = 2;

    [Tooltip("Initial delay in seconds after game start before the first power-up spawns.")]
    [SerializeField] private float initialSpawnDelay = 3f;

    [Tooltip("Delay in seconds between power-up spawn checks when below the maximum limit.")]
    [SerializeField] private float spawnInterval = 8f;

    [Tooltip("Delay in seconds after a power-up is collected before a replacement can spawn.")]
    [SerializeField] private float replacementDelay = 4f;

    [Header("Player Reference")]
    [Tooltip("The player/snake Transform to spawn power-ups near. Automatically finds the active Player with PlayerMovement if unassigned or invalid.")]
    [SerializeField] private Transform player;

    [Header("Spawn Distance & Forward Bias")]
    [Tooltip("Closest a power-up is allowed to spawn to the player.")]
    [SerializeField] private float minSpawnDistanceFromPlayer = 3.5f;

    [Tooltip("Farthest a power-up is allowed to spawn from the player.")]
    [SerializeField] private float maxSpawnDistanceFromPlayer = 12.0f;

    [Tooltip("Chance that a spawn is placed in the forward cone ahead of the snake (e.g. 0.85 = 85% in front).")]
    [Range(0f, 1f)]
    [SerializeField] private float forwardBiasChance = 0.85f;

    [Tooltip("Maximum angle (in degrees) to the left or right of forward heading for forward-biased spawns.")]
    [Range(15f, 90f)]
    [SerializeField] private float forwardConeAngle = 55f;

    [Header("Floor & Height Rules")]
    [Tooltip("FloorManager reference used to check active floor tiles. If unassigned, finds FloorManager in scene.")]
    [SerializeField] private FloorManager floorManager;

    [Header("Collection Effect (Optional Fallback)")]
    [Tooltip("Collection dissolve effect prefab passed to power-ups when collected.")]
    [SerializeField] private GameObject collectionEffectPrefab;

    public GameObject CollectionEffectPrefab => collectionEffectPrefab;

    [Tooltip("World Y position of the floor surface (synced with PickupManager if present).")]
    [SerializeField] private float floorHeight = 0f;

    [Tooltip("Spawn height above the floor surface (synced with PickupManager if present).")]
    [SerializeField] private float heightAboveFloor = 0.2f;

    [Tooltip("Width (X) and depth (Z) around the player within which spawns are clamped.")]
    [SerializeField] private Vector3 floorBoundsSize = new Vector3(40f, 0f, 40f);

    [Header("Spacing & Obstacle Checks")]
    [Tooltip("Minimum horizontal distance required between two power-ups.")]
    [SerializeField] private float minDistanceBetweenPowerUps = 3.0f;

    [Tooltip("Minimum distance from existing pickups to avoid crowding.")]
    [SerializeField] private float minDistanceFromPickups = 1.5f;

    [Tooltip("Maximum candidate positions to test per spawn attempt.")]
    [SerializeField] private int maxSpawnAttempts = 35;

    [Header("Speed Power-Up Fixed Glow Color")]
    [Tooltip("Glow color used for the Speed power-up (electric cyan / neon blue by default).")]
    [SerializeField] private Color speedGlowColor = new Color(0f, 0.85f, 1f, 1f);

    [Header("Camera & Recycling")]
    [Tooltip("Camera used to check visibility. Leave empty to use Camera.main.")]
    [SerializeField] private Camera gameplayCamera;

    // Track active power-up instances in the scene
    private readonly List<PowerUp> activePowerUps = new List<PowerUp>();
    private Coroutine spawnLoopCoroutine;
    private PlayerMovement playerMovement;

    public IReadOnlyList<PowerUp> ActivePowerUps => activePowerUps;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        EnsureReferences();
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
        EnsureReferences();
        SyncSettingsWithPickupManager();

        if (spawnLoopCoroutine == null && gameObject.activeInHierarchy)
        {
            spawnLoopCoroutine = StartCoroutine(SpawnLoopRoutine());
        }
    }

    /// <summary>
    /// Guarantees that the player reference is the true snake player (holding PlayerMovement),
    /// even if the scene had an environment or manager object mistakenly assigned in the Inspector.
    /// </summary>
    private void EnsureReferences()
    {
        bool needResolvePlayer = (player == null) || (player.GetComponent<PlayerMovement>() == null);

        if (needResolvePlayer)
        {
            // 1. Try resolving through PickupManager's player reference
            if (PickupManager.Instance != null && PickupManager.Instance.PlayerTransform != null)
            {
                player = PickupManager.Instance.PlayerTransform;
            }
            // 2. Try GameObject with "Player" tag
            if (player == null || player.GetComponent<PlayerMovement>() == null)
            {
                GameObject playerObj = GameObject.FindWithTag("Player");
                if (playerObj != null && playerObj.GetComponent<PlayerMovement>() != null)
                {
                    player = playerObj.transform;
                }
            }
            // 3. Fallback to searching scene for PlayerMovement
            if (player == null || player.GetComponent<PlayerMovement>() == null)
            {
                PlayerMovement pm = FindObjectOfType<PlayerMovement>();
                if (pm != null)
                {
                    player = pm.transform;
                }
            }
        }

        if (player != null && playerMovement == null)
        {
            playerMovement = player.GetComponent<PlayerMovement>();
        }

        if (floorManager == null)
        {
            floorManager = FindObjectOfType<FloorManager>();
        }

        if (gameplayCamera == null)
        {
            if (PickupManager.Instance != null && PickupManager.Instance.GameplayCamera != null)
            {
                gameplayCamera = PickupManager.Instance.GameplayCamera;
            }
            else
            {
                gameplayCamera = Camera.main;
            }
        }
    }

    /// <summary>
    /// Reuses PickupManager's floor height, player reference, and bounds so power-ups
    /// spawn with the identical ground alignment as pickups.
    /// </summary>
    private void SyncSettingsWithPickupManager()
    {
        if (PickupManager.Instance != null)
        {
            if (PickupManager.Instance.PlayerTransform != null)
            {
                player = PickupManager.Instance.PlayerTransform;
                playerMovement = player.GetComponent<PlayerMovement>();
            }

            floorHeight = PickupManager.Instance.FloorHeight;
            heightAboveFloor = PickupManager.Instance.HeightAboveFloor;
            floorBoundsSize = PickupManager.Instance.FloorBoundsSize;

            if (gameplayCamera == null && PickupManager.Instance.GameplayCamera != null)
            {
                gameplayCamera = PickupManager.Instance.GameplayCamera;
            }
        }
    }

    private void Update()
    {
        activePowerUps.RemoveAll(p => p == null);
        RecyclePowerUpsLeftBehind();
    }

    private void OnDisable()
    {
        if (spawnLoopCoroutine != null)
        {
            StopCoroutine(spawnLoopCoroutine);
            spawnLoopCoroutine = null;
        }
    }

    private IEnumerator SpawnLoopRoutine()
    {
        yield return new WaitForSeconds(initialSpawnDelay);

        while (true)
        {
            activePowerUps.RemoveAll(p => p == null);

            if (activePowerUps.Count < maxActivePowerUps)
            {
                TrySpawnRandomPowerUp();
            }

            yield return new WaitForSeconds(spawnInterval);
        }
    }

    public bool TrySpawnRandomPowerUp()
    {
        EnsureReferences();

        List<PowerUp> availablePrefabs = GetAvailablePrefabs();
        if (availablePrefabs.Count == 0)
        {
            Debug.LogWarning("PowerUpManager: No PowerUp prefabs assigned in Inspector.");
            return false;
        }

        if (!TryGetValidSpawnPosition(out Vector3 spawnPos))
        {
            return false;
        }

        PowerUp prefab = availablePrefabs[Random.Range(0, availablePrefabs.Count)];
        if (prefab == null) return false;

        // Instantiate at spawnPos in world space
        PowerUp instance = Instantiate(prefab, spawnPos, Quaternion.identity);
        instance.transform.position = spawnPos;

        // Parent to manager keeping world position intact
        instance.transform.SetParent(transform, true);
        instance.transform.position = spawnPos;

        ConfigureSpawnedPowerUp(instance, prefab.Type);

        activePowerUps.Add(instance);
        return true;
    }

    private List<PowerUp> GetAvailablePrefabs()
    {
        List<PowerUp> list = new List<PowerUp>();
        if (magnetPrefab != null) list.Add(magnetPrefab);
        if (rearrangePrefab != null) list.Add(rearrangePrefab);
        if (speedPrefab != null) list.Add(speedPrefab);

        foreach (var extra in additionalPrefabs)
        {
            if (extra != null) list.Add(extra);
        }

        return list;
    }

    private void ConfigureSpawnedPowerUp(PowerUp instance, PowerUpType type)
    {
        int targetVal = 2;
        Color glowColor;

        switch (type)
        {
            case PowerUpType.Magnet:
                targetVal = DetermineMagnetTargetValue();
                glowColor = GetColorForValue(targetVal);
                break;

            case PowerUpType.Rearrange:
                targetVal = DetermineRearrangeTargetValue();
                glowColor = GetColorForValue(targetVal);
                break;

            case PowerUpType.Speed:
                targetVal = 0;
                glowColor = speedGlowColor;
                break;

            default:
                glowColor = Color.white;
                break;
        }

        instance.Initialize(type, targetVal, glowColor, this);

        if (collectionEffectPrefab != null && instance.CollectionEffectPrefab == null)
        {
            instance.CollectionEffectPrefab = collectionEffectPrefab;
        }
    }

    private int DetermineMagnetTargetValue()
    {
        if (GameManager.Instance != null)
        {
            var values = GameManager.Instance.GetBlockValues();
            if (values != null && values.Count > 0)
            {
                int roll = Random.Range(0, values.Count);
                return values[roll];
            }
        }

        return 2;
    }

    private int DetermineRearrangeTargetValue()
    {
        if (SnakeGrow.Instance != null && SnakeGrow.Instance.Segments != null)
        {
            var segments = SnakeGrow.Instance.Segments;
            Dictionary<int, int> valueCounts = new Dictionary<int, int>();

            for (int i = 1; i < segments.Count; i++)
            {
                if (segments[i] == null) continue;
                body b = segments[i].GetComponent<body>();
                if (b != null)
                {
                    if (!valueCounts.ContainsKey(b.Value))
                        valueCounts[b.Value] = 0;
                    valueCounts[b.Value]++;
                }
            }

            List<int> multiCountValues = new List<int>();
            foreach (var kvp in valueCounts)
            {
                if (kvp.Value >= 2)
                {
                    multiCountValues.Add(kvp.Key);
                }
            }

            if (multiCountValues.Count > 0)
            {
                return multiCountValues[Random.Range(0, multiCountValues.Count)];
            }

            if (valueCounts.Count > 0)
            {
                List<int> allValues = new List<int>(valueCounts.Keys);
                return allValues[Random.Range(0, allValues.Count)];
            }
        }

        return DetermineMagnetTargetValue();
    }

    private Color GetColorForValue(int value)
    {
        if (GameManager.Instance != null)
        {
            return GameManager.Instance.GetBlockColor(value);
        }

        return body.GetColorForValue(value);
    }

    /// <summary>
    /// Matches PickupManager's candidate position generation:
    /// Follows the player's real world position and forward heading,
    /// verifies the candidate is directly on an active floor surface,
    /// checks obstacle clearance, and prefers camera-visible positions.
    /// </summary>
    private bool TryGetValidSpawnPosition(out Vector3 result)
    {
        EnsureReferences();

        Camera cam = gameplayCamera != null ? gameplayCamera : Camera.main;
        Vector3 fallback = Vector3.zero;
        bool hasFallback = false;

        for (int attempt = 0; attempt < maxSpawnAttempts; attempt++)
        {
            Vector3 candidate = GetRandomPositionNearPlayer();

            // 1. Verify candidate is over an active floor tile and determine exact floor surface height
            if (!IsOverFloor(candidate, out float surfaceY))
            {
                continue;
            }

            candidate.y = surfaceY + heightAboveFloor;

            // 2. Verify candidate is clear of scene props, trees, rocks, chess pieces, and player
            if (IsPositionBlockedBySceneObject(candidate))
            {
                continue;
            }

            // 3. Verify spacing against other power-ups and pickups
            if (!IsFarEnoughFromOthers(candidate))
            {
                continue;
            }

            if (!hasFallback)
            {
                fallback = candidate;
                hasFallback = true;
            }

            if (cam == null)
            {
                result = candidate;
                return true;
            }

            // Prefer positions visible to the camera in front of the player (identical to PickupManager)
            if (IsPointVisibleToCamera(candidate, cam))
            {
                result = candidate;
                return true;
            }
        }

        if (hasFallback)
        {
            result = fallback;
            return true;
        }

        result = Vector3.zero;
        return false;
    }

    /// <summary>
    /// Generates candidate coordinates centered on the player's snake and biased into the forward cone.
    /// Clamps within safety bounds that follow the player along the endless world.
    /// </summary>
    private Vector3 GetRandomPositionNearPlayer()
    {
        Vector3 anchor = player != null ? player.position : Vector3.zero;
        Vector3 forward = GetPlayerForward();

        float distance = Random.Range(minSpawnDistanceFromPlayer, maxSpawnDistanceFromPlayer);

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

        // Safety clamp follows the player
        float minX = anchor.x - floorBoundsSize.x * 0.5f;
        float maxX = anchor.x + floorBoundsSize.x * 0.5f;
        float minZ = anchor.z - floorBoundsSize.z * 0.5f;
        float maxZ = anchor.z + floorBoundsSize.z * 0.5f;

        x = Mathf.Clamp(x, minX, maxX);
        z = Mathf.Clamp(z, minZ, maxZ);

        float y = floorHeight + heightAboveFloor;

        return new Vector3(x, y, z);
    }

    /// <summary>
    /// Verifies that a candidate is directly situated above a valid floor surface.
    /// Casts downward to measure the exact ground Y coordinate.
    /// </summary>
    private bool IsOverFloor(Vector3 candidate, out float exactFloorY)
    {
        exactFloorY = floorHeight;

        // 1. Raycast downward from above candidate to hit the ground/floor collider
        Vector3 rayOrigin = new Vector3(candidate.x, candidate.y + 4.0f, candidate.z);
        RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, 8.0f, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null) continue;

            if (FloorManager.IsGroundOrFloor(col))
            {
                exactFloorY = hits[i].point.y;
                return true;
            }
        }

        // 2. Check if candidate lies within an active floor tile from FloorManager
        if (floorManager != null)
        {
            var activeFloors = floorManager.GetActiveFloors();
            if (activeFloors != null)
            {
                float halfTile = floorManager.FloorTileSize * 0.5f;
                foreach (Transform tile in activeFloors)
                {
                    if (tile == null) continue;
                    Vector3 tilePos = tile.position;
                    if (Mathf.Abs(candidate.x - tilePos.x) <= halfTile && Mathf.Abs(candidate.z - tilePos.z) <= halfTile)
                    {
                        exactFloorY = tilePos.y;
                        return true;
                    }
                }
            }
        }

        // 3. Fallback: if candidate is visible to camera in front of the player, accept at floorHeight
        Camera cam = gameplayCamera != null ? gameplayCamera : Camera.main;
        if (cam != null && IsPointVisibleToCamera(candidate, cam))
        {
            exactFloorY = floorHeight;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether a candidate spawn position is already occupied by an environment prop,
    /// player body segment, animal, or scene obstacle (identical to PickupManager's check).
    /// </summary>
    private bool IsPositionBlockedBySceneObject(Vector3 candidate)
    {
        Collider[] hits = Physics.OverlapSphere(candidate, 0.7f, ~0, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0) return false;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i];
            if (col == null) continue;

            string colName = col.name.ToLowerInvariant();
            if (colName.StartsWith("ground") || colName.StartsWith("floor")) continue;
            if (col.CompareTag("Ground")) continue;

            return true;
        }

        return false;
    }

    private bool IsFarEnoughFromOthers(Vector3 candidate)
    {
        // Distance check against existing power-ups
        for (int i = 0; i < activePowerUps.Count; i++)
        {
            PowerUp p = activePowerUps[i];
            if (p == null) continue;

            Vector3 diff = candidate - p.transform.position;
            diff.y = 0f;
            if (diff.sqrMagnitude < minDistanceBetweenPowerUps * minDistanceBetweenPowerUps)
            {
                return false;
            }
        }

        // Distance check against existing pickups
        if (PickupManager.Instance != null && PickupManager.Instance.ActivePickups != null)
        {
            var pickups = PickupManager.Instance.ActivePickups;
            for (int i = 0; i < pickups.Count; i++)
            {
                Pickup pickup = pickups[i];
                if (pickup == null) continue;

                Vector3 diff = candidate - pickup.transform.position;
                diff.y = 0f;
                if (diff.sqrMagnitude < minDistanceFromPickups * minDistanceFromPickups)
                {
                    return false;
                }
            }
        }

        // Distance check against active enemies
        if (EnemyManager.Instance != null && EnemyManager.Instance.ActiveEnemies != null)
        {
            var enemies = EnemyManager.Instance.ActiveEnemies;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemiesLogic enemy = enemies[i];
                if (enemy == null || enemy.Head == null) continue;

                Vector3 diff = candidate - enemy.Head.position;
                diff.y = 0f;
                if (diff.sqrMagnitude < 2.5f * 2.5f)
                {
                    return false;
                }
            }
        }

        return true;
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

    private void RecyclePowerUpsLeftBehind()
    {
        if (player == null || activePowerUps.Count == 0) return;

        Vector3 playerPos = player.position;
        Vector3 forward = GetPlayerForward();
        Camera cam = gameplayCamera != null ? gameplayCamera : Camera.main;

        for (int i = activePowerUps.Count - 1; i >= 0; i--)
        {
            PowerUp p = activePowerUps[i];
            if (p == null) continue;

            Vector3 toPowerUp = p.transform.position - playerPos;
            toPowerUp.y = 0f;

            float dot = Vector3.Dot(toPowerUp, forward);

            if (dot < -4.5f || toPowerUp.sqrMagnitude > maxSpawnDistanceFromPlayer * maxSpawnDistanceFromPlayer * 2.25f)
            {
                bool isVisible = cam != null && IsPointVisibleToCamera(p.transform.position, cam);
                if (!isVisible)
                {
                    Destroy(p.gameObject);
                    activePowerUps.RemoveAt(i);
                }
            }
        }
    }

    private bool IsPointVisibleToCamera(Vector3 point, Camera cam)
    {
        Vector3 vp = cam.WorldToViewportPoint(point);
        return vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
    }

    public void OnPowerUpCollected(PowerUp powerUp)
    {
        activePowerUps.Remove(powerUp);
        StartCoroutine(ReplacementDelayRoutine());
    }

    public void OnPowerUpDespawned(PowerUp powerUp)
    {
        activePowerUps.Remove(powerUp);
    }

    private IEnumerator ReplacementDelayRoutine()
    {
        yield return new WaitForSeconds(replacementDelay);

        if (activePowerUps.Count < maxActivePowerUps)
        {
            TrySpawnRandomPowerUp();
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 anchor = player != null ? player.position : Vector3.zero;

        Gizmos.color = new Color(0f, 0.8f, 1f, 0.4f);
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
