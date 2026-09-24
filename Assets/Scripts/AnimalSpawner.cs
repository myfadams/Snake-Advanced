using UnityEngine;
using System.Collections.Generic;
using ithappy.Animals_FREE;

public class AnimalSpawner : MonoBehaviour
{
    [System.Serializable]
    public struct EnvironmentAnimalSettings
    {
        public MapEnvironmentType environmentType;
        public bool allowAnimals;
        public GameObject[] specificAnimals; // Optional override for this map
    }

    [Header("Settings")]
    public int maxAnimals = 10;
    [Range(0f, 1f)]
    public float spawnChancePerFloor = 0.5f;
    public float minDistanceBetweenAnimals = 2.5f;
    
    [Header("Environment Configuration")]
    public List<EnvironmentAnimalSettings> environmentSettings = new List<EnvironmentAnimalSettings>();

    private FloorManager floorManager;
    private Transform playerTransform;
    private List<AnimalAI> activeAnimals = new List<AnimalAI>();
    private MapEnvironmentType lastEnv = (MapEnvironmentType)(-1);
    private bool initialized = false;

    public FloorManager FloorManager
    {
        get
        {
            if (floorManager == null) floorManager = GetComponent<FloorManager>() ?? FindObjectOfType<FloorManager>();
            return floorManager;
        }
    }

    public void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        if (floorManager == null) floorManager = GetComponent<FloorManager>() ?? FindObjectOfType<FloorManager>();

        if (environmentSettings == null) environmentSettings = new List<EnvironmentAnimalSettings>();

        // Ensure default settings exist for each environment type
        EnsureSetting(MapEnvironmentType.NeonSciFi, false); // Neon map must NEVER have animals
        EnsureSetting(MapEnvironmentType.ForestNature, true);
        EnsureSetting(MapEnvironmentType.AbandonedWasteland, true);
        EnsureSetting(MapEnvironmentType.ClassicChess, true);

        if (playerTransform == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null) playerTransform = player.transform;
        }
    }

    private void EnsureSetting(MapEnvironmentType env, bool defaultAllow)
    {
        for (int i = 0; i < environmentSettings.Count; i++)
        {
            if (environmentSettings[i].environmentType == env) return;
        }
        environmentSettings.Add(new EnvironmentAnimalSettings { environmentType = env, allowAnimals = defaultAllow });
    }

    private void Awake()
    {
        EnsureInitialized();
    }

    private void Start()
    {
        EnsureInitialized();
        PopulateInitialFloorsIfNeeded();
    }

    /// <summary>
    /// Checks the currently active floors near the player on startup and spawns initial animals
    /// so they are immediately visible on close floor tiles, just like environment props.
    /// </summary>
    public void PopulateInitialFloorsIfNeeded()
    {
        EnsureInitialized();

        MapEnvironmentType currentEnv = FloorManager != null ? FloorManager.GetCurrentEnvironmentType() : MapEnvironmentType.ForestNature;
        EnvironmentAnimalSettings settings = GetSettingsForEnvironment(currentEnv);
        if (!settings.allowAnimals) return;

        activeAnimals.RemoveAll(a => a == null || a.gameObject == null);
        if (activeAnimals.Count >= maxAnimals) return;

        if (FloorManager == null) return;

        var floors = FloorManager.GetActiveFloors();
        if (floors == null) return;

        List<Transform> floorList = new List<Transform>(floors);
        if (playerTransform != null)
        {
            floorList.Sort((a, b) =>
            {
                if (a == null || b == null) return 0;
                float da = Vector3.Distance(a.position, playerTransform.position);
                float db = Vector3.Distance(b.position, playerTransform.position);
                return da.CompareTo(db);
            });
        }

        // Spawn on the closest floors so animals are visible immediately near the player
        int targetInitialCount = Mathf.Min(3, maxAnimals);
        foreach (Transform floor in floorList)
        {
            if (activeAnimals.Count >= targetInitialCount) break;
            if (floor == null) continue;

            TrySpawnAnimalOnFloor(floor, true);
        }
    }

    private void Update()
    {
        if (FloorManager == null) return;

        MapEnvironmentType currentEnv = FloorManager.GetCurrentEnvironmentType();
        
        // Handle map transitions
        if (currentEnv != lastEnv)
        {
            lastEnv = currentEnv;
            EnvironmentAnimalSettings settings = GetSettingsForEnvironment(currentEnv);
            
            if (!settings.allowAnimals && activeAnimals.Count > 0)
            {
                CleanupAllAnimals();
            }
        }
        
        // Clean up nulls from animals that were destroyed by FloorManager's ClearProps
        activeAnimals.RemoveAll(a => a == null || a.gameObject == null);
    }

    public EnvironmentAnimalSettings GetSettingsForEnvironment(MapEnvironmentType env)
    {
        EnsureInitialized();
        foreach (var setting in environmentSettings)
        {
            if (setting.environmentType == env)
                return setting;
        }
        // Default: Neon is false, all other environments allow animals
        bool allow = (env != MapEnvironmentType.NeonSciFi);
        return new EnvironmentAnimalSettings { environmentType = env, allowAnimals = allow };
    }

    /// <summary>
    /// Called by FloorManager when a floor tile is adopted, instantiated, or reused.
    /// Spawns animals strictly on the floor tile following the exact same rules as environment props.
    /// </summary>
    public void SpawnAnimalsForFloor(Transform floor)
    {
        TrySpawnAnimalOnFloor(floor, false);
    }

    public bool TrySpawnAnimalOnFloor(Transform floor, bool forceSpawn = false)
    {
        if (floor == null) return false;
        EnsureInitialized();
        if (FloorManager == null || FloorManager.MaterialsList == null) return false;

        MapEnvironmentType currentEnv = FloorManager.GetCurrentEnvironmentType();
        EnvironmentAnimalSettings settings = GetSettingsForEnvironment(currentEnv);

        if (!settings.allowAnimals) return false;

        // Clean up null references before checking count
        activeAnimals.RemoveAll(a => a == null || a.gameObject == null);

        // Respect population limit
        if (activeAnimals.Count >= maxAnimals) return false;

        // If not force-spawning: roll chance
        if (!forceSpawn)
        {
            // If there are currently no animals in the scene, boost the chance so at least one spawns close
            float effectiveChance = activeAnimals.Count == 0 ? Mathf.Max(0.85f, spawnChancePerFloor) : spawnChancePerFloor;
            if (Random.value > effectiveChance) return false;
        }

        // Get animal array (from override or default)
        GameObject[] animalArray = (settings.specificAnimals != null && settings.specificAnimals.Length > 0) 
            ? settings.specificAnimals 
            : FloorManager.MaterialsList.animals;

        if (animalArray == null || animalArray.Length == 0) return false;

        // Pick a random animal prefab
        GameObject animalPrefab = animalArray[Random.Range(0, animalArray.Length)];
        if (animalPrefab == null) return false;

        float candidateRadius = FloorManager.GetPrefabRadius(animalPrefab);
        if (candidateRadius <= 0f) candidateRadius = 0.5f;

        // Use FloorManager's exact placement checks (strictly inside floor bounds, no prop overlap, no player overlap, no scene obstacle)
        if (!FloorManager.FindValidFloorPosition(floor, candidateRadius, out Vector2 validLocalPos, out Vector3 candidateWorld))
        {
            return false;
        }

        // Check distance against other active animals
        for (int i = 0; i < activeAnimals.Count; i++)
        {
            if (activeAnimals[i] == null) continue;
            if (Vector3.Distance(candidateWorld, activeAnimals[i].transform.position) < minDistanceBetweenAnimals)
            {
                return false; // Too close to another animal
            }
        }

        SpawnAnimal(animalPrefab, validLocalPos, candidateRadius, floor);
        return true;
    }

    private void SpawnAnimal(GameObject prefab, Vector2 localPos, float radius, Transform parentFloor)
    {
        // 1. Calculate candidate world position on the floor tile
        Vector3 candidateWorld = parentFloor.TransformPoint(new Vector3(localPos.x, 0f, localPos.y));

        // 2. Cast downward from 3.0 units above to find the exact top surface of the floor collider
        Vector3 spawnWorldPos = candidateWorld;
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(candidateWorld.x, parentFloor.position.y + 3.0f, candidateWorld.z), Vector3.down, 6.0f);
        float bestGroundY = parentFloor.position.y;
        for (int i = 0; i < hits.Length; i++)
        {
            if (FloorManager.IsGroundOrFloor(hits[i].collider))
            {
                bestGroundY = hits[i].point.y;
                break;
            }
        }
        spawnWorldPos.y = bestGroundY;

        // 3. Instantiate parented to AnimalSpawner (transform) so floor tile recycling never teleports or destroys animals
        Quaternion randomRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        GameObject instance = Instantiate(prefab, spawnWorldPos, randomRot, transform);
        instance.transform.localScale = prefab.transform.localScale;

        // 4. Register placed object with FloorManager so environment props won't overlap the animal's footprint
        FloorManager.RegisterPlacedObject(parentFloor, spawnWorldPos, radius);

        // 5. Ensure demo player input script is removed so legacy Input is never polled
        ithappy.Animals_FREE.MovePlayerInput playerInput = instance.GetComponent<ithappy.Animals_FREE.MovePlayerInput>();
        if (playerInput != null)
        {
            playerInput.enabled = false;
            Destroy(playerInput);
        }

        // 6. Ensure CreatureMover immediately initializes and forces the idle animation pose
        CreatureMover mover = instance.GetComponent<CreatureMover>();
        if (mover != null)
        {
            mover.ForceIdleAnimation();
            if (mover.Controller != null && mover.Controller.enabled)
            {
                mover.Controller.Move(Vector3.down * 0.05f);
            }
        }

        AnimalAI ai = instance.GetComponent<AnimalAI>();
        if (ai == null)
        {
            ai = instance.AddComponent<AnimalAI>();
        }

        if (playerTransform == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null) playerTransform = player.transform;
        }

        ai.Initialize(this, playerTransform);
        activeAnimals.Add(ai);
    }

    public void DespawnAnimal(AnimalAI animal)
    {
        if (activeAnimals.Contains(animal))
        {
            activeAnimals.Remove(animal);
        }
        if (animal != null && animal.gameObject != null)
        {
            Destroy(animal.gameObject);
        }
    }

    private void CleanupAllAnimals()
    {
        foreach (var animal in activeAnimals)
        {
            if (animal != null && animal.gameObject != null)
            {
                Destroy(animal.gameObject);
            }
        }
        activeAnimals.Clear();
    }
}
