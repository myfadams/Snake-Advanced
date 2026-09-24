using UnityEngine;
using System.Collections.Generic;

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

    private void Awake()
    {
        floorManager = GetComponent<FloorManager>();
        if (floorManager == null) floorManager = FindObjectOfType<FloorManager>();

        // Setup default environment settings if empty
        if (environmentSettings.Count == 0)
        {
            environmentSettings.Add(new EnvironmentAnimalSettings { environmentType = MapEnvironmentType.NeonSciFi, allowAnimals = false });
            environmentSettings.Add(new EnvironmentAnimalSettings { environmentType = MapEnvironmentType.ForestNature, allowAnimals = true });
            environmentSettings.Add(new EnvironmentAnimalSettings { environmentType = MapEnvironmentType.AbandonedWasteland, allowAnimals = true });
            environmentSettings.Add(new EnvironmentAnimalSettings { environmentType = MapEnvironmentType.ClassicChess, allowAnimals = false });
        }

        GameObject player = GameObject.FindWithTag("Player");
        if (player != null) playerTransform = player.transform;
    }

    private void Update()
    {
        if (floorManager == null) return;

        MapEnvironmentType currentEnv = floorManager.GetCurrentEnvironmentType();
        
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

    private EnvironmentAnimalSettings GetSettingsForEnvironment(MapEnvironmentType env)
    {
        foreach (var setting in environmentSettings)
        {
            if (setting.environmentType == env)
                return setting;
        }
        return new EnvironmentAnimalSettings { environmentType = env, allowAnimals = false };
    }

    /// <summary>
    /// Called by FloorManager when a floor tile is adopted, instantiated, or reused.
    /// Spawns animals strictly on the floor tile following the exact same rules as environment props.
    /// </summary>
    public void SpawnAnimalsForFloor(Transform floor)
    {
        if (floor == null) return;
        if (floorManager == null) floorManager = GetComponent<FloorManager>() ?? FindObjectOfType<FloorManager>();
        if (floorManager == null || floorManager.MaterialsList == null) return;

        MapEnvironmentType currentEnv = floorManager.GetCurrentEnvironmentType();
        EnvironmentAnimalSettings settings = GetSettingsForEnvironment(currentEnv);

        if (!settings.allowAnimals) return;

        // Clean up null references before checking count
        activeAnimals.RemoveAll(a => a == null || a.gameObject == null);

        // Respect population limit
        if (activeAnimals.Count >= maxAnimals) return;

        // Roll spawn chance per floor tile for natural distribution
        if (Random.value > spawnChancePerFloor) return;

        // Get animal array (from override or default)
        GameObject[] animalArray = (settings.specificAnimals != null && settings.specificAnimals.Length > 0) 
            ? settings.specificAnimals 
            : floorManager.MaterialsList.animals;

        if (animalArray == null || animalArray.Length == 0) return;

        // Pick a random animal prefab
        GameObject animalPrefab = animalArray[Random.Range(0, animalArray.Length)];
        if (animalPrefab == null) return;

        float candidateRadius = floorManager.GetPrefabRadius(animalPrefab);
        if (candidateRadius <= 0f) candidateRadius = 0.5f;

        // Use FloorManager's exact placement checks (strictly inside floor bounds, no prop overlap, no player overlap, no scene obstacle)
        if (!floorManager.FindValidFloorPosition(floor, candidateRadius, out Vector2 validLocalPos, out Vector3 candidateWorld))
        {
            return;
        }

        // Check distance against other active animals
        for (int i = 0; i < activeAnimals.Count; i++)
        {
            if (activeAnimals[i] == null) continue;
            if (Vector3.Distance(candidateWorld, activeAnimals[i].transform.position) < minDistanceBetweenAnimals)
            {
                return; // Too close to another animal
            }
        }

        SpawnAnimal(animalPrefab, validLocalPos, candidateRadius, floor);
    }

    private void SpawnAnimal(GameObject prefab, Vector2 localPos, float radius, Transform parentFloor)
    {
        // 1. Instantiate parented directly to the floor tile
        GameObject instance = Instantiate(prefab, parentFloor);
        
        // 2. Temporarily disable CharacterController so Unity's physics doesn't depenetrate or pop it off the tile
        CharacterController cc = instance.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        instance.transform.localScale = prefab.transform.localScale;
        instance.transform.localPosition = new Vector3(localPos.x, 0f, localPos.y);
        instance.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // 3. Align vertical position so animal feet sit flush on the floor surface (same as environment props)
        floorManager.AlignPropToFloorSurface(instance, parentFloor);
        if (cc != null) cc.enabled = true;

        // 4. Parent has FloorPropMarker so FloorManager.ClearProps() destroys it when the floor despawns
        if (instance.GetComponent<FloorPropMarker>() == null)
        {
            instance.AddComponent<FloorPropMarker>();
        }

        // 5. Register in FloorManager so future props won't spawn on top of this animal
        floorManager.RegisterPlacedObject(parentFloor, instance.transform.position, radius);

        AnimalAI ai = instance.GetComponent<AnimalAI>();
        if (ai == null)
        {
            Debug.LogWarning("Spawned animal prefab is missing AnimalAI component! Adding it automatically.");
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
