using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the snake's growth, block values, visually smooth adjacent-value merging,
/// and body-damage hazard mechanics. Attach this to the same Player GameObject as PlayerMovement.
///
/// Logical value sequence, Head first:
/// Head -> Body -> Body 1 -> Body 2 -> ...
///
/// 1. GROWTH:
/// Eating a pickup inserts a new block immediately after the Head. The new segment smoothly
/// grows and integrates into the movement chain over growthDuration using an easing curve,
/// allowing existing segments to seamlessly glide backward along the path history without snapping.
///
/// 2. MERGES:
/// When adjacent blocks with equal values are detected, they smoothly animate toward their
/// shared midpoint over mergeMoveDuration. Upon meeting, the front block doubles in value
/// and updates its color via Body.cs / GameManager, while the rear block is removed.
/// A subtle scale punch plays as the surviving block returns smoothly into the movement chain.
/// Cascading chain merges resolve sequentially.
///
/// 3. BODY DAMAGE:
/// When a dangerous block or hazard hits a body segment:
/// - The damaged segment tumbles, shrinks, and glides away over damageEjectDuration.
/// - The remaining segments smoothly close the gap along the real path history without snapping.
/// - The Head's value is reduced based on 2048 progression levels (2->lvl 1, 4->lvl 2, etc.),
///   playing an animated scale pop and color update.
/// - The Head itself is immune to normal body damage and never drops below 2.
/// - If reduction would drop the Head below 2, OnDeathCondition is triggered.
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

    [Header("Body Damage Animation")]
    [Tooltip("Duration in seconds for the damaged block to eject, tumble, and shrink away.")]
    [Range(0.1f, 0.5f)]
    [SerializeField] private float damageEjectDuration = 0.22f;

    [Tooltip("Distance the damaged block flies away from the snake as it is destroyed.")]
    [Range(0.3f, 3.0f)]
    [SerializeField] private float damageEjectDistance = 1.0f;

    [Tooltip("Duration in seconds for remaining segments to smoothly close the gap left by the destroyed block.")]
    [Range(0.1f, 0.5f)]
    [SerializeField] private float gapCloseDuration = 0.20f;

    [Tooltip("Easing curve used when smoothly closing the gap.")]
    [SerializeField] private AnimationCurve gapCloseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Head Damage Animation")]
    [Tooltip("Duration in seconds for the Head to animate its value reduction scale punch.")]
    [Range(0.08f, 0.4f)]
    [SerializeField] private float headDamagePunchDuration = 0.16f;

    [Tooltip("Peak scale multiplier for the Head during its damage value reduction pop.")]
    [Range(1.05f, 1.5f)]
    [SerializeField] private float headDamageScaleMultiplier = 1.25f;

    /// <summary>
    /// Event triggered when the Head's value would be reduced below 2.
    /// Can be subscribed to by game-over or death systems.
    /// </summary>
    public event System.Action OnDeathCondition;

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
            playerMovement.SetGapClosingProgress(-1, 1f, 0f);
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

    #region Body Damage System

    /// <summary>
    /// Call when a hazard or dangerous entity hits a snake body segment.
    /// The damaged segment leaves the snake with a smooth ejection animation,
    /// trailing segments close the gap smoothly along the path, and the Head's
    /// value is reduced based on 2048-style progression levels.
    /// The Head itself is immune to this method.
    /// Returns true if a body segment was damaged, false otherwise.
    /// </summary>
    public bool TakeBodyDamage(Transform damagedSegment)
    {
        if (damagedSegment == null)
            return false;

        // The Head cannot take body damage
        if (damagedSegment == head || (segments.Count > 0 && damagedSegment == segments[0]))
            return false;

        int segmentIndex = segments.IndexOf(damagedSegment);
        if (segmentIndex <= 0) // -1 not found, 0 is Head
            return false;

        body damagedBody = damagedSegment.GetComponent<body>();
        int damagedValue = damagedBody != null ? damagedBody.Value : 2;

        // Disable collider immediately to prevent duplicate hits
        Collider col = damagedSegment.GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
        }

        // Unparent so it is no longer bound to the Player's transform hierarchy
        damagedSegment.SetParent(null);

        // Remove from logical segments list immediately
        segments.RemoveAt(segmentIndex);
        UpdateHierarchyOrder();

        // Calculate gap size to close in PlayerMovement
        float gapToClose = playerMovement != null ? playerMovement.GetDefaultGap() : 0.3f;

        // Index in PlayerMovement.bodySegments is segmentIndex - 1 (since segments[0] is Head)
        int bodyIndex = segmentIndex - 1;

        // Sync remaining body segments with PlayerMovement
        SyncMovement();

        // 1. Calculate Head's new value based on 2048 progression levels
        body headBody = head != null ? head.GetComponent<body>() : null;
        int currentHeadVal = headBody != null ? headBody.Value : 2;
        int newHeadVal = CalculateReducedHeadValue(currentHeadVal, damagedValue);

        // 2. Animate damaged segment flying away
        StartCoroutine(AnimateDamagedSegmentEjection(damagedSegment));

        // 3. Smoothly close the gap along the movement path
        if (bodyIndex < segments.Count - 1) // If there are segments behind the removed one
        {
            StartCoroutine(AnimateGapClosing(bodyIndex, gapToClose));
        }

        // 4. Animate Head value reduction pop
        if (headBody != null && head != null)
        {
            StartCoroutine(AnimateHeadDamage(headBody, newHeadVal));
        }

        return true;
    }

    /// <summary>
    /// Calculates the new Head value using 2048-style progression levels:
    /// 2 -> level 1, 4 -> level 2, 8 -> level 3, 16 -> level 4, etc.
    /// Reduces the Head by the number of levels represented by the destroyed segment.
    /// The Head never drops below 2; if it would, it triggers the death condition.
    /// </summary>
    private int CalculateReducedHeadValue(int currentHeadValue, int destroyedSegmentValue)
    {
        int headLevel = Mathf.Max(1, Mathf.RoundToInt(Mathf.Log(Mathf.Max(2, currentHeadValue), 2)));
        int lossLevels = Mathf.Max(1, Mathf.RoundToInt(Mathf.Log(Mathf.Max(2, destroyedSegmentValue), 2)));

        int newLevel = headLevel - lossLevels;
        if (newLevel < 1)
        {
            TriggerDeathOrGameOver();
            return 2;
        }

        return 1 << newLevel;
    }

    private void TriggerDeathOrGameOver()
    {
        Debug.Log("SnakeGrow: Death condition triggered - Head reduced to minimum (2).");
        OnDeathCondition?.Invoke();
    }

    /// <summary>
    /// Smoothly animates the damaged segment flying away from the snake:
    /// drifts sideways/upward, tumbles, and shrinks to zero before being destroyed.
    /// </summary>
    private IEnumerator AnimateDamagedSegmentEjection(Transform ejectedTransform)
    {
        if (ejectedTransform == null)
            yield break;

        Vector3 startPos = ejectedTransform.position;
        Vector3 startScale = ejectedTransform.localScale;
        Quaternion startRot = ejectedTransform.rotation;

        // Ejection trajectory: sideways drift + slight upward impulse
        Vector3 forward = head != null ? head.forward : Vector3.forward;
        Vector3 right = head != null ? head.right : Vector3.right;
        float sideSign = Random.value > 0.5f ? 1f : -1f;
        Vector3 ejectDir = (right * (sideSign * 0.8f) + Vector3.up * 0.9f - forward * 0.2f).normalized;

        Vector3 targetPos = startPos + ejectDir * damageEjectDistance;
        Vector3 randomTorque = new Vector3(
            Random.Range(-180f, 180f),
            Random.Range(-180f, 180f),
            Random.Range(-180f, 180f)
        );

        float elapsed = 0f;
        while (elapsed < damageEjectDuration)
        {
            yield return null;
            if (ejectedTransform == null)
                yield break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / damageEjectDuration);
            float ease = Mathf.SmoothStep(0f, 1f, t);

            ejectedTransform.position = Vector3.Lerp(startPos, targetPos, ease);
            ejectedTransform.rotation = startRot * Quaternion.Euler(randomTorque * ease);
            ejectedTransform.localScale = Vector3.Lerp(startScale, Vector3.zero, ease);
        }

        if (ejectedTransform != null)
        {
            Destroy(ejectedTransform.gameObject);
        }
    }

    /// <summary>
    /// Smoothly eases the trailing segments forward along the snake's path to seal
    /// the gap left by the removed segment without sudden jumping.
    /// </summary>
    private IEnumerator AnimateGapClosing(int removedBodyIndex, float gapAmount)
    {
        if (playerMovement == null)
            yield break;

        float elapsed = 0f;
        while (elapsed < gapCloseDuration)
        {
            yield return null;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / gapCloseDuration);
            float ease = gapCloseCurve != null ? gapCloseCurve.Evaluate(t) : Mathf.SmoothStep(0f, 1f, t);

            if (playerMovement != null)
            {
                playerMovement.SetGapClosingProgress(removedBodyIndex, ease, gapAmount);
            }
        }

        if (playerMovement != null)
        {
            playerMovement.SetGapClosingProgress(-1, 1f, 0f);
        }
    }

    /// <summary>
    /// Smoothly animates the Head's value reduction:
    /// scales up slightly, updates the value and color at the apex, and returns to normal scale.
    /// </summary>
    private IEnumerator AnimateHeadDamage(body headBodyComponent, int newHeadValue)
    {
        if (head == null || headBodyComponent == null)
            yield break;

        Vector3 originalScale = head.localScale;
        bool valueUpdated = false;

        float elapsed = 0f;
        while (elapsed < headDamagePunchDuration)
        {
            yield return null;
            if (head == null)
                yield break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / headDamagePunchDuration);

            // Half-way through the pop, flip the value and color
            if (t >= 0.5f && !valueUpdated)
            {
                valueUpdated = true;
                headBodyComponent.SetValue(newHeadValue);
            }

            // Sine wave pop: 1.0 -> headDamageScaleMultiplier -> 1.0
            float punchFactor = 1f + (headDamageScaleMultiplier - 1f) * Mathf.Sin(t * Mathf.PI);
            head.localScale = originalScale * punchFactor;
        }

        if (!valueUpdated && headBodyComponent != null)
        {
            headBodyComponent.SetValue(newHeadValue);
        }

        if (head != null)
        {
            head.localScale = originalScale;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.GetComponent<Hazard>() != null || collision.gameObject.tag == "Hazard")
        {
            // Identify which child collider on the snake was hit
            if (collision.contactCount > 0)
            {
                Collider hitCollider = collision.GetContact(0).thisCollider;
                if (hitCollider != null)
                {
                    TakeBodyDamage(hitCollider.transform);
                }
            }
        }
    }

    #endregion

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