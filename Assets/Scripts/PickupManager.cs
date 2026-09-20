using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines the probability distribution for pickup block values (2, 4, 8, 16)
/// given a specific Head value tier.
/// </summary>
[System.Serializable]
public class HeadTierDistribution
{
    [Tooltip("The Head value this distribution tier targets (e.g. 2, 4, 8, 16).")]
    public int headValue = 2;

    [Tooltip("Probability weight for block value 2.")]
    [Range(0f, 100f)] public float weightFor2 = 65f;

    [Tooltip("Probability weight for block value 4.")]
    [Range(0f, 100f)] public float weightFor4 = 25f;

    [Tooltip("Probability weight for block value 8.")]
    [Range(0f, 100f)] public float weightFor8 = 10f;

    [Tooltip("Probability weight for block value 16.")]
    [Range(0f, 100f)] public float weightFor16 = 0f;

    public HeadTierDistribution() { }

    public HeadTierDistribution(int headVal, float w2, float w4, float w8, float w16)
    {
        headValue = headVal;
        weightFor2 = w2;
        weightFor4 = w4;
        weightFor8 = w8;
        weightFor16 = w16;
    }

    public float GetWeight(int value)
    {
        switch (value)
        {
            case 2: return weightFor2;
            case 4: return weightFor4;
            case 8: return weightFor8;
            case 16: return weightFor16;
            default: return 0f;
        }
    }
}

/// <summary>
/// Attach to: Environment/PickupManager
/// Maintains a controlled density of active pickups near the player (5 to 8 by default)
/// and manages a balanced, progression-aware value selection system based on Head value.
/// Spawns replacements when pickups are collected or despawned off-screen, and prevents
/// repetitive same-value spawns using consecutive tracking.
/// </summary>
public class PickupManager : MonoBehaviour
{
    [Header("Prefab")]
    [Tooltip("The Pickup prefab to instantiate.")]
    [SerializeField] private Pickup pickupPrefab;

    [Header("Player Reference")]
    [Tooltip("The player/snake transform pickups should spawn near. If left empty, " +
             "Floor Bounds Center below is used as a fallback anchor point.")]
    [SerializeField] private Transform player;

    [Header("Pickup Density")]
    [Tooltip("Minimum number of active pickups in the play area. If the count drops below this, spawning prioritizes refilling.")]
    [SerializeField] private int minActivePickups = 6;

    [Tooltip("Maximum number of active pickups allowed in the play area at any one time.")]
    [SerializeField] private int maxActivePickups = 8;

    [Header("Spawn Distance & Forward Bias")]
    [Tooltip("Closest a pickup is allowed to spawn to the player.")]
    [SerializeField] private float minSpawnDistanceFromPlayer = 2.5f;

    [Tooltip("Farthest a pickup is allowed to spawn from the player.")]
    [SerializeField] private float maxSpawnDistanceFromPlayer = 7f;

    [Tooltip("Chance that a spawn is placed in the forward cone ahead of the snake so pickups appear along the player's path (e.g. 0.85 = 85% in front).")]
    [Range(0f, 1f)]
    [SerializeField] private float forwardBiasChance = 0.85f;

    [Tooltip("Maximum angle (in degrees) to the left or right of the forward direction for forward-biased spawns.")]
    [Range(15f, 90f)]
    [SerializeField] private float forwardConeAngle = 55f;

    [Header("Camera Visibility")]
    [Tooltip("Camera used to check what's currently on screen. Leave empty to use Camera.main.")]
    [SerializeField] private Camera gameplayCamera;

    [Range(0f, 1f)]
    [Tooltip("Chance that a given spawn deliberately targets a position outside the camera's current view (0 = prefer in-view ahead of player).")]
    [SerializeField] private float outOfViewSpawnChance = 0f;

    [Header("Floor Bounds (safety clamp)")]
    [Tooltip("Fallback anchor point (Y is ignored), used only if Player is not assigned. " +
             "With Player assigned, the clamp below follows the player instead of this fixed point - " +
             "important for an endless floor (see FloorManager), where a fixed world position would " +
             "eventually be far from wherever the player has wandered to.")]
    [SerializeField] private Vector3 floorBoundsCenter = Vector3.zero;

    [Tooltip("Width (X) and depth (Z) of the area, centered on the player (or the fallback anchor above), " +
             "that spawns are clamped into. Should comfortably cover Max Spawn Distance From Player.")]
    [SerializeField] private Vector3 floorBoundsSize = new Vector3(40f, 0f, 40f);

    [Header("Spawn Height")]
    [Tooltip("World Y position of the floor surface.")]
    [SerializeField] private float floorHeight = 0f;

    [Tooltip("How far above the floor surface pickups should spawn.")]
    [SerializeField] private float heightAboveFloor = 0.5f;

    [Header("Spawn Spacing & Rules")]
    [Tooltip("Minimum horizontal distance required between two pickups to prevent crowding or stacking.")]
    [SerializeField] private float minDistanceBetweenPickups = 1.8f;

    [Tooltip("How many candidate positions to try per pickup before giving up for this frame.")]
    [SerializeField] private int maxSpawnAttemptsPerPickup = 30;

    [Header("Progression Probability Distributions")]
    [Tooltip("Probability distributions for block values per Head tier. Tiers should be listed in ascending order.")]
    [SerializeField] private List<HeadTierDistribution> tierDistributions = new List<HeadTierDistribution>
    {
        new HeadTierDistribution(2, 65f, 25f, 10f, 0f),
        new HeadTierDistribution(4, 50f, 30f, 20f, 0f),
        new HeadTierDistribution(8, 40f, 30f, 20f, 10f),
        new HeadTierDistribution(16, 25f, 35f, 25f, 15f)
    };

    [Header("Anti-Repetition Settings")]
    [Tooltip("Maximum consecutive times the same block value can spawn before its probability is cut to zero.")]
    [SerializeField] private int maxConsecutiveSameValue = 2;

    [Tooltip("Penalty multiplier applied to a value's weight if it has already spawned consecutively (e.g. 0.35 reduces weight by 65%).")]
    [Range(0.05f, 0.9f)]
    [SerializeField] private float consecutiveRepetitionPenalty = 0.35f;

    [Tooltip("Multiplier applied to the next higher block value when a lower value spawns consecutively (e.g. boosting 4 when multiple 2s spawn).")]
    [Range(1f, 3f)]
    [SerializeField] private float consecutiveUpgradeBoost = 1.8f;

    private readonly List<Pickup> activePickups = new List<Pickup>();
    private PlayerMovement playerMovement;

    // Repetition tracking
    private int lastSpawnedValue = 0;
    private int consecutiveSameValueCount = 0;

    // Track count from previous frame to detect pickup collection/despawning
    private int previousActiveCount = 0;

    private void OnValidate()
    {
        minActivePickups = Mathf.Max(minActivePickups, 1);
        if (maxActivePickups < minActivePickups)
        {
            maxActivePickups = minActivePickups;
        }

        if (maxSpawnDistanceFromPlayer < minSpawnDistanceFromPlayer)
        {
            maxSpawnDistanceFromPlayer = minSpawnDistanceFromPlayer;
        }

        minDistanceBetweenPickups = Mathf.Max(minDistanceBetweenPickups, 0.5f);
        maxConsecutiveSameValue = Mathf.Max(maxConsecutiveSameValue, 1);
    }

    private void Start()
    {
        if (pickupPrefab == null)
        {
            Debug.LogError("PickupManager: Pickup Prefab is not assigned in the Inspector.");
            enabled = false;
            return;
        }

        if (GameManager.Instance == null)
        {
            Debug.LogWarning("PickupManager: GameManager.Instance was not found at Start(). " +
                              "Make sure GameManager exists and runs its Awake() before this.");
        }

        if (player != null)
        {
            playerMovement = player.GetComponent<PlayerMovement>();
            if (playerMovement == null)
            {
                Debug.LogWarning("PickupManager: no PlayerMovement found on the assigned Player Transform; " +
                                  "pickup values will use Head tier 2 as default.");
            }
        }

        // Spawn initial pickups up to maxActivePickups
        RefillPickups(maxActivePickups);
        previousActiveCount = activePickups.Count;
    }

    private void Update()
    {
        // Clear out references to pickups that were collected or despawned off-screen
        activePickups.RemoveAll(pickup => pickup == null);

        // Recycle pickups left far behind the player (out of view) so their slots spawn ahead instead
        RecyclePickupsLeftBehind();

        // 1. Maintain minimum density: if below minActivePickups, replenish immediately
        if (activePickups.Count < minActivePickups)
        {
            int needed = minActivePickups - activePickups.Count;
            for (int i = 0; i < needed; i++)
            {
                if (!SpawnPickup())
                    break;
            }
        }
        // 2. Replacement on collection/despawn: if count decreased since previous frame and is below max, spawn replacement
        else if (activePickups.Count < previousActiveCount && activePickups.Count < maxActivePickups)
        {
            int lost = previousActiveCount - activePickups.Count;
            for (int i = 0; i < lost && activePickups.Count < maxActivePickups; i++)
            {
                if (!SpawnPickup())
                    break;
            }
        }

        previousActiveCount = activePickups.Count;
    }

    /// <summary>
    /// Checks for active pickups that have been left far behind the player and are no longer
    /// visible, destroying them so they can be immediately re-spawned ahead of the player.
    /// </summary>
    private void RecyclePickupsLeftBehind()
    {
        if (player == null || activePickups.Count <= minActivePickups)
            return;

        Vector3 playerPos = player.position;
        Vector3 forward = GetPlayerForward();
        Camera cam = gameplayCamera != null ? gameplayCamera : Camera.main;

        for (int i = activePickups.Count - 1; i >= 0; i--)
        {
            Pickup p = activePickups[i];
            if (p == null) continue;

            Vector3 toPickup = p.transform.position - playerPos;
            toPickup.y = 0f;

            float dot = Vector3.Dot(toPickup, forward);

            // If pickup is more than 3.5m behind the player or more than 1.5x max distance away
            if (dot < -3.5f || toPickup.sqrMagnitude > maxSpawnDistanceFromPlayer * maxSpawnDistanceFromPlayer * 2.25f)
            {
                bool isVisible = cam != null && IsPointVisibleToCamera(p.transform.position, cam);
                if (!isVisible)
                {
                    Destroy(p.gameObject);
                    activePickups.RemoveAt(i);
                }
            }
        }
    }

    private void RefillPickups(int targetCount)
    {
        int amountToSpawn = targetCount - activePickups.Count;
        for (int i = 0; i < amountToSpawn; i++)
        {
            if (!SpawnPickup())
                break;
        }
    }

    private bool SpawnPickup()
    {
        if (!TryGetValidSpawnPosition(out Vector3 spawnPosition))
        {
            return false;
        }

        Pickup newPickup = Instantiate(pickupPrefab, spawnPosition, Quaternion.identity, transform);

        int value = GetProgressionBlockValue();
        newPickup.Initialize(value, player);

        activePickups.Add(newPickup);
        return true;
    }

    /// <summary>
    /// Chooses a pickup value using the Head-dependent probability distribution,
    /// GameManager supported values, and anti-repetition adjustments.
    /// </summary>
    private int GetProgressionBlockValue()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("PickupManager: GameManager.Instance is null; defaulting value to 2.");
            return 2;
        }

        IReadOnlyList<int> supportedValues = GameManager.Instance.GetBlockValues();
        if (supportedValues == null || supportedValues.Count == 0)
        {
            Debug.LogWarning("PickupManager: GameManager returned no block values; defaulting to 2.");
            return 2;
        }

        int headValue = GetHeadValue();
        HeadTierDistribution tier = GetDistributionForHead(headValue);

        List<float> weights = new List<float>(supportedValues.Count);
        float totalWeight = 0f;

        for (int i = 0; i < supportedValues.Count; i++)
        {
            int val = supportedValues[i];
            float w = tier.GetWeight(val);

            // Anti-repetition logic:
            if (val == lastSpawnedValue && consecutiveSameValueCount >= 1)
            {
                if (consecutiveSameValueCount >= maxConsecutiveSameValue)
                {
                    // Strict limit reached: cut weight to zero to force variation
                    w = 0f;
                }
                else
                {
                    // Repeated spawn: apply repetition penalty
                    w *= consecutiveRepetitionPenalty;
                }
            }
            else if (lastSpawnedValue > 0 && consecutiveSameValueCount >= 1)
            {
                // When a lower value has spawned consecutively, boost the next tier
                if (lastSpawnedValue == 2 && val == 4)
                {
                    w *= consecutiveUpgradeBoost;
                }
                else if (lastSpawnedValue == 4 && val == 8)
                {
                    w *= consecutiveUpgradeBoost;
                }
                else if (lastSpawnedValue == 8 && val == 16)
                {
                    w *= consecutiveUpgradeBoost;
                }
            }

            weights.Add(w);
            totalWeight += w;
        }

        // Safety fallback if all weights were zeroed
        if (totalWeight <= 0.0001f)
        {
            totalWeight = 0f;
            for (int i = 0; i < supportedValues.Count; i++)
            {
                float w = (supportedValues[i] == lastSpawnedValue) ? 0f : 1f;
                weights[i] = w;
                totalWeight += w;
            }

            if (totalWeight <= 0.0001f)
            {
                weights[0] = 1f;
                totalWeight = 1f;
            }
        }

        // Weighted random selection
        float roll = Random.value * totalWeight;
        float cumulative = 0f;
        int chosenValue = supportedValues[0];

        for (int i = 0; i < supportedValues.Count; i++)
        {
            cumulative += weights[i];
            if (roll <= cumulative)
            {
                chosenValue = supportedValues[i];
                break;
            }
        }

        // Update repetition tracking
        if (chosenValue == lastSpawnedValue)
        {
            consecutiveSameValueCount++;
        }
        else
        {
            lastSpawnedValue = chosenValue;
            consecutiveSameValueCount = 1;
        }

        return chosenValue;
    }

    private HeadTierDistribution GetDistributionForHead(int headValue)
    {
        if (tierDistributions == null || tierDistributions.Count == 0)
        {
            return new HeadTierDistribution(headValue, 50f, 30f, 20f, 0f);
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

    private int GetHeadValue()
    {
        if (playerMovement == null && player != null)
        {
            playerMovement = player.GetComponent<PlayerMovement>();
        }

        if (playerMovement == null || playerMovement.Head == null)
        {
            return 2;
        }

        body headBody = playerMovement.Head.GetComponent<body>();
        return headBody != null ? headBody.Value : 2;
    }

    private Vector3 GetPlayerForward()
    {
        if (playerMovement != null && playerMovement.Head != null)
        {
            Vector3 fwd = playerMovement.Head.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.001f)
            {
                return fwd.normalized;
            }
        }

        if (player != null)
        {
            Vector3 fwd = player.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.001f)
            {
                return fwd.normalized;
            }
        }

        return Vector3.forward;
    }

    private bool TryGetValidSpawnPosition(out Vector3 result)
    {
        Camera cam = gameplayCamera != null ? gameplayCamera : Camera.main;

        Vector3 fallback = Vector3.zero;
        bool hasFallback = false;

        for (int attempt = 0; attempt < maxSpawnAttemptsPerPickup; attempt++)
        {
            Vector3 candidate = GetRandomPositionNearPlayer();

            if (!IsFarEnoughFromOtherPickups(candidate))
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

            bool isVisible = IsPointVisibleToCamera(candidate, cam);

            // Respect outOfViewSpawnChance only if explicitly configured > 0
            if (outOfViewSpawnChance > 0f && Random.value < outOfViewSpawnChance)
            {
                if (!isVisible)
                {
                    result = candidate;
                    return true;
                }
                continue;
            }

            // Prefer positions visible to the player
            if (isVisible)
            {
                result = candidate;
                return true;
            }
        }

        // If no ideal candidate matched, use the first validly-spaced fallback position
        if (hasFallback)
        {
            result = fallback;
            return true;
        }

        result = Vector3.zero;
        return false;
    }

    private Vector3 GetRandomPositionNearPlayer()
    {
        Vector3 anchor = player != null ? player.position : floorBoundsCenter;
        Vector3 forward = GetPlayerForward();

        float distance = Random.Range(minSpawnDistanceFromPlayer, maxSpawnDistanceFromPlayer);

        Vector2 direction2D;
        if (Random.value < forwardBiasChance)
        {
            // Pick an angle within the forward cone (e.g. +/- 55 degrees from the snake's forward heading)
            float baseAngle = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float angleOffset = Random.Range(-forwardConeAngle, forwardConeAngle);
            float angleRad = (baseAngle + angleOffset) * Mathf.Deg2Rad;
            direction2D = new Vector2(Mathf.Sin(angleRad), Mathf.Cos(angleRad)) * distance;
        }
        else
        {
            // Full 360-degree distribution for occasional side/flank variety
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

        float y = floorHeight + heightAboveFloor;

        return new Vector3(x, y, z);
    }

    private bool IsPointVisibleToCamera(Vector3 point, Camera cam)
    {
        Vector3 viewportPoint = cam.WorldToViewportPoint(point);

        return viewportPoint.z > 0f
            && viewportPoint.x >= 0f && viewportPoint.x <= 1f
            && viewportPoint.y >= 0f && viewportPoint.y <= 1f;
    }

    private bool IsFarEnoughFromOtherPickups(Vector3 candidate)
    {
        foreach (Pickup pickup in activePickups)
        {
            if (pickup == null)
            {
                continue;
            }

            Vector3 otherPos = pickup.transform.position;
            float horizontalDistance = Vector2.Distance(
                new Vector2(candidate.x, candidate.z),
                new Vector2(otherPos.x, otherPos.z));

            if (horizontalDistance < minDistanceBetweenPickups)
            {
                return false;
            }
        }

        return true;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 anchor = player != null ? player.position : floorBoundsCenter;

        Gizmos.color = Color.cyan;
        Vector3 boundsCenter = new Vector3(anchor.x, floorHeight, anchor.z);
        Vector3 boundsSize = new Vector3(floorBoundsSize.x, 0.01f, floorBoundsSize.z);
        Gizmos.DrawWireCube(boundsCenter, boundsSize);
        Gizmos.color = Color.yellow;
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