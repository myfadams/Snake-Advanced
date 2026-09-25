using UnityEngine;

/// <summary>
/// Detects when the snake head collides with its own body segments (self-collision)
/// and triggers Game Over.
/// Attach this script to the trigger cube object inside the snake head.
/// </summary>
public class DetectBody : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Number of front body segments immediately behind the head to ignore, preventing instant self-collision during normal movement and tight turning.")]
    [SerializeField] private int ignoreFrontSegmentsCount = 3;

    private void Awake()
    {
        // 1. CRITICAL UNITY PHYSICS RULE:
        // A Trigger collider MUST have a Rigidbody (even if kinematic) to reliably detect collisions
        // with static colliders (the body blocks). Without a Rigidbody, OnTriggerEnter will NOT fire in Unity.
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        rb.isKinematic = true;
        rb.useGravity = false;

        // 2. Ensure collider is set to Trigger mode
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;

        // Check if the collided object is a body block or tagged SnakeBody
        bool isBody = other.CompareTag("SnakeBody") || other.GetComponent<body>() != null;
        if (!isBody) return;

        // Retrieve the snake segment index to ignore neck/front segments
        if (SnakeGrow.Instance != null)
        {
            var segments = SnakeGrow.Instance.Segments;
            int segmentIndex = -1;

            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i] != null && (segments[i] == other.transform || other.transform.IsChildOf(segments[i])))
                {
                    segmentIndex = i; // Index 0 is Head, index 1 is 1st body segment, etc.
                    break;
                }
            }

            // If the segment hit is further back than the ignored neck/front segments (e.g. index > 3)
            if (segmentIndex > ignoreFrontSegmentsCount)
            {
                Debug.Log($"[DetectBody] Self-collision detected with body segment #{segmentIndex}! Triggering Game Over.");
                SnakeGrow.Instance.TriggerDeathOrGameOver();
            }
        }
        else if (GameStakes.Instance != null)
        {
            GameStakes.Instance.TriggerGameOver();
        }
    }
}
