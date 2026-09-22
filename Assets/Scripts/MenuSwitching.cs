using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuSwitching : MonoBehaviour
{
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private GameObject mapsPanel;

    private void Start()
    {
        if (buttonFunctions.lastButtonPressed == "SinglePlayer" || buttonFunctions.lastButtonPressed == "Maps" || buttonFunctions.lastButtonPressed == "MapSelect")
        {
            if (menuPanel != null) menuPanel.SetActive(false);
            if (mapsPanel != null)
            {
                mapsPanel.SetActive(true);
                Animator anim = mapsPanel.GetComponent<Animator>();
                if (anim != null) anim.Play("mapsFadeIn");
            }
        }
    }
    public void NavigateAwayFromMenu()
    {
        switch (buttonFunctions.lastButtonPressed)
        {
            

            case "SinglePlayer":
                menuPanel.SetActive(false);
                mapsPanel.SetActive(true);
                mapsPanel.GetComponent<Animator>().Play("mapsFadeIn");
                break;

            case "Multiplayer":
                break;
            case "Settings":
                
                break;
            default:
                Debug.LogWarning("Animation event fired, but no matching button ID was found.");
                break;
        }
    }

    public void NavigateAwayFromMaps()
    {
        switch (buttonFunctions.lastButtonPressed)
        {
            

            case "Home":
                mapsPanel.SetActive(false);
                menuPanel.SetActive(true);
                menuPanel.GetComponent<Animator>().Play("menuFadeIn");
                break;

            case "Play":
                SceneManager.LoadScene("Game");
                break;
            default:
                Debug.LogWarning("Animation event fired, but no matching button ID was found.");
                break;
        }
    }
}
