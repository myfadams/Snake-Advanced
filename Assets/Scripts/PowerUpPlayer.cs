using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Helper class managing the UI state, CanvasGroup opacity, scale punching, and smooth blink animations
/// for an individual power-up slot (Magnet, Rearrange, Speed).
/// </summary>
[System.Serializable]
public class PowerUpUIElement
{
    [Tooltip("UI GameObject representing this power-up icon/button in the Canvas.")]
    public GameObject uiObject;

    [Tooltip("CanvasGroup component used to control opacity. If unassigned, automatically fetched or added at Start.")]
    public CanvasGroup canvasGroup;

    private Coroutine blinkCoroutine;
    private MonoBehaviour host;
    private RectTransform rectTransform;
    private Vector3 baseScale = Vector3.one;
    private bool isActive = false;
    private float defaultIdleAlpha = 0.3f;

    private float currentAlpha = 0.3f;
    private float currentScaleMultiplier = 1.0f;
    private float targetScaleMultiplier = 1.0f;

    public bool IsActive => isActive;

    public void Initialize(MonoBehaviour runner, float defaultAlpha = 0.3f)
    {
        host = runner;
        defaultIdleAlpha = defaultAlpha;
        isActive = false;

        if (uiObject != null)
        {
            rectTransform = uiObject.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                baseScale = rectTransform.localScale;
                if (baseScale.sqrMagnitude < 0.001f)
                {
                    baseScale = Vector3.one;
                }
            }

            if (canvasGroup == null)
            {
                canvasGroup = uiObject.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = uiObject.AddComponent<CanvasGroup>();
                }
            }

            // Disable any Animator on the UI icon and all child objects so it cannot conflict with code alpha or scale
            Animator[] anims = uiObject.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < anims.Length; i++)
            {
                if (anims[i] != null) anims[i].enabled = false;
            }
        }

        StopBlink();
        currentAlpha = defaultAlpha;
        currentScaleMultiplier = 1.0f;
        targetScaleMultiplier = 1.0f;
        ApplyState();
    }

    public void SetAlpha(float alpha)
    {
        currentAlpha = alpha;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = alpha;
        }
    }

    public void SetActive(float activeAlpha = 1.0f, float scaleMultiplier = 1.3f)
    {
        isActive = true;
        StopBlink();
        currentAlpha = activeAlpha;
        targetScaleMultiplier = scaleMultiplier;
        ApplyState();
    }

    public void SetIdle(float idleAlpha = 0.3f)
    {
        isActive = false;
        StopBlink();
        currentAlpha = idleAlpha;
        targetScaleMultiplier = 1.0f;
        ApplyState();
    }

    public void StartBlink(float blinkSpeed, float minAlpha, float maxAlpha = 1.0f, float blinkScale = 1.3f)
    {
        if (blinkCoroutine != null) return;
        targetScaleMultiplier = blinkScale;
        if (host != null && host.gameObject.activeInHierarchy)
        {
            blinkCoroutine = host.StartCoroutine(BlinkRoutine(blinkSpeed, minAlpha, maxAlpha, blinkScale));
        }
    }

    public void StopBlink()
    {
        if (blinkCoroutine != null && host != null)
        {
            host.StopCoroutine(blinkCoroutine);
            blinkCoroutine = null;
        }
    }

    private IEnumerator BlinkRoutine(float blinkSpeed, float minAlpha, float maxAlpha, float blinkScale)
    {
        float timer = 0f;
        while (true)
        {
            timer += Time.unscaledDeltaTime * blinkSpeed;
            // Smooth sine wave oscillation between minAlpha and maxAlpha
            float t = (Mathf.Sin(timer * Mathf.PI * 2f) + 1f) * 0.5f;
            currentAlpha = Mathf.Lerp(minAlpha, maxAlpha, t);
            currentScaleMultiplier = Mathf.Lerp(blinkScale * 0.93f, blinkScale * 1.07f, t);
            yield return null;
        }
    }

    public void OnLateUpdate()
    {
        // Smoothly move scale and alpha toward target
        if (blinkCoroutine == null)
        {
            if (isActive)
            {
                // Gentle breathing pulse while active so player clearly sees which power-up is active
                float pulse = 1f + Mathf.Sin(Time.unscaledTime * 4f) * 0.04f;
                currentScaleMultiplier = Mathf.MoveTowards(currentScaleMultiplier, targetScaleMultiplier * pulse, Time.unscaledDeltaTime * 4f);
                currentAlpha = Mathf.MoveTowards(currentAlpha, 1.0f, Time.unscaledDeltaTime * 6f);
            }
            else
            {
                currentScaleMultiplier = Mathf.MoveTowards(currentScaleMultiplier, 1.0f, Time.unscaledDeltaTime * 4f);
                currentAlpha = Mathf.MoveTowards(currentAlpha, defaultIdleAlpha, Time.unscaledDeltaTime * 4f);
            }
        }

        ApplyState();
    }

    private void ApplyState()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = currentAlpha;
        }

        if (rectTransform != null)
        {
            rectTransform.localScale = baseScale * currentScaleMultiplier;
        }
    }
}

/// <summary>
/// Attach to the Player / Snake GameObject.
/// Centralizes the player-side logic for all active power-ups (Magnet, Rearrange, Speed).
/// Supports simultaneous active power-ups, manages UI opacity and warning blinking,
/// animates visual effects on the snake, and integrates smoothly with PlayerMovement and SnakeGrow.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PowerUpPlayer : MonoBehaviour
{
    public static PowerUpPlayer Instance { get; private set; }

    [Header("Core References")]
    [Tooltip("The PlayerMovement component controlling snake locomotion. Auto-fetched if empty.")]
    [SerializeField] private PlayerMovement playerMovement;

    [Tooltip("The SnakeGrow component managing body segments and merges. Auto-fetched if empty.")]
    [SerializeField] private SnakeGrow snakeGrow;

    [Header("Power-Up UI Slots")]
    [Tooltip("UI element for the Magnet power-up.")]
    [SerializeField] private PowerUpUIElement magnetUI = new PowerUpUIElement();

    [Tooltip("UI element for the Rearrange power-up.")]
    [SerializeField] private PowerUpUIElement rearrangeUI = new PowerUpUIElement();

    [Tooltip("UI element for the Speed power-up.")]
    [SerializeField] private PowerUpUIElement speedUI = new PowerUpUIElement();

    [Header("UI Alpha & Scale Settings")]
    [Tooltip("Default opacity when a power-up is NOT available/active.")]
    [Range(0f, 1f)]
    [SerializeField] private float idleAlpha = 0.3f;

    [Tooltip("Opacity when a power-up is available/active.")]
    [Range(0f, 1f)]
    [SerializeField] private float activeAlpha = 1.0f;

    [Tooltip("Scale multiplier for active power-up UI icons so they are easily distinguished.")]
    [Range(1.05f, 1.6f)]
    [SerializeField] private float activeUIScaleMultiplier = 1.3f;

    [Tooltip("Remaining seconds before an effect expires when the UI smoothly blinks.")]
    [SerializeField] private float warningTime = 2.0f;

    [Tooltip("Frequency/speed of the smooth blinking oscillation.")]
    [SerializeField] private float blinkSpeed = 4.0f;

    [Tooltip("Minimum opacity during the blinking oscillation.")]
    [Range(0.1f, 0.8f)]
    [SerializeField] private float minBlinkAlpha = 0.3f;

    [Header("Warning / Announcement UI")]
    [Tooltip("The Warning UI Prefab or Scene GameObject (e.g. warnings.prefab) to display power-up instructions.")]
    [SerializeField] private GameObject warningUIPrefab;

    [Tooltip("How long the power-up announcement banner remains visible on screen (seconds).")]
    [SerializeField] private float announcementDuration = 3.2f;

    [Header("Speed Boost Settings")]
    [Tooltip("If true, speed boost duration only depletes while holding the boost key (fuel-style). " +
             "If false, the boost duration counts down continuously from the moment it is collected.")]
    [SerializeField] private bool consumeDurationOnlyWhileBoosting = true;

    [Tooltip("Smoothing speed when transitioning to boosted movement and back to normal speed.")]
    [SerializeField] private float speedTransitionRate = 6.0f;

    [Header("Character VFX (Optional)")]
    [Tooltip("Visual effect prefab instantiated on the snake head while Magnet is active. Leave empty for built-in subtle aura.")]
    [SerializeField] private GameObject magnetEffectPrefab;

    [Tooltip("Visual effect prefab active on the snake while Speed boost is engaged. Leave empty for built-in subtle trail.")]
    [SerializeField] private GameObject speedEffectPrefab;

    [Header("Audio SFX (Optional)")]
    [Tooltip("Sound played when Magnet activates.")]
    [SerializeField] private AudioClip magnetSoundClip;

    [Tooltip("Sound played when Rearrange executes.")]
    [SerializeField] private AudioClip rearrangeSoundClip;

    [Tooltip("Sound played when Speed boost is engaged.")]
    [SerializeField] private AudioClip speedBoostSoundClip;

    [Tooltip("Audio playback volume.")]
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 1f;

    // Active power-up coroutine trackers
    private Coroutine magnetCoroutine;
    private Coroutine rearrangeCoroutine;
    private Coroutine speedCoroutine;

    // Magnet runtime state
    private bool isMagnetActive = false;
    private int currentMagnetTargetValue = 2;
    private float currentMagnetRadius = 10f;
    private float currentMagnetSpeed = 8f;
    private float magnetTimeRemaining = 0f;
    private int magnetMaxAttracted = 0;
    private GameObject activeMagnetVFX;

    // Rearrange runtime state
    private bool isRearranging = false;

    // Speed runtime state
    private bool isSpeedAvailable = false;
    private bool isCurrentlyBoosting = false;
    private float baseMoveSpeed = 5f;
    private float currentSpeedMultiplier = 2.0f;
    private float speedDurationRemaining = 0f;
    private KeyCode speedActivationKey = KeyCode.LeftShift;
    private GameObject activeSpeedVFX;

    private AudioSource audioSource;

    // Warning / Announcement UI runtime state
    private GameObject activeWarningUIInstance;
    private TMPro.TMP_Text warningText;
    private CanvasGroup warningCanvasGroup;
    private Coroutine announcementCoroutine;

    public bool IsMagnetActive => isMagnetActive;
    public bool IsRearrangeActive => isRearranging;
    public bool IsSpeedAvailable => isSpeedAvailable;
    public float ActiveUIScaleMultiplier { get => activeUIScaleMultiplier; set => activeUIScaleMultiplier = value; }
    public float IdleAlpha { get => idleAlpha; set => idleAlpha = value; }
    public GameObject WarningUIPrefab
    {
        get => warningUIPrefab;
        set
        {
            warningUIPrefab = value;
            activeWarningUIInstance = null;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        EnsureComponentReferences();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        EnsureComponentReferences();

        if (playerMovement != null)
        {
            baseMoveSpeed = playerMovement.MoveSpeed;
        }

        // Initialize UI elements to default idle alpha (0.6)
        magnetUI.Initialize(this, idleAlpha);
        rearrangeUI.Initialize(this, idleAlpha);
        speedUI.Initialize(this, idleAlpha);

        // Subscribe to death/game-over event if present
        if (snakeGrow != null)
        {
            snakeGrow.OnDeathCondition += HandlePlayerDeath;
        }
    }

    private void EnsureComponentReferences()
    {
        if (playerMovement == null) playerMovement = GetComponent<PlayerMovement>();
        if (snakeGrow == null) snakeGrow = GetComponent<SnakeGrow>();

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
        }
    }

    private void Update()
    {
        // Handle Left Shift boost input when Speed power-up is available
        if (isSpeedAvailable)
        {
            HandleSpeedBoostInput();
        }
    }

    private void LateUpdate()
    {
        magnetUI?.OnLateUpdate();
        rearrangeUI?.OnLateUpdate();
        speedUI?.OnLateUpdate();
    }

    private void OnDisable()
    {
        ResetAllPowerUps();
    }

    /// <summary>
    /// Called by a PowerUp collectible when the snake collides with it.
    /// Distributes to the appropriate power-up handler.
    /// </summary>
    public void OnPowerUpCollected(PowerUp powerUp)
    {
        if (powerUp == null) return;

        switch (powerUp.Type)
        {
            case PowerUpType.Magnet:
                ActivateMagnet(powerUp.TargetValue, powerUp.MagnetRadius, powerUp.MagnetSpeed,
                               powerUp.MagnetDuration, powerUp.MagnetMaxAttracted, powerUp.GlowColor);
                ShowAnnouncement($"MAGNET ACTIVATED!\nAttracts value {powerUp.TargetValue} blocks (No button needed)", powerUp.GlowColor);
                break;

            case PowerUpType.Rearrange:
                ActivateRearrange(powerUp.TargetValue, powerUp.RearrangeDuration, powerUp.GlowColor);
                ShowAnnouncement("REARRANGE ACTIVATED!\nSorting snake segments into order (No button needed)", powerUp.GlowColor);
                break;

            case PowerUpType.Speed:
                ActivateSpeed(powerUp.SpeedMultiplier, powerUp.SpeedDuration,
                              powerUp.ActivationKey, powerUp.GlowColor);
                ShowAnnouncement($"SPEED BOOST READY!\nHold [{powerUp.ActivationKey}] to Boost", powerUp.GlowColor);
                break;
        }
    }

    #region 0. Warning / Announcement UI Banner

    /// <summary>
    /// Displays a message on the Warning UI banner explaining what the power-up does,
    /// and showing which button to press if manual activation is required.
    /// </summary>
    public void ShowAnnouncement(string message, Color? highlightColor = null)
    {
        EnsureWarningUIInstance();
        if (activeWarningUIInstance == null) return;

        if (announcementCoroutine != null)
        {
            StopCoroutine(announcementCoroutine);
        }
        announcementCoroutine = StartCoroutine(AnnouncementRoutine(message, highlightColor));
    }

    private void EnsureWarningUIInstance()
    {
        if (activeWarningUIInstance != null) return;

        // 1. Try serialized warningUIPrefab
        if (warningUIPrefab != null)
        {
            if (warningUIPrefab.scene.IsValid())
            {
                activeWarningUIInstance = warningUIPrefab;
            }
            else
            {
                Transform parent = GetTargetUIParent();
                activeWarningUIInstance = Instantiate(warningUIPrefab, parent, false);
            }
        }

        // 2. Fallback: find existing in scene
        if (activeWarningUIInstance == null)
        {
            GameObject sceneWarnings = GameObject.Find("warnings");
            if (sceneWarnings != null)
            {
                activeWarningUIInstance = sceneWarnings;
            }
        }

        if (activeWarningUIInstance != null)
        {
            // Disable Animator on announcement so it doesn't fight alpha/scale fading
            Animator[] anims = activeWarningUIInstance.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < anims.Length; i++)
            {
                if (anims[i] != null) anims[i].enabled = false;
            }

            warningText = activeWarningUIInstance.GetComponentInChildren<TMPro.TMP_Text>(true);
            warningCanvasGroup = activeWarningUIInstance.GetComponent<CanvasGroup>();
            if (warningCanvasGroup == null)
            {
                warningCanvasGroup = activeWarningUIInstance.AddComponent<CanvasGroup>();
            }
            warningCanvasGroup.alpha = 0f;
            activeWarningUIInstance.SetActive(false);
        }
    }

    private Transform GetTargetUIParent()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas != null) return canvas.transform;

        GameObject canvasObj = GameObject.Find("Canvas");
        if (canvasObj != null) return canvasObj.transform;

        return null;
    }

    private IEnumerator AnnouncementRoutine(string message, Color? highlightColor)
    {
        if (activeWarningUIInstance == null) yield break;

        activeWarningUIInstance.SetActive(true);
        activeWarningUIInstance.transform.SetAsLastSibling();

        if (warningText != null)
        {
            warningText.text = message;
            if (highlightColor.HasValue)
            {
                warningText.color = highlightColor.Value;
            }
        }

        RectTransform rt = activeWarningUIInstance.GetComponent<RectTransform>();
        Vector3 baseScale = rt != null ? Vector3.one : Vector3.one;

        // Fade in + Punch scale
        float fadeInDuration = 0.25f;
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeInDuration);
            if (warningCanvasGroup != null) warningCanvasGroup.alpha = t;
            if (rt != null) rt.localScale = baseScale * Mathf.Lerp(0.85f, 1.05f, t);
            yield return null;
        }

        if (rt != null) rt.localScale = baseScale;
        if (warningCanvasGroup != null) warningCanvasGroup.alpha = 1f;

        // Hold display
        yield return new WaitForSecondsRealtime(announcementDuration);

        // Fade out
        float fadeOutDuration = 0.4f;
        elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = 1f - Mathf.Clamp01(elapsed / fadeOutDuration);
            if (warningCanvasGroup != null) warningCanvasGroup.alpha = t;
            yield return null;
        }

        if (warningCanvasGroup != null) warningCanvasGroup.alpha = 0f;
        activeWarningUIInstance.SetActive(false);
        announcementCoroutine = null;
    }

    #endregion

    #region 1. Magnet Power-Up

    /// <summary>
    /// Activates the Magnet power-up. Attracts pickups of targetValue within radius smoothly toward the snake.
    /// Safely refreshes duration if collected while already active.
    /// </summary>
    public void ActivateMagnet(int targetValue, float radius, float speed, float duration, int maxAttracted, Color glowColor)
    {
        currentMagnetTargetValue = targetValue;
        currentMagnetRadius = radius;
        currentMagnetSpeed = speed;
        magnetMaxAttracted = maxAttracted;

        // If already active, refresh duration without breaking state
        if (isMagnetActive && magnetCoroutine != null)
        {
            magnetTimeRemaining = Mathf.Max(magnetTimeRemaining, duration);
            magnetUI.SetActive(activeAlpha, activeUIScaleMultiplier);
            return;
        }

        magnetTimeRemaining = duration;
        magnetCoroutine = StartCoroutine(MagnetRoutine(glowColor));
    }

    private IEnumerator MagnetRoutine(Color glowColor)
    {
        isMagnetActive = true;
        magnetUI.SetActive(activeAlpha, activeUIScaleMultiplier);

        PlaySFX(magnetSoundClip);
        SpawnMagnetVFX(glowColor);

        bool blinkingStarted = false;

        while (magnetTimeRemaining > 0f)
        {
            magnetTimeRemaining -= Time.deltaTime;

            // Trigger smooth warning blinking near expiration
            if (magnetTimeRemaining <= warningTime && !blinkingStarted)
            {
                blinkingStarted = true;
                magnetUI.StartBlink(blinkSpeed, minBlinkAlpha, activeAlpha, activeUIScaleMultiplier);
            }

            // Pull matching pickups toward snake
            PullMatchingPickups();

            yield return null;
        }

        // Magnet expired: clean up
        CleanupMagnetVFX();
        magnetUI.SetIdle(idleAlpha);
        isMagnetActive = false;
        magnetCoroutine = null;
    }

    /// <summary>
    /// Efficiently queries active pickups from PickupManager (or nearby sphere fallback)
    /// and smoothly pulls matching value pickups toward the snake head.
    /// Avoids FindObjectsOfType() allocations.
    /// </summary>
    private void PullMatchingPickups()
    {
        Transform headTransform = playerMovement != null ? playerMovement.Head : transform;
        if (headTransform == null) return;

        Vector3 headPos = headTransform.position;
        float radiusSqr = currentMagnetRadius * currentMagnetRadius;
        int attractedCount = 0;

        // Approach A: Read directly from PickupManager's managed active list (O(N), 5-8 pickups total)
        if (PickupManager.Instance != null && PickupManager.Instance.ActivePickups != null)
        {
            var pickups = PickupManager.Instance.ActivePickups;
            for (int i = 0; i < pickups.Count; i++)
            {
                Pickup p = pickups[i];
                if (p == null) continue;

                if (p.Value == currentMagnetTargetValue)
                {
                    Vector3 diff = p.transform.position - headPos;
                    diff.y = 0f;

                    if (diff.sqrMagnitude <= radiusSqr)
                    {
                        PullPickupTowardHead(p, headPos);
                        attractedCount++;

                        if (magnetMaxAttracted > 0 && attractedCount >= magnetMaxAttracted)
                            break;
                    }
                }
            }
            return;
        }

        // Approach B: Fallback physics overlap query (non-allocating)
        Collider[] hits = Physics.OverlapSphere(headPos, currentMagnetRadius, ~0, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0) return;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null) continue;

            Pickup p = hit.GetComponent<Pickup>() ?? hit.GetComponentInParent<Pickup>();
            if (p != null && p.Value == currentMagnetTargetValue)
            {
                PullPickupTowardHead(p, headPos);
                attractedCount++;

                if (magnetMaxAttracted > 0 && attractedCount >= magnetMaxAttracted)
                    break;
            }
        }
    }

    private void PullPickupTowardHead(Pickup pickup, Vector3 headPos)
    {
        if (pickup == null) return;

        Vector3 currentPos = pickup.transform.position;
        Vector3 target = headPos;
        target.y = currentPos.y; // Keep on pickup's hover height

        // Smoothly move toward head position
        pickup.transform.position = Vector3.MoveTowards(
            currentPos,
            target,
            currentMagnetSpeed * Time.deltaTime
        );
    }

    private void SpawnMagnetVFX(Color glowColor)
    {
        CleanupMagnetVFX();

        Transform headTransform = playerMovement != null ? playerMovement.Head : transform;
        if (headTransform == null) return;

        if (magnetEffectPrefab != null)
        {
            activeMagnetVFX = Instantiate(magnetEffectPrefab, headTransform.position, Quaternion.identity, headTransform);
            ApplyColorToVFX(activeMagnetVFX, glowColor);
        }
        else
        {
            // Procedural subtle pulsing attraction light aura around the head
            GameObject auraObj = new GameObject("Magnet_Aura");
            auraObj.transform.SetParent(headTransform, false);
            auraObj.transform.localPosition = Vector3.up * 0.15f;

            Light auraLight = auraObj.AddComponent<Light>();
            auraLight.type = LightType.Point;
            auraLight.color = glowColor;
            auraLight.range = currentMagnetRadius * 0.45f;
            auraLight.intensity = 1.8f;
            auraLight.shadows = LightShadows.None;

            activeMagnetVFX = auraObj;
        }
    }

    private void CleanupMagnetVFX()
    {
        if (activeMagnetVFX != null)
        {
            Destroy(activeMagnetVFX);
            activeMagnetVFX = null;
        }
    }

    #endregion

    #region 2. Rearrange Power-Up

    /// <summary>
    /// Activates the Rearrange power-up. Visually animates all body segments with matching targetValue
    /// smoothly along the snake's actual recorded path to sit immediately after the Head.
    /// Preserves snake movement and spacing, and allows subsequent adjacent merges once placed.
    /// </summary>
    public void ActivateRearrange(int targetValue, float animationDuration, Color glowColor)
    {
        if (isRearranging)
        {
            // If already in mid-rearrangement, wait for current to finish
            return;
        }

        rearrangeCoroutine = StartCoroutine(RearrangeRoutine(targetValue, animationDuration, glowColor));
    }

    private IEnumerator RearrangeRoutine(int targetValue, float animationDuration, Color glowColor)
    {
        if (playerMovement == null || snakeGrow == null)
            yield break;

        isRearranging = true;
        snakeGrow.IsRearranging = true;
        rearrangeUI.SetActive(activeAlpha, activeUIScaleMultiplier);

        PlaySFX(rearrangeSoundClip);

        // 1. Gather all current segments from SnakeGrow
        var currentSegments = snakeGrow.Segments;
        if (currentSegments == null || currentSegments.Count <= 2)
        {
            // Snake has head only or only 1 body block; no rearrangement possible
            yield return new WaitForSeconds(0.6f);
            EndRearrange();
            yield break;
        }

        Transform head = currentSegments[0];
        List<Transform> newBodyOrder = new List<Transform>();
        for (int i = 1; i < currentSegments.Count; i++)
        {
            if (currentSegments[i] != null) newBodyOrder.Add(currentSegments[i]);
        }

        // Sort body ascending by block value so identical numbers cluster adjacent to each other
        newBodyOrder.Sort((a, b) =>
        {
            int valA = a.GetComponent<body>()?.Value ?? 0;
            int valB = b.GetComponent<body>()?.Value ?? 0;
            return valA.CompareTo(valB);
        });

        List<Transform> matchingBody = new List<Transform>();
        for (int i = 0; i < newBodyOrder.Count; i++)
        {
            body b = newBodyOrder[i].GetComponent<body>();
            if (b != null && b.Value == targetValue)
            {
                matchingBody.Add(newBodyOrder[i]);
            }
        }

        // Build target complete segment order (Head + newBodyOrder)
        List<Transform> newFullOrder = new List<Transform>(newBodyOrder.Count + 1);
        newFullOrder.Add(head);
        newFullOrder.AddRange(newBodyOrder);

        // Current ordered body segments list from PlayerMovement
        var originalBodyList = playerMovement.BodySegments;
        var cumulativeDistances = playerMovement.CumulativeDistances;

        int bodyCount = originalBodyList.Count;
        if (bodyCount == 0 || cumulativeDistances.Count == 0)
        {
            EndRearrange();
            yield break;
        }

        // 2. Map each segment's initial distance and target distance along the path
        float[] startDistances = new float[bodyCount];
        float[] endDistances = new float[bodyCount];

        for (int i = 0; i < bodyCount; i++)
        {
            Transform seg = originalBodyList[i];
            startDistances[i] = (i < cumulativeDistances.Count) ? cumulativeDistances[i] : 0f;

            // Find where this segment sits in the new order
            int newIndex = newBodyOrder.IndexOf(seg);
            if (newIndex >= 0 && newIndex < cumulativeDistances.Count)
            {
                endDistances[i] = cumulativeDistances[newIndex];
            }
            else
            {
                endDistances[i] = startDistances[i];
            }
        }

        // 3. Smoothly animate segments sliding along the path history to their target distances
        float[] currentDistances = new float[bodyCount];
        float elapsed = 0f;

        while (elapsed < animationDuration)
        {
            yield return null; // Wait for Update() so snake moves along path
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / animationDuration);
            // Smooth cubic ease in/out curve
            float ease = Mathf.SmoothStep(0f, 1f, t);

            for (int i = 0; i < bodyCount; i++)
            {
                currentDistances[i] = Mathf.Lerp(startDistances[i], endDistances[i], ease);
            }

            playerMovement.SetCustomSegmentDistances(currentDistances);
        }

        // 4. Restore normal spacing and apply new segment order in SnakeGrow
        playerMovement.SetCustomSegmentDistances(null);
        snakeGrow.RearrangeSegments(newFullOrder);

        // 5. Subtle visual confirmation pop on matching blocks
        StartCoroutine(PopMatchingBlocks(matchingBody));

        EndRearrange();
    }

    private IEnumerator PopMatchingBlocks(List<Transform> matchingBlocks)
    {
        float popDuration = 0.16f;
        float elapsed = 0f;

        List<Vector3> originalScales = new List<Vector3>();
        for (int i = 0; i < matchingBlocks.Count; i++)
        {
            originalScales.Add(matchingBlocks[i] != null ? matchingBlocks[i].localScale : Vector3.one);
        }

        while (elapsed < popDuration)
        {
            yield return null;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / popDuration);
            float punch = 1f + 0.2f * Mathf.Sin(t * Mathf.PI);

            for (int i = 0; i < matchingBlocks.Count; i++)
            {
                if (matchingBlocks[i] != null)
                {
                    matchingBlocks[i].localScale = originalScales[i] * punch;
                }
            }
        }

        for (int i = 0; i < matchingBlocks.Count; i++)
        {
            if (matchingBlocks[i] != null)
            {
                matchingBlocks[i].localScale = originalScales[i];
            }
        }
    }

    private void EndRearrange()
    {
        if (snakeGrow != null)
        {
            snakeGrow.IsRearranging = false;
        }

        rearrangeUI.SetIdle(idleAlpha);
        isRearranging = false;
        rearrangeCoroutine = null;
    }

    #endregion

    #region 3. Speed Power-Up

    /// <summary>
    /// Activates the Speed power-up availability.
    /// The snake does not immediately move faster; the speed boost only engages when the player holds Left Shift.
    /// </summary>
    public void ActivateSpeed(float multiplier, float duration, KeyCode key, Color glowColor)
    {
        currentSpeedMultiplier = multiplier;
        speedActivationKey = key;

        if (playerMovement != null && !isSpeedAvailable)
        {
            baseMoveSpeed = playerMovement.MoveSpeed;
        }

        // Stack or refresh available duration
        speedDurationRemaining = Mathf.Max(speedDurationRemaining, duration);
        isSpeedAvailable = true;

        speedUI.SetActive(activeAlpha, activeUIScaleMultiplier);

        if (speedCoroutine == null)
        {
            speedCoroutine = StartCoroutine(SpeedRoutine(glowColor));
        }
    }

    private IEnumerator SpeedRoutine(Color glowColor)
    {
        bool blinkingStarted = false;

        while (speedDurationRemaining > 0f)
        {
            // If fuel-style, duration only depletes while the player is actively holding the boost key
            if (consumeDurationOnlyWhileBoosting)
            {
                if (isCurrentlyBoosting)
                {
                    speedDurationRemaining -= Time.deltaTime;
                }
            }
            else
            {
                speedDurationRemaining -= Time.deltaTime;
            }

            // Trigger smooth warning blinking near expiration
            if (speedDurationRemaining <= warningTime && !blinkingStarted)
            {
                blinkingStarted = true;
                speedUI.StartBlink(blinkSpeed, minBlinkAlpha, activeAlpha, activeUIScaleMultiplier);
            }

            yield return null;
        }

        // Speed boost expired: smoothly restore normal speed
        isSpeedAvailable = false;
        isCurrentlyBoosting = false;

        yield return StartCoroutine(TransitionSpeed(baseMoveSpeed));

        CleanupSpeedVFX();
        speedUI.SetIdle(idleAlpha);
        speedCoroutine = null;
    }

    /// <summary>
    /// Checks keyboard input for Left Shift (supporting both New and Legacy Input Systems).
    /// Smoothly transitions speed and toggles the speed visual effect.
    /// </summary>
    private void HandleSpeedBoostInput()
    {
        bool isKeyPressed = CheckBoostKeyPressed();

        if (isKeyPressed && !isCurrentlyBoosting && speedDurationRemaining > 0f)
        {
            // Player started holding Left Shift: engage boost
            isCurrentlyBoosting = true;
            PlaySFX(speedBoostSoundClip);
            SpawnSpeedVFX();
        }
        else if (!isKeyPressed && isCurrentlyBoosting)
        {
            // Player released Left Shift: disengage boost
            isCurrentlyBoosting = false;
            CleanupSpeedVFX();
        }

        // Smoothly interpolate current movement speed toward target speed
        if (playerMovement != null)
        {
            float targetSpeed = isCurrentlyBoosting ? (baseMoveSpeed * currentSpeedMultiplier) : baseMoveSpeed;
            playerMovement.MoveSpeed = Mathf.Lerp(playerMovement.MoveSpeed, targetSpeed, Time.deltaTime * speedTransitionRate);
        }
    }

    private bool CheckBoostKeyPressed()
    {
        // 1. New Input System check
        if (Keyboard.current != null)
        {
            if (speedActivationKey == KeyCode.LeftShift)
            {
                if (Keyboard.current.leftShiftKey.isPressed) return true;
            }
            else if (speedActivationKey == KeyCode.RightShift)
            {
                if (Keyboard.current.rightShiftKey.isPressed) return true;
            }
            else if (speedActivationKey == KeyCode.Space)
            {
                if (Keyboard.current.spaceKey.isPressed) return true;
            }
        }

        // 2. Legacy Input fallback check
        try
        {
            if (UnityEngine.Input.GetKey(speedActivationKey))
            {
                return true;
            }
        }
        catch
        {
            // Ignore legacy exception if active input handler is new input system exclusively
        }

        return false;
    }

    private IEnumerator TransitionSpeed(float targetSpeed)
    {
        if (playerMovement == null) yield break;

        float startSpeed = playerMovement.MoveSpeed;
        float elapsed = 0f;
        float duration = 0.25f;

        while (elapsed < duration)
        {
            yield return null;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            playerMovement.MoveSpeed = Mathf.Lerp(startSpeed, targetSpeed, Mathf.SmoothStep(0f, 1f, t));
        }

        playerMovement.MoveSpeed = targetSpeed;
    }

    private void SpawnSpeedVFX()
    {
        CleanupSpeedVFX();

        Transform headTransform = playerMovement != null ? playerMovement.Head : transform;
        if (headTransform == null) return;

        if (speedEffectPrefab != null)
        {
            activeSpeedVFX = Instantiate(speedEffectPrefab, headTransform.position, Quaternion.identity, headTransform);
        }
        else
        {
            // Procedural subtle speed trail
            GameObject trailObj = new GameObject("Speed_Trail");
            trailObj.transform.SetParent(headTransform, false);
            trailObj.transform.localPosition = -Vector3.forward * 0.1f;

            TrailRenderer tr = trailObj.AddComponent<TrailRenderer>();
            tr.time = 0.25f;
            tr.startWidth = 0.35f;
            tr.endWidth = 0f;
            tr.material = new Material(Shader.Find("Sprites/Default"));
            tr.startColor = new Color(0f, 0.85f, 1f, 0.6f);
            tr.endColor = new Color(0f, 0.4f, 1f, 0f);

            activeSpeedVFX = trailObj;
        }
    }

    private void CleanupSpeedVFX()
    {
        if (activeSpeedVFX != null)
        {
            Destroy(activeSpeedVFX);
            activeSpeedVFX = null;
        }
    }

    #endregion

    #region Helper & Cleanup Methods

    private void HandlePlayerDeath()
    {
        ResetAllPowerUps();
    }

    public void ResetAllPowerUps()
    {
        // 1. Reset Magnet
        if (magnetCoroutine != null)
        {
            StopCoroutine(magnetCoroutine);
            magnetCoroutine = null;
        }
        isMagnetActive = false;
        magnetTimeRemaining = 0f;
        CleanupMagnetVFX();
        magnetUI.SetIdle(idleAlpha);

        // 2. Reset Rearrange
        if (rearrangeCoroutine != null)
        {
            StopCoroutine(rearrangeCoroutine);
            rearrangeCoroutine = null;
        }
        if (snakeGrow != null)
        {
            snakeGrow.IsRearranging = false;
        }
        if (playerMovement != null)
        {
            playerMovement.SetCustomSegmentDistances(null);
        }
        isRearranging = false;
        rearrangeUI.SetIdle(idleAlpha);

        // 3. Reset Speed
        if (speedCoroutine != null)
        {
            StopCoroutine(speedCoroutine);
            speedCoroutine = null;
        }
        if (playerMovement != null && baseMoveSpeed > 0f)
        {
            playerMovement.MoveSpeed = baseMoveSpeed;
        }
        isSpeedAvailable = false;
        isCurrentlyBoosting = false;
        speedDurationRemaining = 0f;
        CleanupSpeedVFX();
        speedUI.SetIdle(idleAlpha);
    }

    private void ApplyColorToVFX(GameObject vfxObject, Color color)
    {
        if (vfxObject == null) return;

        ParticleSystem[] particles = vfxObject.GetComponentsInChildren<ParticleSystem>();
        foreach (var ps in particles)
        {
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color);
        }

        Light l = vfxObject.GetComponentInChildren<Light>();
        if (l != null)
        {
            l.color = color;
        }
    }

    private void PlaySFX(AudioClip clip)
    {
        if (clip == null) return;

        EnsureComponentReferences();
        if (audioSource != null)
        {
            audioSource.PlayOneShot(clip, sfxVolume);
        }
    }

    #endregion
}
