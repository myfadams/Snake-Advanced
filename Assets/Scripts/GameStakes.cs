using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Attached to the Health Bar UI object to monitor the player's snake cube count (including head),
/// update the health bar sprite from a MaterialsList ScriptableObject (6 textures ordered from full to empty),
/// animate the health bar decreasing when cubes are lost or reduced,
/// animate a TextMeshPro UI element displaying current cubes / max allowed cubes,
/// and drive the Length Stake / Emergency Shrink (Shed) state machine.
/// </summary>
public class GameStakes : MonoBehaviour
{
    public enum StakeState
    {
        Normal,
        Warning,
        Shedding,
        GameOver
    }

    public static GameStakes Instance { get; private set; }

    #region Serialized Fields

    [Header("Health Bar UI")]
    [Tooltip("The Image component displaying the health bar sprite. If left empty, will auto-detect from this GameObject.")]
    [SerializeField] private Image healthBarImage;

    [Header("Health Bar Sprites (ScriptableObject)")]
    [Tooltip("MaterialsList / ObjectList ScriptableObject containing the 6 health bar sprites ordered from full to empty.")]
    [SerializeField] private MaterialsList spriteScriptableObj;

    [Tooltip("Optional direct array fallback for health bar sprites ordered from full (0) to empty (5). Used if spriteScriptableObj is unassigned.")]
    [SerializeField] private Sprite[] fallbackSprites;

    [Header("Length Stake Settings")]
    [Tooltip("The maximum snake length (including head). At this number, the stake warning triggers.")]
    [SerializeField] private int maxSnakeLength = 10;

    [Tooltip("The allowable length / maximum number of cubes (kept in sync with maxSnakeLength).")]
    [SerializeField] private int maxAllowableCubes = 10;

    [Tooltip("The target snake length the player must shrink back down to.")]
    [SerializeField] private int targetSnakeLength = 8;

    [Tooltip("The initial countdown timer in seconds given to shrink back to the target length.")]
    [SerializeField] private float initialStakeTime = 30f;

    [Tooltip("Seconds subtracted from the timer for each additional block grown beyond maxSnakeLength.")]
    [SerializeField] private float timeReductionPerExtraBlock = 5f;

    [Tooltip("Optional direct reference to SnakeGrow. If left empty, will automatically locate the player in the scene.")]
    [SerializeField] private SnakeGrow snakeGrow;

    [Header("Length Stake UI")]
    [Tooltip("TextMeshPro UI text element displaying the stake countdown timer.")]
    [SerializeField] private TMP_Text countdownText;

    [Tooltip("TextMeshPro UI text element displaying the warning/instruction message. If left empty, will automatically be extracted from the warningUI prefab's child.")]
    [SerializeField] private TMP_Text warningMessageText;

    [Tooltip("The Warning UI GameObject or Prefab (e.g. warnings.prefab).")]
    [SerializeField] private GameObject warningUI;

    [Tooltip("The Shed Button GameObject or Button.")]
    [SerializeField] private GameObject shedButton;

    [Header("Game Over UI")]
    [Tooltip("The Game Over popup GameObject or Prefab (e.g. GameOver.prefab).")]
    [SerializeField] private GameObject gameOverPopup;

    [Tooltip("The Game Over background overlay element (dimming overlay).")]
    [SerializeField] private GameObject gameOverBackground;

    [Tooltip("Target background opacity for the Game Over overlay (default ~0.8).")]
    [Range(0f, 1f)]
    [SerializeField] private float gameOverBackgroundOpacity = 0.8f;

    [Header("Timer Colors & Thresholds")]
    [Tooltip("Color of the timer when plenty of time remains.")]
    [SerializeField] private Color normalTimerColor = Color.white;

    [Tooltip("Color of the timer when roughly half the time remains.")]
    [SerializeField] private Color warningTimerColor = new Color(1f, 0.6f, 0.1f); // Orange

    [Tooltip("Color of the timer when very little time remains.")]
    [SerializeField] private Color criticalTimerColor = Color.red;

    [Header("Warning Messages")]
    [Tooltip("Warning text displayed when exactly at length limit.")]
    [SerializeField] private string limitReachedMessage = "LENGTH LIMIT REACHED!\nSHRINK TO {0} BLOCKS!";

    [Tooltip("Warning text displayed when growing beyond the limit.")]
    [SerializeField] private string tooLongMessage = "TOO LONG! SHRINK NOW!";

    [Header("Warning UI Animation")]
    [Tooltip("Duration of the warning UI fade-in and scale-in animation.")]
    [SerializeField] private float warningAnimationDuration = 0.3f;

    [Header("Cubes Display Text (TMP)")]
    [Tooltip("TextMeshPro UI text element displaying the number of cubes in the snake / number of cubes allowed.")]
    [SerializeField] private TMP_Text cubesText;

    [Tooltip("Format string for the cubes text. {0} is current cubes, {1} is max allowable cubes.")]
    [SerializeField] private string textFormat = "{0} / {1}";

    [Header("Health Bar Decrease Animation")]
    [Tooltip("Time in seconds spent on each intermediate sprite step when animating the health bar decreasing.")]
    [Range(0.02f, 0.3f)]
    [SerializeField] private float decreaseStepDelay = 0.08f;

    [Tooltip("Subtle scale punch multiplier applied to the health bar Image when health decreases.")]
    [Range(1f, 1.5f)]
    [SerializeField] private float barPunchMultiplier = 1.15f;

    [Tooltip("Duration of the health bar scale punch animation.")]
    [Range(0.05f, 0.4f)]
    [SerializeField] private float barPunchDuration = 0.15f;

    [Tooltip("Flash tint color applied to the health bar Image when health decreases.")]
    [SerializeField] private Color decreaseFlashColor = new Color(1f, 0.35f, 0.35f, 1f);

    [Header("Text Animation Settings")]
    [Tooltip("Duration for counting up or down displayed cube numbers.")]
    [Range(0.05f, 0.5f)]
    [SerializeField] private float textCountDuration = 0.2f;

    [Tooltip("Scale punch multiplier applied to the TMP text whenever the count changes.")]
    [Range(1f, 1.6f)]
    [SerializeField] private float textPunchMultiplier = 1.25f;

    [Tooltip("Duration of the TMP text scale punch.")]
    [Range(0.05f, 0.4f)]
    [SerializeField] private float textPunchDuration = 0.15f;

    #endregion

    #region Runtime State

    private StakeState currentState = StakeState.Normal;
    private int monitoredCubes = 1;
    private int displayedCubes = 1;
    private int currentSpriteIndex = -1;

    // Countdown state
    private float currentCountdownTimer = 30f;
    private float activeStakeDuration = 30f;
    private string originalCountdownText = "";
    private Color originalCountdownColor = Color.white;
    private bool hasOriginalCountdownDefaults = false;

    // Instances & Coroutines
    private GameObject activeWarningUIInstance;
    private GameObject activeGameOverPopupInstance;
    private GameObject activeGameOverBackgroundInstance;
    private Coroutine warningUIAnimationCoroutine;
    private Coroutine gameOverAnimationCoroutine;

    private Coroutine healthBarAnimationCoroutine;
    private Coroutine barPunchCoroutine;
    private Coroutine textAnimationCoroutine;
    private Coroutine textPunchCoroutine;

    private Vector3 originalBarScale = Vector3.one;
    private Color originalBarColor = Color.white;
    private Vector3 originalTextScale = Vector3.one;
    private bool hasOriginalBarScale = false;
    private bool hasOriginalTextScale = false;

    #endregion

    #region Public Properties

    /// <summary>The current state of the length stake system.</summary>
    public StakeState State => currentState;

    /// <summary>The remaining time on the stake countdown.</summary>
    public float CurrentCountdownTimer => currentCountdownTimer;

    /// <summary>The maximum snake length triggering the stake warning.</summary>
    public int MaxSnakeLength
    {
        get => maxSnakeLength;
        set
        {
            maxSnakeLength = Mathf.Max(2, value);
            maxAllowableCubes = maxSnakeLength;
            Refresh(animate: true);
        }
    }

    /// <summary>The target snake length to shrink down to.</summary>
    public int TargetSnakeLength
    {
        get => targetSnakeLength;
        set => targetSnakeLength = Mathf.Max(1, value);
    }

    /// <summary>The currently monitored total cube count (including head).</summary>
    public int CurrentCubes => monitoredCubes;

    /// <summary>The maximum allowable cube count (including head) corresponding to a 100% full health bar.</summary>
    public int MaxAllowableCubes
    {
        get => maxAllowableCubes;
        set => SetMaxAllowableCubes(value);
    }

    /// <summary>The health bar Image component.</summary>
    public Image HealthBarImage
    {
        get => healthBarImage;
        set
        {
            healthBarImage = value;
            CacheImageDefaults();
            ApplySprite(currentSpriteIndex);
        }
    }

    /// <summary>The TextMeshPro UI text component.</summary>
    public TMP_Text CubesText
    {
        get => cubesText;
        set
        {
            cubesText = value;
            CacheTextDefaults();
            UpdateCubesTextInstant(displayedCubes);
        }
    }

    /// <summary>The MaterialsList ScriptableObject holding the health bar sprites.</summary>
    public MaterialsList SpriteScriptableObj
    {
        get => spriteScriptableObj;
        set
        {
            spriteScriptableObj = value;
            ApplySprite(currentSpriteIndex);
        }
    }

    #endregion

    private void OnValidate()
    {
        maxAllowableCubes = maxSnakeLength;
        if (targetSnakeLength >= maxSnakeLength)
        {
            targetSnakeLength = Mathf.Max(1, maxSnakeLength - 2);
        }

        // Auto-extract TMP component from the warningUI prefab/child when assigned in Inspector
        if (warningMessageText == null && warningUI != null)
        {
            warningMessageText = FindTMPInHierarchy(warningUI);
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

        maxAllowableCubes = maxSnakeLength;

        if (healthBarImage == null)
        {
            healthBarImage = GetComponent<Image>();
            if (healthBarImage == null)
            {
                healthBarImage = GetComponentInChildren<Image>();
            }
        }

        CacheImageDefaults();
        CacheTextDefaults();
        CacheCountdownDefaults();
        LocateSnakeGrow();
        AutoDetectUIReferences();
    }

    private void OnEnable()
    {
        SnakeGrow.OnSnakeCubesChanged += HandleSnakeCubesChanged;
    }

    private void OnDisable()
    {
        SnakeGrow.OnSnakeCubesChanged -= HandleSnakeCubesChanged;
        StopAllRunningCoroutines();
        ResetVisualScales();
    }

    private void Start()
    {
        LocateSnakeGrow();
        AutoDetectUIReferences();
        CacheCountdownDefaults();

        int initialCubes = snakeGrow != null ? snakeGrow.TotalCubeCount : 1;
        monitoredCubes = initialCubes;
        displayedCubes = initialCubes;

        int initialSpriteIndex = GetSpriteIndexForCubes(initialCubes);
        currentSpriteIndex = initialSpriteIndex;

        ApplySprite(initialSpriteIndex);
        UpdateCubesTextInstant(initialCubes);

        // Ensure initially in Normal state with Shed button disabled and warning UI ready & hidden
        currentState = StakeState.Normal;
        SetShedButtonActive(false);

        if (warningUI != null)
        {
            EnsureWarningUIInstance();
            if (activeWarningUIInstance != null)
            {
                activeWarningUIInstance.SetActive(false);
            }
        }

        if (initialCubes >= maxSnakeLength)
        {
            EnterWarning();
        }
    }

    private void Update()
    {
        // Continuous check to guarantee sync with SnakeGrow even if events were missed
        if (snakeGrow == null)
        {
            LocateSnakeGrow();
        }

        if (snakeGrow != null)
        {
            int current = snakeGrow.TotalCubeCount;
            if (current != monitoredCubes)
            {
                HandleSnakeCubesChanged(current);
            }
        }

        // State machine update
        if (currentState == StakeState.Warning)
        {
            if (snakeGrow != null && snakeGrow.TotalCubeCount < maxSnakeLength)
            {
                EnterNormal(animated: true);
            }
            else
            {
                // Guarantee warning UI is active and visible every frame while in Warning state
                if (activeWarningUIInstance == null || !activeWarningUIInstance.activeSelf || (warningUIAnimationCoroutine == null && activeWarningUIInstance.transform.localScale.sqrMagnitude < 0.5f))
                {
                    EnsureWarningUIInstance();
                    UpdateWarningMessageText(GetWarningMessageForCubes(snakeGrow != null ? snakeGrow.TotalCubeCount : monitoredCubes));
                    AnimateWarningUI(true);
                }

                UpdateWarningState();
            }
        }
        else if (currentState == StakeState.Normal)
        {
            if (snakeGrow != null && snakeGrow.TotalCubeCount >= maxSnakeLength)
            {
                EnterWarning();
            }
        }
    }

    #region Stake State Machine

    private void UpdateWarningState()
    {
        // If length has shrunk to less than 10 (e.g. 9), immediately return to Normal
        if (snakeGrow != null && snakeGrow.TotalCubeCount < maxSnakeLength)
        {
            EnterNormal(animated: true);
            return;
        }

        // Decrement timer
        currentCountdownTimer -= Time.deltaTime;

        if (currentCountdownTimer <= 0f)
        {
            currentCountdownTimer = 0f;
            UpdateCountdownTextDisplay();
            EnterGameOver();
            return;
        }

        UpdateCountdownTextDisplay();

        // Evaluate Shed button interactability every frame in case head value changed
        bool canShed = (snakeGrow != null && snakeGrow.HeadValue >= 8);
        SetShedButtonActive(canShed);
    }

    private void UpdateCountdownTextDisplay()
    {
        if (countdownText == null) return;

        // Decimal formatting e.g. "20.5s", "30.0s", "29.9s"
        countdownText.text = currentCountdownTimer.ToString("F1") + "s";

        // Smooth color transition: normal (ratio >= 0.5) -> warning (0.5) -> critical/red (0.0)
        float ratio = Mathf.Clamp01(currentCountdownTimer / Mathf.Max(0.1f, activeStakeDuration));
        Color targetColor;

        if (ratio > 0.5f)
        {
            float t = (ratio - 0.5f) / 0.5f;
            targetColor = Color.Lerp(warningTimerColor, normalTimerColor, t);
        }
        else
        {
            float t = ratio / 0.5f;
            targetColor = Color.Lerp(criticalTimerColor, warningTimerColor, t);
        }

        countdownText.color = targetColor;
    }

    private void EnterWarning()
    {
        if (currentState == StakeState.Warning || currentState == StakeState.GameOver) return;

        currentState = StakeState.Warning;

        // Calculate stake duration (accounting for any extra blocks if entered above max)
        int extraBlocks = Mathf.Max(0, monitoredCubes - maxSnakeLength);
        activeStakeDuration = Mathf.Max(1f, initialStakeTime - extraBlocks * timeReductionPerExtraBlock);
        currentCountdownTimer = activeStakeDuration;

        CacheCountdownDefaults();
        UpdateCountdownTextDisplay();

        // Show Warning UI smoothly
        EnsureWarningUIInstance();
        UpdateWarningMessageText(GetWarningMessageForCubes(monitoredCubes));
        AnimateWarningUI(true);

        // Evaluate Shed button
        bool canShed = (snakeGrow != null && snakeGrow.HeadValue >= 8);
        SetShedButtonActive(canShed);
    }

    private void EnterNormal(bool animated = true)
    {
        if (currentState == StakeState.Normal) return;

        currentState = StakeState.Normal;

        // Restore original countdown text and color
        RestoreCountdownDefaults();

        // Smoothly hide Warning UI
        AnimateWarningUI(false);

        // Disable Shed button
        SetShedButtonActive(false);
    }

    /// <summary>
    /// Called when the player presses the Shed button.
    /// Initiates emergency shrink to targetSnakeLength, halving the head value and animating removed blocks.
    /// </summary>
    public void TriggerShed()
    {
        if (currentState != StakeState.Warning)
        {
            Debug.LogWarning("[GameStakes] Cannot Shed: Snake is not in Warning state.");
            return;
        }

        if (snakeGrow == null)
        {
            LocateSnakeGrow();
        }

        if (snakeGrow == null)
        {
            Debug.LogError("[GameStakes] Cannot Shed: SnakeGrow reference is missing.");
            return;
        }

        if (snakeGrow.HeadValue < 8)
        {
            Debug.LogWarning($"[GameStakes] Cannot Shed: Head value ({snakeGrow.HeadValue}) is below 8.");
            return;
        }

        currentState = StakeState.Shedding;
        SetShedButtonActive(false);

        snakeGrow.PerformShed(targetSnakeLength, OnShedCompleted);
    }

    private void OnShedCompleted()
    {
        int current = snakeGrow != null ? snakeGrow.TotalCubeCount : monitoredCubes;
        if (current < maxSnakeLength)
        {
            EnterNormal(animated: true);
        }
        else
        {
            // Still 10 or more: re-enter warning
            currentState = StakeState.Normal;
            EnterWarning();
        }
    }

    private void EnterGameOver()
    {
        if (currentState == StakeState.GameOver) return;

        currentState = StakeState.GameOver;

        // Restore timer text and color
        RestoreCountdownDefaults();

        // Disable Shed button
        SetShedButtonActive(false);

        // Instantly hide warning UI
        if (activeWarningUIInstance != null && activeWarningUIInstance.activeSelf)
        {
            activeWarningUIInstance.SetActive(false);
        }

        // Stop player gameplay
        if (snakeGrow != null)
        {
            PlayerMovement pm = snakeGrow.GetComponent<PlayerMovement>();
            if (pm != null)
            {
                pm.enabled = false;
            }
        }

        // Show Game Over overlay and popup
        Transform uiParent = GetTargetUIParent();
        SetupGameOverBackground(uiParent);
        SetupGameOverPopup(uiParent);

        if (gameOverAnimationCoroutine != null)
        {
            StopCoroutine(gameOverAnimationCoroutine);
        }
        gameOverAnimationCoroutine = StartCoroutine(AnimateGameOverCoroutine());
    }

    private IEnumerator AnimateGameOverCoroutine()
    {
        float elapsed = 0f;
        float duration = 0.35f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            if (activeGameOverBackgroundInstance != null)
            {
                SetOpacity(activeGameOverBackgroundInstance, Mathf.Lerp(0f, gameOverBackgroundOpacity, smoothT));
            }

            if (activeGameOverPopupInstance != null)
            {
                SetOpacity(activeGameOverPopupInstance, smoothT);
                activeGameOverPopupInstance.transform.localScale = Vector3.Lerp(Vector3.zero, Vector3.one, smoothT);
            }

            yield return null;
        }

        if (activeGameOverBackgroundInstance != null)
        {
            SetOpacity(activeGameOverBackgroundInstance, gameOverBackgroundOpacity);
        }

        if (activeGameOverPopupInstance != null)
        {
            SetOpacity(activeGameOverPopupInstance, 1f);
            activeGameOverPopupInstance.transform.localScale = Vector3.one;
        }

        gameOverAnimationCoroutine = null;
    }

    #endregion

    #region UI Setup & Animations

    private void EnsureWarningUIInstance()
    {
        if (activeWarningUIInstance == null)
        {
            if (warningUI == null)
            {
                GameObject existingWarnings = GameObject.Find("warnings");
                if (existingWarnings != null) warningUI = existingWarnings;
            }

            if (warningUI == null) return;

            if (warningUI.scene.IsValid())
            {
                activeWarningUIInstance = warningUI;
            }
            else
            {
                Transform parent = GetTargetUIParent();
                activeWarningUIInstance = Instantiate(warningUI, parent, false);
            }
        }

        if (activeWarningUIInstance != null)
        {
            // Disable all Animators on the warning UI instance so they cannot conflict with code animations, force alpha to 0, or shift positions
            Animator[] anims = activeWarningUIInstance.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < anims.Length; i++)
            {
                if (anims[i] != null && anims[i].enabled)
                {
                    anims[i].enabled = false;
                }
            }

            // Always resolve TMP text directly from the scene instance
            warningMessageText = FindTMPInHierarchy(activeWarningUIInstance);
        }
    }

    private void AnimateWarningUI(bool show)
    {
        EnsureWarningUIInstance();
        if (activeWarningUIInstance == null) return;

        if (show)
        {
            activeWarningUIInstance.transform.SetAsLastSibling();
        }

        if (warningUIAnimationCoroutine != null)
        {
            StopCoroutine(warningUIAnimationCoroutine);
        }
        warningUIAnimationCoroutine = StartCoroutine(AnimateWarningUICoroutine(show));
    }

    private IEnumerator AnimateWarningUICoroutine(bool show)
    {
        if (activeWarningUIInstance == null) yield break;

        activeWarningUIInstance.SetActive(true);

        if (show)
        {
            activeWarningUIInstance.transform.SetAsLastSibling();
        }

        // Ensure Animators remain disabled
        Animator[] anims = activeWarningUIInstance.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < anims.Length; i++)
        {
            if (anims[i] != null && anims[i].enabled)
            {
                anims[i].enabled = false;
            }
        }

        // Ensure CanvasGroup is present and enabled
        CanvasGroup cg = activeWarningUIInstance.GetComponent<CanvasGroup>();
        if (cg == null)
        {
            cg = activeWarningUIInstance.AddComponent<CanvasGroup>();
        }
        cg.enabled = true;

        // Ensure RectTransform anchored position is centered at (0, 75)
        RectTransform rt = activeWarningUIInstance.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchoredPosition = new Vector2(0f, 75f);
            rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f);
        }

        float elapsed = 0f;
        float duration = Mathf.Max(0.05f, warningAnimationDuration);

        // Smoothly interpolate from current scale and opacity rather than snapping
        Vector3 startScale = activeWarningUIInstance.transform.localScale;
        if (show && startScale.sqrMagnitude < 0.001f)
        {
            startScale = Vector3.zero;
        }
        Vector3 targetScale = show ? Vector3.one : Vector3.zero;

        float startAlpha = GetOpacity(activeWarningUIInstance);
        if (show && startAlpha < 0.01f)
        {
            startAlpha = 0f;
        }
        float targetAlpha = show ? 1f : 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            activeWarningUIInstance.transform.localScale = Vector3.Lerp(startScale, targetScale, smoothT);
            SetOpacity(activeWarningUIInstance, Mathf.Lerp(startAlpha, targetAlpha, smoothT));

            yield return null;
        }

        activeWarningUIInstance.transform.localScale = targetScale;
        SetOpacity(activeWarningUIInstance, targetAlpha);

        if (!show)
        {
            activeWarningUIInstance.SetActive(false);
        }

        warningUIAnimationCoroutine = null;
    }

    private void UpdateWarningMessageText(string message)
    {
        if (activeWarningUIInstance != null)
        {
            if (warningMessageText == null || !warningMessageText.transform.IsChildOf(activeWarningUIInstance.transform))
            {
                warningMessageText = FindTMPInHierarchy(activeWarningUIInstance);
            }
        }
        else if (warningMessageText == null)
        {
            warningMessageText = ResolveWarningTMP();
        }

        if (warningMessageText != null)
        {
            warningMessageText.text = message;
        }
    }

    /// <summary>
    /// Searches the given root GameObject and its children for the warning TextMeshPro component.
    /// Prioritizes child objects with names like 'text', 'warning', 'msg', or 'message'.
    /// </summary>
    private TMP_Text FindTMPInHierarchy(GameObject root)
    {
        if (root == null) return null;

        TMP_Text[] tmps = root.GetComponentsInChildren<TMP_Text>(true);
        if (tmps == null || tmps.Length == 0) return null;

        for (int i = 0; i < tmps.Length; i++)
        {
            string n = tmps[i].gameObject.name.ToLowerInvariant();
            if (n.Contains("text") || n.Contains("warning") || n.Contains("msg") || n.Contains("message"))
            {
                return tmps[i];
            }
        }

        return tmps[0];
    }

    /// <summary>
    /// Resolves the warning TMP component from the instantiated warning UI, assigned prefab, or scene.
    /// </summary>
    private TMP_Text ResolveWarningTMP()
    {
        if (activeWarningUIInstance != null)
        {
            TMP_Text tmp = FindTMPInHierarchy(activeWarningUIInstance);
            if (tmp != null) return tmp;
        }

        if (warningUI != null)
        {
            TMP_Text tmp = FindTMPInHierarchy(warningUI);
            if (tmp != null) return tmp;
        }

        if (warningMessageText != null)
        {
            return warningMessageText;
        }

        GameObject sceneWarnings = GameObject.Find("warnings");
        if (sceneWarnings != null)
        {
            return FindTMPInHierarchy(sceneWarnings);
        }

        return null;
    }

    private string GetWarningMessageForCubes(int cubes)
    {
        if (cubes > maxSnakeLength)
        {
            return tooLongMessage;
        }

        return string.Format(limitReachedMessage, maxSnakeLength - 1);
    }

    private void SetShedButtonActive(bool active)
    {
        if (shedButton == null)
        {
            shedButton = GameObject.Find("Shed");
        }

        if (shedButton == null) return;

        Button btn = shedButton.GetComponent<Button>();
        if (btn != null)
        {
            btn.interactable = active;
        }

        CanvasGroup cg = shedButton.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.interactable = active;
            cg.blocksRaycasts = active;
            cg.alpha = active ? 1f : 0.4f;
        }
        else
        {
            if (btn == null)
            {
                shedButton.SetActive(active);
            }
        }

        Animator anim = shedButton.GetComponent<Animator>();
        if (anim != null)
        {
            if (active)
            {
                anim.Play("ShedActive", 0, 0f);
            }
            else
            {
                anim.Play("New State", 0, 0f);
            }
        }
    }

    private void SetupGameOverBackground(Transform parent)
    {
        if (gameOverBackground == null)
        {
            GameObject existingBg = GameObject.Find("menuBg");
            if (existingBg != null) gameOverBackground = existingBg;
        }

        if (gameOverBackground == null) return;

        if (gameOverBackground.scene.IsValid())
        {
            activeGameOverBackgroundInstance = gameOverBackground;
        }
        else
        {
            activeGameOverBackgroundInstance = Instantiate(gameOverBackground, parent, false);
            RectTransform rt = activeGameOverBackgroundInstance.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
        }

        activeGameOverBackgroundInstance.SetActive(true);
        activeGameOverBackgroundInstance.transform.SetAsLastSibling();

        Animator anim = activeGameOverBackgroundInstance.GetComponent<Animator>();
        if (anim != null && anim.enabled)
        {
            anim.enabled = false;
        }

        SetOpacity(activeGameOverBackgroundInstance, 0f);
    }

    private void SetupGameOverPopup(Transform parent)
    {
        if (gameOverPopup == null)
        {
            GameObject existing = GameObject.Find("GameOver");
            if (existing != null) gameOverPopup = existing;
        }

        if (gameOverPopup == null) return;

        if (gameOverPopup.scene.IsValid())
        {
            activeGameOverPopupInstance = gameOverPopup;
        }
        else
        {
            activeGameOverPopupInstance = Instantiate(gameOverPopup, parent, false);
        }

        activeGameOverPopupInstance.SetActive(true);
        activeGameOverPopupInstance.transform.SetAsLastSibling();

        RectTransform rt = activeGameOverPopupInstance.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchoredPosition = Vector2.zero;
            rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f);
        }

        activeGameOverPopupInstance.transform.localScale = Vector3.zero;
        SetOpacity(activeGameOverPopupInstance, 0f);

        // Bind existing Restart and Home buttons inside popup
        Button[] buttons = activeGameOverPopupInstance.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button btn = buttons[i];
            string bName = btn.gameObject.name.ToLowerInvariant();

            if (bName.Contains("restart") || bName.Contains("retry") || bName.Contains("again"))
            {
                btn.onClick.RemoveListener(RestartGame);
                btn.onClick.AddListener(RestartGame);
            }
            else if (bName.Contains("home") || bName.Contains("map") || bName.Contains("quit") || bName.Contains("menu"))
            {
                btn.onClick.RemoveListener(QuitToMapSelect);
                btn.onClick.AddListener(QuitToMapSelect);
            }
        }
    }

    /// <summary>
    /// Restarts the current game scene.
    /// </summary>
    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>
    /// Navigates to the map selection screen in the Menus scene.
    /// </summary>
    public void QuitToMapSelect()
    {
        Time.timeScale = 1f;
        buttonFunctions.lastButtonPressed = "SinglePlayer";
        SceneManager.LoadScene("Menus");
    }

    private void AutoDetectUIReferences()
    {
        if (countdownText == null)
        {
            GameObject timerObj = GameObject.Find("Timer");
            if (timerObj == null) timerObj = GameObject.Find("countdown");
            if (timerObj != null) countdownText = timerObj.GetComponent<TMP_Text>();
        }

        if (shedButton == null)
        {
            shedButton = GameObject.Find("Shed");
        }

        if (warningUI == null)
        {
            warningUI = GameObject.Find("warnings");
        }

        if (warningMessageText == null)
        {
            warningMessageText = ResolveWarningTMP();
        }

        if (gameOverPopup == null)
        {
            gameOverPopup = GameObject.Find("GameOver");
        }

        if (gameOverBackground == null)
        {
            gameOverBackground = GameObject.Find("menuBg");
        }
    }

    private Transform GetTargetUIParent()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null) return canvas.transform;

        canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) canvas = FindObjectOfType<Canvas>();

        return canvas != null ? canvas.transform : transform;
    }

    private void CacheCountdownDefaults()
    {
        if (countdownText != null && !hasOriginalCountdownDefaults)
        {
            originalCountdownText = countdownText.text;
            originalCountdownColor = countdownText.color;
            hasOriginalCountdownDefaults = true;
        }
    }

    private void RestoreCountdownDefaults()
    {
        if (countdownText != null && hasOriginalCountdownDefaults)
        {
            countdownText.text = originalCountdownText;
            countdownText.color = originalCountdownColor;
        }
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

    #endregion

    #region Health Bar & Cube Tracking

    private void LocateSnakeGrow()
    {
        if (snakeGrow != null) return;

        if (SnakeGrow.Instance != null)
        {
            snakeGrow = SnakeGrow.Instance;
            return;
        }

        snakeGrow = FindObjectOfType<SnakeGrow>();
        if (snakeGrow == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                snakeGrow = playerObj.GetComponent<SnakeGrow>();
            }
        }
    }

    private void CacheImageDefaults()
    {
        if (healthBarImage != null && !hasOriginalBarScale)
        {
            originalBarScale = healthBarImage.rectTransform.localScale;
            originalBarColor = healthBarImage.color;
            hasOriginalBarScale = true;
        }
    }

    private void CacheTextDefaults()
    {
        if (cubesText != null && !hasOriginalTextScale)
        {
            originalTextScale = cubesText.rectTransform.localScale;
            hasOriginalTextScale = true;
        }
    }

    private void ResetVisualScales()
    {
        if (healthBarImage != null && hasOriginalBarScale)
        {
            healthBarImage.rectTransform.localScale = originalBarScale;
            healthBarImage.color = originalBarColor;
        }

        if (cubesText != null && hasOriginalTextScale)
        {
            cubesText.rectTransform.localScale = originalTextScale;
        }
    }

    private void StopAllRunningCoroutines()
    {
        if (healthBarAnimationCoroutine != null)
        {
            StopCoroutine(healthBarAnimationCoroutine);
            healthBarAnimationCoroutine = null;
        }

        if (barPunchCoroutine != null)
        {
            StopCoroutine(barPunchCoroutine);
            barPunchCoroutine = null;
        }

        if (textAnimationCoroutine != null)
        {
            StopCoroutine(textAnimationCoroutine);
            textAnimationCoroutine = null;
        }

        if (textPunchCoroutine != null)
        {
            StopCoroutine(textPunchCoroutine);
            textPunchCoroutine = null;
        }

        if (warningUIAnimationCoroutine != null)
        {
            StopCoroutine(warningUIAnimationCoroutine);
            warningUIAnimationCoroutine = null;
        }

        if (gameOverAnimationCoroutine != null)
        {
            StopCoroutine(gameOverAnimationCoroutine);
            gameOverAnimationCoroutine = null;
        }
    }

    /// <summary>
    /// Updates the maximum allowable snake length and refreshes the health bar and text display.
    /// </summary>
    public void SetMaxAllowableCubes(int max)
    {
        maxSnakeLength = Mathf.Max(2, max);
        maxAllowableCubes = maxSnakeLength;
        Refresh(animate: true);
    }

    /// <summary>
    /// Refreshes the stakes display according to the current cube count.
    /// </summary>
    public void Refresh(bool animate = true)
    {
        int cubes = snakeGrow != null ? snakeGrow.TotalCubeCount : monitoredCubes;
        if (animate)
        {
            HandleSnakeCubesChanged(cubes);
        }
        else
        {
            monitoredCubes = cubes;
            displayedCubes = cubes;
            currentSpriteIndex = GetSpriteIndexForCubes(cubes);
            ApplySprite(currentSpriteIndex);
            UpdateCubesTextInstant(cubes);
        }
    }

    private void HandleSnakeCubesChanged(int newCubeCount)
    {
        int previousCubes = monitoredCubes;
        monitoredCubes = newCubeCount;

        int targetSpriteIndex = GetSpriteIndexForCubes(newCubeCount);

        // 1. Animate health bar sprite if index changed
        if (targetSpriteIndex != currentSpriteIndex)
        {
            if (targetSpriteIndex > currentSpriteIndex)
            {
                if (healthBarAnimationCoroutine != null)
                {
                    StopCoroutine(healthBarAnimationCoroutine);
                }
                healthBarAnimationCoroutine = StartCoroutine(AnimateHealthDecrease(targetSpriteIndex));
            }
            else
            {
                if (healthBarAnimationCoroutine != null)
                {
                    StopCoroutine(healthBarAnimationCoroutine);
                }
                healthBarAnimationCoroutine = StartCoroutine(AnimateHealthIncrease(targetSpriteIndex));
            }
        }

        // 2. Animate TMP text count
        if (cubesText != null && previousCubes != newCubeCount)
        {
            if (textAnimationCoroutine != null)
            {
                StopCoroutine(textAnimationCoroutine);
            }
            textAnimationCoroutine = StartCoroutine(AnimateTextCount(newCubeCount));
        }

        // 3. Length Stake state transitions & timer adjustment
        if (currentState == StakeState.Normal)
        {
            if (newCubeCount >= maxSnakeLength)
            {
                EnterWarning();
            }
        }
        else if (currentState == StakeState.Warning)
        {
            if (newCubeCount < maxSnakeLength)
            {
                // Successful shrink!
                EnterNormal(animated: true);
            }
            else
            {
                // If snake grew larger while in Warning state, reduce the timer
                if (newCubeCount > previousCubes && newCubeCount > maxSnakeLength)
                {
                    int extra = newCubeCount - Mathf.Max(previousCubes, maxSnakeLength);
                    if (extra > 0)
                    {
                        float reduction = extra * timeReductionPerExtraBlock;
                        currentCountdownTimer = Mathf.Max(0f, currentCountdownTimer - reduction);

                        // Recalculate maximum stake duration for the new length
                        activeStakeDuration = Mathf.Max(1f, initialStakeTime - (newCubeCount - maxSnakeLength) * timeReductionPerExtraBlock);
                        currentCountdownTimer = Mathf.Min(currentCountdownTimer, activeStakeDuration);

                        if (currentCountdownTimer <= 0f)
                        {
                            EnterGameOver();
                            return;
                        }
                    }
                }

                // Update warning text dynamically
                UpdateWarningMessageText(GetWarningMessageForCubes(newCubeCount));

                // If snake grew larger while in Warning state or UI was not visible, re-trigger warning animation
                if (newCubeCount > previousCubes || activeWarningUIInstance == null || !activeWarningUIInstance.activeSelf || activeWarningUIInstance.transform.localScale.sqrMagnitude < 0.5f)
                {
                    AnimateWarningUI(true);
                }

                // Re-evaluate Shed button
                bool canShed = (snakeGrow != null && snakeGrow.HeadValue >= 8);
                SetShedButtonActive(canShed);
            }
        }
    }

    /// <summary>
    /// Returns the active health bar sprite collection from the ScriptableObject or fallback array.
    /// </summary>
    public Sprite[] GetSprites()
    {
        if (spriteScriptableObj != null && spriteScriptableObj.HealthBars != null && spriteScriptableObj.HealthBars.Length > 0)
        {
            return spriteScriptableObj.HealthBars;
        }

        if (fallbackSprites != null && fallbackSprites.Length > 0)
        {
            return fallbackSprites;
        }

        return null;
    }

    /// <summary>
    /// Maps the current number of cubes to the correct sprite index (0 = Full/5-5ths, 5 = Empty/0-5ths).
    /// </summary>
    public int GetSpriteIndexForCubes(int cubes)
    {
        int max = Mathf.Max(2, maxSnakeLength);

        if (cubes >= max)
        {
            return 0;
        }

        if (cubes <= 1)
        {
            return 5;
        }

        float ratio = Mathf.Clamp01((float)cubes / max);
        int fifths = Mathf.FloorToInt(ratio * 5f);
        fifths = Mathf.Clamp(fifths, 0, 5);

        int targetIndex = 5 - fifths;

        Sprite[] sprites = GetSprites();
        if (sprites != null && sprites.Length > 0)
        {
            targetIndex = Mathf.Clamp(targetIndex, 0, sprites.Length - 1);
        }

        return targetIndex;
    }

    private void ApplySprite(int spriteIndex)
    {
        if (healthBarImage == null) return;

        Sprite[] sprites = GetSprites();
        if (sprites != null && sprites.Length > 0)
        {
            int safeIndex = Mathf.Clamp(spriteIndex, 0, sprites.Length - 1);
            if (sprites[safeIndex] != null)
            {
                healthBarImage.sprite = sprites[safeIndex];
            }
        }
    }

    private IEnumerator AnimateHealthDecrease(int targetSpriteIndex)
    {
        TriggerBarPunch(isDecrease: true);

        while (currentSpriteIndex < targetSpriteIndex)
        {
            currentSpriteIndex++;
            ApplySprite(currentSpriteIndex);

            if (currentSpriteIndex < targetSpriteIndex)
            {
                yield return new WaitForSecondsRealtime(decreaseStepDelay);
            }
        }

        currentSpriteIndex = targetSpriteIndex;
        ApplySprite(currentSpriteIndex);
        healthBarAnimationCoroutine = null;
    }

    private IEnumerator AnimateHealthIncrease(int targetSpriteIndex)
    {
        TriggerBarPunch(isDecrease: false);

        while (currentSpriteIndex > targetSpriteIndex)
        {
            currentSpriteIndex--;
            ApplySprite(currentSpriteIndex);

            if (currentSpriteIndex > targetSpriteIndex)
            {
                yield return new WaitForSecondsRealtime(decreaseStepDelay * 0.75f);
            }
        }

        currentSpriteIndex = targetSpriteIndex;
        ApplySprite(currentSpriteIndex);
        healthBarAnimationCoroutine = null;
    }

    private void TriggerBarPunch(bool isDecrease)
    {
        if (healthBarImage == null) return;

        if (barPunchCoroutine != null)
        {
            StopCoroutine(barPunchCoroutine);
        }

        barPunchCoroutine = StartCoroutine(BarPunchCoroutine(isDecrease));
    }

    private IEnumerator BarPunchCoroutine(bool isDecrease)
    {
        RectTransform rt = healthBarImage.rectTransform;
        float elapsed = 0f;
        Color flashColor = isDecrease ? decreaseFlashColor : originalBarColor;

        while (elapsed < barPunchDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / barPunchDuration);

            float punchFactor = 1f + (barPunchMultiplier - 1f) * Mathf.Sin(t * Mathf.PI);
            rt.localScale = originalBarScale * punchFactor;

            if (isDecrease)
            {
                healthBarImage.color = Color.Lerp(flashColor, originalBarColor, t);
            }

            yield return null;
        }

        rt.localScale = originalBarScale;
        healthBarImage.color = originalBarColor;
        barPunchCoroutine = null;
    }

    private IEnumerator AnimateTextCount(int targetCubes)
    {
        TriggerTextPunch();

        float startValue = displayedCubes;
        float elapsed = 0f;
        float duration = Mathf.Max(0.05f, textCountDuration);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            int currentVal = Mathf.RoundToInt(Mathf.Lerp(startValue, targetCubes, smoothT));
            if (currentVal != displayedCubes)
            {
                displayedCubes = currentVal;
                UpdateCubesTextInstant(displayedCubes);
            }

            yield return null;
        }

        displayedCubes = targetCubes;
        UpdateCubesTextInstant(displayedCubes);
        textAnimationCoroutine = null;
    }

    private void TriggerTextPunch()
    {
        if (cubesText == null) return;

        if (textPunchCoroutine != null)
        {
            StopCoroutine(textPunchCoroutine);
        }

        textPunchCoroutine = StartCoroutine(TextPunchCoroutine());
    }

    private IEnumerator TextPunchCoroutine()
    {
        RectTransform rt = cubesText.rectTransform;
        float elapsed = 0f;

        while (elapsed < textPunchDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / textPunchDuration);

            float punchFactor = 1f + (textPunchMultiplier - 1f) * Mathf.Sin(t * Mathf.PI);
            rt.localScale = originalTextScale * punchFactor;

            yield return null;
        }

        rt.localScale = originalTextScale;
        textPunchCoroutine = null;
    }

    private void UpdateCubesTextInstant(int count)
    {
        if (cubesText != null)
        {
            cubesText.text = string.Format(textFormat, count, maxSnakeLength);
        }
    }

    #endregion
}
