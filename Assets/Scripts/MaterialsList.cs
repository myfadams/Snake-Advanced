using UnityEngine;

[CreateAssetMenu(fileName = "ObjectList", menuName = "Game/Object List")]
public class MaterialsList : ScriptableObject
{
    public Material[] materials;

    public GameObject[] chessEnvironmentProps;
    public GameObject[] treeProps;
    public GameObject[] carProps;
    public GameObject[] ScifiProps;
    public GameObject[] AnyProp;
}