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
/// NOTE ON SHAPE: this loads a circular area around the reference/camera by
/// design -- "radius" is literally a circle, which is what gives uniform draw
/// distance in every direction regardless of which way you turn. That's
/// standard for endless-terrain streaming, but it does mean total tile count
/// scales with the AREA of that circle, which grows fast with big tiles. Max
/// Active Tiles below is the actual safety valve for that -- tune the radii
/// for how far you want to see, and let Max Active Tiles guarantee you never
/// pay for more tiles than your game can afford, regardless of how those
/// radii are set. (A forward-facing wedge instead of a full circle is
/// possible as a further optimization, but is a separate change.)
///
/// Reuse still happens first: a tile that needs to disappear from one cell and
/// a cell that needs a tile are paired up and the tile is simply repositioned.
/// Destroy only fires for genuine surplus -- tiles beyond Despawn Radius (or
/// beyond Max Active Tiles) for which there is no missing cell to reuse them
/// into. This keeps the tile count bounded by what's actually near the
/// camera, which matters here since each Floor is four large Ground meshes.
///
/// Attach to the "Environment" GameObject.
/// Grid convention: Vector2Int(x, y) -> world (X, Z). Y is floor height.
/// </summary>
public enum MapEnvironmentType
{
    ClassicChess,
    AbandonedWasteland,
    NeonSciFi,
    ForestNature
}

public enum EnvironmentMode
{
    Auto,
    Manual
}

/// <summary>
/// Marker component attached to instantiated props so they can be identified and cleaned up.
/// </summary>
public class FloorPropMarker : MonoBehaviour { }

[DisallowMultipleComponent]
public class FloorManager : MonoBehaviour
{
    private AnimalSpawner animalSpawner;

    [Header("Floor Prefab")]
    [SerializeField] private GameObject floorPrefab;

    [Header("Environment Prop Spawning")]
    [Tooltip("MaterialsList ScriptableObject asset containing prop prefab categories.")]
    [SerializeField] private MaterialsList materialsList;

    [Tooltip("Minimum number of props to spawn per floor tile.")]
    [SerializeField] private int minimumProps = 1;

    [Tooltip("Maximum number of props to spawn per floor tile.")]
    [SerializeField] private int maximumProps = 3;

    [Tooltip("Minimum distance required between props on the same floor tile.")]
    [SerializeField] private float minimumPropDistance = 1.0f;

    [Tooltip("Margin from the edges of the floor tile where props will not spawn.")]
    [SerializeField] private float edgeMargin = 0.4f;

    [Tooltip("Vertical offset applied to prop placement. Positive raises props, negative lowers them.")]
    [SerializeField] private float propVerticalOffset = 0f;

    [Tooltip("Automatic map detection mode or manual override.")]
    [SerializeField] private EnvironmentMode environmentMode = EnvironmentMode.Auto;

    [Tooltip("Manual environment type used if Environment Mode is set to Manual.")]
    [SerializeField] private MapEnvironmentType manualEnvironment = MapEnvironmentType.ClassicChess;

    [Header("Category Prop Spawning & Probabilities")]
    [Tooltip("Enable category-based prop spawning with custom counts and probabilities for Trees, Bushes, Any Objects, etc.")]
    [SerializeField] private bool useCategorySpawning = true;

    [Header("Trees Spawning")]
    [Tooltip("Probability (0.0 to 1.0) that Trees will spawn on a floor tile.")]
    [Range(0f, 1f)]
    [SerializeField] private float treeSpawnProbability = 0.9f;
    [Tooltip("Minimum number of trees to spawn per floor tile if probability check passes.")]
    [SerializeField] private int minTreesPerTile = 2;
    [Tooltip("Maximum number of trees to spawn per floor tile if probability check passes.")]
    [SerializeField] private int maxTreesPerTile = 4;

    [Header("Bushes Spawning")]
    [Tooltip("Probability (0.0 to 1.0) that Bushes will spawn on a floor tile.")]
    [Range(0f, 1f)]
    [SerializeField] private float bushSpawnProbability = 0.8f;
    [Tooltip("Minimum number of bushes to spawn per floor tile if probability check passes.")]
    [SerializeField] private int minBushesPerTile = 1;
    [Tooltip("Maximum number of bushes to spawn per floor tile if probability check passes.")]
    [SerializeField] private int maxBushesPerTile = 3;

    [Header("Any Objects Spawning")]
    [Tooltip("Probability (0.0 to 1.0) that Any Objects (AnyProp) will spawn on a floor tile.")]
    [Range(0f, 1f)]
    [SerializeField] private float anyPropSpawnProbability = 0.85f;
    [Tooltip("Minimum number of Any Objects to spawn per floor tile if probability check passes.")]
    [SerializeField] private int minAnyPropsPerTile = 2;
    [Tooltip("Maximum number of Any Objects to spawn per floor tile if probability check passes.")]
    [SerializeField] private int maxAnyPropsPerTile = 4;

    [Header("Environment-Specific Props (Cars / Chess / Sci-Fi)")]
    [Tooltip("Probability (0.0 to 1.0) that map-specific environment props (Cars, Chess pieces, Sci-Fi structures) will spawn on a floor tile.")]
    [Range(0f, 1f)]
    [SerializeField] private float envPropsSpawnProbability = 0.8f;
    [Tooltip("Minimum number of environment-specific props to spawn per floor tile.")]
    [SerializeField] private int minEnvPropsPerTile = 1;
    [Tooltip("Maximum number of environment-specific props to spawn per floor tile.")]
    [SerializeField] private int maxEnvPropsPerTile = 3;

    public MaterialsList MaterialsList { get => materialsList; set => materialsList = value; }
    public int MinimumProps { get => minimumProps; set => minimumProps = value; }
    public int MaximumProps { get => maximumProps; set => maximumProps = value; }
    public float MinimumPropDistance { get => minimumPropDistance; set => minimumPropDistance = value; }
    public float EdgeMargin { get => edgeMargin; set => edgeMargin = value; }
    public float PropVerticalOffset { get => propVerticalOffset; set => propVerticalOffset = value; }
    public EnvironmentMode EnvironmentModeSetting { get => environmentMode; set => environmentMode = value; }
    public MapEnvironmentType ManualEnvironment { get => manualEnvironment; set => manualEnvironment = value; }

    public bool UseCategorySpawning { get => useCategorySpawning; set => useCategorySpawning = value; }

    public float TreeSpawnProbability { get => treeSpawnProbability; set => treeSpawnProbability = Mathf.Clamp01(value); }
    public int MinTreesPerTile { get => minTreesPerTile; set => minTreesPerTile = Mathf.Max(0, value); }
    public int MaxTreesPerTile { get => maxTreesPerTile; set => maxTreesPerTile = Mathf.Max(minTreesPerTile, value); }

    public float BushSpawnProbability { get => bushSpawnProbability; set => bushSpawnProbability = Mathf.Clamp01(value); }
    public int MinBushesPerTile { get => minBushesPerTile; set => minBushesPerTile = Mathf.Max(0, value); }
    public int MaxBushesPerTile { get => maxBushesPerTile; set => maxBushesPerTile = Mathf.Max(minBushesPerTile, value); }

    public float AnyPropSpawnProbability { get => anyPropSpawnProbability; set => anyPropSpawnProbability = Mathf.Clamp01(value); }
    public int MinAnyPropsPerTile { get => minAnyPropsPerTile; set => minAnyPropsPerTile = Mathf.Max(0, value); }
    public int MaxAnyPropsPerTile { get => maxAnyPropsPerTile; set => maxAnyPropsPerTile = Mathf.Max(minAnyPropsPerTile, value); }

    public float EnvPropsSpawnProbability { get => envPropsSpawnProbability; set => envPropsSpawnProbability = Mathf.Clamp01(value); }
    public int MinEnvPropsPerTile { get => minEnvPropsPerTile; set => minEnvPropsPerTile = Mathf.Max(0, value); }
    public int MaxEnvPropsPerTile { get => maxEnvPropsPerTile; set => maxEnvPropsPerTile = Mathf.Max(minEnvPropsPerTile, value); }

    [Header("Map Material Sync")]
    [Tooltip("Optional list of map materials if you want FloorManager to look up by index from PlayerPrefs as a fallback when testing the Game scene directly.")]
    [SerializeField] private Material[] mapMaterials;

    private Material activeFloorMaterial;

    [Header("Reference")]
    [Tooltip("Center of the active area (player, vehicle, character root...).")]
    [SerializeField] private Transform referenceTransform;

    [Tooltip("Camera that looks at the world. Included so a chase camera behind/above the reference still has floor under its view. Leave empty to use Camera.main.")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private bool includeCamera = true;

    [Header("Tile")]
    [Tooltip("Exact world-space width/depth of one complete Floor prefab. If this doesn't match the real size of your Floor prefab, tiles will overlap or leave gaps and far more tiles than intended will be spawned. A mismatch warning is logged automatically on Play.")]
    [SerializeField] private float floorTileSize = 4f;

    [SerializeField] private bool useCustomFloorHeight = false;
    [SerializeField] private float floorHeight = 0f;

    [Header("Spawn / Despawn Radii")]
    [Tooltip("How far the camera can actually see across the ground (far clip plane, or where fog fully hides the world). Tiles inside this range must exist. Keep this proportional to Floor Tile Size -- with large tiles, this should usually only be 1-2 tile widths, not hundreds of units.")]
    [SerializeField] private float viewDistance = 12f;

    [Tooltip("Extra margin added to View Distance for the spawn radius. This is what makes a tile appear BEFORE the camera could see it.")]
    [SerializeField] private float spawnBuffer = 4f;

    [Tooltip("Extra margin added on top of the spawn radius before a tile is destroyed. This is the 'not anywhere close to coming back' zone -- keep it comfortably larger than one tile so tiles don't get destroyed and immediately recreated as the reference moves back and forth.")]
    [SerializeField] private float despawnBuffer = 8f;

    [Tooltip("World distance the reference/camera must move before the grid is re-evaluated. <= 0 auto-derives from Floor Tile Size.")]
    [SerializeField] private float updateThreshold = -1f;

    [Header("Hard Cap")]
    [Tooltip("Absolute ceiling on how many tiles may exist at once, no matter what the radii above would otherwise produce. If the radii+tile size combination would need more tiles than this, the farthest ones are destroyed instead. This is the real, reliable dial for performance -- set the radii for how far you want to see, and rely on this to guarantee you never pay for more tiles than that.")]
    [SerializeField] private int maxActiveTiles = 64;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [Tooltip("Logs every spawn/destroy. Noisy -- use temporarily to sanity-check behaviour.")]
    [SerializeField] private bool verboseLogging = false;
    [Tooltip("Warn in the Console if the active tile count ever exceeds this. With Max Active Tiles enforced above, this should rarely fire unless Max Active Tiles itself is set high -- it exists mainly as a second opinion.")]
    [SerializeField] private int sanityWarnTileCount = 80;

    // --- runtime state -------------------------------------------------

    private readonly Dictionary<Vector2Int, Transform> occupied = new Dictionary<Vector2Int, Transform>();

    // Scratch collections reused every pass -> zero steady-state allocations.
    private readonly List<Vector2Int> missingScratch = new List<Vector2Int>();
    private readonly List<Vector2Int> staleScratch = new List<Vector2Int>();
    private readonly List<Vector2Int> removableScratch = new List<Vector2Int>();

    private Vector3 gridOrigin;   // world position of cell (0,0)
    private Vector3 lastRefPos;
    private Vector3 lastCamPos;
    private bool initialized;
    private bool sanityWarned;
    private float sqrUpdateThreshold;

    public int ActiveTileCount => occupied.Count;
    public IEnumerable<Transform> GetActiveFloors() => occupied.Values;
    public float FloorTileSize => floorTileSize;
    public float SpawnRadius => Mathf.Max(0f, viewDistance) + Mathf.Max(0f, spawnBuffer);
    public float DespawnRadius => SpawnRadius + Mathf.Max(0f, despawnBuffer);

    // --- lifecycle -----------------------------------------------------

    private void InitializeFloorMaterial()
    {
        // 1. Try reading the static material from MapSelectManager (set in the Menu)
        if (MapSelectManager.SelectedMapMaterial != null)
        {
            activeFloorMaterial = MapSelectManager.SelectedMapMaterial;
        }
        // 2. Fallback: read from PlayerPrefs if mapMaterials are assigned here
        else if (mapMaterials != null && mapMaterials.Length > 0)
        {
            int index = PlayerPrefs.GetInt("SelectedMapIndex", 0);
            index = Mathf.Clamp(index, 0, mapMaterials.Length - 1);
            activeFloorMaterial = mapMaterials[index];
        }
    }

    private void ApplyMaterialToTile(Transform tile)
    {
        if (activeFloorMaterial == null || tile == null) return;

        Renderer[] renderers = tile.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].sharedMaterial = activeFloorMaterial;
            }
        }
    }

    private void Awake()
    {
        if (cameraTransform == null && includeCamera && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        InitializeFloorMaterial();
        ValidateFloorTileSize();
        animalSpawner = GetComponent<AnimalSpawner>();
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
        if (!initialized || referenceTransform == null) return;

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
    /// Compares Floor Tile Size against the Floor Prefab's actual measured footprint
    /// (from its renderers) and warns if they don't roughly match. A mismatch here is
    /// the most common cause of tiles overlapping or leaving gaps, which in turn makes
    /// the grid think it needs far more tiles than it actually does.
    /// </summary>
    private void ValidateFloorTileSize()
    {
        if (floorPrefab == null) return;

        Renderer[] renderers = floorPrefab.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        float measuredWidth = Mathf.Max(bounds.size.x, bounds.size.z);
        if (measuredWidth <= 0f) return;

        float ratio = measuredWidth / floorTileSize;
        if (ratio < 0.9f || ratio > 1.1f)
        {
            Debug.LogWarning(
                $"FloorManager: Floor Tile Size is set to {floorTileSize}, but the Floor Prefab's " +
                $"actual measured footprint is about {measuredWidth:F1} units wide. These should match " +
                "closely -- otherwise tiles will overlap or leave gaps, and the grid will spawn more " +
                "tiles than intended to compensate. Update Floor Tile Size to match your prefab's real size.",
                this);
        }
    }

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
            ApplyMaterialToTile(floor);
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
            SpawnPropsForFloor(floor);
            if (animalSpawner != null) animalSpawner.SpawnAnimalsForFloor(floor);
        }
    }

    // --- grid maintenance ------------------------------------------------

    /// <summary>
    /// Spawns tiles for every needed cell that doesn't have one, destroys
    /// tiles that have fallen beyond Despawn Radius (reusing repositioned
    /// tiles wherever a swap is possible), and then enforces Max Active
    /// Tiles as a hard backstop regardless of what the radii produced.
    /// </summary>
    private void UpdateGrid()
    {
        if (referenceTransform == null) return;

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

        // Even with nothing missing or stale, occupied could still be over the
        // cap (e.g. more pre-placed floors than Max Active Tiles allows), so
        // don't skip the enforcement pass below in that case.
        if (missingScratch.Count == 0 && staleScratch.Count == 0 && occupied.Count <= maxActiveTiles)
        {
            return;
        }

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
                ClearProps(tile);
                tile.position = CellToWorld(cell);
                occupied[cell] = tile;
                SpawnPropsForFloor(tile);
                if (animalSpawner != null) animalSpawner.SpawnAnimalsForFloor(tile);
            }
            else if (floorPrefab != null)
            {
                GameObject go = Instantiate(floorPrefab, transform);
                tile = go.transform;
                tile.position = CellToWorld(cell);
                occupied[cell] = tile;
                ApplyMaterialToTile(tile);
                SpawnPropsForFloor(tile);
                if (animalSpawner != null) animalSpawner.SpawnAnimalsForFloor(tile);
                if (verboseLogging) Debug.Log($"FloorManager: spawned tile at {cell}.", this);
            }
            else
            {
                Debug.LogWarning("FloorManager: a tile is needed but no Floor Prefab is assigned.", this);
                continue;
            }
        }

        // Genuine surplus: too far away to be reused this pass, so destroy it
        // rather than let it accumulate.
        for (; staleIndex < staleScratch.Count; staleIndex++)
        {
            Vector2Int from = staleScratch[staleIndex];
            Transform tile = occupied[from];
            occupied.Remove(from);
            if (verboseLogging) Debug.Log($"FloorManager: destroyed tile at {from}.", this);
            ClearProps(tile);
            Destroy(tile.gameObject);
        }

        EnforceMaxActiveTiles(refPos, hasCam, camPos, spawnRSqr);

        if (!sanityWarned && occupied.Count > sanityWarnTileCount)
        {
            sanityWarned = true;
            Debug.LogWarning(
                $"FloorManager: {occupied.Count} tiles active at once, more than Sanity Warn Tile Count ({sanityWarnTileCount}). " +
                "This usually means Floor Tile Size doesn't match the real size of your Floor prefab, or View Distance / the buffers are larger than intended. " +
                $"Current radii: spawn {SpawnRadius}, despawn {DespawnRadius}, tile size {floorTileSize}, cap {maxActiveTiles}.", this);
        }
    }

    /// <summary>
    /// Backstop: trims tile count toward Max Active Tiles by destroying the farthest
    /// tiles first, but ONLY among tiles that are already outside Spawn Radius (i.e.
    /// not currently required). A tile still within Spawn Radius is never touched here,
    /// no matter how far over the cap the count is -- destroying a still-required tile
    /// would just cause UpdateGrid to see it as "missing" and instantiate it again on
    /// the very next pass, which repeats forever: an instantiate/destroy thrash loop
    /// every re-evaluation. If Spawn Radius alone needs more tiles than the cap allows,
    /// the cap is exceeded rather than fought - that's a sign the radii and Max Active
    /// Tiles are set inconsistently (or Floor Tile Size doesn't match your prefab; see
    /// the mismatch warning in Awake), and the fix is to change those settings, not to
    /// destroy tiles that are still on screen.
    /// </summary>
    private void EnforceMaxActiveTiles(Vector3 refPos, bool hasCam, Vector3 camPos, float spawnRSqr)
    {
        int cap = Mathf.Max(1, maxActiveTiles);
        int excess = occupied.Count - cap;
        if (excess <= 0) return;

        removableScratch.Clear();
        foreach (KeyValuePair<Vector2Int, Transform> kvp in occupied)
        {
            Vector3 cellPos = CellToWorld(kvp.Key);
            if (SqrDistanceToNearest(cellPos, refPos, hasCam, camPos) > spawnRSqr)
            {
                removableScratch.Add(kvp.Key);
            }
        }

        int toRemove = Mathf.Min(excess, removableScratch.Count);

        if (toRemove < excess && verboseLogging)
        {
            Debug.LogWarning(
                $"FloorManager: {occupied.Count} tiles are within Spawn Radius alone, above Max Active " +
                $"Tiles ({cap}). Leaving the required tiles in place rather than thrashing - increase " +
                "Max Active Tiles, shrink View Distance/Spawn Buffer, or check the Floor Tile Size " +
                "mismatch warning.", this);
        }

        for (int i = 0; i < toRemove; i++)
        {
            int farthestIndex = -1;
            float farthestSqr = -1f;

            for (int j = 0; j < removableScratch.Count; j++)
            {
                Vector3 cellPos = CellToWorld(removableScratch[j]);
                float sqr = SqrDistanceToNearest(cellPos, refPos, hasCam, camPos);
                if (sqr > farthestSqr)
                {
                    farthestSqr = sqr;
                    farthestIndex = j;
                }
            }

            if (farthestIndex < 0) break;

            Vector2Int cell = removableScratch[farthestIndex];
            removableScratch.RemoveAt(farthestIndex);

            Transform tile = occupied[cell];
            occupied.Remove(cell);
            if (verboseLogging) Debug.Log($"FloorManager: over Max Active Tiles cap, destroyed tile at {cell}.", this);
            ClearProps(tile);
            Destroy(tile.gameObject);
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
        if (maxActiveTiles < 1) maxActiveTiles = 1;
        if (sanityWarnTileCount < 1) sanityWarnTileCount = 1;
        if (minimumProps < 0) minimumProps = 0;
        if (maximumProps < minimumProps) maximumProps = minimumProps;
        if (minimumPropDistance < 0f) minimumPropDistance = 0f;
        if (edgeMargin < 0f) edgeMargin = 0f;

        treeSpawnProbability = Mathf.Clamp01(treeSpawnProbability);
        minTreesPerTile = Mathf.Max(0, minTreesPerTile);
        if (maxTreesPerTile < minTreesPerTile) maxTreesPerTile = minTreesPerTile;

        bushSpawnProbability = Mathf.Clamp01(bushSpawnProbability);
        minBushesPerTile = Mathf.Max(0, minBushesPerTile);
        if (maxBushesPerTile < minBushesPerTile) maxBushesPerTile = minBushesPerTile;

        anyPropSpawnProbability = Mathf.Clamp01(anyPropSpawnProbability);
        minAnyPropsPerTile = Mathf.Max(0, minAnyPropsPerTile);
        if (maxAnyPropsPerTile < minAnyPropsPerTile) maxAnyPropsPerTile = minAnyPropsPerTile;

        envPropsSpawnProbability = Mathf.Clamp01(envPropsSpawnProbability);
        minEnvPropsPerTile = Mathf.Max(0, minEnvPropsPerTile);
        if (maxEnvPropsPerTile < minEnvPropsPerTile) maxEnvPropsPerTile = minEnvPropsPerTile;
    }

    // --- environment prop spawning ---------------------------------------

    /// <summary>
    /// Determines the active map environment type using active floor material, MapSelectManager, or PlayerPrefs fallback.
    /// </summary>
    public MapEnvironmentType GetCurrentEnvironmentType()
    {
        if (environmentMode == EnvironmentMode.Manual)
        {
            return manualEnvironment;
        }

        // 1. Check activeFloorMaterial or MapSelectManager.SelectedMapMaterial
        Material mat = activeFloorMaterial != null ? activeFloorMaterial : MapSelectManager.SelectedMapMaterial;
        if (mat != null)
        {
            string matName = mat.name.ToLowerInvariant();
            if (matName.Contains("neon") || matName.Contains("scifi"))
            {
                return MapEnvironmentType.NeonSciFi;
            }
            if (matName.Contains("chess"))
            {
                return MapEnvironmentType.ClassicChess;
            }
            if (matName.Contains("pavement") || matName.Contains("overgrown") || matName.Contains("mud") || matName.Contains("abandoned") || matName.Contains("waste") || matName.Contains("car"))
            {
                return MapEnvironmentType.AbandonedWasteland;
            }
            if (matName.Contains("ground") || matName.Contains("nature") || matName.Contains("forest") || matName.Contains("tree"))
            {
                return MapEnvironmentType.ForestNature;
            }
        }

        // 2. Fallback to SelectedMapIndex
        int mapIdx = MapSelectManager.SelectedMapIndex;
        if (PlayerPrefs.HasKey("SelectedMapIndex"))
        {
            mapIdx = PlayerPrefs.GetInt("SelectedMapIndex", mapIdx);
        }

        switch (mapIdx)
        {
            case 0: return MapEnvironmentType.ClassicChess;      // chess / Checkmate
            case 1: return MapEnvironmentType.ForestNature;       // ground / Open Grounds
            case 2: return MapEnvironmentType.AbandonedWasteland; // mud / Mudlands
            case 3: return MapEnvironmentType.NeonSciFi;          // neon / Neon Rush
            case 4: return MapEnvironmentType.AbandonedWasteland; // overgrown-pavement / Wild Streets
            default: return MapEnvironmentType.ForestNature;
        }
    }

    /// <summary>
    /// Returns the allowed non-empty prop categories for the specified map environment.
    /// </summary>
    private List<GameObject[]> GetAllowedPropCategories(MapEnvironmentType env)
    {
        List<GameObject[]> list = new List<GameObject[]>();
        if (materialsList == null) return list;

        switch (env)
        {
            case MapEnvironmentType.ClassicChess:
                AddCategoryIfValid(list, materialsList.chessEnvironmentProps);
                AddCategoryIfValid(list, materialsList.treeProps);
                AddCategoryIfValid(list, materialsList.bushes);
                AddCategoryIfValid(list, materialsList.AnyProp);
                break;

            case MapEnvironmentType.AbandonedWasteland:
                AddCategoryIfValid(list, materialsList.carProps);
                AddCategoryIfValid(list, materialsList.treeProps);
                AddCategoryIfValid(list, materialsList.bushes);
                AddCategoryIfValid(list, materialsList.AnyProp);
                break;

            case MapEnvironmentType.NeonSciFi:
                // ScifiProps only. AnyProp must NEVER appear on the Neon map.
                AddCategoryIfValid(list, materialsList.ScifiProps);
                break;

            case MapEnvironmentType.ForestNature:
                AddCategoryIfValid(list, materialsList.treeProps);
                AddCategoryIfValid(list, materialsList.bushes);
                AddCategoryIfValid(list, materialsList.AnyProp);
                break;
        }

        return list;
    }

    private static void AddCategoryIfValid(List<GameObject[]> list, GameObject[] array)
    {
        if (array == null || array.Length == 0) return;

        // Ensure there is at least one non-null prefab in the array
        for (int i = 0; i < array.Length; i++)
        {
            if (array[i] != null)
            {
                list.Add(array);
                return;
            }
        }
    }

    private static GameObject GetRandomValidPrefab(GameObject[] array)
    {
        if (array == null || array.Length == 0) return null;

        int startIndex = Random.Range(0, array.Length);
        for (int i = 0; i < array.Length; i++)
        {
            int index = (startIndex + i) % array.Length;
            if (array[index] != null)
            {
                return array[index];
            }
        }

        return null;
    }

    private struct PlacedPropInfo
    {
        public Vector3 worldPosition;
        public float radius;
    }

    private readonly Dictionary<Transform, List<PlacedPropInfo>> floorProps = new Dictionary<Transform, List<PlacedPropInfo>>();
    private readonly Dictionary<GameObject, float> prefabRadiusCache = new Dictionary<GameObject, float>();

    private void OnDestroy()
    {
        floorProps.Clear();
        prefabRadiusCache.Clear();
    }

    public float GetPrefabRadius(GameObject prefab)
    {
        if (prefab == null) return 0.4f;

        if (prefabRadiusCache.TryGetValue(prefab, out float cachedRadius))
        {
            return cachedRadius;
        }

        float maxRadius = 0.35f;

        MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter mf = filters[i];
            if (mf != null && mf.sharedMesh != null)
            {
                Bounds b = mf.sharedMesh.bounds;
                Vector3 scale = mf.transform.lossyScale;
                float extX = Mathf.Abs(b.extents.x * scale.x);
                float extY = Mathf.Abs(b.extents.y * scale.y);
                float extZ = Mathf.Abs(b.extents.z * scale.z);
                float r = Mathf.Max(extX, Mathf.Max(extY, extZ));
                if (r > maxRadius) maxRadius = r;
            }
        }

        BoxCollider[] boxes = prefab.GetComponentsInChildren<BoxCollider>(true);
        for (int i = 0; i < boxes.Length; i++)
        {
            BoxCollider bc = boxes[i];
            if (bc != null)
            {
                Vector3 size = Vector3.Scale(bc.size, bc.transform.lossyScale) * 0.5f;
                float r = Mathf.Max(Mathf.Abs(size.x), Mathf.Max(Mathf.Abs(size.y), Mathf.Abs(size.z)));
                if (r > maxRadius) maxRadius = r;
            }
        }

        maxRadius = Mathf.Clamp(maxRadius, 0.35f, 2.5f);
        prefabRadiusCache[prefab] = maxRadius;
        return maxRadius;
    }

    public bool IsOverlappingAnyActiveProp(Vector3 candidateWorldPos, float candidateRadius)
    {
        foreach (KeyValuePair<Transform, List<PlacedPropInfo>> kvp in floorProps)
        {
            List<PlacedPropInfo> list = kvp.Value;
            if (list == null) continue;

            for (int i = 0; i < list.Count; i++)
            {
                PlacedPropInfo placed = list[i];
                float dx = candidateWorldPos.x - placed.worldPosition.x;
                float dz = candidateWorldPos.z - placed.worldPosition.z;
                float distSqr = dx * dx + dz * dz;

                float requiredDistance = candidateRadius + placed.radius + minimumPropDistance;
                if (distSqr < requiredDistance * requiredDistance)
                {
                    return true;
                }
            }
        }
        return false;
    }

    public bool IsTooCloseToPlayer(Vector3 candidateWorldPos, float candidateRadius)
    {
        if (referenceTransform == null) return false;

        float dx = candidateWorldPos.x - referenceTransform.position.x;
        float dz = candidateWorldPos.z - referenceTransform.position.z;
        float distSqr = dx * dx + dz * dz;

        float playerClearance = candidateRadius + 1.2f;
        return distSqr < playerClearance * playerClearance;
    }

    public bool IsPositionBlockedByExistingSceneObject(Vector3 candidateWorldPos, float candidateRadius, Transform currentFloor)
    {
        float surfaceY = currentFloor.TransformPoint(Vector3.zero).y;
        Vector3 sphereCenter = new Vector3(candidateWorldPos.x, surfaceY + Mathf.Max(0.4f, candidateRadius * 0.5f), candidateWorldPos.z);
        float checkRadius = Mathf.Max(0.35f, candidateRadius * 0.85f);

        Collider[] hits = Physics.OverlapSphere(sphereCenter, checkRadius, ~0, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0) return false;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null) continue;

            if (IsGroundOrFloor(hit)) continue;

            return true;
        }

        return false;
    }

    public static bool IsGroundOrFloor(Collider col)
    {
        if (col == null) return false;

        if (col.GetComponent<FloorPropMarker>() != null || col.GetComponentInParent<FloorPropMarker>() != null)
        {
            return false;
        }

        if (col.CompareTag("ChessProp") || col.CompareTag("Player") || col.CompareTag("Respawn"))
        {
            return false;
        }

        if (col.GetComponent<Pickup>() != null || col.GetComponentInParent<Pickup>() != null)
        {
            return false;
        }

        string colName = col.name.ToLowerInvariant();
        if (colName.StartsWith("ground") || colName.StartsWith("floor"))
        {
            return true;
        }

        if (col.CompareTag("Ground"))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Registers an object in the floor's placed props list so subsequent spawns don't overlap it.
    /// </summary>
    public void RegisterPlacedObject(Transform floor, Vector3 worldPos, float radius)
    {
        if (floor == null) return;
        if (!floorProps.TryGetValue(floor, out List<PlacedPropInfo> thisFloorProps))
        {
            thisFloorProps = new List<PlacedPropInfo>();
            floorProps[floor] = thisFloorProps;
        }
        thisFloorProps.Add(new PlacedPropInfo
        {
            worldPosition = worldPos,
            radius = radius
        });
    }

    /// <summary>
    /// Finds a valid non-overlapping local and world position strictly within the floor tile boundaries.
    /// </summary>
    public bool FindValidFloorPosition(Transform floor, float candidateRadius, out Vector2 validLocalPos, out Vector3 validWorldPos)
    {
        validLocalPos = Vector2.zero;
        validWorldPos = Vector3.zero;
        if (floor == null) return false;

        float halfSize = floorTileSize * 0.5f;
        float safeMargin = Mathf.Clamp(edgeMargin, 0.05f, Mathf.Max(0.05f, halfSize * 0.45f));
        float effectiveMargin = Mathf.Max(safeMargin, candidateRadius * 0.75f);
        float minBound = -halfSize + effectiveMargin;
        float maxBound = halfSize - effectiveMargin;

        if (maxBound < minBound)
        {
            minBound = 0f;
            maxBound = 0f;
        }

        const int maxPlacementAttempts = 35;
        for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
        {
            float randX = minBound < maxBound ? Random.Range(minBound, maxBound) : 0f;
            float randZ = minBound < maxBound ? Random.Range(minBound, maxBound) : 0f;
            Vector2 candidateLocal = new Vector2(randX, randZ);
            Vector3 candidateWorld = floor.TransformPoint(new Vector3(candidateLocal.x, 0f, candidateLocal.y));

            // A. Check against all currently active props across ALL floor tiles
            if (IsOverlappingAnyActiveProp(candidateWorld, candidateRadius))
            {
                continue;
            }

            // B. Check against player reference
            if (IsTooCloseToPlayer(candidateWorld, candidateRadius))
            {
                continue;
            }

            // C. Check physics scene for existing colliders
            if (IsPositionBlockedByExistingSceneObject(candidateWorld, candidateRadius, floor))
            {
                continue;
            }

            validLocalPos = candidateLocal;
            validWorldPos = candidateWorld;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Spawns random environment props for the specified floor tile based on the active map's allowed categories.
    /// Supports both category-specific spawning (trees, bushes, any objects, env props) and legacy global spawning.
    /// </summary>
    public void SpawnPropsForFloor(Transform floor)
    {
        if (floor == null || materialsList == null) return;

        if (useCategorySpawning)
        {
            SpawnCategoryPropsForFloor(floor);
            return;
        }

        int minP = Mathf.Max(0, minimumProps);
        int maxP = Mathf.Max(minP, maximumProps);
        if (maxP == 0) return;

        int propsToSpawn = Random.Range(minP, maxP + 1);
        if (propsToSpawn <= 0) return;

        MapEnvironmentType env = GetCurrentEnvironmentType();
        List<GameObject[]> allowedCategories = GetAllowedPropCategories(env);

        if (allowedCategories == null || allowedCategories.Count == 0)
        {
            return;
        }

        for (int i = 0; i < propsToSpawn; i++)
        {
            GameObject[] chosenCategory = allowedCategories[Random.Range(0, allowedCategories.Count)];
            SpawnSinglePropFromCategory(floor, chosenCategory);
        }
    }

    /// <summary>
    /// Spawns props category by category (Trees, Bushes, Any Objects, Environment Props)
    /// using their individual min/max counts and spawn probabilities.
    /// </summary>
    private void SpawnCategoryPropsForFloor(Transform floor)
    {
        MapEnvironmentType env = GetCurrentEnvironmentType();

        // 1. Environment-Specific Props (Cars, Chess pieces, Sci-Fi structures)
        if (env == MapEnvironmentType.ClassicChess)
        {
            TrySpawnCategoryProps(floor, materialsList.chessEnvironmentProps, envPropsSpawnProbability, minEnvPropsPerTile, maxEnvPropsPerTile);
        }
        else if (env == MapEnvironmentType.AbandonedWasteland)
        {
            TrySpawnCategoryProps(floor, materialsList.carProps, envPropsSpawnProbability, minEnvPropsPerTile, maxEnvPropsPerTile);
        }
        else if (env == MapEnvironmentType.NeonSciFi)
        {
            TrySpawnCategoryProps(floor, materialsList.ScifiProps, envPropsSpawnProbability, minEnvPropsPerTile, maxEnvPropsPerTile);
        }

        // 2. Trees (ForestNature, Wasteland, Chess, or if prefabs present)
        if (env != MapEnvironmentType.NeonSciFi || (materialsList.treeProps != null && materialsList.treeProps.Length > 0))
        {
            TrySpawnCategoryProps(floor, materialsList.treeProps, treeSpawnProbability, minTreesPerTile, maxTreesPerTile);
        }

        // 3. Bushes
        if (env != MapEnvironmentType.NeonSciFi || (materialsList.bushes != null && materialsList.bushes.Length > 0))
        {
            TrySpawnCategoryProps(floor, materialsList.bushes, bushSpawnProbability, minBushesPerTile, maxBushesPerTile);
        }

        // 4. Any Objects (AnyProp)
        if (env != MapEnvironmentType.NeonSciFi || (materialsList.AnyProp != null && materialsList.AnyProp.Length > 0))
        {
            TrySpawnCategoryProps(floor, materialsList.AnyProp, anyPropSpawnProbability, minAnyPropsPerTile, maxAnyPropsPerTile);
        }
    }

    /// <summary>
    /// Attempts to spawn props from a specific category array on a floor tile
    /// based on its spawn probability and count range.
    /// </summary>
    private void TrySpawnCategoryProps(Transform floor, GameObject[] categoryPrefabs, float probability, int minCount, int maxCount)
    {
        if (categoryPrefabs == null || categoryPrefabs.Length == 0) return;
        if (Random.value > probability) return;

        int countToSpawn = Random.Range(minCount, maxCount + 1);
        if (countToSpawn <= 0) return;

        for (int i = 0; i < countToSpawn; i++)
        {
            SpawnSinglePropFromCategory(floor, categoryPrefabs);
        }
    }

    /// <summary>
    /// Instantiates and places a single non-overlapping prop from a category array onto the floor tile.
    /// </summary>
    private bool SpawnSinglePropFromCategory(Transform floor, GameObject[] categoryPrefabs)
    {
        GameObject prefab = GetRandomValidPrefab(categoryPrefabs);
        if (prefab == null) return false;

        float candidateRadius = GetPrefabRadius(prefab);

        if (!FindValidFloorPosition(floor, candidateRadius, out Vector2 validLocalPos, out Vector3 candidateWorld))
        {
            return false;
        }

        GameObject propInstance = Instantiate(prefab, floor);
        propInstance.transform.localScale = prefab.transform.localScale;
        propInstance.transform.localPosition = new Vector3(validLocalPos.x, 0f, validLocalPos.y);

        Quaternion baseRotation = prefab.transform.localRotation;
        if (IsChessPiece(prefab, propInstance))
        {
            if (Mathf.Abs(Quaternion.Angle(baseRotation, Quaternion.identity)) < 1f ||
                (Mathf.Abs(baseRotation.eulerAngles.x) < 1f && Mathf.Abs(baseRotation.eulerAngles.z) < 1f))
            {
                baseRotation = Quaternion.Euler(-90f, 0f, 0f);
            }
        }

        Quaternion randomY = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        propInstance.transform.localRotation = randomY * baseRotation;

        AlignPropToFloorSurface(propInstance, floor);
        propInstance.AddComponent<FloorPropMarker>();

        Physics.SyncTransforms();
        RegisterPlacedObject(floor, propInstance.transform.position, candidateRadius);

        return true;
    }

    /// <summary>
    /// Checks if a prefab or instance represents a chess piece that requires upright orientation.
    /// </summary>
    private static bool IsChessPiece(GameObject prefab, GameObject instance)
    {
        if (instance != null && instance.CompareTag("ChessProp")) return true;
        if (prefab != null && prefab.CompareTag("ChessProp")) return true;

        if (instance != null && instance.GetComponent<ChessPiece>() != null) return true;
        if (prefab != null && prefab.GetComponent<ChessPiece>() != null) return true;

        string name = (instance != null ? instance.name : (prefab != null ? prefab.name : "")).ToLowerInvariant();
        if (name.Contains("chess") || name.Contains("rook") || name.Contains("bishop") ||
            name.Contains("king") || name.Contains("queen") || name.Contains("knight") || name.Contains("pawn"))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Aligns the vertical position of a spawned prop or animal so that its lowest bounding point (from Renderers or Colliders)
    /// rests flush on top of the floor tile surface rather than embedding halfway in the ground.
    /// </summary>
    public void AlignPropToFloorSurface(GameObject propInstance, Transform floor, float customOffset = 0f)
    {
        if (propInstance == null || floor == null) return;

        CharacterController cc = propInstance.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        float targetSurfaceWorldY = floor.TransformPoint(Vector3.zero).y + propVerticalOffset + customOffset;
        float lowestWorldY = float.MaxValue;
        bool foundBound = false;

        Renderer[] renderers = propInstance.GetComponentsInChildren<Renderer>();
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer rend = renderers[r];
            if (rend == null || !rend.gameObject.activeInHierarchy || !rend.enabled || rend is ParticleSystemRenderer) continue;

            lowestWorldY = Mathf.Min(lowestWorldY, rend.bounds.min.y);
            foundBound = true;
        }

        if (!foundBound)
        {
            Collider[] colliders = propInstance.GetComponentsInChildren<Collider>();
            for (int c = 0; c < colliders.Length; c++)
            {
                Collider col = colliders[c];
                if (col == null || !col.gameObject.activeInHierarchy || !col.enabled || col.isTrigger) continue;

                lowestWorldY = Mathf.Min(lowestWorldY, col.bounds.min.y);
                foundBound = true;
            }
        }

        if (foundBound && lowestWorldY < float.MaxValue)
        {
            float yOffset = targetSurfaceWorldY - lowestWorldY;
            propInstance.transform.position += new Vector3(0f, yOffset, 0f);
        }

        if (cc != null) cc.enabled = true;
    }

    /// <summary>
    /// Clears any previously spawned props from a floor tile before reusing or destroying it.
    /// Preserves ground/floor mesh children.
    /// </summary>
    public void ClearProps(Transform floor)
    {
        if (floor == null) return;

        floorProps.Remove(floor);

        for (int i = floor.childCount - 1; i >= 0; i--)
        {
            Transform child = floor.GetChild(i);
            if (child != null && (child.GetComponent<FloorPropMarker>() != null || !child.name.StartsWith("ground")))
            {
                child.gameObject.SetActive(false);
                child.SetParent(null);
                Destroy(child.gameObject);
            }
        }
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