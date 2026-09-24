using TMPro;
using UnityEngine;

/// <summary>
/// Attach to the pickup prefab. Stores its assigned value, colors itself and writes
/// its value onto a TMP text (expected on a child Quad), hovers slightly, rotates,
/// grows the snake and destroys itself when the player touches it, and self-destructs
/// if it stays continuously outside camera view too long.
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

    [Header("Collection Effect")]
    [Tooltip("Particle effect prefab to spawn at this pickup's position the moment it's " +
             "collected (e.g. Unity's 'Respawn' particle prefab) - plays in place of the " +
             "pickup just silently vanishing. Leave empty to skip.")]
    [SerializeField] private GameObject collectionEffectPrefab;

    [Tooltip("Fallback lifetime for the spawned effect if it has no ParticleSystem to measure a duration from.")]
    [SerializeField] private float collectionEffectFallbackDuration = 2f;

    [Header("Audio")]
    [Tooltip("Sound played when this pickup is eaten. If left unassigned, SnakeGrow's Eat Sound Clip is played.")]
    [SerializeField] private AudioClip eatSoundClip;

    [Tooltip("Playback volume for the eat sound.")]
    [Range(0f, 1f)]
    [SerializeField] private float eatSoundVolume = 1f;

    public AudioClip EatSoundClip { get => eatSoundClip; set => eatSoundClip = value; }
    public float EatSoundVolume { get => eatSoundVolume; set => eatSoundVolume = value; }

    [Header("Out-of-View Lifetime")]
    [Tooltip("Camera used to check visibility. Leave empty to use Camera.main.")]
    [SerializeField] private Camera gameplayCamera;

    [Tooltip("How many seconds the pickup can stay continuously out of camera view " +
             "before it destroys itself. The timer resets to 0 the instant it becomes visible again.")]
    [SerializeField] private float maxTimeOutOfView = 4f;

    private int value;
    private Vector3 spawnPosition;
    private float timeOutOfView;
    private bool collected;

    // Not [SerializeField]: a pickup is instantiated at runtime from a prefab asset,
    // and Unity won't let a prefab asset hold a reference to a scene object (that's
    // the "type mismatch" you get trying to drag Player onto this component directly).
    // PickupManager already holds a proper scene reference to the player, so it passes
    // it in here via Initialize() instead - see PickupManager.SpawnPickup().
    private Transform player;

    // Fetched from the same player Transform - the component that actually grows the
    // snake and resolves merges when this pickup is collected.
    private SnakeGrow snakeGrow;

    public int Value => value;
    public GameObject CollectionEffectPrefab { get => collectionEffectPrefab; set => collectionEffectPrefab = value; }

    private GameObject dissolveObject;

    private void Awake()
    {
        if (bodyRenderer == null)
        {
            bodyRenderer = GetComponentInChildren<Renderer>();
        }

        // Auto-detect Dissolve effect object (keep INACTIVE while spawned in world so it only plays when eaten)
        Transform dissolveTr = transform.Find("DissolveSolidHorizontal") ?? transform.Find("Dissolve");
        if (dissolveTr != null)
        {
            dissolveObject = dissolveTr.gameObject;
        }
        else
        {
            Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in allRenderers)
            {
                if (r != null && r.name.ToLowerInvariant().Contains("dissolve"))
                {
                    dissolveObject = r.gameObject;
                    break;
                }
            }
        }

        if (dissolveObject != null)
        {
            dissolveObject.SetActive(false);
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
            return;
        }

        snakeGrow = player.GetComponent<SnakeGrow>();

        if (snakeGrow == null)
        {
            Debug.LogWarning($"Pickup '{name}': no SnakeGrow component found on the assigned player Transform; " +
                              "this pickup will be collectible but won't grow the snake.");
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
        if (!collected && IsPlayer(other))
        {
            collected = true;
            Collect();
        }
    }

    private void Collect()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.AddPickupScore();
        }

        if (snakeGrow != null)
        {
            snakeGrow.Grow(value);
        }

        SpawnCollectionEffect();

        // Hide visuals immediately so dissolve effect takes over seamlessly
        if (bodyRenderer != null) bodyRenderer.enabled = false;
        if (valueText != null) valueText.enabled = false;
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // -------------------------------------------------------------------------
        // PREVIOUS SOUND CODE (FLAWED):
        // AudioSource eatSound = gameObject.GetComponent<AudioSource>();
        // eatSound?.Play();
        //
        // WHY IT FAILED:
        // Destroy(gameObject) is called on the very next line, which instantly destroys
        // this GameObject and its AudioSource component in the same frame, abruptly cutting
        // off the sound before it can finish (or even begin) playing.
        //
        // BETTER IMPLEMENTATION:
        // PlayEatSound() delegates playback to the player's persistent SnakeGrow AudioSource
        // (with PlayOneShot and dynamic pitch randomization), or falls back to
        // AudioSource.PlayClipAtPoint, which spawns an independent temporary audio object
        // that safely plays the entire sound and automatically cleans itself up.
        // -------------------------------------------------------------------------
        PlayEatSound();

        Destroy(gameObject);
    }

    /// <summary>
    /// Instantiates the collection effect at this pickup's position/rotation as its own
    /// independent object (not a child of this pickup, so destroying the pickup doesn't
    /// cut the effect off), then schedules its own cleanup based on how long it actually
    /// plays for - no need to hand-tune a delay per effect.
    /// </summary>
    private void SpawnCollectionEffect()
    {
        Renderer targetRenderer = bodyRenderer != null ? bodyRenderer : GetComponent<Renderer>();
        Vector3 spawnPos = targetRenderer != null ? targetRenderer.bounds.center : transform.position;
        Quaternion spawnRot = targetRenderer != null ? targetRenderer.transform.rotation : transform.rotation;

        GameObject effectToSpawn = dissolveObject != null ? dissolveObject : collectionEffectPrefab;
        if (effectToSpawn == null) return;

        GameObject effect = Instantiate(effectToSpawn, spawnPos, spawnRot);
        effect.SetActive(true);

        PowerUp.ScaleEffectToMatchRenderer(effect, targetRenderer, transform.lossyScale);
        ApplyBlockColorToEffect(effect);

        float effectDuration = 3.0f;
        SpawnEffect spawnEff = effect.GetComponent<SpawnEffect>();
        if (spawnEff != null)
        {
            spawnEff.enabled = true;
            if (spawnEff.spawnEffectTime > 3.0f)
            {
                spawnEff.spawnEffectTime = 2.5f;
            }
            effectDuration = spawnEff.spawnEffectTime + 0.5f;
        }
        else
        {
            effectDuration = GetEffectLifetime(effect);
        }

        Destroy(effect, effectDuration);
    }

    /// <summary>
    /// Overrides every particle system's Start Color (root and any sub-emitters) to this
    /// pickup's block color, so the effect matches whichever value/color was collected.
    /// Any Color Over Lifetime module still multiplies against this, so fade-outs etc.
    /// on the original effect are preserved - only the base hue changes.
    /// </summary>
    private void ApplyBlockColorToEffect(GameObject effect)
    {
        if (GameManager.Instance == null || effect == null)
        {
            return;
        }

        Color color = GameManager.Instance.GetBlockColor(value);
        bool hasSpawnEffect = effect.GetComponent<SpawnEffect>() != null || effect.GetComponentInChildren<SpawnEffect>() != null;

        ParticleSystem[] systems = effect.GetComponentsInChildren<ParticleSystem>(true);

        foreach (ParticleSystem ps in systems)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color);
            if (!hasSpawnEffect)
            {
                ps.Play();
            }
        }

        Renderer[] renderers = effect.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            if (r is ParticleSystemRenderer) continue;
            if (r.material != null)
            {
                if (r.material.HasProperty("_Color")) r.material.SetColor("_Color", color);
                if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", color);
                if (r.material.HasProperty("_EmissionColor")) r.material.SetColor("_EmissionColor", color * 1.5f);
                if (r.material.HasProperty("_Color_Glow")) r.material.SetColor("_Color_Glow", color * 2.2f);
                if (r.material.HasProperty("_ColorEdge")) r.material.SetColor("_ColorEdge", color * 2.2f);
                if (r.material.HasProperty("_Coloredges")) r.material.SetColor("_Coloredges", color * 2.2f);
                if (r.material.HasProperty("_Edge_Color")) r.material.SetColor("_Edge_Color", color * 2.2f);
                if (r.material.HasProperty("_Main_Color")) r.material.SetColor("_Main_Color", color);
            }
        }

        MonoBehaviour spawnEff = effect.GetComponent("SpawnEffect") as MonoBehaviour;
        if (spawnEff != null)
        {
            spawnEff.enabled = true;
        }
    }

    private float GetEffectLifetime(GameObject effect)
    {
        ParticleSystem[] systems = effect.GetComponentsInChildren<ParticleSystem>();
        if (systems.Length == 0)
        {
            return collectionEffectFallbackDuration;
        }

        // Longest of (duration + max start lifetime) across all sub-emitters, so a multi-part
        // effect (e.g. a burst plus lingering sparks) is given time to fully finish before cleanup.
        float longest = 0f;
        foreach (ParticleSystem ps in systems)
        {
            float lifetime = ps.main.duration + ps.main.startLifetime.constantMax;
            if (lifetime > longest)
            {
                longest = lifetime;
            }
        }

        return longest > 0f ? longest : collectionEffectFallbackDuration;
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

    /// <summary>
    /// Plays the pickup eating sound effect.
    /// Prefers delegating to SnakeGrow so the sound plays through the player's AudioSource,
    /// or falls back to AudioSource.PlayClipAtPoint so the sound continues playing even after
    /// this pickup GameObject is destroyed.
    /// </summary>
    private void PlayEatSound()
    {
        if (snakeGrow != null)
        {
            snakeGrow.PlayEatSound(eatSoundClip, eatSoundVolume);
            return;
        }

        if (SnakeGrow.Instance != null)
        {
            SnakeGrow.Instance.PlayEatSound(eatSoundClip, eatSoundVolume);
            return;
        }

        if (eatSoundClip != null)
        {
            AudioSource.PlayClipAtPoint(eatSoundClip, transform.position, eatSoundVolume);
        }
    }
}