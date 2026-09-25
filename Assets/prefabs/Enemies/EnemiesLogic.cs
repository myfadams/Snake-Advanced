using System;
using TMPro;
using UnityEngine;

public class EnemiesLogic : MonoBehaviour
{
    [SerializeField] private float enmemySpeed = 3f;
    [SerializeField] private float rotationSpeed = 3f;
    
    private GameObject player;
    private static System.Random random;
    
    // FIX 1: Do not use "new TextMeshProUGUI()" here. Leave it unassigned.
    private TextMeshPro valueText; 
    private int enemyValue;

    void Awake()
    {
        if (random == null)
        {
            random = new System.Random();
        }
        enemyValue = chooseEnemyValue();
    }

    void Start()
    {
       body myScript = transform.GetChild(0).GetComponent("Body") as body;
       if(myScript)
            myScript.SetValue(chooseEnemyValue());
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("SnakeHead"))
        {
            player = other.gameObject;
            Debug.Log("Player Locked");
        }
    }

    void Update()
    {
        if (player)
        {
            Vector3 direction = player.transform.position - transform.position;
            direction.y = 0f;
            
            if (direction.magnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, 
                    targetRotation, 
                    rotationSpeed * Time.deltaTime
                );
            }

            transform.position = Vector3.MoveTowards(transform.position, player.transform.position, enmemySpeed * Time.deltaTime);
        }
    }

    int chooseEnemyValue()
    {
        int[] values = { 2, 4, 8, 16 };
        // FIX 3: random.Next(values.Length) returns index 0 to 3. 
        // We use that index to extract the actual value (2, 4, 8, or 16).
        int randomIndex = random.Next(values.Length);
        return values[randomIndex];
    }
}
