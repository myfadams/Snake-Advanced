using TMPro;
using UnityEngine;

/// <summary>
/// Attach to the pickup prefab. Stores its assigned value, colors itself and writes
/// its value onto a TMP text (expected on a child Quad), hovers slightly, rotates,
/// destroys itself when the player touches it, and self-destructs if it stays
/// continuously outside camera view too long.
/// The Collider on this object must have "Is Trigger" enabled.
///
/// Expected hierarchy:
/// Pickup (this script + Collider(isTrigger) + body Renderer, e.g. a Cube)
/// └── Quad
///     └── ValueText (TextMeshPro, 3D world text - not TextMeshProUGUI/Canvas)
/// </summary>
[RequireComponent(typeof(Collider))]
public class Pickup : MonoBehaviour
{
    [Header("Visuals")]
    [Tooltip("The renderer to color based on the pickup's value (e.g. the cube/body mesh). " +
             "If left empty, the first Renderer found in children is used as a fallback.")]
    [SerializeField] private Renderer bodyRenderer;

    [Tooltip("The TMP text (on the child Quad) that displays the pickup's numeric value.")]
    [SerializeField] private TMP_Text valueText;

    [Header("Hover Settings")]
    [Tooltip("How far up and down the pickup bobs from its spawn height. Keep this small - " +
             "it should look like a slight bob, not a big up-and-down motion.")]
    [Range(0f, 0.3f)]
    [SerializeField] private float hoverHeight = 0.08f;

    [Tooltip("How fast the pickup bobs up and down.")]
    [SerializeField] private float hoverSpeed = 2f;

    [Header("Rotation Settings")]
    [Tooltip("Degrees per second the pickup spins around its Y axis.")]
    [SerializeField] private float rotationSpeed = 90f;

    [Header("Out-of-View Lifetime")]
    [Tooltip("Camera used to check visibility. Leave empty to use Camera.main.")]
    [SerializeField] private Camera gameplayCamera;

    [Tooltip("How many seconds the pickup can stay continuously out of camera view " +
             "before it destroys itself. The timer resets to 0 the instant it becomes visible again.")]
    [SerializeField] private float maxTimeOutOfView = 10f;

    private int value;
    private Vector3 spawnPosition;
    private float timeOutOfView;

    // Not [SerializeField]: a pickup is instantiated at runtime from a prefab asset,
    // and Unity won't let a prefab asset hold a reference to a scene object (that's
    // the "type mismatch" you get trying to drag Player onto this component directly).
    // PickupManager already holds a proper scene reference to the player, so it passes
    // it in here via Initialize() instead - see PickupManager.SpawnPickup().
    private Transform player;

    public int Value => value;

    private void Awake()
    {
        if (bodyRenderer == null)
        {
            bodyRenderer = GetComponentInChildren<Renderer>();
        }

        Collider col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning($"Pickup '{name}': Collider should have 'Is Trigger' enabled.");
        }

        // Default spawn position in case Initialize() isn't called before the first frame.
        spawnPosition = transform.position;
    }

    private void Update()
    {
        HandleHover();
        HandleRotation();
        HandleVisibilityLifetime();
    }

    /// <summary>
    /// Called by PickupManager immediately after instantiating this pickup.
    /// Sets the value it represents, locks in its hover origin, applies its visuals,
    /// and receives the player reference PickupManager already holds (see the note
    /// on the `player` field above for why this can't be wired up in the Inspector).
    /// </summary>
    public void Initialize(int assignedValue, Transform playerTransform)
    {
        value = assignedValue;
        player = playerTransform;
        spawnPosition = transform.position;
        timeOutOfView = 0f;
        ApplyVisuals();

        if (player == null)
        {
            Debug.LogWarning($"Pickup '{name}': no player Transform was passed in from PickupManager; " +
                              "it will never detect collection. Make sure PickupManager's Player field is assigned.");
        }
    }

    private void HandleHover()
    {
        float newY = spawnPosition.y + Mathf.Sin(Time.time * hoverSpeed) * hoverHeight;
        Vector3 position = transform.position;
        position.y = newY;
        transform.position = position;
    }

    private void HandleRotation()
    {
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
    }

    /// <summary>
    /// Tracks how long this pickup has been continuously outside camera view.
    /// Resets the moment it re-enters view; destroys the pickup if the streak
    /// reaches maxTimeOutOfView.
    /// </summary>
    private void HandleVisibilityLifetime()
    {
        Camera cam = gameplayCamera != null ? gameplayCamera : Camera.main;

        if (cam == null)
        {
            // No camera available to check against - never auto-destroy in that case.
            return;
        }

        if (IsVisibleToCamera(cam))
        {
            timeOutOfView = 0f;
            return;
        }

        timeOutOfView += Time.deltaTime;

        if (timeOutOfView >= maxTimeOutOfView)
        {
            Destroy(gameObject);
        }
    }

    private bool IsVisibleToCamera(Camera cam)
    {
        Vector3 viewportPoint = cam.WorldToViewportPoint(transform.position);

        return viewportPoint.z > 0f
            && viewportPoint.x >= 0f && viewportPoint.x <= 1f
            && viewportPoint.y >= 0f && viewportPoint.y <= 1f;
    }

    private void ApplyVisuals()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("Pickup: GameManager.Instance is null; cannot apply color.");
        }
        else
        {
            Color color = GameManager.Instance.GetBlockColor(value);

            if (bodyRenderer != null)
            {
                // .material creates an instance so each pickup can have its own color
                // without affecting other pickups sharing the same base material.
                bodyRenderer.material.color = color;
            }
        }

        if (valueText != null)
        {
            valueText.text = value.ToString();
        }
        else
        {
            Debug.LogWarning($"Pickup '{name}': Value Text (TMP_Text) reference is not assigned.");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsPlayer(other))
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// True if the collider that touched this pickup belongs to the player Transform
    /// passed in via Initialize(), or any of its children (e.g. a snake segment), so a
    /// multi-part player is recognized everywhere without a per-segment script.
    /// </summary>
    private bool IsPlayer(Collider other)
    {
        if (player == null)
        {
            return false;
        }

        return other.transform.IsChildOf(player);
    }
}