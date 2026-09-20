using UnityEngine;
using TMPro;

public class body : MonoBehaviour
{
    [SerializeField] private TMP_Text cubeText;
    [SerializeField] private int cubeValueInt = 2;

    /// <summary>The value currently shown on this block.</summary>
    public int Value => cubeValueInt;

    private void Start()
    {
        ApplyVisuals();
    }

    /// <summary>
    /// Updates this block's value and refreshes its color and text
    /// through the existing GameManager color system. Call this instead
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
        gameObject.GetComponent<Renderer>().material.color = GameManager.Instance.GetBlockColor(cubeValueInt);

        if (cubeText != null)
        {
            cubeText.text = cubeValueInt.ToString();
        }
    }
}