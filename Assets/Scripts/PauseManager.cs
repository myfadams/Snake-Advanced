using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// Manages pausing the game, activating/instantiating the background dimming overlay, pause menu,
/// and confirmation popups (for Restart and Home/Quit).
/// Handles smooth animations (fade-in, fade-out, scale-up, and background dimming transitions)
/// using unscaled time so animations run smoothly while Time.timeScale = 0.
/// </summary>
public class PauseManager : MonoBehaviour
{
    public enum ConfirmationAction
    {
        Restart,
        QuitToMainMenu
    }

    public static PauseManager Instance { get; private set; }

    [Header("UI Prefabs & References")]
    [Tooltip("The pause menu prefab to instantiate when paused (e.g. mainPause).")]
    [SerializeField] private GameObject menuPrefab;

    [Tooltip("The background overlay UI element or prefab that dims the screen (e.g. menuBg).")]
    [SerializeField] private GameObject backgroundUIElement;

    [Tooltip("Parent transform to instantiate the menu and background under (usually Canvas). If unassigned, automatically finds the nearest Canvas in the scene.")]
    [SerializeField] private Transform uiParent;

    [Tooltip("Optional reference to the Pause Button GameObject to hide while paused.")]
    [SerializeField] private GameObject pauseButton;

    [Header("Confirmation Popup Settings")]
    [Tooltip("The confirmation popup prefab to instantiate (e.g. Assets/Menu/popup.prefab).")]
    [SerializeField] private GameObject confirmationPopupPrefab;

    [Tooltip("Target background opacity when the confirmation popup is active (default 0.8).")]
    [Range(0f, 1f)]
    [SerializeField] private float popupBackgroundOpacity = 0.8f;

    [Tooltip("Text message to display when confirming restart.")]
    [SerializeField] private string restartConfirmMessage = "Are you sure you want to restart?";

    [Tooltip("Text message to display when confirming quitting to main menu.")]
    [SerializeField] private string quitConfirmMessage = "Are you sure you want to quit?";

    [Header("Animation Settings")]
    [Tooltip("Duration of the fade-in and scale-up animation in seconds (unscaled time).")]
    [Range(0.05f, 2f)]
    [SerializeField] private float animationDuration = 0.3f;

    [Tooltip("Target opacity/alpha for the background overlay when the pause menu is open (default 0.5).")]
    [Range(0f, 1f)]
    [SerializeField] private float targetBackgroundOpacity = 0.5f;

    [Tooltip("Easing curve for scaling up menus and popups from the center.")]
    [SerializeField] private AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Interaction & Shortcuts")]
    [Tooltip("If enabled, pressing Escape or P toggles pause.")]
    [SerializeField] private bool enableKeyboardShortcut = true;

    [Tooltip("Automatically wire up buttons inside the instantiated menu named 'resume', 'restart', 'quit', etc.")]
    [SerializeField] private bool autoBindMenuButtons = true;

    [Tooltip("Scene to load when quitting to the main menu.")]
    [SerializeField] private string mainMenuSceneName = "Menus";

    [Header("Instantiation Settings")]
    [Tooltip("If true, always instantiates a new instance of backgroundUIElement even if it is a scene object. If false, activates the scene object directly if it is in the scene.")]
    [SerializeField] private bool alwaysInstantiateBackground = false;

    private bool isPaused = false;
    private GameObject currentMenuInstance;
    private GameObject currentBackgroundInstance;
    private GameObject currentPopupInstance;
    private Coroutine transitionCoroutine;
    private Coroutine popupTransitionCoroutine;
    private bool isInstantiatedBackground = false;
    private Animator cachedBackgroundAnimator;

    public bool IsPaused => isPaused;
    public GameObject MenuPrefab { get => menuPrefab; set => menuPrefab = value; }
    public GameObject BackgroundUIElement { get => backgroundUIElement; set => backgroundUIElement = value; }
    public GameObject ConfirmationPopupPrefab { get => confirmationPopupPrefab; set => confirmationPopupPrefab = value; }
    public float TargetBackgroundOpacity { get => targetBackgroundOpacity; set => targetBackgroundOpacity = value; }
    public float PopupBackgroundOpacity { get => popupBackgroundOpacity; set => popupBackgroundOpacity = value; }
    public string RestartConfirmMessage { get => restartConfirmMessage; set => restartConfirmMessage = value; }
    public string QuitConfirmMessage { get => quitConfirmMessage; set => quitConfirmMessage = value; }
    public GameObject CurrentMenuInstance => currentMenuInstance;
    public GameObject CurrentPopupInstance => currentPopupInstance;

    private void OnValidate()
    {
        if (scaleCurve == null || scaleCurve.length == 0)
        {
            scaleCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (pauseButton == null && GetComponent<Button>() != null)
        {
            pauseButton = gameObject;
        }

        if (scaleCurve == null || scaleCurve.length == 0)
        {
            scaleCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }
    }

    private void Update()
    {
        if (enableKeyboardShortcut)
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (currentPopupInstance != null && currentPopupInstance.activeSelf)
                {
                    // Escape closes the active popup first
                    CloseConfirmationPopup();
                }
                else
                {
                    TogglePause();
                }
            }
        }
    }

    /// <summary>
    /// Toggles the pause state of the game.
    /// </summary>
    public void TogglePause()
    {
        if (isPaused)
        {
            ResumeGame();
        }
        else
        {
            PauseGame();
        }
    }

    /// <summary>
    /// Pauses the game, activates/instantiates the background and menu, and animates them in.
    /// </summary>
    public void PauseGame()
    {
        if (isPaused) return;

        isPaused = true;
        Time.timeScale = 0f;

        Transform parentTransform = GetTargetUIParent();

        // 1. Setup & Activate Background Overlay
        SetupBackground(parentTransform);

        // 2. Setup & Activate Menu Instance
        SetupMenu(parentTransform);

        // 3. Hide Pause Button visually without disabling this script's GameObject
        SetPauseButtonVisible(false);

        // 4. Start Unscaled Animation In
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
        }
        transitionCoroutine = StartCoroutine(AnimateInCoroutine());
    }

    /// <summary>
    /// Resumes the game, animates out the background and menu, and restores Time.timeScale = 1.
    /// </summary>
    public void ResumeGame()
    {
        if (!isPaused) return;

        // If a popup is open, close/destroy it
        if (popupTransitionCoroutine != null)
        {
            StopCoroutine(popupTransitionCoroutine);
            popupTransitionCoroutine = null;
        }

        if (currentPopupInstance != null)
        {
            Destroy(currentPopupInstance);
            currentPopupInstance = null;
        }

        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
        }
        transitionCoroutine = StartCoroutine(AnimateOutCoroutine());
    }

    /// <summary>
    /// Called when the Restart button is pressed.
    /// Opens the confirmation popup if assigned; otherwise restarts immediately.
    /// </summary>
    public void OnRestartButtonPressed()
    {
        if (confirmationPopupPrefab != null)
        {
            OpenConfirmation(ConfirmationAction.Restart, restartConfirmMessage);
        }
        else
        {
            Debug.LogWarning("[PauseManager] Confirmation Popup Prefab is not assigned in the Inspector. Restarting directly.", this);
            RestartGame();
        }
    }

    /// <summary>
    /// Called when the Home / Quit button is pressed.
    /// Opens the confirmation popup if assigned; otherwise quits immediately.
    /// </summary>
    public void OnQuitButtonPressed()
    {
        if (confirmationPopupPrefab != null)
        {
            OpenConfirmation(ConfirmationAction.QuitToMainMenu, quitConfirmMessage);
        }
        else
        {
            Debug.LogWarning("[PauseManager] Confirmation Popup Prefab is not assigned in the Inspector. Quitting to main menu directly.", this);
            QuitToMainMenu();
        }
    }

    /// <summary>
    /// Opens confirmation popup for the specified action (Restart or Quit).
    /// </summary>
    public void OpenConfirmation(ConfirmationAction action, string message)
    {
        if (confirmationPopupPrefab == null)
        {
            if (action == ConfirmationAction.Restart) RestartGame();
            else QuitToMainMenu();
            return;
        }

        Transform parentTransform = GetTargetUIParent();

        // 1. Setup popup GameObject and wire buttons
        SetupPopup(parentTransform, action, message);

        // 2. Animate: menu fades/scales out, background goes to 0.8, popup fades/scales in
        if (popupTransitionCoroutine != null)
        {
            StopCoroutine(popupTransitionCoroutine);
        }
        popupTransitionCoroutine = StartCoroutine(AnimateOpenPopupCoroutine());
    }

    /// <summary>
    /// Closes the confirmation popup, restores background to 0.5, and animates the pause menu back in.
    /// </summary>
    public void CloseConfirmationPopup()
    {
        if (popupTransitionCoroutine != null)
        {
            StopCoroutine(popupTransitionCoroutine);
        }
        popupTransitionCoroutine = StartCoroutine(AnimateClosePopupCoroutine());
    }

    /// <summary>
    /// Reloads the active scene (called when restart is confirmed).
    /// </summary>
    public void RestartGame()
    {
        Time.timeScale = 1f;
        isPaused = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>
    /// Loads the main menu scene (called when quit/home is confirmed).
    /// </summary>
    public void QuitToMainMenu()
    {
        Time.timeScale = 1f;
        isPaused = false;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private Transform GetTargetUIParent()
    {
        if (uiParent != null) return uiParent;

        // Try getting parent Canvas of this GameObject
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas != null) return parentCanvas.transform;

        // Search in scene
        Canvas anyCanvas = FindFirstObjectByType<Canvas>();
        if (anyCanvas == null)
        {
            anyCanvas = FindObjectOfType<Canvas>();
        }

        return anyCanvas != null ? anyCanvas.transform : transform;
    }

    private void SetupBackground(Transform parent)
    {
        if (backgroundUIElement == null)
        {
            Debug.LogWarning("[PauseManager] backgroundUIElement is not assigned in the Inspector!", this);
            return;
        }

        bool isSceneObject = backgroundUIElement.scene.IsValid();

        if (isSceneObject && !alwaysInstantiateBackground)
        {
            currentBackgroundInstance = backgroundUIElement;
            isInstantiatedBackground = false;
        }
        else
        {
            // Instantiate background prefab
            currentBackgroundInstance = Instantiate(backgroundUIElement, parent, false);
            isInstantiatedBackground = true;

            // Anchor to full screen stretch if RectTransform
            RectTransform rt = currentBackgroundInstance.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f);
            }
        }

        // CRITICAL: Explicitly activate background and all of its parent hierarchy
        currentBackgroundInstance.SetActive(true);
        Transform p = currentBackgroundInstance.transform.parent;
        while (p != null && p != parent && p != p.root)
        {
            p.gameObject.SetActive(true);
            p = p.parent;
        }

        currentBackgroundInstance.transform.SetAsLastSibling();

        // Suppress any active Animator on the background so it doesn't fight the alpha fade
        cachedBackgroundAnimator = currentBackgroundInstance.GetComponent<Animator>();
        if (cachedBackgroundAnimator != null && cachedBackgroundAnimator.enabled)
        {
            cachedBackgroundAnimator.enabled = false;
        }

        // Start from opacity 0 before fading in
        SetOpacity(currentBackgroundInstance, 0f);
    }

    private void SetupMenu(Transform parent)
    {
        if (menuPrefab == null)
        {
            Debug.LogError("[PauseManager] Menu Prefab is not assigned in the Inspector! Please assign your pause menu prefab (e.g. mainPause).", this);
            return;
        }

        // Instantiate menu prefab under the Canvas without preserving world-space coordinates
        currentMenuInstance = Instantiate(menuPrefab, parent, false);
        currentMenuInstance.SetActive(true);

        // Ensure parent hierarchy is active
        Transform p = currentMenuInstance.transform.parent;
        while (p != null && p != parent && p != p.root)
        {
            p.gameObject.SetActive(true);
            p = p.parent;
        }

        currentMenuInstance.transform.SetAsLastSibling();

        // Center on screen and zero out Z
        RectTransform rt = currentMenuInstance.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchoredPosition = Vector2.zero;
            rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f);
        }

        // Start from scale 0 at the center (will animate up to full scale)
        currentMenuInstance.transform.localScale = Vector3.zero;

        // Auto-bind internal buttons if requested
        if (autoBindMenuButtons)
        {
            BindMenuButtons(currentMenuInstance);
        }
    }

    private void SetupPopup(Transform parent, ConfirmationAction action, string message)
    {
        if (currentPopupInstance != null)
        {
            Destroy(currentPopupInstance);
            currentPopupInstance = null;
        }

        currentPopupInstance = Instantiate(confirmationPopupPrefab, parent, false);
        currentPopupInstance.SetActive(true);

        currentPopupInstance.transform.SetAsLastSibling();

        // Center on screen
        RectTransform rt = currentPopupInstance.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchoredPosition = Vector2.zero;
            rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f);
        }

        // Set initial scale and opacity to 0
        currentPopupInstance.transform.localScale = Vector3.zero;
        SetOpacity(currentPopupInstance, 0f);

        // Find and update the TMP text message (e.g. menuText in popup.prefab)
        TMP_Text tmp = currentPopupInstance.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null && !string.IsNullOrEmpty(message))
        {
            tmp.text = message;
        }

        // Bind Accept and Decline buttons inside popup
        Button[] buttons = currentPopupInstance.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button btn = buttons[i];
            string bName = btn.gameObject.name.ToLowerInvariant();

            if (bName.Contains("accept") || bName.Contains("yes") || bName.Contains("confirm") || bName.Contains("ok"))
            {
                btn.onClick.RemoveAllListeners();
                if (action == ConfirmationAction.Restart)
                {
                    btn.onClick.AddListener(RestartGame);
                }
                else
                {
                    btn.onClick.AddListener(QuitToMainMenu);
                }
            }
            else if (bName.Contains("decline") || bName.Contains("no") || bName.Contains("cancel") || bName.Contains("back"))
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(CloseConfirmationPopup);
            }
        }
    }

    private void BindMenuButtons(GameObject menu)
    {
        Button[] buttons = menu.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button btn = buttons[i];
            string btnName = btn.gameObject.name.ToLowerInvariant();

            if (btnName.Contains("resume") || btnName.Contains("continue") || btnName.Contains("play"))
            {
                btn.onClick.RemoveListener(ResumeGame);
                btn.onClick.AddListener(ResumeGame);
            }
            else if (btnName.Contains("restart") || btnName.Contains("retry"))
            {
                btn.onClick.RemoveListener(OnRestartButtonPressed);
                btn.onClick.AddListener(OnRestartButtonPressed);
            }
            else if (btnName.Contains("quit") || btnName.Contains("exit") || btnName.Contains("home") || btnName.Contains("menu"))
            {
                btn.onClick.RemoveListener(OnQuitButtonPressed);
                btn.onClick.AddListener(OnQuitButtonPressed);
            }
        }
    }

    private void SetPauseButtonVisible(bool visible)
    {
        if (pauseButton == null) return;

        if (pauseButton == gameObject)
        {
            // CRITICAL: Never call SetActive(false) on the GameObject running this script!
            // Deactivating this GameObject immediately terminates all coroutines and Update().
            // Instead, hide visuals and disable clicks via CanvasGroup.
            CanvasGroup cg = GetComponent<CanvasGroup>();
            if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
            cg.alpha = visible ? 1f : 0f;
            cg.interactable = visible;
            cg.blocksRaycasts = visible;
        }
        else
        {
            pauseButton.SetActive(visible);
        }
    }

    private IEnumerator AnimateInCoroutine()
    {
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, animationDuration);

        Vector3 targetScale = Vector3.one;
        if (menuPrefab != null && menuPrefab.transform.localScale != Vector3.zero)
        {
            targetScale = menuPrefab.transform.localScale;
        }

        // Make sure background and menu are explicitly active before animating
        if (currentBackgroundInstance != null)
        {
            currentBackgroundInstance.SetActive(true);
            SetOpacity(currentBackgroundInstance, 0f);
        }

        if (currentMenuInstance != null)
        {
            currentMenuInstance.SetActive(true);
            currentMenuInstance.transform.localScale = Vector3.zero;
        }

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // 1. Fade background from 0 to targetBackgroundOpacity (0.5)
            if (currentBackgroundInstance != null)
            {
                float currentAlpha = Mathf.Lerp(0f, targetBackgroundOpacity, t);
                SetOpacity(currentBackgroundInstance, currentAlpha);
            }

            // 2. Scale menu up from center using the easing curve
            if (currentMenuInstance != null)
            {
                float curveValue = (scaleCurve != null && scaleCurve.length > 0) ? scaleCurve.Evaluate(t) : t;
                currentMenuInstance.transform.localScale = targetScale * curveValue;
            }

            yield return null;
        }

        // Ensure final values
        if (currentBackgroundInstance != null)
        {
            SetOpacity(currentBackgroundInstance, targetBackgroundOpacity);
        }

        if (currentMenuInstance != null)
        {
            currentMenuInstance.transform.localScale = targetScale;
        }

        transitionCoroutine = null;
    }

    private IEnumerator AnimateOutCoroutine()
    {
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, animationDuration);

        float initialAlpha = currentBackgroundInstance != null ? GetOpacity(currentBackgroundInstance) : targetBackgroundOpacity;
        Vector3 initialScale = currentMenuInstance != null ? currentMenuInstance.transform.localScale : Vector3.one;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // 1. Fade background to 0
            if (currentBackgroundInstance != null)
            {
                float currentAlpha = Mathf.Lerp(initialAlpha, 0f, t);
                SetOpacity(currentBackgroundInstance, currentAlpha);
            }

            // 2. Scale menu down to 0
            if (currentMenuInstance != null)
            {
                currentMenuInstance.transform.localScale = initialScale * (1f - t);
            }

            yield return null;
        }

        // Restore animator on background if it had one
        if (cachedBackgroundAnimator != null)
        {
            cachedBackgroundAnimator.enabled = true;
            cachedBackgroundAnimator = null;
        }

        // Clean up or deactivate
        if (currentMenuInstance != null)
        {
            Destroy(currentMenuInstance);
            currentMenuInstance = null;
        }

        if (currentBackgroundInstance != null)
        {
            if (isInstantiatedBackground)
            {
                Destroy(currentBackgroundInstance);
            }
            else
            {
                SetOpacity(currentBackgroundInstance, 0f);
                currentBackgroundInstance.SetActive(false);
            }
            currentBackgroundInstance = null;
        }

        // Show pause button again
        SetPauseButtonVisible(true);

        Time.timeScale = 1f;
        isPaused = false;
        transitionCoroutine = null;
    }

    private IEnumerator AnimateOpenPopupCoroutine()
    {
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, animationDuration);

        float startBgAlpha = currentBackgroundInstance != null ? GetOpacity(currentBackgroundInstance) : targetBackgroundOpacity;
        float targetBgAlpha = popupBackgroundOpacity; // 0.8f

        Vector3 startMenuScale = currentMenuInstance != null ? currentMenuInstance.transform.localScale : Vector3.one;
        float startMenuAlpha = currentMenuInstance != null ? GetOpacity(currentMenuInstance) : 1f;

        Vector3 targetPopupScale = Vector3.one;
        if (confirmationPopupPrefab != null && confirmationPopupPrefab.transform.localScale != Vector3.zero)
        {
            targetPopupScale = confirmationPopupPrefab.transform.localScale;
        }

        // Disable pause menu interaction during and after transition
        if (currentMenuInstance != null)
        {
            CanvasGroup menuCg = currentMenuInstance.GetComponent<CanvasGroup>();
            if (menuCg != null)
            {
                menuCg.interactable = false;
                menuCg.blocksRaycasts = false;
            }
        }

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // 1. Transition background opacity from 0.5 to 0.8
            if (currentBackgroundInstance != null)
            {
                SetOpacity(currentBackgroundInstance, Mathf.Lerp(startBgAlpha, targetBgAlpha, t));
            }

            // 2. Animate pause menu fading out & scaling down slightly
            if (currentMenuInstance != null)
            {
                SetOpacity(currentMenuInstance, Mathf.Lerp(startMenuAlpha, 0f, t));
                currentMenuInstance.transform.localScale = Vector3.Lerp(startMenuScale, Vector3.zero, t);
            }

            // 3. Animate popup fading in & scaling up from center
            if (currentPopupInstance != null)
            {
                float curveValue = (scaleCurve != null && scaleCurve.length > 0) ? scaleCurve.Evaluate(t) : t;
                SetOpacity(currentPopupInstance, t);
                currentPopupInstance.transform.localScale = targetPopupScale * curveValue;
            }

            yield return null;
        }

        // Ensure final values
        if (currentBackgroundInstance != null)
        {
            SetOpacity(currentBackgroundInstance, targetBgAlpha);
        }

        if (currentMenuInstance != null)
        {
            SetOpacity(currentMenuInstance, 0f);
            currentMenuInstance.transform.localScale = Vector3.zero;
            currentMenuInstance.SetActive(false); // Fully hide while popup is open
        }

        if (currentPopupInstance != null)
        {
            SetOpacity(currentPopupInstance, 1f);
            currentPopupInstance.transform.localScale = targetPopupScale;
            CanvasGroup popupCg = currentPopupInstance.GetComponent<CanvasGroup>();
            if (popupCg != null)
            {
                popupCg.interactable = true;
                popupCg.blocksRaycasts = true;
            }
        }

        popupTransitionCoroutine = null;
    }

    private IEnumerator AnimateClosePopupCoroutine()
    {
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, animationDuration);

        float startBgAlpha = currentBackgroundInstance != null ? GetOpacity(currentBackgroundInstance) : popupBackgroundOpacity;
        float targetBgAlpha = targetBackgroundOpacity; // 0.5f

        Vector3 startPopupScale = currentPopupInstance != null ? currentPopupInstance.transform.localScale : Vector3.one;
        float startPopupAlpha = currentPopupInstance != null ? GetOpacity(currentPopupInstance) : 1f;

        Vector3 targetMenuScale = (menuPrefab != null && menuPrefab.transform.localScale != Vector3.zero) ? menuPrefab.transform.localScale : Vector3.one;

        // Re-activate pause menu
        if (currentMenuInstance != null)
        {
            currentMenuInstance.SetActive(true);
            SetOpacity(currentMenuInstance, 0f);
            currentMenuInstance.transform.localScale = Vector3.zero;
        }

        // Disable popup interaction during transition
        if (currentPopupInstance != null)
        {
            CanvasGroup popupCg = currentPopupInstance.GetComponent<CanvasGroup>();
            if (popupCg != null)
            {
                popupCg.interactable = false;
                popupCg.blocksRaycasts = false;
            }
        }

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // 1. Transition background opacity back from 0.8 to 0.5
            if (currentBackgroundInstance != null)
            {
                SetOpacity(currentBackgroundInstance, Mathf.Lerp(startBgAlpha, targetBgAlpha, t));
            }

            // 2. Animate popup fading out & scaling down
            if (currentPopupInstance != null)
            {
                SetOpacity(currentPopupInstance, Mathf.Lerp(startPopupAlpha, 0f, t));
                currentPopupInstance.transform.localScale = Vector3.Lerp(startPopupScale, Vector3.zero, t);
            }

            // 3. Animate pause menu fading in & scaling back up
            if (currentMenuInstance != null)
            {
                float curveValue = (scaleCurve != null && scaleCurve.length > 0) ? scaleCurve.Evaluate(t) : t;
                SetOpacity(currentMenuInstance, t);
                currentMenuInstance.transform.localScale = targetMenuScale * curveValue;
            }

            yield return null;
        }

        // Ensure final values
        if (currentBackgroundInstance != null)
        {
            SetOpacity(currentBackgroundInstance, targetBgAlpha);
        }

        if (currentMenuInstance != null)
        {
            SetOpacity(currentMenuInstance, 1f);
            currentMenuInstance.transform.localScale = targetMenuScale;
            CanvasGroup menuCg = currentMenuInstance.GetComponent<CanvasGroup>();
            if (menuCg != null)
            {
                menuCg.interactable = true;
                menuCg.blocksRaycasts = true;
            }
        }

        // Clean up popup instance
        if (currentPopupInstance != null)
        {
            Destroy(currentPopupInstance);
            currentPopupInstance = null;
        }

        popupTransitionCoroutine = null;
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

        // If neither exists or object has children, add CanvasGroup to control whole hierarchy cleanly
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

    private void OnDestroy()
    {
        // Safety guard: ensure Time.timeScale is never permanently stuck at 0 if destroyed while paused
        if (isPaused)
        {
            Time.timeScale = 1f;
        }
    }
}
