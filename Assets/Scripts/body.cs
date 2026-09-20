using UnityEngine;
using TMPro;
public class body : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [SerializeField] private  TMP_Text cubeText ;
    [SerializeField] private  int cubeValueInt = 2; 
    void Start()
    {
        gameObject.GetComponent<Renderer>().material.color = GameManager.Instance.GetBlockColor(cubeValueInt);
        cubeText.text = cubeValueInt.ToString();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
