using UnityEngine;

public class ChessPiece : MonoBehaviour
{
    [Header("Chess Prop Materials")]
    [SerializeField] MaterialsList chessMaterials;

    private bool isChessProp = false;

    void Start()
    {
        if (gameObject.CompareTag("ChessProp"))
        {
            isChessProp = true;
        }

        if (isChessProp)
        {
            GetComponent<Renderer>().material = RandomMaterial();
        }
    }

    Material RandomMaterial()
    {
        return chessMaterials.materials[
            Random.Range(0, chessMaterials.materials.Length)
        ];
    }
}