using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attached to the Health Bar UI object to monitor the player's snake cube count (including head),
/// update the health bar sprite from a MaterialsList ScriptableObject (6 textures ordered from full to empty),
/// animate the health bar decreasing when cubes are lost or reduced,
/// and animate a TextMeshPro UI element displaying current cubes / max allowed cubes.
/// </summary>
public class GameStakes : MonoBehaviour
{
    [Header("Health Bar UI")]
    [Tooltip("The Image component displaying the health bar sprite. If left empty, will auto-detect from this GameObject.")]
    [SerializeField] private Image healthBarImage;

    [Header("Health Bar Sprites (ScriptableObject)")]
    [Tooltip("MaterialsList / ObjectList ScriptableObject containing the 6 health bar sprites ordered from full to empty.")]
    [SerializeField] private MaterialsList spriteScriptableObj;

    [Tooltip("Optional direct array fallback for health bar sprites ordered from full (0) to empty (5). Used if spriteScriptableObj is unassigned.")]
    [SerializeField] private Sprite[] fallbackSprites;

    [Header("Snake Length Stakes")]
    [Tooltip("The allowable length / maximum number of cubes (including the head). At this number, the health bar is 100% full (5/5).")]
    [SerializeField] private int maxAllowableCubes = 10;

    [Tooltip("Optional direct reference to SnakeGrow. If left empty, will automatically locate the player in the scene.")]
    [SerializeField] private SnakeGrow snakeGrow;

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

    // Runtime state
    private int monitoredCubes = 1;
    private int displayedCubes = 1;
    private int currentSpriteIndex = -1;

    private Coroutine healthBarAnimationCoroutine;
    private Coroutine barPunchCoroutine;
    private Coroutine textAnimationCoroutine;
    private Coroutine textPunchCoroutine;

    private Vector3 originalBarScale = Vector3.one;
    private Color originalBarColor = Color.white;
    private Vector3 originalTextScale = Vector3.one;
    private bool hasOriginalBarScale = false;
    private bool hasOriginalTextScale = false;

    #region Public Properties

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

    private void Awake()
    {
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
        LocateSnakeGrow();
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

        int initialCubes = snakeGrow != null ? snakeGrow.TotalCubeCount : 1;
        monitoredCubes = initialCubes;
        displayedCubes = initialCubes;

        int initialSpriteIndex = GetSpriteIndexForCubes(initialCubes);
        currentSpriteIndex = initialSpriteIndex;

        ApplySprite(initialSpriteIndex);
        UpdateCubesTextInstant(initialCubes);
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
    }

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
    }

    /// <summary>
    /// Updates the maximum allowable snake length and refreshes the health bar and text display.
    /// </summary>
    public void SetMaxAllowableCubes(int max)
    {
        maxAllowableCubes = Mathf.Max(2, max);
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
                // Health decreased (moved toward empty sprite 5): animate stepping through intermediate sprites
                if (healthBarAnimationCoroutine != null)
                {
                    StopCoroutine(healthBarAnimationCoroutine);
                }
                healthBarAnimationCoroutine = StartCoroutine(AnimateHealthDecrease(targetSpriteIndex));
            }
            else
            {
                // Health increased (moved toward full sprite 0): transition to new sprite
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
        int max = Mathf.Max(2, maxAllowableCubes);

        // Maximum or more: 5/5 -> Full (Index 0)
        if (cubes >= max)
        {
            return 0;
        }

        // Only head remaining or empty: 0/5 -> Empty (Index 5)
        if (cubes <= 1)
        {
            return 5;
        }

        // Divide snake length into 5 proportional steps
        float ratio = Mathf.Clamp01((float)cubes / max);
        int fifths = Mathf.FloorToInt(ratio * 5f);
        fifths = Mathf.Clamp(fifths, 0, 5);

        // 5 fifths -> 0 (Full)
        // 4 fifths -> 1 (4/5)
        // 3 fifths -> 2 (3/5)
        // 2 fifths -> 3 (2/5)
        // 1 fifth  -> 4 (1/5)
        // 0 fifths -> 5 (Empty)
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

    #region Health Bar Decrease Animation

    /// <summary>
    /// Animates the health bar decreasing by sequentially stepping through intermediate sprites
    /// from currentSpriteIndex toward targetSpriteIndex, with scale punch and flash feedback.
    /// </summary>
    private IEnumerator AnimateHealthDecrease(int targetSpriteIndex)
    {
        TriggerBarPunch(isDecrease: true);

        // Step through intermediate sprites sequentially to visually simulate decreasing
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

    /// <summary>
    /// Transitions the health bar when health increases / snake grows.
    /// </summary>
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

            // Sine wave pop: normal -> punch scale -> normal
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

    #endregion

    #region Text Count Animation

    /// <summary>
    /// Smoothly animates the TMP text counter from displayedCubes to targetCubes,
    /// triggering a scale punch on the text object.
    /// </summary>
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
            cubesText.text = string.Format(textFormat, count, maxAllowableCubes);
        }
    }

    #endregion
}
