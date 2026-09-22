using UnityEngine;

/// <summary>
/// Relay component attached to the animated UI GameObject (which has the Animator).
/// Receives Animation Events during UI transition clips and triggers the material change on MapSelectManager.
/// </summary>
public class MapAnimationEvents : MonoBehaviour
{
    [Header("Manager Reference")]
    [Tooltip("Reference to the MapSelectManager in the scene.")]
    [SerializeField] private MapSelectManager mapSelectManager;

    private void Awake()
    {
        // Auto-locate MapSelectManager if not assigned in the Inspector
        if (mapSelectManager == null)
        {
            mapSelectManager = FindObjectOfType<MapSelectManager>();
        }
    }

    /// <summary>
    /// Invoked by the Animation Event in the UI transition clip (at switch point / t=0.95s).
    /// </summary>
    public void ApplyMaterialChange()
    {
        if (mapSelectManager == null)
        {
            mapSelectManager = MapSelectManager.Instance != null ? MapSelectManager.Instance : FindObjectOfType<MapSelectManager>();
        }

        if (mapSelectManager != null)
        {
            mapSelectManager.ApplyMaterialChange();
        }
        else
        {
            Debug.LogWarning("[MapAnimationEvents] MapSelectManager reference is missing. Could not apply material change.");
        }
    }
}
