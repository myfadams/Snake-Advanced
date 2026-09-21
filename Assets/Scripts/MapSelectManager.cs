using UnityEngine;

/// <summary>
/// Manages the map floor environment materials and the active map index.
/// Attached to a standalone GameObject (e.g., "MapSelectManager") in the scene.
/// </summary>
public class MapSelectManager : MonoBehaviour
{
    [Header("Environment References")]
    [Tooltip("The parent GameObject containing the floor pieces.")]
    [SerializeField] private GameObject mapFloor;

    [Tooltip("Materials corresponding to each selectable map.")]
    [SerializeField] private Material[] mapMaterials;

    [Tooltip("The ground prefab whose material will be updated to the chosen map so newly formed floors in the game match the selected menu map.")]
    [SerializeField] private GameObject groundPrefabMaterial;

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

    private void Start()
    {
        if (PlayerPrefs.HasKey("SelectedMapIndex"))
        {
            activeMapIndex = WrapIndex(PlayerPrefs.GetInt("SelectedMapIndex", activeMapIndex));
        }

        // Apply initial material on start so the scene starts on the correct map
        ApplyMaterialChange();
    }

    /// <summary>
    /// Safely wraps the given index within the range [0, mapMaterials.Length - 1].
    /// Handles both positive overflow and negative underflow.
    /// </summary>
    public int WrapIndex(int index)
    {
        if (mapMaterials == null || mapMaterials.Length == 0)
        {
            return 0;
        }

        int count = mapMaterials.Length;
        return ((index % count) + count) % count;
    }

    /// <summary>
    /// Updates the selected map index with safe wrapping.
    /// Does not immediately change the material until ApplyMaterialChange is called.
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
    }

    /// <summary>
    /// Applies the material corresponding to activeMapIndex to all child MeshRenderers in mapFloor.
    /// Called via Animation Event during UI transition animation.
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
