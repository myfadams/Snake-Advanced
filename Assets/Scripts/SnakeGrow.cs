using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the snake's growth, block values, and adjacent-value merging.
/// Attach this to the same Player GameObject as PlayerMovement.
///
/// The Head and every body segment (auto-detected the same way
/// PlayerMovement does - every direct child of Player except Head) form
/// one logical value sequence, Head first: Head -> Body -> Body 1 -> Body 2 -> ...
///
/// Eating a pickup inserts a new block immediately after the Head.
/// The whole sequence, including the Head, is then immediately checked for
/// adjacent equal values and merged in a 2048-style cascading chain reaction until stable.
/// The Head is always preserved as the Head GameObject (updated in place via body.SetValue).
/// Growth and merges are pushed to PlayerMovement.SyncBodySegments so the movement
/// system's real path history is preserved - nothing ever resets or snaps the snake's motion,
/// and constant spacing between segments is maintained.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class SnakeGrow : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The Body prefab to instantiate when a new segment is inserted after the Head.")]
    [SerializeField] private GameObject bodyPrefab;

    [Tooltip("The PlayerMovement component that drives the snake's movement. If left empty, this is fetched automatically from this GameObject at Start.")]
    [SerializeField] private PlayerMovement playerMovement;

    // segments[0] is always the Head; segments[1..] are the body, in the
    // same order as the physical hierarchy under Player.
    private readonly List<Transform> segments = new List<Transform>();

    private Transform head;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void Start()
    {
        EnsureInitialized();
        DetectSegments();
        UpdateHierarchyOrder();
    }

    private void EnsureInitialized()
    {
        if (playerMovement == null)
        {
            playerMovement = GetComponent<PlayerMovement>();
        }

        if (playerMovement == null)
        {
            Debug.LogError("SnakeGrow: no PlayerMovement found on " + name + " - the movement system will not stay in sync as the snake grows.");
        }
        else if (head == null)
        {
            head = playerMovement.Head;
        }

        if (head != null && head.GetComponent<body>() == null)
        {
            head.gameObject.AddComponent<body>();
        }
    }

    /// <summary>
    /// Call this when the snake eats a pickup (e.g. from a Pickup
    /// script's trigger callback: GetComponent&lt;SnakeGrow&gt;().Grow(value)).
    /// Inserts a new block worth pickupValue immediately after the Head, resolves any
    /// resulting adjacent merges (the Head included) in a cascading chain reaction,
    /// and syncs the result with the movement system in one step.
    /// </summary>
    public void Grow(int pickupValue)
    {
        if (bodyPrefab == null)
        {
            Debug.LogWarning("SnakeGrow: no Body Prefab assigned - cannot add a new segment.");
            return;
        }

        EnsureInitialized();

        if (segments.Count == 0)
        {
            DetectSegments();
        }

        InsertSegmentAfterHead(pickupValue);
        ResolveMerges();
        SyncMovement();
    }

    /// <summary>
    /// Finds the Head (via PlayerMovement) and every direct child of
    /// Player other than Head, in hierarchy order - no manual Inspector
    /// assignment required, and it works with any number of segments.
    /// </summary>
    private void DetectSegments()
    {
        segments.Clear();

        if (head != null)
        {
            segments.Add(head);
        }

        foreach (Transform child in transform)
        {
            if (child != head)
            {
                segments.Add(child);
            }
        }
    }

    /// <summary>
    /// Instantiates a new Body block and inserts it immediately after the Head (index 1).
    /// Updates the physical hierarchy under Player so that it stays synchronized with the logical order.
    /// </summary>
    private void InsertSegmentAfterHead(int value)
    {
        if (head == null)
        {
            Debug.LogWarning("SnakeGrow: head is null - cannot insert segment after Head.");
            return;
        }

        // Spawn position between Head and the existing first body segment (if any),
        // or directly at Head if no body segments currently exist.
        Vector3 spawnPosition = head.position;
        Quaternion spawnRotation = head.rotation;

        if (segments.Count > 1 && segments[1] != null)
        {
            spawnPosition = (head.position + segments[1].position) * 0.5f;
            spawnRotation = segments[1].rotation;
        }

        GameObject instance = Instantiate(bodyPrefab, spawnPosition, spawnRotation, transform);
        instance.name = bodyPrefab.name;
        Transform newSegment = instance.transform;

        body newBody = newSegment.GetComponent<body>();
        if (newBody != null)
        {
            newBody.SetValue(value);
        }
        else
        {
            Debug.LogWarning("SnakeGrow: the Body Prefab has no 'body' component - cannot assign its value.");
        }

        // Insert directly after the Head (index 1 in the logical sequence)
        segments.Insert(1, newSegment);

        // Synchronize the physical Player hierarchy order immediately
        UpdateHierarchyOrder();
    }

    /// <summary>
    /// Scans the whole sequence (Head included) for adjacent equal
    /// values and merges them, restarting the scan after every merge so
    /// consecutive/cascading merges (e.g. [2][2][4] -> [4][4] -> [8])
    /// resolve completely before returning. The front block of a merging
    /// pair is always the one kept and updated via body.SetValue; the
    /// back one is removed - so the Head is only ever updated in place,
    /// never destroyed.
    /// </summary>
    private void ResolveMerges()
    {
        bool mergedAny = true;

        while (mergedAny)
        {
            mergedAny = false;

            for (int i = 0; i < segments.Count - 1; i++)
            {
                if (segments[i] == null || segments[i + 1] == null)
                    continue;

                body current = segments[i].GetComponent<body>();
                body next = segments[i + 1].GetComponent<body>();

                if (current == null || next == null)
                    continue;

                if (current.Value != next.Value)
                    continue;

                // Merge into the front block
                current.SetValue(current.Value * 2);

                // Remove the rear block
                Transform removed = segments[i + 1];
                segments.RemoveAt(i + 1);

                if (removed != null)
                {
                    removed.gameObject.SetActive(false);
                    removed.SetParent(null);
                    Destroy(removed.gameObject);
                }

                mergedAny = true;
                break; // Restart scan from the front to continue chain reactions
            }
        }

        UpdateHierarchyOrder();
    }

    /// <summary>
    /// Ensures the physical Player hierarchy matches the logical segment order:
    /// segments[0] (Head) at sibling index 0, followed by segments[1], segments[2], etc.
    /// </summary>
    private void UpdateHierarchyOrder()
    {
        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i] != null)
            {
                segments[i].SetSiblingIndex(i);
            }
        }
    }

    /// <summary>
    /// Pushes the current (post-growth/merge) body order to PlayerMovement
    /// so it repositions/spaces everything correctly - using the same
    /// real path history the snake has already been following.
    /// </summary>
    private void SyncMovement()
    {
        if (playerMovement == null)
            return;

        List<Transform> bodyList = new List<Transform>();
        for (int i = 1; i < segments.Count; i++)
        {
            if (segments[i] != null)
            {
                bodyList.Add(segments[i]);
            }
        }

        playerMovement.SyncBodySegments(bodyList);
    }
}