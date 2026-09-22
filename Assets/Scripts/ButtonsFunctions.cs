using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles UI map left/right button clicks, navigation animations, and scene transitions.
/// Synchronizes map index changes with MapSelectManager and updates map detail UI text.
/// </summary>
public class buttonFunctions : MonoBehaviour
{
    [Header("Animation Settings")]
    [SerializeField] private Animator uiAnimator;
    [SerializeField] private Animator menuLeaveAnimation;
    [SerializeField] private Animator mapLeaveAnimation;
    [SerializeField] private string mapLeft = "mapLeft";
    [SerializeField] private string mapRight = "MapRight";

    [Header("Map details text")]
    [SerializeField] private TMP_Text mapNumberText;
    [SerializeField] private TMP_Text mapNameText;

    [Header("Manager Reference")]
    [SerializeField] private MapSelectManager mapSelectManager;

    [Header("Scene Management")]
    [SerializeField] string gameSceneName = "Game";

    public static string lastButtonPressed = "";

    /// <summary>
    /// Evaluates the current active map index from MapSelectManager as the single source of truth.
    /// </summary>
    private int CurrentMapIndex
    {
        get
        {
            if (mapSelectManager != null)
            {
                return mapSelectManager.ActiveMapIndex;
            }

            if (MapSelectManager.Instance != null)
            {
                return MapSelectManager.Instance.ActiveMapIndex;
            }

            return MapSelectManager.SelectedMapIndex;
        }
    }

    private void EnsureMapSelectManager()
    {
        if (mapSelectManager == null)
        {
            mapSelectManager = MapSelectManager.Instance != null ? MapSelectManager.Instance : FindObjectOfType<MapSelectManager>();
        }
    }

    public void NavigateAwayFromMenu()
    {
        lastButtonPressed = gameObject.name;
        if (menuLeaveAnimation != null)
        {
            menuLeaveAnimation.Play("menuFadeOut", 0, 0f);
        }
    }

    public void NavigateAwayFromMaps()
    {
        lastButtonPressed = gameObject.name;
        if (mapLeaveAnimation != null)
        {
            mapLeaveAnimation.Play("mapsFadeOut", 0, 0f);
        }
    }

    private void Awake()
    {
        EnsureMapSelectManager();
    }

    private void OnEnable()
    {
        MapSelectManager.OnMapIndexChanged += HandleMapIndexChanged;
        UpdateMapDisplay();
    }

    private void OnDisable()
    {
        MapSelectManager.OnMapIndexChanged -= HandleMapIndexChanged;
    }

    private void HandleMapIndexChanged(int newIndex)
    {
        UpdateMapDisplay();
    }

    private void Start()
    {
        EnsureMapSelectManager();
        UpdateMapDisplay();
    }

    public void LoadGameScene()
    {
        EnsureMapSelectManager();

        if (mapSelectManager != null)
        {
            mapSelectManager.ApplyMaterialChange();
        }

        lastButtonPressed = gameObject.name;
        if (mapLeaveAnimation != null)
        {
            mapLeaveAnimation.Play("mapsFadeOut", 0, 0f);
        }
    }

    /// <summary>
    /// Called when the Left button is pressed.
    /// Steps back to the previous map, communicates with MapSelectManager, and triggers the transition animation.
    /// </summary>
    public void MapLeftPressed()
    {
        EnsureMapSelectManager();

        if (mapSelectManager != null)
        {
            mapSelectManager.PreviousMap();
        }

        if (uiAnimator != null)
        {
            uiAnimator.Play(mapLeft, 0, 0f);
        }
        else if (mapSelectManager != null)
        {
            mapSelectManager.ApplyMaterialChange();
        }

        UpdateMapDisplay();
    }

    /// <summary>
    /// Called when the Right button is pressed.
    /// Advances to the next map, communicates with MapSelectManager, and triggers the transition animation.
    /// </summary>
    public void MapRightPressed()
    {
        EnsureMapSelectManager();

        if (mapSelectManager != null)
        {
            mapSelectManager.NextMap();
        }

        if (uiAnimator != null)
        {
            uiAnimator.Play(mapRight, 0, 0f);
        }
        else if (mapSelectManager != null)
        {
            mapSelectManager.ApplyMaterialChange();
        }

        UpdateMapDisplay();
    }

    /// <summary>
    /// Synchronizes both the map number label (e.g. "MAP 01 OF 05") and map name (e.g. "Checkmate").
    /// </summary>
    public void UpdateMapDisplay()
    {
        if (mapNumberText == null && mapNameText == null)
        {
            return;
        }

        EnsureMapSelectManager();

        int currentIndex = CurrentMapIndex;
        int totalMaps = 0;

        if (mapSelectManager != null)
        {
            totalMaps = mapSelectManager.TotalMaps;
        }
        else if (MapSelectManager.Instance != null)
        {
            totalMaps = MapSelectManager.Instance.TotalMaps;
        }
        else
        {
            totalMaps = MapSelectManager.numberMaps;
        }

        if (totalMaps <= 0)
        {
            totalMaps = 5;
        }

        if (mapNumberText != null)
        {
            mapNumberText.text = MapSelectManager.FormatMapNumber(currentIndex, totalMaps);
        }

        if (mapNameText != null)
        {
            string displayName = "";
            if (mapSelectManager != null)
            {
                displayName = mapSelectManager.GetMapName(currentIndex);
            }
            else if (MapSelectManager.Instance != null)
            {
                displayName = MapSelectManager.Instance.GetMapName(currentIndex);
            }
            else
            {
                displayName = MapSelectManager.GetMapNameByIndex(currentIndex);
            }

            mapNameText.text = displayName;
        }
    }

    private void Update()
    {
        // Only run display update on instances that are assigned to the map text UI
        if (mapNumberText != null || mapNameText != null)
        {
            UpdateMapDisplay();
        }
    }

    /// <summary>
    /// Pauses the game via PauseManager.
    /// </summary>
    public void PauseGame()
    {
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.PauseGame();
        }
        else
        {
            PauseManager pm = FindObjectOfType<PauseManager>();
            if (pm != null) pm.PauseGame();
        }
    }

    /// <summary>
    /// Resumes the game via PauseManager.
    /// </summary>
    public void ResumeGame()
    {
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.ResumeGame();
        }
        else
        {
            PauseManager pm = FindObjectOfType<PauseManager>();
            if (pm != null) pm.ResumeGame();
        }
    }

    /// <summary>
    /// Toggles pause state via PauseManager.
    /// </summary>
    public void TogglePause()
    {
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.TogglePause();
        }
        else
        {
            PauseManager pm = FindObjectOfType<PauseManager>();
            if (pm != null) pm.TogglePause();
        }
    }

    /// <summary>
    /// Opens the restart confirmation popup via PauseManager.
    /// </summary>
    public void OpenRestartConfirmation()
    {
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.OnRestartButtonPressed();
        }
        else
        {
            PauseManager pm = FindObjectOfType<PauseManager>();
            if (pm != null) pm.OnRestartButtonPressed();
        }
    }

    /// <summary>
    /// Opens the quit/home confirmation popup via PauseManager.
    /// </summary>
    public void OpenQuitConfirmation()
    {
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.OnQuitButtonPressed();
        }
        else
        {
            PauseManager pm = FindObjectOfType<PauseManager>();
            if (pm != null) pm.OnQuitButtonPressed();
        }
    }
}

