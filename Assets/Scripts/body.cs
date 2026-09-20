using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class body : MonoBehaviour
{
    [SerializeField] private TMP_Text cubeText;
    [SerializeField] private int cubeValueInt = 2;

    // Cache of dynamically generated colors for values not predefined in GameManager.
    // Static so all body instances share the same color for a given value throughout the playthrough.
    private static readonly Dictionary<int, Color> dynamicColorMap = new Dictionary<int, Color>();

    /// <summary>The value currently shown on this block.</summary>
    public int Value => cubeValueInt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDynamicColors()
    {
        dynamicColorMap.Clear();
    }

    private void Start()
    {
        ApplyVisuals();
    }

    /// <summary>
    /// Updates this block's value and refreshes its color and text
    /// through the existing GameManager / dynamic color system. Call this instead
    /// of changing the value directly so a block's visuals never fall
    /// out of sync with its value.
    /// </summary>
    public void SetValue(int newValue)
    {
        cubeValueInt = newValue;
        ApplyVisuals();
    }

    private void ApplyVisuals()
    {
        Renderer blockRenderer = GetComponent<Renderer>();
        if (blockRenderer == null)
        {
            blockRenderer = GetComponentInChildren<Renderer>();
        }

        if (blockRenderer != null)
        {
            blockRenderer.material.color = GetColorForValue(cubeValueInt);
        }

        if (cubeText == null)
        {
            cubeText = GetComponentInChildren<TMP_Text>();
        }

        if (cubeText != null)
        {
            cubeText.text = cubeValueInt.ToString();
        }
    }

    /// <summary>
    /// Returns the color associated with this block value. If predefined in GameManager, uses that.
    /// Otherwise, checks if a dynamic color was already generated for this value during this
    /// playthrough; if not, generates a distinct random color that isn't already specified
    /// and associates it with this value for the remainder of the playthrough.
    /// </summary>
    public static Color GetColorForValue(int value)
    {
        // 1. Check if a dynamic color was already assigned for this value during this playthrough
        if (dynamicColorMap.TryGetValue(value, out Color cachedColor))
        {
            return cachedColor;
        }

        // 2. Check if GameManager has a predefined color for this value
        if (GameManager.Instance != null && GameManager.Instance.TryGetBlockColor(value, out Color predefinedColor))
        {
            return predefinedColor;
        }

        // 3. Not specified - generate a distinct random color that isn't already specified
        Color newColor = GenerateDistinctRandomColor();
        dynamicColorMap[value] = newColor;

        // Also register with GameManager so any other systems (pickups, particles) stay in sync
        if (GameManager.Instance != null)
        {
            GameManager.Instance.RegisterBlockColor(value, newColor);
        }

        return newColor;
    }

    /// <summary>
    /// Generates a vibrant, distinct random color that maximizes separation
    /// from existing specified and already-generated colors.
    /// </summary>
    private static Color GenerateDistinctRandomColor()
    {
        List<Color> existingColors = new List<Color>();

        if (GameManager.Instance != null)
        {
            existingColors.AddRange(GameManager.Instance.GetAllDefinedColors());
        }

        existingColors.AddRange(dynamicColorMap.Values);

        Color bestCandidate = Color.magenta;
        float bestMinDistance = -1f;

        // Try candidate hues and select one that is well-separated from all existing colors
        for (int attempt = 0; attempt < 50; attempt++)
        {
            float hue = Random.value;
            float sat = Random.Range(0.7f, 0.95f);
            float val = Random.Range(0.75f, 0.95f);
            Color candidate = Color.HSVToRGB(hue, sat, val);

            float minDistance = float.MaxValue;
            foreach (Color existing in existingColors)
            {
                float dist = ColorDistance(candidate, existing);
                if (dist < minDistance)
                {
                    minDistance = dist;
                }
            }

            // If well-separated from all existing colors (> 0.35 distance in RGB), accept immediately
            if (minDistance > 0.35f)
            {
                return candidate;
            }

            if (minDistance > bestMinDistance)
            {
                bestMinDistance = minDistance;
                bestCandidate = candidate;
            }
        }

        return bestCandidate;
    }

    private static float ColorDistance(Color a, Color b)
    {
        float dr = a.r - b.r;
        float dg = a.g - b.g;
        float db = a.b - b.b;
        return Mathf.Sqrt(dr * dr + dg * dg + db * db);
    }
}