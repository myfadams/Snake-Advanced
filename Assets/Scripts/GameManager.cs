using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central source of truth for the game's block values and their associated colors.
/// Attach to: Game/GameManager
/// Other scripts read from GameManager.Instance instead of defining their own values.
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

    private Dictionary<int, Color> valueColorMap;

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
}