using UnityEngine;

/// <summary>
/// Smoothly follows the player snake, keeping the camera rock-solid and stable.
/// Prevents camera glitches when the player collides with obstacles or stops abruptly:
/// - Locks camera Y-height so collision bounces or vertical physics impulses never jerk the camera view.
/// - Uses SmoothDamp to eliminate high-frequency collision jitter when stopped against walls.
/// - Validates target coordinates to prevent NaN/Infinity camera errors.
/// - Auto-recovers if distance between camera and target exceeds safe threshold.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("Target Tracking")]
    [Tooltip("The Transform to follow. If unassigned, automatically finds the snake player in the scene.")]
    [SerializeField] private Transform player;

    [Tooltip("If true, follows the snake's actual Head transform if available, keeping the head centered in view.")]
    [SerializeField] private bool followHead = true;

    [Header("Positioning & Offset")]
    [Tooltip("Default camera offset relative to the target.")]
    [SerializeField] private Vector3 offset = new Vector3(-0.49f, 3.83f, -3.74f);

    [Tooltip("If true, captures the starting camera-to-target offset at Start.")]
    [SerializeField] private bool useInitialOffset = true;

    [Header("Height Stabilization")]
    [Tooltip("Locks the camera's Y position to a fixed altitude, preventing collision bumps or physics impulses from throwing the camera off.")]
    [SerializeField] private bool lockHeight = true;

    [Tooltip("The fixed Y altitude of the camera when lockHeight is enabled.")]
    [SerializeField] private float fixedHeight = 4.77f;

    [Header("Smooth Follow Settings")]
    [Tooltip("Follow smoothing speed (legacy speed value, mapped to smoothTime).")]
    [SerializeField] private float smoothSpeed = 5f;

    [Tooltip("Smooth time for the camera follow. Lower = snappier, Higher = smoother.")]
    [Range(0.01f, 0.4f)]
    [SerializeField] private float smoothTime = 0.12f;

    [Tooltip("Maximum camera speed in units per second.")]
    [SerializeField] private float maxSpeed = 40f;

    [Header("Glitch Recovery")]
    [Tooltip("Maximum allowed distance between camera and target position before snapping to prevent player leaving view.")]
    [SerializeField] private float maxAllowedDistance = 15f;

    private Transform activeTarget;
    private Vector3 currentVelocity = Vector3.zero;
    private bool isInitialized = false;

    public Transform Player
    {
        get => player;
        set
        {
            player = value;
            ResolveTarget();
        }
    }

    private void Awake()
    {
        ResolveTarget();

        if (fixedHeight <= 0f)
        {
            fixedHeight = transform.position.y;
        }
    }

    private void Start()
    {
        ResolveTarget();

        if (activeTarget != null)
        {
            if (useInitialOffset)
            {
                offset = transform.position - activeTarget.position;
            }

            fixedHeight = transform.position.y;

            // Immediately snap to initial target position to avoid first-frame lerp hitch
            Vector3 startTarget = CalculateTargetPosition();
            if (IsValidPosition(startTarget))
            {
                transform.position = startTarget;
            }

            isInitialized = true;
        }
    }

    private void ResolveTarget()
    {
        if (player == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
            }
            else
            {
                PlayerMovement pm = FindObjectOfType<PlayerMovement>();
                if (pm != null)
                {
                    player = pm.transform;
                }
            }
        }

        if (player != null)
        {
            if (followHead)
            {
                PlayerMovement movement = player.GetComponent<PlayerMovement>();
                if (movement != null && movement.Head != null)
                {
                    activeTarget = movement.Head;
                    return;
                }
            }

            activeTarget = player;
        }
    }

    private void LateUpdate()
    {
        if (activeTarget == null)
        {
            ResolveTarget();
            if (activeTarget == null)
                return;
        }

        Vector3 targetPosition = CalculateTargetPosition();

        if (!IsValidPosition(targetPosition))
            return;

        // Auto-recovery if camera somehow separated excessively from target
        float distToTarget = Vector3.Distance(transform.position, targetPosition);
        if (distToTarget > maxAllowedDistance || !isInitialized)
        {
            transform.position = targetPosition;
            currentVelocity = Vector3.zero;
            isInitialized = true;
            return;
        }

        // Calculate smooth time from smoothSpeed or smoothTime
        float effectiveSmoothTime = smoothTime;
        if (smoothSpeed > 0.01f && smoothTime <= 0.02f)
        {
            effectiveSmoothTime = 1f / smoothSpeed;
        }

        // Smoothly follow the target position using SmoothDamp to absorb collision jitter
        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPosition,
            ref currentVelocity,
            effectiveSmoothTime,
            maxSpeed,
            Time.deltaTime
        );
    }

    private Vector3 CalculateTargetPosition()
    {
        if (activeTarget == null)
            return transform.position;

        Vector3 target = activeTarget.position + offset;

        // Lock Y-axis height so vertical collision impulses never throw off the camera view
        if (lockHeight)
        {
            target.y = fixedHeight;
        }

        return target;
    }

    private bool IsValidPosition(Vector3 pos)
    {
        return !float.IsNaN(pos.x) && !float.IsNaN(pos.y) && !float.IsNaN(pos.z) &&
               !float.IsInfinity(pos.x) && !float.IsInfinity(pos.y) && !float.IsInfinity(pos.z);
    }
}
