using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles UI map left/right button clicks and plays the corresponding transition animation.
/// Communicates the target map index to MapSelectManager without directly changing the floor material.
/// </summary>
public class buttonFunctions : MonoBehaviour
{
    [Header("Animation Settings")]
    [SerializeField] private Animator uiAnimator;
    [SerializeField] private Animator menuLeaveAnimation;
    [SerializeField] private Animator mapLeaveAnimation;
    [SerializeField] private string mapLeft = "mapLeft";
    [SerializeField] private string mapRight = "MapRight";

    [Header("Manager Reference")]
    [SerializeField] private MapSelectManager mapSelectManager;
      [Header("Scene Management")]
    [SerializeField] string gameSceneName = "Game";
    private int activeMapIndex = 0;
    public static string lastButtonPressed="";
public void NavigateAwayFromMenu()
    {
        lastButtonPressed=gameObject.name;
        menuLeaveAnimation.Play("menuFadeOut", 0, 0f);
    }
    public void NavigateAwayFromMaps()
    {
        lastButtonPressed=gameObject.name;
        mapLeaveAnimation.Play("mapsFadeOut", 0, 0f);
    }
    private void Awake()
    {
        // Auto-locate MapSelectManager if not assigned in the Inspector
        if (mapSelectManager == null)
        {
            mapSelectManager = FindObjectOfType<MapSelectManager>();
        }
    }

    private void Start()
    {
        if (mapSelectManager != null)
        {
            activeMapIndex = mapSelectManager.ActiveMapIndex;
        }
    }
    public void LoadGameScene()
    {
        if (mapSelectManager == null)
        {
            mapSelectManager = FindObjectOfType<MapSelectManager>();
        }

        if (mapSelectManager != null)
        {
            mapSelectManager.ApplyMaterialChange();
        }

        // This instantly breaks out of the menu and opens your game
        // SceneManager.LoadScene(gameSceneName);
        lastButtonPressed=gameObject.name;
        mapLeaveAnimation.Play("mapsFadeOut", 0, 0f);
    }
    /// <summary>
    /// Called when the Left button is pressed.
    /// Updates the selected index, communicates with MapSelectManager, and triggers the transition animation.
    /// </summary>
    public void MapLeftPressed()
    {
        if (mapSelectManager != null)
        {
            activeMapIndex = mapSelectManager.ActiveMapIndex + 1;
            mapSelectManager.SetActiveMapIndex(activeMapIndex);
            activeMapIndex = mapSelectManager.ActiveMapIndex;
        }
        else
        {
            activeMapIndex++;
        }

        if (uiAnimator != null)
        {
            uiAnimator.Play(mapLeft, 0, 0f);
        }
    }

    /// <summary>
    /// Called when the Right button is pressed.
    /// Updates the selected index, communicates with MapSelectManager, and triggers the transition animation.
    /// </summary>
    public void MapRightPressed()
    {
        if (mapSelectManager != null)
        {
            activeMapIndex = mapSelectManager.ActiveMapIndex - 1;
            mapSelectManager.SetActiveMapIndex(activeMapIndex);
            activeMapIndex = mapSelectManager.ActiveMapIndex;
        }
        else
        {
            activeMapIndex--;
        }

        if (uiAnimator != null)
        {
            uiAnimator.Play(mapRight, 0, 0f);
        }
    }
}

