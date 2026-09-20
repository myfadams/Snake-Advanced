using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the snake's growth, block values, and adjacent-value merging.
/// Attach this to the same Player GameObject as PlayerMovement.
///
/// The Head and every body segment (auto-detected the same way
/// PlayerMovement does - every direct child of Player except Head) form
/// one logical value sequence, Head first. Eating a pickup appends a new
/// block to the tail; the whole sequence, including the Head, is then
/// checked for adjacent equal values and merged until stable. Growth and
/// merges are pushed to PlayerMovement.SyncBodySegments so the movement
/// system's real path history is preserved - nothing ever resets or
/// snaps the snake's motion.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class SnakeGrow : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The Body prefab to instantiate when a new segment is added to the tail.")]
    [SerializeField] private GameObject bodyPrefab;

    [Tooltip("The PlayerMovement component that drives the snake's movement. If left empty, this is fetched automatically from this GameObject at Start.")]
    [SerializeField] private PlayerMovement playerMovement;

    // segments[0] is always the Head; segments[1..] are the body, in the
    // same order as the physical hierarchy under Player.
    private readonly List<Transform> segments = new List<Transform>();

    private Transform head;

    private void Start()
    {
        if (playerMovement == null)
        {
            playerMovement = GetComponent<PlayerMovement>();
        }

        if (playerMovement == null)
        {
            Debug.LogError("SnakeGrow: no PlayerMovement found on " + name + " - the movement system will not stay in sync as the snake grows.");
        }
        else
        {
            head = playerMovement.Head;
        }

        DetectSegments();
    }

    /// <summary>
    /// Call this when the snake eats a pickup (e.g. from a Pickup
    /// script's trigger callback: GetComponent&lt;SnakeGrow&gt;().Grow(value)).
    /// Adds a new block worth pickupValue to the tail, resolves any
    /// resulting adjacent merges (the Head included), then syncs the
    /// result with the movement system in one step.
    /// </summary>
    public void Grow(int pickupValue)
    {
        if (bodyPrefab == null)
        {
            Debug.LogWarning("SnakeGrow: no Body Prefab assigned - cannot add a new segment.");
            return;
        }

        AddSegmentAtTail(pickupValue);
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

    private void AddSegmentAtTail(int value)
    {
        Transform tail = segments[segments.Count - 1];

        // Spawn right at the current tail - it separates smoothly on its
        // own as the snake keeps moving and the path extends behind it,
        // rather than popping in somewhere unrelated to the snake.
        GameObject instance = Instantiate(bodyPrefab, tail.position, tail.rotation, transform);
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

        segments.Add(newSegment);
    }

    /// <summary>
    /// Scans the whole sequence (Head included) for adjacent equal
    /// values and merges them, restarting the scan after every merge so
    /// consecutive/cascading merges (e.g. [2][2][2][2] -> [4][4] -> [8])
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
                body current = segments[i].GetComponent<body>();
                body next = segments[i + 1].GetComponent<body>();

                if (current == null || next == null)
                    continue;

                if (current.Value != next.Value)
                    continue;

                current.SetValue(current.Value * 2);

                Transform removed = segments[i + 1];
                segments.RemoveAt(i + 1);
                Destroy(removed.gameObject);

                mergedAny = true;
                break; // restart the scan - indices shifted after the removal
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
        if (playerMovement == null || segments.Count == 0)
            return;

        playerMovement.SyncBodySegments(segments.GetRange(1, segments.Count - 1));
    }
}