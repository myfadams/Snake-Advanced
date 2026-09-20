using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to: Environment/PickupManager
/// Spawns and maintains a configured number of Pickup instances near the player,
/// occasionally placing some outside the camera's current view, assigning each one
/// a value obtained from GameManager (never a locally defined list).
///
/// Values are weighted toward the Head's current value rather than picked uniformly:
/// values many power-of-2 tiers away from the Head are rarer, and that bias fades
/// to fully uniform once the Head's value reaches the highest value GameManager
/// currently defines - see PickWeightedByHeadValue().
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

    [Header("Spawn Distance From Player")]
    [Tooltip("Closest a pickup is allowed to spawn to the player.")]
    [SerializeField] private float minSpawnDistanceFromPlayer = 3f;

    [Tooltip("Farthest a pickup is allowed to spawn from the player.")]
    [SerializeField] private float maxSpawnDistanceFromPlayer = 15f;

    [Header("Camera Visibility")]
    [Tooltip("Camera used to check what's currently on screen. Leave empty to use Camera.main.")]
    [SerializeField] private Camera gameplayCamera;

    [Range(0f, 1f)]
    [Tooltip("Chance that a given spawn deliberately targets a position outside the camera's current view, " +
             "rather than not caring about visibility at all.")]
    [SerializeField] private float outOfViewSpawnChance = 0.3f;

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

    [Header("Spawn Rules")]
    [Tooltip("How many pickups should be active at once (minimum 10). " +
             "Replacements spawn automatically as pickups are collected or time out off-screen.")]
    [SerializeField] private int desiredPickupCount = 10;

    [Tooltip("Minimum horizontal distance required between two pickups.")]
    [SerializeField] private float minDistanceBetweenPickups = 2f;

    [Tooltip("How many candidate positions to try per pickup before giving up for this frame.")]
    [SerializeField] private int maxSpawnAttemptsPerPickup = 30;

    [Header("Head-Relative Value Weighting")]
    [Range(0f, 5f)]
    [Tooltip("How strongly spawn values are pulled toward the Head's current value. 0 = always uniform " +
             "across all values. Higher = values many power-of-2 tiers away from the Head become much " +
             "rarer (though never impossible). This bias fades out smoothly as the Head's value approaches " +
             "the highest value GameManager currently defines, reaching fully uniform once the Head " +
             "reaches (or exceeds) it - e.g. once the Head is 16, every defined value spawns equally often.")]
    [SerializeField] private float headValueBiasStrength = 1.5f;

    private readonly List<Pickup> activePickups = new List<Pickup>();

    // Cached from the assigned Player Transform - lets pickup values be weighted
    // toward the Head's current value. Optional: falls back to a uniform pick if
    // this isn't found.
    private PlayerMovement playerMovement;

    private void OnValidate()
    {
        if (desiredPickupCount < 10)
        {
            desiredPickupCount = 10;
        }

        if (maxSpawnDistanceFromPlayer < minSpawnDistanceFromPlayer)
        {
            maxSpawnDistanceFromPlayer = minSpawnDistanceFromPlayer;
        }

        headValueBiasStrength = Mathf.Max(headValueBiasStrength, 0f);
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
                                  "pickup values will spawn uniformly instead of being weighted toward the Head's value.");
            }
        }

        FillUpToDesiredCount();
    }

    private void Update()
    {
        // Clear out entries for pickups that were collected or timed out since last frame.
        // Whenever the count drops below the target, replacements are spawned below -
        // and once the count reaches desiredPickupCount (10 by default), spawning stops.
        activePickups.RemoveAll(pickup => pickup == null);

        if (activePickups.Count < desiredPickupCount)
        {
            FillUpToDesiredCount();
        }
    }

    private void FillUpToDesiredCount()
    {
        int amountToSpawn = desiredPickupCount - activePickups.Count;

        for (int i = 0; i < amountToSpawn; i++)
        {
            SpawnPickup();
        }
    }

    private void SpawnPickup()
    {
        if (!TryGetValidSpawnPosition(out Vector3 spawnPosition))
        {
            Debug.LogWarning("PickupManager: Couldn't find a valid spawn position this attempt; will retry next frame.");
            return;
        }

        Pickup newPickup = Instantiate(pickupPrefab, spawnPosition, Quaternion.identity, transform);

        int value = GetRandomBlockValue();
        newPickup.Initialize(value, player);

        activePickups.Add(newPickup);
    }

    private int GetRandomBlockValue()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("PickupManager: GameManager.Instance is null; defaulting value to 2.");
            return 2;
        }

        IReadOnlyList<int> values = GameManager.Instance.GetBlockValues();

        if (values == null || values.Count == 0)
        {
            Debug.LogWarning("PickupManager: GameManager returned no block values; defaulting to 2.");
            return 2;
        }

        int headValue = GetHeadValue();

        if (headValue <= 0)
        {
            // No Head value available - fall back to a plain uniform pick.
            return values[Random.Range(0, values.Count)];
        }

        return PickWeightedByHeadValue(values, headValue);
    }

    private int GetHeadValue()
    {
        if (playerMovement == null || playerMovement.Head == null)
        {
            return 0;
        }

        body headBody = playerMovement.Head.GetComponent<body>();
        return headBody != null ? headBody.Value : 0;
    }

    /// <summary>
    /// Weighted-random pick across all defined values. Each value's weight falls
    /// off with its power-of-2 tier distance from the Head's current value, so
    /// values close to the Head are common and far ones are rare but not
    /// impossible. The falloff strength itself shrinks toward zero as the Head's
    /// value approaches the highest defined value, so the pick becomes fully
    /// uniform once the Head has "outgrown" the whole defined range.
    /// </summary>
    private int PickWeightedByHeadValue(IReadOnlyList<int> values, int headValue)
    {
        int maxKnownValue = values[0];

        for (int i = 1; i < values.Count; i++)
        {
            if (values[i] > maxKnownValue)
            {
                maxKnownValue = values[i];
            }
        }

        maxKnownValue = Mathf.Max(maxKnownValue, 1);

        float headProgress = Mathf.Clamp01((float)headValue / maxKnownValue);
        float effectiveBias = headValueBiasStrength * (1f - headProgress);
        float headTier = Mathf.Log(headValue, 2f);

        float[] weights = new float[values.Count];
        float totalWeight = 0f;

        for (int i = 0; i < values.Count; i++)
        {
            float valueTier = Mathf.Log(values[i], 2f);
            float tierDistance = Mathf.Abs(headTier - valueTier);
            float weight = 1f / (1f + effectiveBias * tierDistance);

            weights[i] = weight;
            totalWeight += weight;
        }

        float roll = Random.value * totalWeight;
        float cumulative = 0f;

        for (int i = 0; i < values.Count; i++)
        {
            cumulative += weights[i];

            if (roll <= cumulative)
            {
                return values[i];
            }
        }

        // Floating point safety net - should only be reached by a hair of rounding error.
        return values[values.Count - 1];
    }

    private bool TryGetValidSpawnPosition(out Vector3 result)
    {
        // Decide once per pickup whether this particular spawn should deliberately
        // land outside the camera's current view.
        bool wantsOutOfView = Random.value < outOfViewSpawnChance;
        Camera cam = gameplayCamera != null ? gameplayCamera : Camera.main;

        for (int attempt = 0; attempt < maxSpawnAttemptsPerPickup; attempt++)
        {
            Vector3 candidate = GetRandomPositionNearPlayer();

            if (!IsFarEnoughFromOtherPickups(candidate))
            {
                continue;
            }

            if (cam == null)
            {
                // No camera to check against - accept any position with valid spacing.
                result = candidate;
                return true;
            }

            bool isVisible = IsPointVisibleToCamera(candidate, cam);

            // Only enforce "must be out of view" for the fraction of spawns we
            // specifically want hidden. Otherwise, visibility doesn't matter.
            if (wantsOutOfView && isVisible)
            {
                continue;
            }

            result = candidate;
            return true;
        }

        // Couldn't satisfy the visibility preference within the attempt budget -
        // fall back to spacing-only so the pool can still refill instead of stalling.
        Vector3 fallback = GetRandomPositionNearPlayer();
        if (IsFarEnoughFromOtherPickups(fallback))
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

        float distance = Random.Range(minSpawnDistanceFromPlayer, maxSpawnDistanceFromPlayer);
        Vector2 direction = Random.insideUnitCircle.normalized * distance;

        float x = anchor.x + direction.x;
        float z = anchor.z + direction.y;

        // Clamp relative to the player's current position (not a fixed world point),
        // so this still makes sense once the player has wandered far from the origin
        // on an endless floor. It only ever trims the occasional extreme outlier from
        // Random.insideUnitCircle - Min/Max Spawn Distance From Player already keeps
        // things close under normal circumstances.
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

            // Compare horizontal distance only, so each pickup's hover animation
            // doesn't interfere with the spacing check.
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

    // Visualizes the floor bounds (cyan) and the player's spawn ring (yellow) in the Scene view.
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