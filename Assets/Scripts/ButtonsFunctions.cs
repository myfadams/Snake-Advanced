using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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

    [Header("Emergency Shrink (Shed)")]
    [Tooltip("Reference to the Shed Button UI GameObject.")]
    [SerializeField] private GameObject shedButton;

    [Header("Quit Confirmation Popup")]
    [Tooltip("The confirmation popup prefab to instantiate (e.g. Assets/Menu/popup.prefab).")]
    [SerializeField] private GameObject quitPopupPrefab;

    [Tooltip("The confirmation message displayed on the popup.")]
    [SerializeField] private string quitConfirmMessage = "Are you sure you want to quit?";

    [Tooltip("The main menu UI GameObject/panel to fade out while the quit popup appears.")]
    [SerializeField] private GameObject mainMenuUI;

    [Tooltip("Duration of the popup fade and scale animation.")]
    [Range(0.05f, 1f)]
    [SerializeField] private float popupAnimationDuration = 0.25f;

    [Header("Scene Management")]
    [SerializeField] string gameSceneName = "Game";

    public GameObject ShedButton => shedButton;
    public GameObject QuitPopupPrefab { get => quitPopupPrefab; set => quitPopupPrefab = value; }
    public string QuitConfirmMessage { get => quitConfirmMessage; set => quitConfirmMessage = value; }
    public GameObject MainMenuUI { get => mainMenuUI; set => mainMenuUI = value; }

    public static string lastButtonPressed = "";

    private GameObject activeQuitPopupInstance;
    private Coroutine quitPopupCoroutine;

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
    /// Opens the quit confirmation popup.
    /// If PauseManager is active in scene, delegates to it; otherwise opens via QuitGame().
    /// </summary>
    public void OpenQuitConfirmation()
    {
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.OnQuitButtonPressed();
        }
        else
        {
            QuitGame();
        }
    }

    /// <summary>
    /// Called when the Quit button is clicked.
    /// Instantiates/shows the assigned quit popup prefab, sets the confirmation message,
    /// and animates the popup smoothly into view.
    /// </summary>
    public void QuitGame()
    {
        // If in game with PauseManager active and no local popup prefab assigned, use PauseManager
        if (quitPopupPrefab == null && PauseManager.Instance != null)
        {
            PauseManager.Instance.OnQuitButtonPressed();
            return;
        }

        if (quitPopupPrefab == null)
        {
            // Auto-detect popup prefab or scene object if unassigned
            GameObject existing = GameObject.Find("popup");
            if (existing != null) quitPopupPrefab = existing;
        }

        if (mainMenuUI == null)
        {
            // Optional fallback: look for MenuPanel or Menu in scene if unassigned
            GameObject menuPanel = GameObject.Find("MenuPanel");
            if (menuPanel == null) menuPanel = GameObject.Find("Menu");
            if (menuPanel != null) mainMenuUI = menuPanel;
        }

        if (quitPopupPrefab == null)
        {
            Debug.LogWarning("[buttonFunctions] No Quit Popup Prefab assigned. Quitting directly.", this);
            ConfirmQuitGame();
            return;
        }

        Transform parentTransform = GetTargetUIParent();

        // Clean up any existing active popup
        if (activeQuitPopupInstance != null)
        {
            Destroy(activeQuitPopupInstance);
            activeQuitPopupInstance = null;
        }

        if (quitPopupPrefab.scene.IsValid())
        {
            activeQuitPopupInstance = quitPopupPrefab;
        }
        else
        {
            activeQuitPopupInstance = Instantiate(quitPopupPrefab, parentTransform, false);
        }

        activeQuitPopupInstance.SetActive(true);
        activeQuitPopupInstance.transform.SetAsLastSibling();

        // Center on screen
        RectTransform rt = activeQuitPopupInstance.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchoredPosition = Vector2.zero;
            rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f);
        }

        // Start from scale 0 and alpha 0
        activeQuitPopupInstance.transform.localScale = Vector3.zero;
        SetOpacity(activeQuitPopupInstance, 0f);

        // Update the message text inside the popup (child TMP e.g. menuText in popup.prefab)
        TMP_Text tmp = activeQuitPopupInstance.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null && !string.IsNullOrEmpty(quitConfirmMessage))
        {
            tmp.text = quitConfirmMessage;
        }

        // Bind Accept and Decline buttons inside popup
        Button[] buttons = activeQuitPopupInstance.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button btn = buttons[i];
            string bName = btn.gameObject.name.ToLowerInvariant();

            if (bName.Contains("accept") || bName.Contains("yes") || bName.Contains("confirm") || bName.Contains("ok"))
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(ConfirmQuitGame);
            }
            else if (bName.Contains("decline") || bName.Contains("no") || bName.Contains("cancel") || bName.Contains("back"))
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(CloseQuitPopup);
            }
        }

        // Animate popup in and fade out main menu UI simultaneously
        if (quitPopupCoroutine != null)
        {
            StopCoroutine(quitPopupCoroutine);
        }
        quitPopupCoroutine = StartCoroutine(AnimateOpenQuitPopup(activeQuitPopupInstance));
    }

    /// <summary>
    /// Confirms quitting and closes the application or stops Play mode in the editor.
    /// </summary>
    public void ConfirmQuitGame()
    {
        Debug.Log("[buttonFunctions] Application quitting...");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>
    /// Closes the quit confirmation popup smoothly with animation, fading main menu UI back in.
    /// </summary>
    public void CloseQuitPopup()
    {
        if (quitPopupCoroutine != null)
        {
            StopCoroutine(quitPopupCoroutine);
        }
        quitPopupCoroutine = StartCoroutine(AnimateCloseQuitPopup(activeQuitPopupInstance));
    }

    private IEnumerator AnimateOpenQuitPopup(GameObject popup)
    {
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, popupAnimationDuration);
        float startMenuAlpha = mainMenuUI != null ? GetOpacity(mainMenuUI) : 1f;

        if (mainMenuUI != null)
        {
            SetInteractable(mainMenuUI, false);
        }

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            if (popup != null)
            {
                popup.transform.localScale = Vector3.Lerp(Vector3.zero, Vector3.one, smoothT);
                SetOpacity(popup, smoothT);
            }

            if (mainMenuUI != null)
            {
                SetOpacity(mainMenuUI, Mathf.Lerp(startMenuAlpha, 0f, smoothT));
            }

            yield return null;
        }

        if (popup != null)
        {
            popup.transform.localScale = Vector3.one;
            SetOpacity(popup, 1f);
        }

        if (mainMenuUI != null)
        {
            SetOpacity(mainMenuUI, 0f);
            SetInteractable(mainMenuUI, false);
        }

        quitPopupCoroutine = null;
    }

    private IEnumerator AnimateCloseQuitPopup(GameObject popup)
    {
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, popupAnimationDuration);
        float startMenuAlpha = mainMenuUI != null ? GetOpacity(mainMenuUI) : 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            if (popup != null)
            {
                popup.transform.localScale = Vector3.Lerp(Vector3.one, Vector3.zero, smoothT);
                SetOpacity(popup, 1f - smoothT);
            }

            if (mainMenuUI != null)
            {
                SetOpacity(mainMenuUI, Mathf.Lerp(startMenuAlpha, 1f, smoothT));
            }

            yield return null;
        }

        if (popup != null)
        {
            if (popup.scene.IsValid() && popup == quitPopupPrefab)
            {
                popup.SetActive(false);
            }
            else
            {
                Destroy(popup);
            }
            activeQuitPopupInstance = null;
        }

        if (mainMenuUI != null)
        {
            SetOpacity(mainMenuUI, 1f);
            SetInteractable(mainMenuUI, true);
        }

        quitPopupCoroutine = null;
    }

    private static void SetInteractable(GameObject obj, bool interactable)
    {
        if (obj == null) return;

        CanvasGroup cg = obj.GetComponent<CanvasGroup>();
        if (cg == null)
        {
            cg = obj.AddComponent<CanvasGroup>();
        }
        cg.interactable = interactable;
        cg.blocksRaycasts = interactable;
    }

    private Transform GetTargetUIParent()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null) return canvas.transform;

        canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) canvas = FindObjectOfType<Canvas>();

        return canvas != null ? canvas.transform : transform;
    }

    private static void SetOpacity(GameObject obj, float alpha)
    {
        if (obj == null) return;

        CanvasGroup cg = obj.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = alpha;
            cg.blocksRaycasts = alpha > 0.01f;
            return;
        }

        Image img = obj.GetComponent<Image>();
        if (img != null && obj.transform.childCount == 0)
        {
            Color c = img.color;
            c.a = alpha;
            img.color = c;
            return;
        }

        cg = obj.AddComponent<CanvasGroup>();
        cg.alpha = alpha;
        cg.blocksRaycasts = alpha > 0.01f;
    }

    private static float GetOpacity(GameObject obj)
    {
        if (obj == null) return 0f;

        CanvasGroup cg = obj.GetComponent<CanvasGroup>();
        if (cg != null) return cg.alpha;

        Image img = obj.GetComponent<Image>();
        if (img != null) return img.color.a;

        return 1f;
    }

    /// <summary>
    /// Called when the Shed button is pressed.
    /// Triggers emergency shrink via GameStakes.
    /// </summary>
    public void Shed()
    {
        if (GameStakes.Instance != null)
        {
            GameStakes.Instance.TriggerShed();
        }
        else
        {
            GameStakes gs = FindObjectOfType<GameStakes>();
            if (gs != null) gs.TriggerShed();
        }
    }
}

