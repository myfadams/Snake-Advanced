using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Endless-world floor grid using spawn/despawn radii instead of a fixed pool.
///
/// A tile is kept alive while it is within Spawn Radius (View Distance + Spawn
/// Buffer) of the reference and/or camera. A tile is only ever DESTROYED once
/// it falls beyond Despawn Radius (Spawn Radius + Despawn Buffer) -- i.e. once
/// it is not just out of view, but far enough out that it has no realistic
/// chance of being needed again soon. Anything between the two radii is left
/// alone; that gap is what stops tiles being destroyed and immediately
/// re-created as the reference jitters back and forth near a boundary.
///
/// Reuse still happens first: a tile that needs to disappear from one cell and
/// a cell that needs a tile are paired up and the tile is simply repositioned.
/// Destroy only fires for genuine surplus -- tiles beyond Despawn Radius for
/// which there is no missing cell to reuse them into. This keeps the tile
/// count bounded by what's actually near the camera, which matters here since
/// each Floor is four large Ground meshes.
///
/// Attach to the "Environment" GameObject.
/// Grid convention: Vector2Int(x, y) -> world (X, Z). Y is floor height.
/// </summary>
[DisallowMultipleComponent]
public class FloorManager : MonoBehaviour
{
    [Header("Floor Prefab")]
    [SerializeField] private GameObject floorPrefab;

    [Header("Reference")]
    [Tooltip("Center of the active area (player, vehicle, character root...).")]
    [SerializeField] private Transform referenceTransform;

    [Tooltip("Camera that looks at the world. Included so a chase camera behind/above the reference still has floor under its view. Leave empty to use Camera.main.")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private bool includeCamera = true;

    [Header("Tile")]
    [Tooltip("Exact world-space width/depth of one complete Floor prefab. If this doesn't match the real size of your Floor prefab, tile counts and positions will be wrong -- check the Console warning on Play if the count looks off.")]
    [SerializeField] private float floorTileSize = 100f;

    [SerializeField] private bool useCustomFloorHeight = false;
    [SerializeField] private float floorHeight = 0f;

    [Header("Spawn / Despawn Radii")]
    [Tooltip("How far the camera can actually see across the ground (far clip plane, or where fog fully hides the world). Tiles inside this range must exist.")]
    [SerializeField] private float viewDistance = 250f;

    [Tooltip("Extra margin added to View Distance for the spawn radius. This is what makes a tile appear BEFORE the camera could see it.")]
    [SerializeField] private float spawnBuffer = 50f;

    [Tooltip("Extra margin added on top of the spawn radius before a tile is destroyed. This is the 'not anywhere close to coming back' zone -- keep it comfortably larger than one tile so tiles don't get destroyed and immediately recreated as the reference moves back and forth.")]
    [SerializeField] private float despawnBuffer = 100f;

    [Tooltip("World distance the reference/camera must move before the grid is re-evaluated. <= 0 auto-derives from Floor Tile Size.")]
    [SerializeField] private float updateThreshold = -1f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [Tooltip("Logs every spawn/destroy. Noisy -- use temporarily to sanity-check behaviour.")]
    [SerializeField] private bool verboseLogging = false;
    [Tooltip("Warn in the Console if the active tile count ever exceeds this. Usually means Floor Tile Size doesn't match the real prefab size, or the radii are set far larger than intended.")]
    [SerializeField] private int sanityWarnTileCount = 60;

    // --- runtime state -------------------------------------------------

    private readonly Dictionary<Vector2Int, Transform> occupied = new Dictionary<Vector2Int, Transform>();

    // Scratch collections reused every pass -> zero steady-state allocations.
    private readonly List<Vector2Int> missingScratch = new List<Vector2Int>();
    private readonly List<Vector2Int> staleScratch = new List<Vector2Int>();

    private Vector3 gridOrigin;   // world position of cell (0,0)
    private Vector3 lastRefPos;
    private Vector3 lastCamPos;
    private bool initialized;
    private bool sanityWarned;
    private float sqrUpdateThreshold;

    public int ActiveTileCount => occupied.Count;
    public float SpawnRadius => Mathf.Max(0f, viewDistance) + Mathf.Max(0f, spawnBuffer);
    public float DespawnRadius => SpawnRadius + Mathf.Max(0f, despawnBuffer);

    // --- lifecycle -----------------------------------------------------

    private void Awake()
    {
        if (cameraTransform == null && includeCamera && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        AdoptExistingFloors();

        if (referenceTransform == null)
        {
            Debug.LogWarning("FloorManager: no Reference Transform assigned on " + name + ".", this);
            return;
        }

        float threshold = updateThreshold > 0f ? updateThreshold : floorTileSize * 0.25f;
        sqrUpdateThreshold = threshold * threshold;

        UpdateGrid(); // Establish correct coverage before the first frame renders.

        lastRefPos = referenceTransform.position;
        lastCamPos = cameraTransform != null ? cameraTransform.position : lastRefPos;
        initialized = true;
    }

    private void Update()
    {
        if (!initialized) return;

        Vector3 refPos = referenceTransform.position;
        Vector3 camPos = cameraTransform != null ? cameraTransform.position : refPos;

        // Cheap per-frame test: only re-evaluate the grid once something has
        // actually moved far enough to matter.
        bool moved = (refPos - lastRefPos).sqrMagnitude >= sqrUpdateThreshold
                  || (camPos - lastCamPos).sqrMagnitude >= sqrUpdateThreshold;

        if (!moved) return;

        lastRefPos = refPos;
        lastCamPos = camPos;
        UpdateGrid();
    }

    // --- setup ---------------------------------------------------------

    /// <summary>
    /// Adopts Floor objects already parented under this GameObject as the
    /// starting set and maps each to its grid cell from its world position.
    /// Nothing is instantiated here. With no children present, the grid is
    /// anchored to this GameObject's own position instead.
    /// </summary>
    private void AdoptExistingFloors()
    {
        string prefabName = floorPrefab != null ? floorPrefab.name : null;
        List<Transform> found = new List<Transform>();

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (prefabName != null && !child.name.StartsWith(prefabName)) continue;
            found.Add(child);
        }

        gridOrigin = found.Count > 0 ? found[0].position : transform.position;

        foreach (Transform floor in found)
        {
            Vector2Int cell = WorldToCell(floor.position);

            if (occupied.ContainsKey(cell))
            {
                // Duplicate cell (mismatched Floor Tile Size or a stray copy) --
                // this one isn't part of the addressable grid, so remove it
                // rather than let it silently sit on top of another tile.
                Destroy(floor.gameObject);
                continue;
            }

            occupied.Add(cell, floor);
        }
    }

    // --- grid maintenance ------------------------------------------------

    /// <summary>
    /// Spawns tiles for every needed cell that doesn't have one, and destroys
    /// tiles that have fallen beyond Despawn Radius, reusing repositioned
    /// tiles wherever a swap is possible instead of destroying+instantiating.
    /// </summary>
    private void UpdateGrid()
    {
        Vector3 refPos = referenceTransform.position;
        bool hasCam = includeCamera && cameraTransform != null;
        Vector3 camPos = hasCam ? cameraTransform.position : refPos;

        float spawnR = SpawnRadius;
        float despawnR = DespawnRadius;
        float spawnRSqr = spawnR * spawnR;
        float despawnRSqr = despawnR * despawnR;

        Vector2Int refCell = WorldToCell(refPos);
        Vector2Int camCell = hasCam ? WorldToCell(camPos) : refCell;

        int cellRadius = Mathf.CeilToInt(despawnR / floorTileSize) + 1;
        int minCX = Mathf.Min(refCell.x, camCell.x) - cellRadius;
        int maxCX = Mathf.Max(refCell.x, camCell.x) + cellRadius;
        int minCZ = Mathf.Min(refCell.y, camCell.y) - cellRadius;
        int maxCZ = Mathf.Max(refCell.y, camCell.y) + cellRadius;

        missingScratch.Clear();
        for (int cx = minCX; cx <= maxCX; cx++)
        {
            for (int cz = minCZ; cz <= maxCZ; cz++)
            {
                Vector2Int cell = new Vector2Int(cx, cz);
                if (occupied.ContainsKey(cell)) continue;

                Vector3 cellPos = CellToWorld(cell);
                if (SqrDistanceToNearest(cellPos, refPos, hasCam, camPos) <= spawnRSqr)
                {
                    missingScratch.Add(cell);
                }
            }
        }

        staleScratch.Clear();
        foreach (KeyValuePair<Vector2Int, Transform> kvp in occupied)
        {
            Vector3 cellPos = CellToWorld(kvp.Key);
            if (SqrDistanceToNearest(cellPos, refPos, hasCam, camPos) > despawnRSqr)
            {
                staleScratch.Add(kvp.Key);
            }
        }

        if (missingScratch.Count == 0 && staleScratch.Count == 0) return;

        int staleIndex = 0;

        for (int i = 0; i < missingScratch.Count; i++)
        {
            Vector2Int cell = missingScratch[i];
            Transform tile;

            if (staleIndex < staleScratch.Count)
            {
                // Reuse: move a tile that's beyond despawn range straight
                // onto the cell that needs one. No Destroy, no Instantiate.
                Vector2Int from = staleScratch[staleIndex++];
                tile = occupied[from];
                occupied.Remove(from);
            }
            else if (floorPrefab != null)
            {
                GameObject go = Instantiate(floorPrefab, transform);
                tile = go.transform;
                if (verboseLogging) Debug.Log($"FloorManager: spawned tile at {cell}.", this);
            }
            else
            {
                Debug.LogWarning("FloorManager: a tile is needed but no Floor Prefab is assigned.", this);
                continue;
            }

            tile.position = CellToWorld(cell);
            occupied[cell] = tile;
        }

        // Genuine surplus: too far away to be reused this pass, so destroy it
        // rather than let it accumulate.
        for (; staleIndex < staleScratch.Count; staleIndex++)
        {
            Vector2Int from = staleScratch[staleIndex];
            Transform tile = occupied[from];
            occupied.Remove(from);
            if (verboseLogging) Debug.Log($"FloorManager: destroyed tile at {from}.", this);
            Destroy(tile.gameObject);
        }

        if (!sanityWarned && occupied.Count > sanityWarnTileCount)
        {
            sanityWarned = true;
            Debug.LogWarning(
                $"FloorManager: {occupied.Count} tiles active at once, more than Sanity Warn Tile Count ({sanityWarnTileCount}). " +
                "This usually means Floor Tile Size doesn't match the real size of your Floor prefab, or View Distance / the buffers are larger than intended. " +
                $"Current radii: spawn {SpawnRadius}, despawn {DespawnRadius}, tile size {floorTileSize}.", this);
        }
    }

    private static float SqrDistanceToNearest(Vector3 point, Vector3 refPos, bool hasCam, Vector3 camPos)
    {
        float dx = point.x - refPos.x;
        float dz = point.z - refPos.z;
        float best = dx * dx + dz * dz;

        if (hasCam)
        {
            float cx = point.x - camPos.x;
            float cz = point.z - camPos.z;
            float camSqr = cx * cx + cz * cz;
            if (camSqr < best) best = camSqr;
        }

        return best;
    }

    // --- helpers -------------------------------------------------------

    private Vector2Int WorldToCell(Vector3 world)
    {
        return new Vector2Int(
            Mathf.RoundToInt((world.x - gridOrigin.x) / floorTileSize),
            Mathf.RoundToInt((world.z - gridOrigin.z) / floorTileSize));
    }

    private Vector3 CellToWorld(Vector2Int cell)
    {
        return new Vector3(
            gridOrigin.x + cell.x * floorTileSize,
            useCustomFloorHeight ? floorHeight : gridOrigin.y,
            gridOrigin.z + cell.y * floorTileSize);
    }

    private void OnValidate()
    {
        if (floorTileSize <= 0f) floorTileSize = 0.01f;
        if (viewDistance < 0f) viewDistance = 0f;
        if (spawnBuffer < 0f) spawnBuffer = 0f;
        if (despawnBuffer < 0f) despawnBuffer = 0f;
        if (sanityWarnTileCount < 1) sanityWarnTileCount = 1;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || referenceTransform == null) return;

        Vector3 refPos = referenceTransform.position;
        float y = Application.isPlaying ? gridOrigin.y : refPos.y;

        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.8f);
        DrawCircle(new Vector3(refPos.x, y, refPos.z), SpawnRadius);

        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.8f);
        DrawCircle(new Vector3(refPos.x, y, refPos.z), DespawnRadius);

        if (includeCamera)
        {
            Transform cam = cameraTransform != null ? cameraTransform : (Camera.main != null ? Camera.main.transform : null);
            if (cam != null)
            {
                Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.5f);
                DrawCircle(new Vector3(cam.position.x, y, cam.position.z), SpawnRadius);
                Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.5f);
                DrawCircle(new Vector3(cam.position.x, y, cam.position.z), DespawnRadius);
            }
        }

        if (Application.isPlaying)
        {
            Gizmos.color = Color.green;
            foreach (Transform tile in occupied.Values)
            {
                Gizmos.DrawWireCube(tile.position, new Vector3(floorTileSize * 0.95f, 0.05f, floorTileSize * 0.95f));
            }
        }
    }

    private static void DrawCircle(Vector3 center, float radius)
    {
        if (radius <= 0f) return;
        const int segments = 48;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}