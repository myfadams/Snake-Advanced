using TMPro;
using UnityEngine;

/// <summary>
/// Manages the map floor environment materials, active map index, and map metadata.
/// Attached to a standalone GameObject (e.g., "MapSelectManager") in the scene.
/// </summary>
public class MapSelectManager : MonoBehaviour
{
    public static MapSelectManager Instance { get; private set; }

    /// <summary>
    /// Event triggered whenever the active map index changes.
    /// </summary>
    public static event System.Action<int> OnMapIndexChanged;

    [Header("Environment References")]
    [Tooltip("The parent GameObject containing the floor pieces.")]
    [SerializeField] private GameObject mapFloor;

    [Tooltip("Materials corresponding to each selectable map.")]
    [SerializeField] private Material[] mapMaterials;

    [Tooltip("The ground prefab whose material will be updated to the chosen map so newly formed floors in the game match the selected menu map.")]
    [SerializeField] private GameObject groundPrefabMaterial;

    [Header("Optional UI References")]
    [Tooltip("Optional direct reference to the map number display text.")]
    [SerializeField] private TMP_Text mapNumberText;

    [Tooltip("Optional direct reference to the map name display text.")]
    [SerializeField] private TMP_Text mapNameText;

    /// <summary>
    /// Static reference to the currently selected map material so any script (e.g. FloorManager or game start) can access it across scenes.
    /// </summary>
    public static Material SelectedMapMaterial { get; private set; }

    /// <summary>
    /// Static reference to the selected map index across scenes.
    /// </summary>
    public static int SelectedMapIndex { get; private set; } = 0;

    [Header("State")]
    [Tooltip("The currently selected map index.")]
    [SerializeField] private int activeMapIndex = 0;

    /// <summary>
    /// Current selected map index with boundary wrapping.
    /// </summary>
    public int ActiveMapIndex
    {
        get => activeMapIndex;
        set => activeMapIndex = WrapIndex(value);
    }

    /// <summary>
    /// Total count of available map materials.
    /// </summary>
    public int TotalMaps => mapMaterials != null ? mapMaterials.Length : 0;

    public static int numberMaps;

    private void Awake()
    {
        Instance = this;

        if (mapMaterials != null)
        {
            numberMaps = mapMaterials.Length;
        }

        if (PlayerPrefs.HasKey("SelectedMapIndex"))
        {
            activeMapIndex = WrapIndex(PlayerPrefs.GetInt("SelectedMapIndex", activeMapIndex));
        }

        SelectedMapIndex = activeMapIndex;

        if (mapMaterials != null && activeMapIndex >= 0 && activeMapIndex < mapMaterials.Length)
        {
            SelectedMapMaterial = mapMaterials[activeMapIndex];
        }

        UpdateUI();
    }

    private void Start()
    {
        if (mapMaterials != null)
        {
            numberMaps = mapMaterials.Length;
        }

        // Apply initial material on start so the scene starts on the correct map
        ApplyMaterialChange();
        UpdateUI();
    }

    /// <summary>
    /// Returns the human-readable display name mapped to a given material name.
    /// Supports variations with dashes, spaces, and (Instance) suffixes.
    /// </summary>
    public static string GetMapDisplayName(string materialName)
    {
        if (string.IsNullOrWhiteSpace(materialName))
        {
            return "Unknown Map";
        }

        // Remove any Unity "(Instance)" suffix and trim
        string clean = materialName.Replace("(Instance)", "").Trim();

        // Normalise by removing dashes, underscores, and spaces, and convert to lowercase
        string normalized = clean.ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", "");

        switch (normalized)
        {
            case "neon":
                return "Neon Rush";
            case "overgrownpavement":
                return "Wild Streets";
            case "mud":
                return "Mudlands";
            case "chess":
            case "chessboard":
                return "Checkmate";
            case "ground":
                return "Open Grounds";
            default:
                return clean;
        }
    }

    /// <summary>
    /// Returns the human-readable display name for the given Material asset.
    /// </summary>
    public static string GetMapDisplayName(Material material)
    {
        if (material == null) return "Unknown Map";
        return GetMapDisplayName(material.name);
    }

    /// <summary>
    /// Returns the display name of the map at the given index.
    /// </summary>
    public string GetMapName(int index)
    {
        if (mapMaterials != null && index >= 0 && index < mapMaterials.Length && mapMaterials[index] != null)
        {
            return GetMapDisplayName(mapMaterials[index]);
        }

        return GetMapNameByIndex(index);
    }

    /// <summary>
    /// Fallback static lookup for default map display names by index.
    /// </summary>
    public static string GetMapNameByIndex(int index)
    {
        switch (index)
        {
            case 0: return "Checkmate";
            case 1: return "Open Grounds";
            case 2: return "Mudlands";
            case 3: return "Neon Rush";
            case 4: return "Wild Streets";
            default: return $"Map {(index + 1):D2}";
        }
    }

    /// <summary>
    /// Formats the map number label with two-digit padding and correct "OF" text (e.g. "MAP 01 OF 05").
    /// </summary>
    public static string FormatMapNumber(int activeIndex, int totalMaps)
    {
        int current = activeIndex + 1;
        int total = totalMaps > 0 ? totalMaps : 5;
        return $"MAP {current:D2} OF {total:D2}";
    }

    /// <summary>
    /// Safely wraps the given index within the range [0, mapMaterials.Length - 1].
    /// Handles both positive overflow and negative underflow.
    /// </summary>
    public int WrapIndex(int index)
    {
        int count = TotalMaps;
        if (count <= 0)
        {
            return 0;
        }

        return ((index % count) + count) % count;
    }

    /// <summary>
    /// Advances to the next map.
    /// </summary>
    public void NextMap()
    {
        SetActiveMapIndex(activeMapIndex + 1);
    }

    /// <summary>
    /// Returns to the previous map.
    /// </summary>
    public void PreviousMap()
    {
        SetActiveMapIndex(activeMapIndex - 1);
    }

    /// <summary>
    /// Updates the selected map index with safe wrapping and caches state.
    /// Does not immediately change 3D floor materials until ApplyMaterialChange is called.
    /// </summary>
    public void SetActiveMapIndex(int newIndex)
    {
        activeMapIndex = WrapIndex(newIndex);
        SelectedMapIndex = activeMapIndex;
        PlayerPrefs.SetInt("SelectedMapIndex", activeMapIndex);
        PlayerPrefs.Save();

        if (mapMaterials != null && activeMapIndex >= 0 && activeMapIndex < mapMaterials.Length)
        {
            SelectedMapMaterial = mapMaterials[activeMapIndex];
        }

        UpdateUI();
        OnMapIndexChanged?.Invoke(activeMapIndex);
    }

    /// <summary>
    /// Updates UI text components attached directly to this manager if present.
    /// </summary>
    public void UpdateUI()
    {
        int total = TotalMaps > 0 ? TotalMaps : numberMaps;
        if (total <= 0) total = 5;

        if (mapNumberText != null)
        {
            mapNumberText.text = FormatMapNumber(activeMapIndex, total);
        }

        if (mapNameText != null)
        {
            mapNameText.text = GetMapName(activeMapIndex);
        }
    }

    /// <summary>
    /// Applies the material corresponding to activeMapIndex to all child MeshRenderers in mapFloor.
    /// Called via Animation Event during UI transition animation or when entering the arena.
    /// </summary>
    public void ApplyMaterialChange()
    {
        if (mapFloor == null)
        {
            Debug.LogWarning("[MapSelectManager] mapFloor reference is not set in the Inspector.");
            return;
        }

        if (mapMaterials == null || mapMaterials.Length == 0)
        {
            Debug.LogWarning("[MapSelectManager] mapMaterials array is empty or not set.");
            return;
        }

        activeMapIndex = WrapIndex(activeMapIndex);

        Material selectedMaterial = mapMaterials[activeMapIndex];
        if (selectedMaterial == null)
        {
            Debug.LogWarning($"[MapSelectManager] Material at index {activeMapIndex} is null.");
            return;
        }

        // Change all child renderers (including all 4 floor pieces)
        MeshRenderer[] renderers = mapFloor.GetComponentsInChildren<MeshRenderer>(true);
        foreach (MeshRenderer ren in renderers)
        {
            if (ren != null)
            {
                ren.material = selectedMaterial;
            }
        }

        // Cache the selected material for any scripts starting or loading the game
        SelectedMapMaterial = selectedMaterial;

        // Update the ground prefab material so floors formed from the prefab in-game match the selected map
        if (groundPrefabMaterial != null)
        {
            Renderer[] prefabRenderers = groundPrefabMaterial.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer ren in prefabRenderers)
            {
                if (ren != null)
                {
                    ren.sharedMaterial = selectedMaterial;
                }
            }
        }
    }
}
