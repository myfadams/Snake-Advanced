using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to: Environment/PickupManager
/// Spawns and maintains a configured number of Pickup instances near the player,
/// occasionally placing some outside the camera's current view, assigning each one
/// a value obtained from GameManager (never a locally defined list).
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

    private readonly List<Pickup> activePickups = new List<Pickup>();

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
        int randomIndex = Random.Range(0, values.Count);
        return values[randomIndex];
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