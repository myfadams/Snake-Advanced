using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the snake's growth, block values, and visually smooth adjacent-value merging.
/// Attach this to the same Player GameObject as PlayerMovement.
///
/// The Head and every body segment form one logical value sequence, Head first:
/// Head -> Body -> Body 1 -> Body 2 -> ...
///
/// Eating a pickup inserts a new block immediately after the Head.
/// The new segment is smoothly grown and integrated into the snake's movement chain over
/// growthDuration using an easing curve, allowing existing segments to seamlessly glide
/// backward along the path history without snapping or jumping.
///
/// Once growth completes, adjacent blocks with equal values are detected and smoothly animate
/// moving toward their shared midpoint using an easing curve.
/// Upon meeting, the front block doubles in value and updates its color via Body.cs / GameManager,
/// while the rear block is removed. A subtle scale punch plays as the surviving block returns
/// smoothly to its exact position in the snake's movement chain.
///
/// Chain merges (e.g. [8] [4] [2] [2] -> [8] [4] [4] -> [8] [8] -> [16]) are resolved
/// sequentially, each with its own distinct smooth merge animation and scale punch.
/// The Head participates fully in merge animations without ever being destroyed or recreated.
/// Snake movement along the path continues smoothly without pausing or freezing during growth and merges.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class SnakeGrow : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The Body prefab to instantiate when a new segment is inserted after the Head.")]
    [SerializeField] private GameObject bodyPrefab;

    [Tooltip("The PlayerMovement component that drives the snake's movement. If left empty, this is fetched automatically from this GameObject at Start.")]
    [SerializeField] private PlayerMovement playerMovement;

    [Header("Growth Animation")]
    [Tooltip("Duration in seconds for a newly inserted body segment to smoothly grow and integrate behind the Head.")]
    [Range(0.05f, 0.4f)]
    [SerializeField] private float growthDuration = 0.15f;

    [Tooltip("Easing curve used when smoothly inserting the new body segment.")]
    [SerializeField] private AnimationCurve growthCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Merge Animation")]
    [Tooltip("Duration in seconds for two adjacent blocks to move toward their shared midpoint.")]
    [Range(0.05f, 0.5f)]
    [SerializeField] private float mergeMoveDuration = 0.18f;

    [Tooltip("Easing curve used when moving merging blocks toward their shared midpoint.")]
    [SerializeField] private AnimationCurve mergeMovementCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Scale Punch Effect")]
    [Tooltip("Duration in seconds of the subtle scale pop when two blocks merge.")]
    [Range(0.03f, 0.3f)]
    [SerializeField] private float scalePunchDuration = 0.10f;

    [Tooltip("Peak scale multiplier for the surviving block during the merge pop.")]
    [Range(1.05f, 1.6f)]
    [SerializeField] private float scalePunchMultiplier = 1.25f;

    // segments[0] is always the Head; segments[1..] are the body, in the
    // same order as the physical hierarchy under Player.
    private readonly List<Transform> segments = new List<Transform>();

    private Transform head;
    private Vector3 originalHeadLocalPos;
    private readonly Queue<int> pendingPickups = new Queue<int>();
    private bool isProcessing = false;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void Start()
    {
        EnsureInitialized();
        DetectSegments();
        UpdateHierarchyOrder();

        // Check if the scene already contains any adjacent mergeable pairs
        if (FindMergeablePairIndex() != -1 && !isProcessing && gameObject.activeInHierarchy)
        {
            StartCoroutine(ProcessGrowthAndMergesCoroutine());
        }
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        isProcessing = false;
        pendingPickups.Clear();

        if (playerMovement != null)
        {
            playerMovement.SetInsertionProgress(1f);
        }

        if (head != null)
        {
            head.localPosition = originalHeadLocalPos;
        }
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

        if (head != null)
        {
            originalHeadLocalPos = head.localPosition;

            if (head.GetComponent<body>() == null)
            {
                head.gameObject.AddComponent<body>();
            }
        }
    }

    /// <summary>
    /// Call this when the snake eats a pickup (e.g. from Pickup.cs trigger callback).
    /// Queues the pickup and starts the asynchronous growth and smooth merge pipeline.
    /// Rapid pickup collections are queued safely so animations never conflict or drop items.
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

        pendingPickups.Enqueue(pickupValue);

        if (!isProcessing && gameObject.activeInHierarchy)
        {
            StartCoroutine(ProcessGrowthAndMergesCoroutine());
        }
    }

    /// <summary>
    /// Processes all queued pickups, smoothly animating each block's insertion growth,
    /// followed by any resulting cascading merges until the snake reaches a stable state.
    /// </summary>
    private IEnumerator ProcessGrowthAndMergesCoroutine()
    {
        isProcessing = true;

        while (pendingPickups.Count > 0)
        {
            int pickupValue = pendingPickups.Dequeue();
            Transform newSegment = InsertSegmentAfterHead(pickupValue);
            SyncMovement();

            // Smoothly animate the newly inserted block's growth and path integration
            if (newSegment != null)
            {
                yield return StartCoroutine(AnimateGrowth(newSegment));
            }

            // Sequentially resolve all cascading chain merges with smooth visual animations
            while (true)
            {
                int pairIndex = FindMergeablePairIndex();
                if (pairIndex == -1)
                    break;

                yield return StartCoroutine(AnimateMergePair(pairIndex));
            }
        }

        isProcessing = false;
    }

    /// <summary>
    /// Smoothly animates the newly inserted block growing behind the Head:
    /// scales it up from a small initial size while smoothly transitioning
    /// PlayerMovement's insertion progress from 0 to 1 so existing segments
    /// slide back along the path without any sudden jumping or snapping.
    /// </summary>
    private IEnumerator AnimateGrowth(Transform newSegment)
    {
        if (newSegment == null)
            yield break;

        Vector3 targetScale = newSegment.localScale;
        Vector3 startScale = targetScale * 0.2f;
        newSegment.localScale = startScale;

        if (playerMovement != null)
        {
            playerMovement.SetInsertionProgress(0f);
        }

        float elapsed = 0f;
        while (elapsed < growthDuration)
        {
            yield return null; // Wait for Update() so PlayerMovement has moved the snake along its path

            if (newSegment == null)
                break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / growthDuration);
            float ease = growthCurve != null ? growthCurve.Evaluate(t) : Mathf.SmoothStep(0f, 1f, t);

            if (playerMovement != null)
            {
                playerMovement.SetInsertionProgress(ease);
            }

            newSegment.localScale = Vector3.Lerp(startScale, targetScale, ease);
        }

        if (newSegment != null)
        {
            newSegment.localScale = targetScale;
        }

        if (playerMovement != null)
        {
            playerMovement.SetInsertionProgress(1f);
        }
    }

    /// <summary>
    /// Scans the segment list from front to back and returns the index of the first
    /// adjacent pair with identical values. Returns -1 if no adjacent pair matches.
    /// Index 0 indicates the Head and the first body segment match.
    /// </summary>
    private int FindMergeablePairIndex()
    {
        for (int i = 0; i < segments.Count - 1; i++)
        {
            if (segments[i] == null || segments[i + 1] == null)
                continue;

            body current = segments[i].GetComponent<body>();
            body next = segments[i + 1].GetComponent<body>();

            if (current == null || next == null)
                continue;

            if (current.Value == next.Value)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Smoothly animates two adjacent equal-value blocks moving toward their shared midpoint,
    /// merges into the surviving front block, updates its value and color, removes the rear block,
    /// and plays a subtle scale punch while smoothly returning the surviving block to its
    /// exact slot in the snake's movement chain.
    /// </summary>
    private IEnumerator AnimateMergePair(int i)
    {
        if (i < 0 || i >= segments.Count - 1)
            yield break;

        Transform segmentA = segments[i];
        Transform segmentB = segments[i + 1];

        if (segmentA == null || segmentB == null)
            yield break;

        bool isHeadMerge = (i == 0);

        // 1. Animate moving toward shared midpoint
        float elapsed = 0f;
        while (elapsed < mergeMoveDuration)
        {
            yield return null; // Wait for Update() so PlayerMovement has moved the snake along its path

            if (segmentA == null || segmentB == null)
                break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / mergeMoveDuration);
            float ease = mergeMovementCurve != null ? mergeMovementCurve.Evaluate(t) : Mathf.SmoothStep(0f, 1f, t);

            Vector3 posA = segmentA.position;
            Vector3 posB = segmentB.position;
            Vector3 midpoint = (posA + posB) * 0.5f;

            segmentA.position = Vector3.Lerp(posA, midpoint, ease);
            segmentB.position = Vector3.Lerp(posB, midpoint, ease);

            if (isHeadMerge)
            {
                // Restore head.localPosition at end of frame so next frame's PlayerMovement.Update()
                // always records an uncorrupted, forward-moving path history point.
                yield return new WaitForEndOfFrame();
                if (head != null)
                {
                    head.localPosition = originalHeadLocalPos;
                }
            }
        }

        if (isHeadMerge && head != null)
        {
            head.localPosition = originalHeadLocalPos;
        }

        // Validate segments before completing merge
        if (i >= segments.Count - 1 || segments[i] != segmentA || segments[i + 1] != segmentB)
            yield break;

        // Offset from segmentA's natural position to the meeting midpoint
        Vector3 initialOffset = (segmentB.position - segmentA.position) * 0.5f;

        // 2. Complete the merge
        body currentBody = segmentA.GetComponent<body>();
        if (currentBody != null)
        {
            currentBody.SetValue(currentBody.Value * 2);
        }

        // Remove the rear block
        segments.RemoveAt(i + 1);
        if (segmentB != null)
        {
            segmentB.gameObject.SetActive(false);
            segmentB.SetParent(null);
            Destroy(segmentB.gameObject);
        }

        UpdateHierarchyOrder();
        SyncMovement();

        // 3. Subtle scale punch on surviving block and smooth return to movement chain
        if (segmentA != null)
        {
            Vector3 initialScale = segmentA.localScale;
            float punchElapsed = 0f;

            while (punchElapsed < scalePunchDuration)
            {
                yield return null; // Wait for Update() to update segment positions along the path
                if (segmentA == null)
                    break;

                punchElapsed += Time.deltaTime;
                float punchT = Mathf.Clamp01(punchElapsed / scalePunchDuration);

                // Subtle scale pop: normal -> slightly larger -> back to normal (sine wave)
                float punchFactor = 1f + (scalePunchMultiplier - 1f) * Mathf.Sin(punchT * Mathf.PI);
                segmentA.localScale = initialScale * punchFactor;

                // Smoothly ease position from meeting midpoint back to the exact movement chain slot
                Vector3 currentOffset = Vector3.Lerp(initialOffset, Vector3.zero, punchT);
                segmentA.position += currentOffset;

                if (isHeadMerge)
                {
                    yield return new WaitForEndOfFrame();
                    if (head != null)
                    {
                        head.localPosition = originalHeadLocalPos;
                    }
                }
            }

            if (segmentA != null)
            {
                segmentA.localScale = initialScale;
            }
            if (isHeadMerge && head != null)
            {
                head.localPosition = originalHeadLocalPos;
            }
        }
    }

    /// <summary>
    /// Finds the Head (via PlayerMovement) and every direct child of
    /// Player other than Head, in hierarchy order.
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
    /// Returns the instantiated Transform so growth animation can be driven on it.
    /// </summary>
    private Transform InsertSegmentAfterHead(int value)
    {
        if (head == null)
        {
            Debug.LogWarning("SnakeGrow: head is null - cannot insert segment after Head.");
            return null;
        }

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

        return newSegment;
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
    /// Pushes the current body order to PlayerMovement so it repositions and spaces
    /// everything correctly along the real path history without resetting movement.
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