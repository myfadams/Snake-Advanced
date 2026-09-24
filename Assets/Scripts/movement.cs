using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The Head leads the snake: it moves forward continuously and steers
/// toward the mouse. Every body segment continuously flows along the
/// exact path the Head has already travelled (a recorded position
/// history), instead of chasing the transform in front of it. That is
/// what removes stationary segments, corner-cutting on turns, and any
/// snapping/teleporting.
///
/// Spacing between neighbours (head-to-first-body, and body-to-body) is
/// measured automatically from each segment's actual rendered/collider
/// size, so segments of different scales (e.g. a bigger head, smaller
/// body cubes) sit just touching instead of leaving a manual, guessed gap.
/// </summary>
public class PlayerMovement : MonoBehaviour
{
    [Header("Snake")]
    [SerializeField] private Transform head;

    /// <summary>The current head Transform, exposed so companion systems (e.g. a growth/merge manager) can reference it without a duplicate Inspector assignment.</summary>
    public Transform Head => head;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;

    /// <summary>Current movement speed of the snake head.</summary>
    public float MoveSpeed
    {
        get => moveSpeed;
        set => moveSpeed = Mathf.Max(0.1f, value);
    }
    [Tooltip("How cautiously the snake turns, as a multiple of its own body radius. 1 = turns exactly as tight as physically possible without segments overlapping (maximum responsiveness, zero safety margin). Higher = a little more margin (smoother, slightly slower turns); lower than 1 = snappier but risks a touch of visual overlap on the sharpest turns. The actual turn speed (deg/sec) is derived automatically from this, moveSpeed, and the segments' measured size, so turning stays as fast as possible while remaining safe - even if you resize things later.")]
    [SerializeField] private float turnTightness = 1.5f;

    [Header("Body Spacing")]
    [Tooltip("Extra distance added between every pair of neighbouring segments, on top of their auto-measured sizes. 0 = segments just touch, negative = slight overlap (often looks more seamless on curves), positive = a visible gap.")]
    [SerializeField] private float extraSpacing = 0f;
    [Tooltip("Radius used for a segment only if it has no Renderer or Collider to measure.")]
    [SerializeField] private float fallbackSegmentRadius = 0.15f;

    [Header("Path History")]
    [Tooltip("A new history point is recorded once the head has moved at least this far since the last one. Smaller = smoother curves, but more points stored.")]
    [SerializeField] private float historyPointSpacing = 0.05f;
    [Tooltip("Extra path length kept behind the last body segment, as a safety margin so interpolation never runs out of history.")]
    [SerializeField] private float historyBuffer = 1f;

    private readonly List<Transform> bodySegments = new List<Transform>();

    // Cumulative arc-length distance from the head to each body segment,
    // in hierarchy order. cumulativeDistances[i] is how far bodySegments[i]
    // should sit behind the head, measured along the path.
    private readonly List<float> cumulativeDistances = new List<float>();

    // Recorded head positions over time.
    // Index 0 = oldest / farthest behind. Last index = newest / closest to the head.
    private readonly List<Vector3> pathHistory = new List<Vector3>();

    private float requiredHistoryLength;

    // The largest measured radius among the head and all body segments,
    // used to derive a safe turn speed automatically.
    private float maxSegmentRadius = 0.1f;

    // Smooth insertion progress (0..1) for a newly added segment at index 0.
    // 1f = normal full spacing; 0f = newly added segment at head, existing segments at pre-growth distances.
    private float insertionProgress = 1f;

    // Gap closing state after a segment is removed
    private int gapClosingIndex = -1;
    private float gapClosingProgress = 1f;
    private float gapClosingAmount = 0f;

    /// <summary>
    /// Sets the smooth insertion transition progress (0..1) for a newly inserted segment at index 0.
    /// At 0, the new segment starts at the head, and all existing segments remain at their exact pre-growth positions.
    /// At 1 (default), all segments sit at their full standard cumulative spacing.
    /// </summary>
    public void SetInsertionProgress(float progress)
    {
        insertionProgress = Mathf.Clamp01(progress);
        MoveBodyAlongPath();
    }

    /// <summary>
    /// Smoothly transitions remaining segments forward to close the gap after a segment at removedIndex is destroyed.
    /// progress: 0 (segments remain at their pre-removal distances) -> 1 (gap fully closed).
    /// </summary>
    public void SetGapClosingProgress(int removedIndex, float progress, float gapAmount)
    {
        gapClosingIndex = removedIndex;
        gapClosingProgress = Mathf.Clamp01(progress);
        gapClosingAmount = gapAmount;
        MoveBodyAlongPath();
    }

    // Custom segment distances override used for animated power-up rearrangements
    private float[] customSegmentDistances;

    /// <summary>Direct read-only access to current body segment transforms in order.</summary>
    public IReadOnlyList<Transform> BodySegments => bodySegments;

    /// <summary>Direct read-only access to current cumulative arc-length distances from the head.</summary>
    public IReadOnlyList<float> CumulativeDistances => cumulativeDistances;

    /// <summary>
    /// Temporarily overrides segment target distances along the path history for animated rearrangements.
    /// Pass null to restore normal automatic spacing.
    /// </summary>
    public void SetCustomSegmentDistances(float[] distances)
    {
        customSegmentDistances = distances;
        MoveBodyAlongPath();
    }

    /// <summary>
    /// Gets the standard spacing gap between neighbouring segments.
    /// </summary>
    public float GetDefaultGap()
    {
        return cumulativeDistances.Count > 0 ? cumulativeDistances[0] : (fallbackSegmentRadius * 2f + extraSpacing);
    }

    private void OnValidate()
    {
        historyPointSpacing = Mathf.Max(historyPointSpacing, 0.001f);
        historyBuffer = Mathf.Max(historyBuffer, 0f);
        fallbackSegmentRadius = Mathf.Max(fallbackSegmentRadius, 0.01f);
        turnTightness = Mathf.Max(turnTightness, 0.1f);
    }

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.FreezeRotationX |
                             RigidbodyConstraints.FreezeRotationZ |
                             RigidbodyConstraints.FreezePositionY;
        }
    }

    private void Start()
    {
        RefreshBodySegments();
    }

    private void Update()
    {
        if (head == null)
            return;

        TurnHeadWithMouse();
        MoveHead();
        RecordHistoryPoint();
        MoveBodyAlongPath();
    }

    /// <summary>
    /// Re-scans the hierarchy for body segments and re-measures spacing
    /// from their current sizes. Call this again at runtime if segments
    /// were added/removed directly in the hierarchy, or resized.
    /// </summary>
    public void RefreshBodySegments()
    {
        FindBodySegments();
        RecomputeAndApply();
    }

    /// <summary>
    /// Directly supplies the current ordered list of body segments
    /// (everything after the head), bypassing the automatic hierarchy
    /// scan. Use this when another system (e.g. a growth/merge manager)
    /// is the authoritative source of the snake's segment order and has
    /// just changed it - it keeps the same real path history, so growth
    /// and merges never reset or snap the snake's movement.
    /// </summary>
    public void SyncBodySegments(IReadOnlyList<Transform> orderedSegments)
    {
        bodySegments.Clear();
        bodySegments.AddRange(orderedSegments);
        RecomputeAndApply();
    }

    /// <summary>
    /// Re-measures spacing for the current bodySegments list and applies
    /// it. Only seeds a fresh straight-line path history the very first
    /// time this runs (when there is no history yet) - later calls, such
    /// as after the snake grows or a merge removes a segment, leave the
    /// real recorded path untouched so nothing ever snaps.
    /// </summary>
    private void RecomputeAndApply()
    {
        ComputeSegmentSpacing();

        float lastDistance = cumulativeDistances.Count > 0
            ? cumulativeDistances[cumulativeDistances.Count - 1]
            : 0f;

        requiredHistoryLength = lastDistance + historyBuffer;

        if (pathHistory.Count == 0)
        {
            SeedInitialHistory();
        }

        MoveBodyAlongPath();
    }

    private void FindBodySegments()
    {
        bodySegments.Clear();

        foreach (Transform child in transform)
        {
            if (child != head)
            {
                bodySegments.Add(child);
            }
        }
    }

    /// <summary>
    /// Builds cumulativeDistances so each segment sits exactly
    /// touching (or overlapping/gapped by extraSpacing) the one in front
    /// of it, based on their real measured sizes.
    /// </summary>
    private void ComputeSegmentSpacing()
    {
        cumulativeDistances.Clear();

        if (head == null)
            return;

        float previousRadius = GetSegmentRadius(head);
        float runningDistance = 0f;
        maxSegmentRadius = previousRadius;

        for (int i = 0; i < bodySegments.Count; i++)
        {
            float radius = GetSegmentRadius(bodySegments[i]);
            float gap = Mathf.Max(previousRadius + radius + extraSpacing, 0.01f);

            runningDistance += gap;
            cumulativeDistances.Add(runningDistance);

            previousRadius = radius;
            maxSegmentRadius = Mathf.Max(maxSegmentRadius, radius);
        }
    }

    /// <summary>
    /// The fastest the head can turn (in degrees/sec) without the path
    /// curving tighter than the snake's own body can follow without
    /// overlapping. Derived from moveSpeed, turnTightness, and the
    /// largest measured segment radius - so it adapts automatically if
    /// moveSpeed or segment sizes change.
    /// </summary>
    private float GetSafeTurnSpeedDegreesPerSecond()
    {
        float minTurnRadius = Mathf.Max(maxSegmentRadius * turnTightness, 0.01f);
        float turnSpeedRadiansPerSecond = moveSpeed / minTurnRadius;
        return turnSpeedRadiansPerSecond * Mathf.Rad2Deg;
    }

    /// <summary>
    /// Approximates a segment's "radius" in the horizontal plane from its
    /// real rendered/collider size (world space, so scale is included).
    /// Falls back to fallbackSegmentRadius if neither is present.
    /// </summary>
    private float GetSegmentRadius(Transform segment)
    {
        Renderer[] renderers = segment.GetComponentsInChildren<Renderer>();

        if (renderers.Length > 0)
        {
            Bounds combined = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
            {
                combined.Encapsulate(renderers[i].bounds);
            }

            return Mathf.Max(combined.size.x, combined.size.z) * 0.5f;
        }

        Collider[] colliders = segment.GetComponentsInChildren<Collider>();

        if (colliders.Length > 0)
        {
            Bounds combined = colliders[0].bounds;

            for (int i = 1; i < colliders.Length; i++)
            {
                combined.Encapsulate(colliders[i].bounds);
            }

            return Mathf.Max(combined.size.x, combined.size.z) * 0.5f;
        }

        return fallbackSegmentRadius;
    }

    private void TurnHeadWithMouse()
    {
        if (Mouse.current == null)
            return;

        Camera mainCamera = Camera.main;

        if (mainCamera == null)
            return;

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = mainCamera.ScreenPointToRay(mousePosition);

        // Horizontal plane at Y = 0
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

        if (groundPlane.Raycast(ray, out float distance))
        {
            Vector3 mouseWorldPosition = ray.GetPoint(distance);
            Vector3 direction = mouseWorldPosition - head.position;

            // Only rotate around Y so the snake stays flat.
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                head.rotation = Quaternion.RotateTowards(
                    head.rotation,
                    targetRotation,
                    GetSafeTurnSpeedDegreesPerSecond() * Time.deltaTime
                );
            }
        }
    }

    private void MoveHead()
    {
        // The head leads: the whole snake advances along whatever
        // direction the head currently faces.
        transform.position += head.forward * moveSpeed * Time.deltaTime;
    }

    private void RecordHistoryPoint()
    {
        if (pathHistory.Count == 0)
        {
            pathHistory.Add(head.position);
            return;
        }

        float distanceFromLastPoint = Vector3.Distance(
            head.position,
            pathHistory[pathHistory.Count - 1]
        );

        if (distanceFromLastPoint >= historyPointSpacing)
        {
            pathHistory.Add(head.position);
            TrimHistory();
        }
    }

    private void TrimHistory()
    {
        float totalLength = 0f;
        Vector3 previous = head.position;

        for (int i = pathHistory.Count - 1; i >= 0; i--)
        {
            totalLength += Vector3.Distance(previous, pathHistory[i]);
            previous = pathHistory[i];

            if (totalLength > requiredHistoryLength)
            {
                // Keep one extra point before this one so interpolation
                // at the boundary always has two points to work with.
                int keepFrom = Mathf.Max(i - 1, 0);

                if (keepFrom > 0)
                {
                    pathHistory.RemoveRange(0, keepFrom);
                }

                return;
            }
        }
    }

    private void SeedInitialHistory()
    {
        pathHistory.Clear();

        if (head == null)
            return;

        Vector3 backwards = -head.forward;
        int pointsNeeded = Mathf.CeilToInt(requiredHistoryLength / historyPointSpacing) + 1;

        // Oldest first, newest (closest to the head) last - this lays
        // the body out in a straight line behind the head at start.
        for (int i = pointsNeeded; i >= 1; i--)
        {
            pathHistory.Add(head.position + backwards * (historyPointSpacing * i));
        }
    }

    private void MoveBodyAlongPath()
    {
        if (bodySegments.Count == 0 || head == null)
            return;

        Vector3 aheadPosition = head.position;
        float firstSegmentGap = cumulativeDistances.Count > 0 ? cumulativeDistances[0] : 0f;
        float gapAdjustment = firstSegmentGap * (1f - insertionProgress);

        for (int i = 0; i < bodySegments.Count; i++)
        {
            Transform segment = bodySegments[i];
            if (segment == null)
                continue;

            float standardDist = i < cumulativeDistances.Count ? cumulativeDistances[i] : 0f;
            float targetDistance = standardDist;

            if (customSegmentDistances != null && i < customSegmentDistances.Length)
            {
                targetDistance = customSegmentDistances[i];
            }
            else if (insertionProgress < 1f)
            {
                targetDistance = (i == 0)
                    ? firstSegmentGap * insertionProgress
                    : Mathf.Max(standardDist - gapAdjustment, 0.001f);
            }
            else if (gapClosingIndex >= 0 && i >= gapClosingIndex && gapClosingProgress < 1f)
            {
                float remainingGap = gapClosingAmount * (1f - gapClosingProgress);
                targetDistance = standardDist + remainingGap;
            }

            Vector3 targetPosition = GetPointAtDistance(targetDistance);
            targetPosition.y = segment.position.y;

            // Face the point ahead of this segment on the path (the head,
            // or the previous segment) - never the mouse directly.
            Vector3 facing = aheadPosition - targetPosition;
            facing.y = 0f;

            if (facing.sqrMagnitude > 0.0001f)
            {
                segment.rotation = Quaternion.LookRotation(facing.normalized);
            }

            segment.position = targetPosition;
            aheadPosition = targetPosition;
        }
    }

    public Vector3 GetPointAtDistance(float distance)
    {
        Vector3 previousPoint = head.position;
        float distanceCovered = 0f;

        for (int i = pathHistory.Count - 1; i >= 0; i--)
        {
            Vector3 currentPoint = pathHistory[i];
            float segmentLength = Vector3.Distance(previousPoint, currentPoint);

            if (distanceCovered + segmentLength >= distance)
            {
                float remaining = distance - distanceCovered;
                float t = segmentLength > 0.0001f ? remaining / segmentLength : 0f;
                return Vector3.Lerp(previousPoint, currentPoint, t);
            }

            distanceCovered += segmentLength;
            previousPoint = currentPoint;
        }

        // Not enough recorded history yet - fall back to the farthest
        // point currently available.
        return previousPoint;
    }
}