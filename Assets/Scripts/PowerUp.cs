using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Type of power-up effect represented by this collectible.
/// </summary>
public enum PowerUpType
{
    Magnet,
    Rearrange,
    Speed
}

/// <summary>
/// Reusable collectible component attached to power-up prefabs (e.g. Box 1, Box 2, Box 3).
/// Handles smooth levitation bobbing, rotation, Spot Light glow, horizontal dissolve effect
/// color synchronization, player detection, and collection notification.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PowerUp : MonoBehaviour
{
    [Header("Power-Up Identity")]
    [Tooltip("The type of power-up this collectible grants.")]
    [SerializeField] private PowerUpType powerUpType = PowerUpType.Magnet;

    [Tooltip("Target block value for Magnet or Rearrange (e.g. 2, 4, 8, 16).")]
    [SerializeField] private int targetValue = 2;

    [Header("Visual Glow & Dissolve")]
    [Tooltip("The Spot Light or Point Light inside the prefab responsible for the visible colored glow.")]
    [SerializeField] private Light glowLight;

    [Tooltip("The Renderer holding the horizontal dissolve effect (e.g. on child 'DissolveSolidHorizontal').")]
    [SerializeField] private Renderer dissolveRenderer;

    [Tooltip("Optional base model/box renderer (if you want its color or emission tinted).")]
    [SerializeField] private Renderer modelRenderer;

    [Tooltip("Fixed override glow color, primarily used for Speed power-up.")]
    [SerializeField] private Color overrideGlowColor = new Color(0f, 0.85f, 1f, 1f);

    [Tooltip("If true, always uses overrideGlowColor instead of looking up GameManager block color.")]
    [SerializeField] private bool useOverrideColor = false;

    [Header("Visual Scale")]
    [Tooltip("Visual scale multiplier for the power-up collectible in the world, making it easier to see.")]
    [SerializeField] private float worldScaleMultiplier = 1.35f;

    [Header("Hover & Rotation")]
    [Tooltip("How high the power-up bobs up and down from its spawn position.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float hoverHeight = 0.12f;

    [Tooltip("How fast the power-up bobs up and down.")]
    [SerializeField] private float hoverSpeed = 2f;

    [Tooltip("Degrees per second the power-up rotates around its vertical (Y) axis.")]
    [SerializeField] private float rotationSpeed = 60f;

    [Header("Magnet Configuration")]
    [Tooltip("Radius around the snake within which target-value pickups are attracted.")]
    [SerializeField] private float magnetRadius = 10f;

    [Tooltip("Speed at which attracted pickups glide toward the snake.")]
    [SerializeField] private float magnetSpeed = 8f;

    [Tooltip("Duration in seconds the magnet effect remains active.")]
    [SerializeField] private float magnetDuration = 8f;

    [Tooltip("Maximum pickups attracted simultaneously (0 = unlimited).")]
    [SerializeField] private int magnetMaxAttracted = 0;

    [Header("Rearrange Configuration")]
    [Tooltip("Duration in seconds of the smooth visual rearrangement animation.")]
    [Range(0.2f, 2f)]
    [SerializeField] private float rearrangeDuration = 0.65f;

    [Header("Speed Configuration")]
    [Tooltip("Speed multiplier applied to PlayerMovement when holding Left Shift.")]
    [Range(1.2f, 3.5f)]
    [SerializeField] private float speedMultiplier = 2.0f;

    [Tooltip("Available boost time in seconds.")]
    [SerializeField] private float speedDuration = 5f;

    [Tooltip("Keyboard key that activates the boost when held. Default is Left Shift.")]
    [SerializeField] private KeyCode activationKey = KeyCode.LeftShift;

    [Header("Collection Audio & VFX")]
    [Tooltip("Particle effect prefab instantiated when collected (e.g. Respawn particle prefab).")]
    [SerializeField] private GameObject collectionEffectPrefab;

    [Tooltip("Sound clip played upon collecting this power-up.")]
    [SerializeField] private AudioClip collectSoundClip;

    [Tooltip("Playback volume for the collection sound.")]
    [Range(0f, 1f)]
    [SerializeField] private float collectSoundVolume = 1f;

    [Header("Out-of-View Lifetime")]
    [Tooltip("Camera used to verify visibility. Leave unassigned to use Camera.main.")]
    [SerializeField] private Camera gameplayCamera;

    [Tooltip("How many seconds this power-up can remain continuously out of view before auto-cleaning up. 0 = never.")]
    [SerializeField] private float maxTimeOutOfView = 15f;

    // Runtime state
    private Vector3 spawnPosition;
    private Color currentGlowColor = Color.yellow;
    private bool collected = false;
    private float timeOutOfView = 0f;
    private PowerUpManager manager;
    private GameObject dissolveObject;

    public PowerUpType Type => powerUpType;
    public int TargetValue => targetValue;
    public Color GlowColor => currentGlowColor;
    public float MagnetRadius => magnetRadius;
    public float MagnetSpeed => magnetSpeed;
    public float MagnetDuration => magnetDuration;
    public int MagnetMaxAttracted => magnetMaxAttracted;
    public float RearrangeDuration => rearrangeDuration;
    public float SpeedMultiplier => speedMultiplier;
    public float SpeedDuration => speedDuration;
    public KeyCode ActivationKey => activationKey;
    public bool IsCollected => collected;
    public GameObject CollectionEffectPrefab { get => collectionEffectPrefab; set => collectionEffectPrefab = value; }

    private void Awake()
    {
        // Auto-detect Light if unassigned
        if (glowLight == null)
        {
            glowLight = GetComponentInChildren<Light>(true);
        }

        // Auto-detect Dissolve effect object (keep INACTIVE while spawned in world so it does not dissolve immediately)
        if (dissolveObject == null)
        {
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
        }

        if (dissolveObject != null)
        {
            dissolveRenderer = dissolveObject.GetComponent<Renderer>();
            // Keep dissolve effect inactive while spawned on the floor
            dissolveObject.SetActive(false);
        }

        // Ensure all colliders on this power-up and children are triggers
        Collider[] allColliders = GetComponentsInChildren<Collider>(true);
        foreach (Collider c in allColliders)
        {
            if (c != null)
            {
                c.isTrigger = true;
            }
        }

        spawnPosition = transform.position;
    }

    private void Start()
    {
        // Slightly scale up collectible model in world so power-ups are prominent and clear
        if (worldScaleMultiplier > 0.01f && Mathf.Abs(worldScaleMultiplier - 1f) > 0.01f)
        {
            transform.localScale *= worldScaleMultiplier;
        }

        // Refresh glow colors in case Initialize wasn't called externally (e.g. placed directly in scene)
        if (!collected)
        {
            ResolveAndApplyColor();
        }
    }

    private void Update()
    {
        if (collected) return;

        HandleHover();
        HandleRotation();
        HandleVisibilityLifetime();
    }

    /// <summary>
    /// Configures the power-up dynamically when instantiated by PowerUpManager.
    /// Sets its type, target value, and glow color, and links it with the manager.
    /// </summary>
    public void Initialize(PowerUpType type, int assignedTargetValue, Color glowColor, PowerUpManager powerUpManager = null)
    {
        powerUpType = type;
        targetValue = assignedTargetValue;
        manager = powerUpManager;
        spawnPosition = transform.position;
        timeOutOfView = 0f;

        ApplyGlowColor(glowColor);
    }

    /// <summary>
    /// Resolves the power-up's glow color from GameManager or the override color,
    /// then applies it to both the Spot Light and the horizontal dissolve effect.
    /// </summary>
    public void ResolveAndApplyColor()
    {
        Color colorToApply;

        if (useOverrideColor || powerUpType == PowerUpType.Speed)
        {
            colorToApply = overrideGlowColor;
        }
        else if (GameManager.Instance != null)
        {
            colorToApply = GameManager.Instance.GetBlockColor(targetValue);
        }
        else
        {
            colorToApply = overrideGlowColor;
        }

        ApplyGlowColor(colorToApply);
    }

    /// <summary>
    /// Synchronizes the Spot Light color and the horizontal dissolve effect material colors.
    /// The Spot Light and horizontal dissolve effect use the exact same color.
    /// Also colors any child particle systems (Embers, Smoke, Flakes).
    /// </summary>
    public void ApplyGlowColor(Color color)
    {
        currentGlowColor = color;

        // 1. Configure Spot Light / Point Light color
        if (glowLight != null)
        {
            glowLight.color = color;
        }

        // 2. Configure Horizontal Dissolve Effect material color
        if (dissolveRenderer != null)
        {
            // .material instantiates the material locally so prefabs don't overwrite each other's asset
            Material mat = dissolveRenderer.material;
            if (mat != null)
            {
                // Shader graph HDR glow properties
                if (mat.HasProperty("_Color_Glow"))
                {
                    mat.SetColor("_Color_Glow", color * 2.2f);
                }
                if (mat.HasProperty("_ColorEdge"))
                {
                    mat.SetColor("_ColorEdge", color * 2.2f);
                }
                if (mat.HasProperty("_Coloredges"))
                {
                    mat.SetColor("_Coloredges", color * 2.2f);
                }
                if (mat.HasProperty("_Edge_Color"))
                {
                    mat.SetColor("_Edge_Color", color);
                }
                if (mat.HasProperty("_BaseColor"))
                {
                    mat.SetColor("_BaseColor", color);
                }
                if (mat.HasProperty("_Color"))
                {
                    mat.SetColor("_Color", color);
                }
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", color);
                }
            }
        }

        // 3. Configure any child particle systems (e.g. Embers, Smoke inside DissolveSolidHorizontal)
        ParticleSystem[] particles = GetComponentsInChildren<ParticleSystem>();
        foreach (ParticleSystem ps in particles)
        {
            if (ps == null) continue;
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color);
        }
    }

    private void HandleHover()
    {
        float newY = spawnPosition.y + Mathf.Sin(Time.time * hoverSpeed) * hoverHeight;
        Vector3 pos = transform.position;
        pos.y = newY;
        transform.position = pos;
    }

    private void HandleRotation()
    {
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
    }

    private void HandleVisibilityLifetime()
    {
        if (maxTimeOutOfView <= 0f) return;

        Camera cam = gameplayCamera != null ? gameplayCamera : Camera.main;
        if (cam == null) return;

        if (IsVisibleToCamera(cam))
        {
            timeOutOfView = 0f;
            return;
        }

        timeOutOfView += Time.deltaTime;
        if (timeOutOfView >= maxTimeOutOfView)
        {
            if (manager != null)
            {
                manager.OnPowerUpDespawned(this);
            }
            Destroy(gameObject);
        }
    }

    private bool IsVisibleToCamera(Camera cam)
    {
        Vector3 vp = cam.WorldToViewportPoint(transform.position);
        return vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (collected || other == null) return;

        // Verify if the collider belongs to the Player snake
        PowerUpPlayer player = other.GetComponentInParent<PowerUpPlayer>();
        if (player == null && (other.CompareTag("Player") || other.transform.root.CompareTag("Player")))
        {
            player = other.transform.root.GetComponentInChildren<PowerUpPlayer>();
        }

        if (player != null)
        {
            collected = true;
            Collect(player);
        }
    }

    private void Collect(PowerUpPlayer player)
    {
        // 1. Tell PowerUpPlayer which power-up was collected to activate effect
        if (player != null)
        {
            player.OnPowerUpCollected(this);
        }

        // 2. Play collection audio
        PlayCollectSound(player);

        // 3. Play collection particle / dissolve effect at exact position, rotation, and size
        SpawnCollectionEffect();

        // 4. Hide visible visuals immediately so only the dissolving cube is visible
        HideVisuals();

        // 5. Notify PowerUpManager
        if (manager != null)
        {
            manager.OnPowerUpCollected(this);
        }

        // 6. Destroy power-up collectible GameObject
        Destroy(gameObject);
    }

    private void HideVisuals()
    {
        Renderer[] allRends = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in allRends)
        {
            if (r != null && (dissolveObject == null || (r.gameObject != dissolveObject && !r.transform.IsChildOf(dissolveObject.transform))))
            {
                r.enabled = false;
            }
        }
        Collider[] allCols = GetComponentsInChildren<Collider>(true);
        foreach (Collider c in allCols)
        {
            if (c != null) c.enabled = false;
        }
        if (glowLight != null)
        {
            glowLight.enabled = false;
        }
    }

    /// <summary>
    /// Locates the active visual Renderer representing the power-up cube (model/box).
    /// </summary>
    public Renderer GetPowerUpCubeRenderer()
    {
        if (modelRenderer != null) return modelRenderer;

        Transform boxChild = transform.Find("box") ?? transform.Find("box_006") ?? transform.Find("Cube");
        if (boxChild != null)
        {
            Renderer r = boxChild.GetComponent<Renderer>();
            if (r != null) return r;
        }

        Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in allRenderers)
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            if (dissolveObject != null && (r.gameObject == dissolveObject || r.transform.IsChildOf(dissolveObject.transform))) continue;
            if (r.gameObject.activeInHierarchy) return r;
        }

        return GetComponent<Renderer>();
    }

    /// <summary>
    /// Scales and aligns the dissolve effect to match the exact size, mesh bounds,
    /// and dimensions of the target cube renderer.
    /// </summary>
    public static void ScaleEffectToMatchRenderer(GameObject effect, Renderer targetRenderer, Vector3 fallbackLossyScale)
    {
        if (effect == null) return;

        // 1. Deactivate inner 'Ball Dissolve' sphere child so it doesn't create an unnatural ball artifact
        Transform ballDissolve = effect.transform.Find("Ball Dissolve");
        if (ballDissolve != null)
        {
            ballDissolve.gameObject.SetActive(false);
        }

        // 2. Determine target world dimensions of the cube
        Vector3 targetWorldScale = targetRenderer != null ? targetRenderer.transform.lossyScale : fallbackLossyScale;
        Vector3 targetMeshSize = Vector3.one;

        if (targetRenderer != null)
        {
            MeshFilter targetMf = targetRenderer.GetComponent<MeshFilter>();
            if (targetMf != null && targetMf.sharedMesh != null)
            {
                targetMeshSize = targetMf.sharedMesh.bounds.size;
            }
        }

        Vector3 targetDimensions = Vector3.Scale(targetWorldScale, targetMeshSize);

        // 3. Determine the dissolve effect's mesh dimensions
        MeshFilter effectMf = effect.GetComponent<MeshFilter>() ?? effect.GetComponentInChildren<MeshFilter>();
        Vector3 effectMeshSize = Vector3.one;
        if (effectMf != null && effectMf.sharedMesh != null)
        {
            effectMeshSize = effectMf.sharedMesh.bounds.size;
        }

        // 4. Compute exact localScale for the effect so its rendered world size matches targetDimensions
        float scaleX = effectMeshSize.x > 0.001f ? targetDimensions.x / effectMeshSize.x : targetDimensions.x;
        float scaleY = effectMeshSize.y > 0.001f ? targetDimensions.y / effectMeshSize.y : targetDimensions.y;
        float scaleZ = effectMeshSize.z > 0.001f ? targetDimensions.z / effectMeshSize.z : targetDimensions.z;

        effect.transform.localScale = new Vector3(scaleX, scaleY, scaleZ);
    }

    private void SpawnCollectionEffect()
    {
        Renderer cubeRenderer = GetPowerUpCubeRenderer();
        Vector3 spawnPos = cubeRenderer != null ? cubeRenderer.bounds.center : transform.position;
        Quaternion spawnRot = cubeRenderer != null ? cubeRenderer.transform.rotation : transform.rotation;

        GameObject effectToSpawn = null;

        // 1. Prioritize dissolve cube effect on this power-up
        if (dissolveObject != null)
        {
            effectToSpawn = dissolveObject;
        }
        else if (collectionEffectPrefab != null)
        {
            effectToSpawn = collectionEffectPrefab;
        }
        else if (PowerUpManager.Instance != null && PowerUpManager.Instance.CollectionEffectPrefab != null)
        {
            effectToSpawn = PowerUpManager.Instance.CollectionEffectPrefab;
        }

        if (effectToSpawn == null) return;

        // Instantiate effect at exact cube center and rotation
        GameObject effect = Instantiate(effectToSpawn, spawnPos, spawnRot);
        effect.SetActive(true);

        // Match exact size, dimensions, and hide inner ball sphere
        ScaleEffectToMatchRenderer(effect, cubeRenderer, transform.lossyScale);

        // Apply glow colors to particle systems and dissolve material
        ApplyColorToEffect(effect);

        // Trigger SpawnEffect dissolve animation and schedule cleanup
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

        Destroy(effect, effectDuration);
    }

    private void ApplyColorToEffect(GameObject effect)
    {
        if (effect == null) return;

        bool hasSpawnEffect = effect.GetComponent<SpawnEffect>() != null || effect.GetComponentInChildren<SpawnEffect>() != null;

        ParticleSystem[] systems = effect.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem ps in systems)
        {
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(currentGlowColor);
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
                if (r.material.HasProperty("_Color")) r.material.SetColor("_Color", currentGlowColor);
                if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", currentGlowColor);
                if (r.material.HasProperty("_EmissionColor")) r.material.SetColor("_EmissionColor", currentGlowColor * 1.5f);
                if (r.material.HasProperty("_Color_Glow")) r.material.SetColor("_Color_Glow", currentGlowColor * 2.2f);
                if (r.material.HasProperty("_ColorEdge")) r.material.SetColor("_ColorEdge", currentGlowColor * 2.2f);
                if (r.material.HasProperty("_Coloredges")) r.material.SetColor("_Coloredges", currentGlowColor * 2.2f);
                if (r.material.HasProperty("_Edge_Color")) r.material.SetColor("_Edge_Color", currentGlowColor * 2.2f);
                if (r.material.HasProperty("_Main_Color")) r.material.SetColor("_Main_Color", currentGlowColor);
            }
        }
    }

    private void PlayCollectSound(PowerUpPlayer player)
    {
        if (collectSoundClip == null) return;

        AudioSource playerAudio = player != null ? player.GetComponent<AudioSource>() : null;
        if (playerAudio != null)
        {
            playerAudio.PlayOneShot(collectSoundClip, collectSoundVolume);
        }
        else
        {
            AudioSource.PlayClipAtPoint(collectSoundClip, transform.position, collectSoundVolume);
        }
    }
}
