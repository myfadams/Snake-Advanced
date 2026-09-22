using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

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
        UpdateScoreText(0);
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
}