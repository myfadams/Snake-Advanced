using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.AppUI.UI;
using Unity.VectorGraphics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Central source of truth for the game's block values, their associated colors, and the game scoring system.
/// Attach to: Game/GameManager
/// Other scripts read from GameManager.Instance and award points through public scoring methods.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // The fixed set of valid block values. This is the single source of truth -
    // PickupManager and Pickup should never hardcode their own copy of this list.
    private static readonly int[] BlockValues = { 2, 4, 8, 16 };

    [Header("Block Colors")]
    [Tooltip("Color used for value 2 pickups.")]
    [SerializeField] private Color colorForTwo = Color.yellow;

    [Tooltip("Color used for value 4 pickups.")]
    [SerializeField] private Color colorForFour = Color.green;

    [Tooltip("Color used for value 8 pickups.")]
    [SerializeField] private Color colorForEight = new Color(1f, 0.5f, 0f); // Orange

    [Tooltip("Color used for value 16 pickups.")]
    [SerializeField] private Color colorForSixteen = Color.red;

    [Header("Scoring System")]
    [Tooltip("Points awarded when collecting a pickup block.")]
    [SerializeField] private int pickupScore = 5;

    [Tooltip("Points awarded when merging with the snake head.")]
    [SerializeField] private int headMergeScore = 25;

    [Tooltip("Points awarded when two body segments merge.")]
    [SerializeField] private int bodyMergeScore = 10;

    [Tooltip("Points awarded when defeating/killing an enemy snake.")]
    [SerializeField] private int enemyKillScore = 30;

    [Header("Score UI")]
    [Tooltip("TextMeshPro text element displaying the current score.")]
    [SerializeField] private TMP_Text scoreText;

    [Header("Map Details UI")]
    [Tooltip("TextMeshPro text element displaying the current map name.")]
    [SerializeField] private TMP_Text mapNameText;

    [Header("Score Animation Settings")]
    [Tooltip("Maximum duration in seconds for the count-up animation.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float maxAnimationDuration = 0.35f;

    [Tooltip("Minimum counting speed in points per second.")]
    [SerializeField] private float minCountSpeed = 25f;

    [Header("Timer UI")]
    [Tooltip("TextMeshPro text element displaying time spent in the game")]
    [SerializeField] private TMP_Text TimerText;
    [SerializeField] private Scene gameScene;
    public static float timePlayed;

    [Header("Timer Animation Settings")]
    [Tooltip("Duration in seconds of the smooth second-tick animation.")]
    [Range(0.1f, 0.6f)]
    [SerializeField] private float timerTickDuration = 0.25f;

    [Tooltip("Scale punch multiplier applied to TimerText on every second tick.")]
    [Range(1.0f, 1.3f)]
    [SerializeField] private float timerTickScaleMultiplier = 1.08f;

    [Tooltip("Target minimum alpha during the second tick transition.")]
    [Range(0.3f, 1.0f)]
    [SerializeField] private float timerTickMinAlpha = 0.75f;

    [Header("Game Over Score UI")]
    [Tooltip("TextMeshPro text element on the Game Over popup displaying the final score.")]
    [SerializeField] private TMP_Text gameOverScore;

    private CanvasGroup timerCanvasGroup;
    private RectTransform timerRectTransform;
    private Vector3 timerBaseScale = Vector3.one;
    private Coroutine gameTimeCoroutine;
    private Coroutine timerAnimCoroutine;

    private Dictionary<int, Color> valueColorMap;

    // Actual score stored in GameManager (persists while the game is running)
    private int currentScore = 0;

    // Animated score currently displayed on the UI
    private int displayedScore = 0;
    private float animatedScoreValue = 0f;
    private Coroutine scoreAnimationCoroutine;

    /// <summary>The actual total score earned by the player.</summary>
    public int Score => currentScore;

    /// <summary>The score currently displayed by the count-up animation.</summary>
    public int DisplayedScore => displayedScore;

    /// <summary>Exposes the score UI text component.</summary>
    public TMP_Text ScoreText
    {
        get => scoreText;
        set
        {
            scoreText = value;
            UpdateScoreText(displayedScore);
        }
    }

    /// <summary>Exposes the map name UI text component.</summary>
    public TMP_Text MapNameText
    {
        get => mapNameText;
        set
        {
            mapNameText = value;
            UpdateMapNameText();
        }
    }

    /// <summary>Exposes the timer UI text component.</summary>
    public TMP_Text TimerTextComponent
    {
        get => TimerText;
        set
        {
            TimerText = value;
            InitTimerUI();
        }
    }

    /// <summary>Exposes the Game Over score UI text component.</summary>
    public TMP_Text GameOverScoreText
    {
        get => gameOverScore;
        set => gameOverScore = value;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("GameManager: Duplicate instance found, destroying the new one.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        BuildColorMap();

        if (scoreText == null)
        {
            // Graceful fallback to find score text in scene if not yet wired in Inspector
            GameObject scoreObj = GameObject.Find("score");
            if (scoreObj != null)
            {
                scoreText = scoreObj.GetComponent<TMP_Text>();
            }
        }
        if (gameOverScore == null)
        {
            // Graceful fallback to find score text in scene if not yet wired in Inspector
            GameObject gameOverScoreObj = GameObject.Find("gameOverScore");
            if (gameOverScoreObj != null)
            {
                gameOverScore = gameOverScoreObj.GetComponent<TMP_Text>();
            }
        }

        if (mapNameText == null)
        {
            // Graceful fallback to find map name text in scene if not yet wired in Inspector
            GameObject mapNameObj = GameObject.Find("Mapname");
            if (mapNameObj != null)
            {
                mapNameText = mapNameObj.GetComponent<TMP_Text>();
            }
        }
    }

    private void Start()
    {
        // When the game starts: Score = 0, Score Text = 0
        ResetScore();
        UpdateMapNameText();
        InitTimerUI();
        StartGameTimer();
    }

    private void BuildColorMap()
    {
        valueColorMap = new Dictionary<int, Color>
        {
            { 2, colorForTwo },
            { 4, colorForFour },
            { 8, colorForEight },
            { 16, colorForSixteen }
        };
    }

    #region Scoring System

    /// <summary>
    /// Resets the score to 0 and updates the UI text to "0".
    /// The score resets automatically when a new game begins.
    /// </summary>
    public void ResetScore()
    {
        if (scoreAnimationCoroutine != null)
        {
            StopCoroutine(scoreAnimationCoroutine);
            scoreAnimationCoroutine = null;
        }

        currentScore = 0;
        displayedScore = 0;
        animatedScoreValue = 0f;
        hasRecordedHighScoreForCurrentRun = false;
        UpdateScoreText(0);
        ResetGameTimer();
    }

    /// <summary>
    /// Awards points for picking up a block (+5 points).
    /// </summary>
    public void AddPickupScore()
    {
        AddScore(pickupScore);
    }

    /// <summary>
    /// Awards points for a head merge (+25 points).
    /// </summary>
    public void AddHeadMergeScore()
    {
        AddScore(headMergeScore);
    }

    /// <summary>
    /// Awards points for a body segment merge (+10 points).
    /// </summary>
    public void AddBodyMergeScore()
    {
        AddScore(bodyMergeScore);
    }

    /// <summary>
    /// Awards points for defeating/killing an enemy snake (+30 points).
    /// </summary>
    public void AddEnemyKillScore()
    {
        AddScore(enemyKillScore);
    }

    /// <summary>
    /// Increases the actual score by the given amount and triggers the animated UI count-up.
    /// </summary>
    public void AddScore(int points)
    {
        if (points <= 0) return;

        currentScore += points;

        if (scoreText != null && gameObject.activeInHierarchy)
        {
            if (scoreAnimationCoroutine == null)
            {
                scoreAnimationCoroutine = StartCoroutine(AnimateScoreCoroutine());
            }
        }
        else
        {
            displayedScore = currentScore;
            animatedScoreValue = currentScore;
            UpdateScoreText(displayedScore);
        }
    }

    /// <summary>
    /// Animates the displayed score from displayedScore to currentScore using unscaled time.
    /// Handles rapid successive score additions smoothly without backward jumps or skips.
    /// </summary>
    private IEnumerator AnimateScoreCoroutine()
    {
        while (displayedScore < currentScore)
        {
            int diff = currentScore - displayedScore;
            float pointsPerSecond = Mathf.Max(minCountSpeed, diff / maxAnimationDuration);

            animatedScoreValue += pointsPerSecond * Time.unscaledDeltaTime;
            if (animatedScoreValue > currentScore)
            {
                animatedScoreValue = currentScore;
            }

            int newDisplay = Mathf.FloorToInt(animatedScoreValue);
            if (newDisplay != displayedScore)
            {
                displayedScore = newDisplay;
                UpdateScoreText(displayedScore);
            }

            yield return null;
        }

        displayedScore = currentScore;
        animatedScoreValue = currentScore;
        UpdateScoreText(displayedScore);
        scoreAnimationCoroutine = null;
    }

    private void UpdateScoreText(int value)
    {
        if (scoreText != null)
        {
            scoreText.text = value.ToString();
        }
    }

    #endregion

    #region Map Details

    /// <summary>
    /// Updates the map name text UI element with the currently active map's display name.
    /// </summary>
    public void UpdateMapNameText()
    {
        if (mapNameText != null)
        {
            mapNameText.text = GetActiveMapName();
        }
    }

    /// <summary>
    /// Sets a custom map name directly on the UI text.
    /// </summary>
    public void SetMapName(string name)
    {
        if (mapNameText != null)
        {
            mapNameText.text = name;
        }
    }

    /// <summary>
    /// Resolves the current active map display name using MapSelectManager or PlayerPrefs fallback.
    /// </summary>
    public static string GetActiveMapName()
    {
        if (MapSelectManager.SelectedMapMaterial != null)
        {
            return MapSelectManager.GetMapDisplayName(MapSelectManager.SelectedMapMaterial);
        }

        if (MapSelectManager.Instance != null)
        {
            return MapSelectManager.Instance.GetMapName(MapSelectManager.Instance.ActiveMapIndex);
        }

        int savedIndex = PlayerPrefs.GetInt("SelectedMapIndex", 0);
        return MapSelectManager.GetMapNameByIndex(savedIndex);
    }

    /// <summary>
    /// Resolves the current active map zero-based index using MapSelectManager or PlayerPrefs fallback.
    /// </summary>
    public static int GetActiveMapIndex()
    {
        if (MapSelectManager.Instance != null)
        {
            return MapSelectManager.Instance.ActiveMapIndex;
        }

        return PlayerPrefs.GetInt("SelectedMapIndex", 0);
    }

    #endregion

    #region Block Colors API

    /// <summary>
    /// Returns the fixed list of valid block values (2, 4, 8, 16).
    /// PickupManager calls this instead of keeping its own list.
    /// </summary>
    public IReadOnlyList<int> GetBlockValues()
    {
        return BlockValues;
    }

    /// <summary>
    /// Checks if a color is predefined for the given block value.
    /// Returns true and sets color if found; otherwise false.
    /// </summary>
    public bool TryGetBlockColor(int value, out Color color)
    {
        if (valueColorMap != null && valueColorMap.TryGetValue(value, out color))
        {
            return true;
        }

        color = Color.white;
        return false;
    }

    /// <summary>
    /// Registers or updates a block value color at runtime (e.g. for dynamic values like 32, 64).
    /// </summary>
    public void RegisterBlockColor(int value, Color color)
    {
        if (valueColorMap == null)
        {
            BuildColorMap();
        }

        valueColorMap[value] = color;
    }

    /// <summary>
    /// Returns all currently defined colors in the color map.
    /// </summary>
    public IEnumerable<Color> GetAllDefinedColors()
    {
        if (valueColorMap == null)
        {
            BuildColorMap();
        }

        return valueColorMap.Values;
    }

    /// <summary>
    /// Returns the configured or dynamically generated color for a given block value.
    /// </summary>
    public Color GetBlockColor(int value)
    {
        if (valueColorMap != null && valueColorMap.TryGetValue(value, out Color color))
        {
            return color;
        }

        return body.GetColorForValue(value);
    }

    // Rebuilds the color map if colors are tweaked in the Inspector during Play Mode.
    private void OnValidate()
    {
        if (Application.isPlaying && Instance == this)
        {
            BuildColorMap();
        }
    }

    #endregion

    #region Game Timer & Animations

    /// <summary>
    /// Checks whether the game is in an active Game Over state
    /// either via GameStakes or SnakeGrow.
    /// </summary>
    public bool IsGameOver()
    {
        if (GameStakes.Instance != null && GameStakes.Instance.State == GameStakes.StakeState.GameOver)
            return true;

        if (SnakeGrow.Instance != null && SnakeGrow.Instance.IsDying)
            return true;

        return false;
    }

    /// <summary>
    /// Resolves TimerText if not assigned, caches CanvasGroup and RectTransform,
    /// and initializes the timer display.
    /// </summary>
    public void InitTimerUI()
    {
        if (TimerText == null)
        {
            // Auto-detect Timer text in the scene if unassigned
            GameObject timerObj = GameObject.Find("Timer");
            if (timerObj != null)
            {
                TimerText = timerObj.GetComponentInChildren<TMP_Text>();
            }

            if (TimerText == null)
            {
                GameObject highScoreObj = GameObject.Find("HighScore");
                if (highScoreObj != null)
                {
                    TimerText = highScoreObj.GetComponent<TMP_Text>();
                }
            }

            if (TimerText == null)
            {
                GameObject timerTextObj = GameObject.Find("TimerText");
                if (timerTextObj != null)
                {
                    TimerText = timerTextObj.GetComponent<TMP_Text>();
                }
            }
        }

        if (TimerText != null)
        {
            timerCanvasGroup = TimerText.GetComponent<CanvasGroup>();
            if (timerCanvasGroup == null)
            {
                timerCanvasGroup = TimerText.gameObject.AddComponent<CanvasGroup>();
            }

            timerRectTransform = TimerText.rectTransform;
            if (timerRectTransform != null)
            {
                timerBaseScale = timerRectTransform.localScale;
                if (timerBaseScale.sqrMagnitude < 0.001f)
                {
                    timerBaseScale = Vector3.one;
                }
            }

            TimeSpan time = TimeSpan.FromSeconds(timePlayed);
            TimerText.text = string.Format("{0:00}:{1:00}:{2:00}", 
                (int)time.TotalHours, 
                time.Minutes, 
                time.Seconds);
        }
    }

    /// <summary>
    /// Starts or restarts the game timer coroutine.
    /// </summary>
    public void StartGameTimer()
    {
        if (gameTimeCoroutine != null)
        {
            StopCoroutine(gameTimeCoroutine);
            gameTimeCoroutine = null;
        }

        if (!IsGameOver() && gameObject.activeInHierarchy)
        {
            gameTimeCoroutine = StartCoroutine(showGametime());
        }
    }

    /// <summary>
    /// Stops the game timer coroutine.
    /// </summary>
    public void StopGameTimer()
    {
        if (gameTimeCoroutine != null)
        {
            StopCoroutine(gameTimeCoroutine);
            gameTimeCoroutine = null;
        }
    }

    /// <summary>
    /// Resets the time played back to 0 and updates the timer display.
    /// </summary>
    public void ResetGameTimer()
    {
        timePlayed = 0f;
        if (TimerText != null)
        {
            TimerText.text = "00:00:00";
            if (timerCanvasGroup != null) timerCanvasGroup.alpha = 1f;
            if (timerRectTransform != null) timerRectTransform.localScale = timerBaseScale;
        }
    }

    private void Update()
    {
        // Ensure the timer coroutine is active during gameplay without starting multiple duplicates
        if (gameTimeCoroutine == null && !IsGameOver() && gameObject.activeInHierarchy)
        {
            gameTimeCoroutine = StartCoroutine(showGametime());
        }

        if (IsGameOver())
        {
            if (gameOverScore == null || !gameOverScore.gameObject.activeInHierarchy)
            {
                GameObject popup = GameStakes.Instance != null ? GameStakes.Instance.ActiveGameOverPopupInstance : null;
                UpdateGameOverScore(popup);
            }
        }
    }

    /// <summary>
    /// Tracks gameplay time continuously and animates the timer display on every second tick.
    /// Pauses cleanly when the game is paused, and stops when Game Over is reached.
    /// </summary>
    public IEnumerator showGametime()
    {
        int lastDisplayedSecond = Mathf.FloorToInt(timePlayed);

        while (!IsGameOver())
        {
            // Do not advance timer while paused
            if ((PauseManager.Instance != null && PauseManager.Instance.IsPaused) || Time.timeScale <= 0f)
            {
                yield return null;
                continue;
            }

            timePlayed += Time.deltaTime;
            int currentSecond = Mathf.FloorToInt(timePlayed);

            if (currentSecond != lastDisplayedSecond)
            {
                lastDisplayedSecond = currentSecond;
                TimeSpan time = TimeSpan.FromSeconds(timePlayed);

                // Format as Hours:Minutes:Seconds (00:00:00)
                string formattedTime = string.Format("{0:00}:{1:00}:{2:00}", 
                    (int)time.TotalHours, 
                    time.Minutes, 
                    time.Seconds);

                if (TimerText != null)
                {
                    TimerText.text = formattedTime;

                    // Trigger smooth modern tick animation (with extra emphasis on full minute rollover)
                    bool isMinuteRoll = (time.Seconds == 0 && currentSecond > 0);
                    AnimateTimerTick(isMinuteRoll);
                }
            }

            yield return null;
        }

        OnGameTimerEnded();
    }

    private void OnGameTimerEnded()
    {
        gameTimeCoroutine = null;

        if (TimerText != null)
        {
            TimeSpan time = TimeSpan.FromSeconds(timePlayed);
            TimerText.text = string.Format("{0:00}:{1:00}:{2:00}", 
                (int)time.TotalHours, 
                time.Minutes, 
                time.Seconds);

            // Final settling pulse on game over
            AnimateTimerTick(true);
        }

        // Display score on the Game Over popup
        GameObject popupInstance = GameStakes.Instance != null ? GameStakes.Instance.ActiveGameOverPopupInstance : null;
        UpdateGameOverScore(popupInstance);
    }

    /// <summary>
    /// Triggers a smooth scale-punch and alpha-breathing animation on the timer text.
    /// </summary>
    private void AnimateTimerTick(bool isMinuteRoll)
    {
        if (TimerText == null || !gameObject.activeInHierarchy) return;

        if (timerAnimCoroutine != null)
        {
            StopCoroutine(timerAnimCoroutine);
        }

        timerAnimCoroutine = StartCoroutine(TimerTickAnimationRoutine(isMinuteRoll));
    }

    private IEnumerator TimerTickAnimationRoutine(bool isMinuteRoll)
    {
        if (timerRectTransform == null && TimerText != null)
        {
            timerRectTransform = TimerText.rectTransform;
            if (timerRectTransform != null)
            {
                timerBaseScale = timerRectTransform.localScale;
            }
        }

        if (timerCanvasGroup == null && TimerText != null)
        {
            timerCanvasGroup = TimerText.GetComponent<CanvasGroup>() ?? TimerText.gameObject.AddComponent<CanvasGroup>();
        }

        float duration = timerTickDuration;
        float targetScaleMult = isMinuteRoll ? (timerTickScaleMultiplier * 1.15f) : timerTickScaleMultiplier;
        float minAlpha = timerTickMinAlpha;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Sine wave creates a natural 0 -> 1 -> 0 rise and fall
            float pulseT = Mathf.Sin(t * Mathf.PI);

            if (timerRectTransform != null)
            {
                timerRectTransform.localScale = timerBaseScale * Mathf.Lerp(1.0f, targetScaleMult, pulseT);
            }

            if (timerCanvasGroup != null)
            {
                timerCanvasGroup.alpha = Mathf.Lerp(1.0f, minAlpha, pulseT);
            }

            yield return null;
        }

        if (timerRectTransform != null)
        {
            timerRectTransform.localScale = timerBaseScale;
        }

        if (timerCanvasGroup != null)
        {
            timerCanvasGroup.alpha = 1.0f;
        }

        timerAnimCoroutine = null;
    }

    #endregion

    #region Game Over Score Display & Animations

    private Coroutine gameOverScoreAnimCoroutine;

    /// <summary>
    /// Locates the score text component on the Game Over popup and displays the final score.
    /// Supports a smooth count-up animation and scale punch.
    /// </summary>
    public void UpdateGameOverScore(GameObject popupInstance = null, bool animate = true)
    {
        RecordHighScoreIfEligible();
        ResolveGameOverScoreText(popupInstance);

        if (gameOverScore == null) return;

        if (gameOverScoreAnimCoroutine != null)
        {
            StopCoroutine(gameOverScoreAnimCoroutine);
            gameOverScoreAnimCoroutine = null;
        }

        if (animate && gameObject.activeInHierarchy)
        {
            gameOverScoreAnimCoroutine = StartCoroutine(AnimateGameOverScoreRoutine());
        }
        else
        {
            gameOverScore.text = currentScore.ToString("N0");
        }
    }

    private bool hasRecordedHighScoreForCurrentRun = false;

    /// <summary>
    /// Evaluates the finished run's score and elapsed time, and saves it to highscores.json
    /// if it qualifies as a new high score for the currently active map.
    /// </summary>
    public void RecordHighScoreIfEligible()
    {
        if (hasRecordedHighScoreForCurrentRun) return;

        hasRecordedHighScoreForCurrentRun = true;
        string activeMap = GetActiveMapName();
        int activeIdx = GetActiveMapIndex();

        HighScoreManager.TryRecordScore(activeMap, activeIdx, currentScore, timePlayed, out bool isNewRecord);
        if (isNewRecord)
        {
            Debug.Log($"[GameManager] New High Score achieved on '{activeMap}' (Index {activeIdx}): {currentScore:N0} in {HighScoreManager.FormatDuration(timePlayed)}!");
        }
    }

    /// <summary>
    /// Finds the gameOverScore TMP_Text component in the provided popup instance or active scene.
    /// </summary>
    private void ResolveGameOverScoreText(GameObject popupInstance)
    {
        if (gameOverScore != null && gameOverScore.gameObject.activeInHierarchy)
            return;

        if (popupInstance != null)
        {
            Transform t = popupInstance.transform.Find("gameOverScore");
            if (t != null)
            {
                gameOverScore = t.GetComponent<TMP_Text>();
                if (gameOverScore != null) return;
            }

            TMP_Text[] allTexts = popupInstance.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < allTexts.Length; i++)
            {
                string nameLower = allTexts[i].gameObject.name.ToLowerInvariant();
                if (nameLower.Contains("gameoverscore") || nameLower == "score" || nameLower.Contains("finalscore"))
                {
                    gameOverScore = allTexts[i];
                    return;
                }
            }

            for (int i = 0; i < allTexts.Length; i++)
            {
                string nameLower = allTexts[i].gameObject.name.ToLowerInvariant();
                if (!nameLower.Contains("menu") && !nameLower.Contains("title") && !nameLower.Contains("restart") && !nameLower.Contains("home"))
                {
                    gameOverScore = allTexts[i];
                    return;
                }
            }
        }

        if (gameOverScore == null)
        {
            GameObject obj = GameObject.Find("gameOverScore");
            if (obj != null)
            {
                gameOverScore = obj.GetComponent<TMP_Text>();
            }
        }
    }

    private IEnumerator AnimateGameOverScoreRoutine()
    {
        if (gameOverScore == null) yield break;

        // Brief delay so the Game Over popup scale-up animation is well underway
        yield return new WaitForSecondsRealtime(0.12f);

        float duration = 0.55f;
        float elapsed = 0f;
        int targetScore = currentScore;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            int displayedVal = Mathf.RoundToInt(Mathf.Lerp(0f, targetScore, smoothT));
            if (gameOverScore != null)
            {
                gameOverScore.text = displayedVal.ToString("N0");
            }

            yield return null;
        }

        if (gameOverScore != null)
        {
            gameOverScore.text = targetScore.ToString("N0");
            StartCoroutine(PunchGameOverScoreScale());
        }

        gameOverScoreAnimCoroutine = null;
    }

    private IEnumerator PunchGameOverScoreScale()
    {
        if (gameOverScore == null) yield break;
        RectTransform rt = gameOverScore.rectTransform;
        if (rt == null) yield break;

        Vector3 baseScale = rt.localScale;
        if (baseScale.sqrMagnitude < 0.001f) baseScale = Vector3.one;

        float punchDuration = 0.22f;
        float elapsed = 0f;

        while (elapsed < punchDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / punchDuration);
            float pulse = Mathf.Sin(t * Mathf.PI);

            rt.localScale = baseScale * Mathf.Lerp(1.0f, 1.15f, pulse);
            yield return null;
        }

        rt.localScale = baseScale;
    }

    #endregion
}
