using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

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

    [Header("Death Dissolve Animation")]
    [Tooltip("Duration in seconds for the player dissolve and fade-out animation when dying.")]
    [Range(0.5f, 4f)]
    [SerializeField] private float deathDissolveDuration = 1.6f;

    [Tooltip("Whether to destroy the Player GameObject smoothly after the dissolve animation completes.")]
    [SerializeField] private bool destroyPlayerAfterDissolve = true;

    public float DeathDissolveDuration { get => deathDissolveDuration; set => deathDissolveDuration = Mathf.Max(0.1f, value); }
    public bool DestroyPlayerAfterDissolve { get => destroyPlayerAfterDissolve; set => destroyPlayerAfterDissolve = value; }

    [Header("Shed Animation & Audio")]
    [Tooltip("Sound played when shedding blocks. The shedding animation duration will automatically match the length of this audio clip.")]
    [SerializeField] private AudioClip shedSoundClip;

    [Tooltip("Playback volume for the shed sound.")]
    [Range(0f, 1f)]
    [SerializeField] private float shedSoundVolume = 1f;

    [Tooltip("Fallback duration in seconds for the shed animation if no shed audio clip is assigned.")]
    [Range(0.2f, 3.0f)]
    [SerializeField] private float fallbackShedDuration = 0.8f;

    [Tooltip("Distance the shed blocks reverse backward away from the snake before shrinking and being destroyed.")]
    [Range(0.5f, 6.0f)]
    [SerializeField] private float shedReverseDistance = 2.5f;

    [Tooltip("Easing curve used when reversing the shed blocks backward out of the snake.")]
    [SerializeField] private AnimationCurve shedReverseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Audio Settings")]
    [Tooltip("Sound played when collecting / eating a pickup. Assign your eat sound clip here in the Inspector.")]
    [SerializeField] private AudioClip eatSoundClip;

    [Tooltip("Playback volume for the eat sound.")]
    [Range(0f, 1f)]
    [SerializeField] private float eatSoundVolume = 1f;

    [Tooltip("Sound played when two blocks merge together. Assign your merge sound clip here in the Inspector.")]
    [SerializeField] private AudioClip mergeSoundClip;

    [Tooltip("Playback volume for the merge sound.")]
    [Range(0f, 1f)]
    [SerializeField] private float mergeSoundVolume = 1f;

    [Tooltip("Slightly randomize pitch when playing audio to keep fast consecutive eats/merges dynamic and punchy.")]
    [SerializeField] private bool randomizePitch = true;

    [Tooltip("AudioSource component on the Player. If unassigned, will automatically find or add one.")]
    [SerializeField] private AudioSource audioSource;

    public AudioClip EatSoundClip { get => eatSoundClip; set => eatSoundClip = value; }
    public float EatSoundVolume { get => eatSoundVolume; set => eatSoundVolume = value; }
    public AudioClip MergeSoundClip { get => mergeSoundClip; set => mergeSoundClip = value; }
    public float MergeSoundVolume { get => mergeSoundVolume; set => mergeSoundVolume = value; }
    public AudioClip ShedSoundClip { get => shedSoundClip; set => shedSoundClip = value; }
    public float ShedSoundVolume { get => shedSoundVolume; set => shedSoundVolume = value; }
    public float FallbackShedDuration { get => fallbackShedDuration; set => fallbackShedDuration = value; }
    public float ShedReverseDistance { get => shedReverseDistance; set => shedReverseDistance = value; }
    public AnimationCurve ShedReverseCurve { get => shedReverseCurve; set => shedReverseCurve = value; }
    public AudioSource SnakeAudioSource { get => audioSource; set => audioSource = value; }

    /// <summary>
    /// Event triggered when the Head's value would be reduced below 2.
    /// Can be subscribed to by game-over or death systems.
    /// </summary>
    public event System.Action OnDeathCondition;

    public static SnakeGrow Instance { get; private set; }

    /// <summary>
    /// Event triggered whenever the snake's cube count (including head) changes.
    /// </summary>
    public static event System.Action<int> OnSnakeCubesChanged;

    /// <summary>
    /// Total number of cubes currently in the snake, including the head.
    /// </summary>
    public int TotalCubeCount
    {
        get
        {
            if (segments != null && segments.Count > 0)
            {
                int validCount = 0;
                for (int i = 0; i < segments.Count; i++)
                {
                    if (segments[i] != null) validCount++;
                }
                return Mathf.Max(1, validCount);
            }

            return Mathf.Max(1, transform.childCount);
        }
    }

    private void NotifyCubesChanged()
    {
        OnSnakeCubesChanged?.Invoke(TotalCubeCount);
    }

    /// <summary>
    /// The current numerical value of the Head (e.g. 2, 4, 8, 16).
    /// </summary>
    public int HeadValue
    {
        get
        {
            if (head != null)
            {
                body b = head.GetComponent<body>();
                if (b != null) return b.Value;
            }
            return 2;
        }
    }

    /// <summary>
    /// Transform of the Head segment.
    /// </summary>
    public Transform HeadSegment => head;

    /// <summary>Direct reference to the body prefab asset used for snake growth.</summary>
    public GameObject BodyPrefab => bodyPrefab;

    /// <summary>Direct read-only access to all snake segments in order (segments[0] is Head).</summary>
    public IReadOnlyList<Transform> Segments => segments;

    /// <summary>True if growth, merge, or damage coroutine is currently processing.</summary>
    public bool IsProcessing => isProcessing;

    /// <summary>Set to true while a power-up rearrangement animation is running to pause auto-merges until segments arrive.</summary>
    public bool IsRearranging { get; set; }

    // segments[0] is always the Head; segments[1..] are the body, in the
    // same order as the physical hierarchy under Player.
    private readonly List<Transform> segments = new List<Transform>();

    private Transform head;
    private Vector3 originalHeadLocalPos;
    private readonly Queue<int> pendingPickups = new Queue<int>();
    private bool isProcessing = false;

    private void Awake()
    {
        Instance = this;
        EnsureInitialized();
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
        EnsureInitialized();
        DetectSegments();
        UpdateHierarchyOrder();
        NotifyCubesChanged();

        // Check if the scene already contains any adjacent mergeable pairs
        if (FindMergeablePairIndex() != -1 && !isProcessing && gameObject.activeInHierarchy)
        {
            StartCoroutine(ProcessGrowthAndMergesCoroutine());
        }
    }

    private void Update()
    {
        // Continuously check if blocks next to each other in the snake body are the same value,
        // or if there are pending pickups to process, and merge them automatically.
        if (!isProcessing && !IsRearranging && gameObject.activeInHierarchy && (pendingPickups.Count > 0 || FindMergeablePairIndex() != -1))
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

        EnsureAudioSource();
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
    /// Processes queued pickups and checks the snake body to sequentially resolve
    /// all adjacent identical-block merges until the snake reaches a stable state.
    /// </summary>
    private IEnumerator ProcessGrowthAndMergesCoroutine()
    {
        isProcessing = true;

        while (pendingPickups.Count > 0 || FindMergeablePairIndex() != -1)
        {
            if (pendingPickups.Count > 0)
            {
                int pickupValue = pendingPickups.Dequeue();
                Transform newSegment = InsertSegmentAfterHead(pickupValue);
                SyncMovement();

                // Smoothly animate the newly inserted block's growth and path integration
                if (newSegment != null)
                {
                    yield return StartCoroutine(AnimateGrowth(newSegment));
                }
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
            playerMovement.RefreshBodySegments();
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
        NotifyCubesChanged();

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

    private bool isDying = false;

    /// <summary>Returns true if the snake is currently playing its death dissolve animation.</summary>
    public bool IsDying => isDying;

    /// <summary>
    /// Promotes the next body segment (segments[1]) to become the new Head of the player
    /// when the current head is consumed by a higher-value enemy.
    /// Returns true if player survived with a new head, false if no cubes remain (Game Over).
    /// </summary>
    public bool PromoteNewHead(int consumedHeadValue)
    {
        if (segments == null || segments.Count <= 1)
        {
            Debug.Log("[SnakeGrow] Head consumed and no remaining body segments -> Triggering Game Over!");
            TriggerDeathOrGameOver();
            return false;
        }

        Transform oldHead = head;
        Transform newHead = segments[1];

        Debug.Log($"[SnakeGrow] Head consumed! Promoting segment '{newHead.name}' to become new Head.");

        // Preserve DetectBody trigger child by reparenting to newHead before oldHead dissolves
        if (oldHead != null && newHead != null)
        {
            DetectBody detectBody = oldHead.GetComponentInChildren<DetectBody>();
            if (detectBody != null)
            {
                detectBody.transform.SetParent(newHead, false);
                detectBody.transform.localPosition = new Vector3(-0.0143f, 0f, 0.2935f);
                detectBody.transform.localRotation = Quaternion.identity;
            }
        }

        // 1. Immediately disable all colliders on old head so it cannot trigger duplicate hits while dissolving
        if (oldHead != null)
        {
            Collider[] cols = oldHead.GetComponentsInChildren<Collider>();
            foreach (var c in cols) if (c != null) c.enabled = false;

            oldHead.SetParent(null);
            AnimateSegmentDissolveAndDestroyObject(oldHead.gameObject, 0.6f);
        }

        // 2. Remove old head from segments list
        segments.RemoveAt(0);

        // 3. Update head reference
        head = newHead;
        if (head != null)
        {
            head.tag = "SnakeHead";
            originalHeadLocalPos = Vector3.zero;
        }

        // 4. Ensure new head has body component
        if (head != null && head.GetComponent<body>() == null)
        {
            head.gameObject.AddComponent<body>();
        }

        // 5. Update PlayerMovement (repositions root and trims path history ahead of new head)
        if (playerMovement != null)
        {
            playerMovement.SetHead(newHead);
        }

        UpdateHierarchyOrder();
        NotifyCubesChanged();

        return true;
    }

    /// <summary>
    /// Called when an enemy eats a player's body segment in combat.
    /// Dissolves the target segment using the existing dissolve effect, removes it from the snake,
    /// and smoothly closes the gap in the remaining body with no gap along the path history.
    /// Does NOT reduce head value or trigger Game Over unless no cubes remain.
    /// Returns true if segment was successfully consumed, and outputs the consumed value.
    /// </summary>
    public bool ConsumeBodySegment(Transform targetSegment, out int consumedValue)
    {
        consumedValue = 0;
        if (targetSegment == null || isDying) return false;

        // The Head cannot be consumed via ConsumeBodySegment (must use PromoteNewHead)
        if (targetSegment == head || (segments.Count > 0 && targetSegment == segments[0]))
            return false;

        int segmentIndex = segments.IndexOf(targetSegment);
        if (segmentIndex <= 0) return false;

        body segmentBody = targetSegment.GetComponent<body>();
        consumedValue = segmentBody != null ? segmentBody.Value : 2;

        // 1. Immediately disable all colliders so it cannot trigger duplicate hits
        Collider[] cols = targetSegment.GetComponentsInChildren<Collider>();
        foreach (var c in cols) if (c != null) c.enabled = false;

        // 2. Unparent target segment so it dissolves independently
        targetSegment.SetParent(null);

        // 3. Remove from logical segments list immediately
        segments.RemoveAt(segmentIndex);
        UpdateHierarchyOrder();
        NotifyCubesChanged();

        // 4. Calculate gap to close and sync remaining body segments with PlayerMovement
        float gapToClose = playerMovement != null ? playerMovement.GetDefaultGap() : 0.3f;
        int bodyIndex = segmentIndex - 1;
        SyncMovement();

        // 5. Smoothly close the gap along the movement path so no gap remains
        if (bodyIndex < segments.Count - 1)
        {
            StartCoroutine(AnimateGapClosing(bodyIndex, gapToClose));
        }

        // 6. Dissolve and destroy the consumed segment using the existing dissolve effect
        AnimateSegmentDissolveAndDestroyObject(targetSegment.gameObject, 0.6f);

        return true;
    }

    /// <summary>
    /// Plays the dissolve particle/shader animation on a target segment or game object and destroys it.
    /// </summary>
    public static void AnimateSegmentDissolveAndDestroyObject(GameObject targetObj, float duration = 0.6f)
    {
        if (targetObj == null) return;

        if (Instance != null)
        {
            Instance.StartCoroutine(DoSegmentDissolve(targetObj, duration));
        }
        else
        {
            Destroy(targetObj, duration);
        }
    }

    private static IEnumerator DoSegmentDissolve(GameObject targetObj, float duration)
    {
        if (targetObj == null) yield break;

        // Disable colliders immediately to prevent double hits
        Collider[] cols = targetObj.GetComponentsInChildren<Collider>();
        foreach (var c in cols) if (c != null) c.enabled = false;

        // Activate child dissolve objects
        Transform[] children = targetObj.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child == null || child == targetObj.transform) continue;
            string nameLower = child.name.ToLowerInvariant();
            if (nameLower.Contains("dissolve") || nameLower.Contains("vfx") || nameLower.Contains("effect"))
            {
                child.gameObject.SetActive(true);
                ParticleSystem[] psArray = child.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in psArray)
                {
                    if (ps != null)
                    {
                        if (ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        var main = ps.main;
                        main.duration = duration;
                        ps.Play();
                    }
                }
            }
        }

        Renderer[] rends = targetObj.GetComponentsInChildren<Renderer>(true);
        List<Material> dynamicMats = new List<Material>();
        foreach (var rend in rends)
        {
            if (rend == null || !rend.enabled || rend is ParticleSystemRenderer) continue;
            foreach (var m in rend.materials)
            {
                if (m != null && !dynamicMats.Contains(m)) dynamicMats.Add(m);
            }
        }

        TMP_Text[] tmproTexts = targetObj.GetComponentsInChildren<TMP_Text>(true);

        int cutoffID = Shader.PropertyToID("_Cutoff");
        int cutoffLowerID = Shader.PropertyToID("_cutoff");
        int dissolveID = Shader.PropertyToID("_Dissolve");

        Vector3 initialScale = targetObj.transform.localScale;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            yield return null;
            if (targetObj == null) yield break;

            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);

            foreach (Material m in dynamicMats)
            {
                if (m == null) continue;
                if (m.HasProperty(cutoffID)) m.SetFloat(cutoffID, progress);
                if (m.HasProperty(cutoffLowerID)) m.SetFloat(cutoffLowerID, progress);
                if (m.HasProperty(dissolveID)) m.SetFloat(dissolveID, progress);
            }

            foreach (TMP_Text txt in tmproTexts)
            {
                if (txt == null) continue;
                Color c = txt.color;
                c.a = Mathf.Lerp(1f, 0f, progress);
                txt.color = c;
            }

            if (progress > 0.4f)
            {
                float shrinkT = (progress - 0.4f) / 0.6f;
                targetObj.transform.localScale = Vector3.Lerp(initialScale, Vector3.zero, Mathf.SmoothStep(0f, 1f, shrinkT));
            }
        }

        if (targetObj != null)
        {
            Destroy(targetObj);
        }
    }

    /// <summary>
    /// Triggers the death / game over condition, playing the dissolve effect across all head and body segments
    /// before destroying the player object smoothly.
    /// </summary>
    public void TriggerDeathOrGameOver()
    {
        if (isDying) return;
        isDying = true;

        Debug.Log("SnakeGrow: Death condition triggered - playing dissolve animation.");
        OnDeathCondition?.Invoke();

        // Start dissolve and smooth destroy sequence
        StartCoroutine(AnimateDeathDissolveAndDestroy());
    }

    /// <summary>
    /// Coroutine that activates child Dissolve objects, plays particle systems, drives material dissolve properties,
    /// fades out alpha and scale across the head and body segments, and smoothly destroys the player object.
    /// </summary>
    public IEnumerator AnimateDeathDissolveAndDestroy()
    {
        // 1. Stop movement immediately
        PlayerMovement pm = GetComponent<PlayerMovement>();
        if (pm != null)
        {
            pm.enabled = false;
        }

        // Disable colliders on head and segments to prevent further physics hits
        Collider[] playerColliders = GetComponentsInChildren<Collider>();
        foreach (var col in playerColliders)
        {
            if (col != null) col.enabled = false;
        }

        // 2. Gather all segments (Head + Body segments)
        List<Transform> allSegments = new List<Transform>();
        if (head != null) allSegments.Add(head);
        if (segments != null)
        {
            foreach (var seg in segments)
            {
                if (seg != null && !allSegments.Contains(seg))
                {
                    allSegments.Add(seg);
                }
            }
        }

        // Cache initial scales and materials
        Dictionary<Transform, Vector3> initialScales = new Dictionary<Transform, Vector3>();
        List<Material> dynamicMaterials = new List<Material>();
        List<TMP_Text> tmproTexts = new List<TMP_Text>();

        foreach (Transform seg in allSegments)
        {
            if (seg == null) continue;
            initialScales[seg] = seg.localScale;

            // Activate child Dissolve objects or VFX particles
            Transform[] children = seg.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                if (child == null || child == seg) continue;

                string nameLower = child.name.ToLowerInvariant();
                if (nameLower.Contains("dissolve") || nameLower.Contains("vfx") || nameLower.Contains("effect") || nameLower.Contains("ember"))
                {
                    child.gameObject.SetActive(true);

                    ParticleSystem[] psArray = child.GetComponentsInChildren<ParticleSystem>(true);
                    foreach (var ps in psArray)
                    {
                        if (ps != null)
                        {
                            if (ps.isPlaying)
                            {
                                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                            }
                            var main = ps.main;
                            main.duration = deathDissolveDuration;
                            ps.Play();
                        }
                    }
                }
            }

            // Also check for ParticleSystems directly on the segment
            ParticleSystem[] directPs = seg.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in directPs)
            {
                if (ps != null && !ps.isPlaying)
                {
                    ps.Play();
                }
            }

            // Gather Renderers and instantiate unique material instances so asset files are untouched
            Renderer[] rends = seg.GetComponentsInChildren<Renderer>(true);
            foreach (var rend in rends)
            {
                if (rend == null || !rend.enabled || rend is ParticleSystemRenderer) continue;

                Material[] mats = rend.materials; // Accessing .materials creates dynamic instances for dissolving
                foreach (var m in mats)
                {
                    if (m != null && !dynamicMaterials.Contains(m))
                    {
                        dynamicMaterials.Add(m);
                    }
                }
            }

            // Gather TMPro text elements
            TMP_Text[] texts = seg.GetComponentsInChildren<TMP_Text>(true);
            foreach (var txt in texts)
            {
                if (txt != null && !tmproTexts.Contains(txt))
                {
                    tmproTexts.Add(txt);
                }
            }
        }

        // Shader property IDs commonly used for dissolve / cutoff effects
        int cutoffID = Shader.PropertyToID("_Cutoff");
        int cutoffLowerID = Shader.PropertyToID("_cutoff");
        int dissolveID = Shader.PropertyToID("_Dissolve");
        int dissolveAmountID = Shader.PropertyToID("_DissolveAmount");
        int amountID = Shader.PropertyToID("_Amount");
        int progressID = Shader.PropertyToID("_Progress");

        float elapsed = 0f;
        while (elapsed < deathDissolveDuration)
        {
            yield return null;
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / deathDissolveDuration);

            // Animate shader dissolve properties on all segment materials
            foreach (Material m in dynamicMaterials)
            {
                if (m == null) continue;

                if (m.HasProperty(cutoffID)) m.SetFloat(cutoffID, progress);
                if (m.HasProperty(cutoffLowerID)) m.SetFloat(cutoffLowerID, progress);
                if (m.HasProperty(dissolveID)) m.SetFloat(dissolveID, progress);
                if (m.HasProperty(dissolveAmountID)) m.SetFloat(dissolveAmountID, progress);
                if (m.HasProperty(amountID)) m.SetFloat(amountID, progress);
                if (m.HasProperty(progressID)) m.SetFloat(progressID, progress);
            }

            // Fade TMPro text alpha
            foreach (TMP_Text txt in tmproTexts)
            {
                if (txt == null) continue;
                Color c = txt.color;
                c.a = Mathf.Lerp(1f, 0f, progress);
                txt.color = c;
            }

            // In the second half of the dissolve, smoothly shrink segment scales down
            if (progress > 0.5f)
            {
                float shrinkT = (progress - 0.5f) / 0.5f;
                float shrinkEase = Mathf.SmoothStep(0f, 1f, shrinkT);

                foreach (Transform seg in allSegments)
                {
                    if (seg != null && initialScales.TryGetValue(seg, out Vector3 startScale))
                    {
                        seg.localScale = Vector3.Lerp(startScale, Vector3.zero, shrinkEase);
                    }
                }
            }
        }

        // Final cutoff enforcement
        foreach (Material m in dynamicMaterials)
        {
            if (m == null) continue;
            if (m.HasProperty(cutoffID)) m.SetFloat(cutoffID, 1f);
            if (m.HasProperty(cutoffLowerID)) m.SetFloat(cutoffLowerID, 1f);
            if (m.HasProperty(dissolveID)) m.SetFloat(dissolveID, 1f);
            if (m.HasProperty(dissolveAmountID)) m.SetFloat(dissolveAmountID, 1f);
            if (m.HasProperty(amountID)) m.SetFloat(amountID, 1f);
            if (m.HasProperty(progressID)) m.SetFloat(progressID, 1f);
        }

        // 3. Smoothly destroy body segments and Player object
        if (destroyPlayerAfterDissolve)
        {
            foreach (Transform seg in segments)
            {
                if (seg != null && seg != head)
                {
                    Destroy(seg.gameObject);
                }
            }
            segments.Clear();

            // Destroy Player object smoothly
            Destroy(gameObject, 0.05f);
        }
        else
        {
            gameObject.SetActive(false);
        }
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
    /// Reduces the snake body to targetSegmentCount blocks (e.g. 8 blocks) as part of the emergency Shed mechanic.
    /// Excess blocks smoothly reverse backwards out of the snake over the duration of the shed audio clip,
    /// shrinking and being destroyed as the audio finishes, while the Head's value is reduced according to the
    /// consecutive Shed cost formula: Shed Cost = Head Power / 2^n.
    /// </summary>
    public void PerformShed(int targetSegmentCount, int shedLevel = 1, System.Action onComplete = null, AudioClip overrideAudioClip = null)
    {
        EnsureInitialized();

        if (segments.Count == 0)
        {
            DetectSegments();
        }

        StartCoroutine(ShedCoroutine(targetSegmentCount, shedLevel, onComplete, overrideAudioClip));
    }

    public void PerformShed(int targetSegmentCount, System.Action onComplete)
    {
        PerformShed(targetSegmentCount, 1, onComplete, null);
    }

    private IEnumerator ShedCoroutine(int targetSegmentCount, int shedLevel, System.Action onComplete, AudioClip overrideAudioClip = null)
    {
        isProcessing = true;

        int removeCount = segments.Count - targetSegmentCount;
        List<Transform> blocksToEject = new List<Transform>();

        if (removeCount > 0)
        {
            // Gather the excess segments from the tail
            int startIndex = Mathf.Max(1, segments.Count - removeCount);
            for (int i = segments.Count - 1; i >= startIndex; i--)
            {
                if (i < segments.Count && segments[i] != null && segments[i] != head)
                {
                    Transform seg = segments[i];
                    blocksToEject.Add(seg);
                    segments.RemoveAt(i);
                }
            }

            // Immediately update hierarchy and sync PlayerMovement to the remaining segments
            UpdateHierarchyOrder();
            SyncMovement();
            NotifyCubesChanged();
        }

        // Determine audio clip and duration (duration matches audio clip length)
        AudioClip clipToPlay = overrideAudioClip != null ? overrideAudioClip : shedSoundClip;
        float shedDuration = (clipToPlay != null && clipToPlay.length > 0f) ? clipToPlay.length : fallbackShedDuration;
        shedDuration = Mathf.Max(0.1f, shedDuration);

        // Play the shed audio immediately
        PlayShedSound(clipToPlay);

        // Calculate reduced head value using Shed Cost = Head Power / 2^n formula
        body headBody = head != null ? head.GetComponent<body>() : null;
        int currentHeadVal = headBody != null ? headBody.Value : 2;
        int multiplier = 1 << Mathf.Clamp(shedLevel, 1, 30); // 2^n: 2, 4, 8, 16...
        int newHeadVal = currentHeadVal / multiplier;

        if (newHeadVal <= 1)
        {
            Debug.Log($"[SnakeGrow] Shed reduced head to {newHeadVal} (<= 1) -> Game Over triggered!");
            TriggerDeathOrGameOver();
            newHeadVal = 2; // Keep visual representation safe while Game Over sequence occurs
        }

        // Animate Head value reduction pop
        if (headBody != null && head != null)
        {
            StartCoroutine(AnimateHeadDamage(headBody, newHeadVal));
        }

        // Animate all removed blocks reversing backwards out of the snake before being destroyed
        for (int i = 0; i < blocksToEject.Count; i++)
        {
            Transform ejectedBlock = blocksToEject[i];
            if (ejectedBlock != null)
            {
                // Disable colliders so they don't interact with pickups or hazards
                Collider col = ejectedBlock.GetComponent<Collider>();
                if (col != null) col.enabled = false;

                ejectedBlock.SetParent(null);

                // blocksToEject[0] is the tail block (furthest back).
                // Stagger reverse distance so blocks reverse in clean formation without colliding
                float extraReverse = (blocksToEject.Count - 1 - i) * 0.4f;
                float totalDist = shedReverseDistance + extraReverse;

                Vector3 reverseDir = -ejectedBlock.forward;
                reverseDir.y = 0f;
                if (reverseDir.sqrMagnitude < 0.001f)
                {
                    reverseDir = head != null ? -head.forward : -Vector3.forward;
                    reverseDir.y = 0f;
                }
                reverseDir.Normalize();

                StartCoroutine(AnimateShedReverseOut(ejectedBlock, reverseDir, totalDist, shedDuration));
            }
        }

        // Wait for the full shed animation and audio duration to finish
        float waitDuration = Mathf.Max(shedDuration, headDamagePunchDuration);
        yield return new WaitForSeconds(waitDuration);

        // Check the snake body for any blocks next to each other that are the same value and merge them
        while (FindMergeablePairIndex() != -1)
        {
            int pairIndex = FindMergeablePairIndex();
            yield return StartCoroutine(AnimateMergePair(pairIndex));
        }

        isProcessing = false;

        onComplete?.Invoke();
    }

    /// <summary>
    /// Smoothly animates a shed body block reversing backwards out of the snake before shrinking and being destroyed.
    /// The animation duration is synchronized with the assigned shed audio clip duration.
    /// </summary>
    private IEnumerator AnimateShedReverseOut(Transform ejectedTransform, Vector3 reverseDir, float reverseDist, float duration)
    {
        if (ejectedTransform == null)
            yield break;

        Vector3 startPos = ejectedTransform.position;
        Vector3 startScale = ejectedTransform.localScale;
        Quaternion startRot = ejectedTransform.rotation;

        Vector3 targetPos = startPos + reverseDir * reverseDist;
        targetPos.y = startPos.y; // Keep grounded on the snake's plane

        float elapsed = 0f;
        while (elapsed < duration)
        {
            yield return null;
            if (ejectedTransform == null)
                yield break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float posEase = shedReverseCurve != null ? shedReverseCurve.Evaluate(t) : Mathf.SmoothStep(0f, 1f, t);

            ejectedTransform.position = Vector3.Lerp(startPos, targetPos, posEase);
            ejectedTransform.rotation = startRot;

            // Retain full scale during the reverse-out slide (first ~70% of duration),
            // then smoothly shrink down to zero in the final ~30% before destruction
            if (t <= 0.7f)
            {
                ejectedTransform.localScale = startScale;
            }
            else
            {
                float shrinkT = Mathf.Clamp01((t - 0.7f) / 0.3f);
                float shrinkEase = Mathf.SmoothStep(0f, 1f, shrinkT);
                ejectedTransform.localScale = Vector3.Lerp(startScale, Vector3.zero, shrinkEase);
            }
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
        if (collision == null || collision.gameObject == null) return;

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
            return;
        }

        EnemiesLogic enemy = collision.gameObject.GetComponentInParent<EnemiesLogic>();
        if (enemy != null)
        {
            enemy.HandleCombatCollision(collision);
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        if (collision == null || collision.gameObject == null) return;

        EnemiesLogic enemy = collision.gameObject.GetComponentInParent<EnemiesLogic>();
        if (enemy != null)
        {
            enemy.HandleCombatCollision(collision);
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
        for (int i = segments.Count - 1; i >= 0; i--)
        {
            if (segments[i] == null)
            {
                segments.RemoveAt(i);
            }
        }

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

        // Award merge score based on whether the head was involved
        if (GameManager.Instance != null)
        {
            if (isHeadMerge)
            {
                GameManager.Instance.AddHeadMergeScore();
            }
            else
            {
                GameManager.Instance.AddBodyMergeScore();
            }
        }

        // Play the merge sound effect
        PlayMergeSound(isHeadMerge);

        // Remove the rear block
        segments.RemoveAt(i + 1);
        NotifyCubesChanged();
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
            head.tag = "SnakeHead";
            segments.Add(head);
        }

        foreach (Transform child in transform)
        {
            if (child != head)
            {
                child.tag = "SnakeBody";
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
        instance.tag = "SnakeBody";
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
        NotifyCubesChanged();

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

    /// <summary>
    /// Updates the snake's segment order to match a newly rearranged sequence,
    /// synchronizes the physical hierarchy and movement chain, and triggers cube count events.
    /// Used by the Rearrange power-up.
    /// </summary>
    public void RearrangeSegments(IReadOnlyList<Transform> newOrder)
    {
        if (newOrder == null || newOrder.Count == 0)
            return;

        segments.Clear();
        for (int i = 0; i < newOrder.Count; i++)
        {
            if (newOrder[i] != null)
            {
                segments.Add(newOrder[i]);
            }
        }

        UpdateHierarchyOrder();
        SyncMovement();
        NotifyCubesChanged();
    }

    /// <summary>
    /// Ensures that an AudioSource component is ready on this GameObject.
    /// Configures it for 2D playback so player sounds stay crisp and balanced regardless of camera distance.
    /// </summary>
    public void EnsureAudioSource()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f; // 2D stereo playback for clear player SFX
        }
    }

    /// <summary>
    /// Plays the pickup eating sound effect.
    /// If an override clip is passed (e.g. from Pickup.cs), that clip is played;
    /// otherwise falls back to the serialized eatSoundClip assigned in the Inspector.
    /// </summary>
    public void PlayEatSound(AudioClip overrideClip = null, float volumeMultiplier = 1f)
    {
        AudioClip clipToPlay = overrideClip != null ? overrideClip : eatSoundClip;
        if (clipToPlay == null) return;

        EnsureAudioSource();

        float finalVolume = eatSoundVolume * Mathf.Clamp01(volumeMultiplier);
        if (audioSource != null)
        {
            float originalPitch = audioSource.pitch;
            if (randomizePitch)
            {
                audioSource.pitch = UnityEngine.Random.Range(0.95f, 1.05f);
            }

            audioSource.PlayOneShot(clipToPlay, finalVolume);
            audioSource.pitch = originalPitch;
        }
        else
        {
            AudioSource.PlayClipAtPoint(clipToPlay, transform.position, finalVolume);
        }
    }

    /// <summary>
    /// Plays the merge sound effect when two blocks merge into one.
    /// Supports a slight pitch boost on Head merges for rewarding audio feedback.
    /// </summary>
    public void PlayMergeSound(bool isHeadMerge = false)
    {
        if (mergeSoundClip == null) return;

        EnsureAudioSource();

        if (audioSource != null)
        {
            float originalPitch = audioSource.pitch;
            if (randomizePitch)
            {
                float basePitch = isHeadMerge ? 1.08f : 1.0f;
                audioSource.pitch = basePitch * UnityEngine.Random.Range(0.97f, 1.03f);
            }
            else if (isHeadMerge)
            {
                audioSource.pitch = 1.08f;
            }

            audioSource.PlayOneShot(mergeSoundClip, mergeSoundVolume);
            audioSource.pitch = originalPitch;
        }
        else
        {
            AudioSource.PlayClipAtPoint(mergeSoundClip, transform.position, mergeSoundVolume);
        }
    }

    /// <summary>
    /// Plays the shed sound effect when emergency shed is activated.
    /// </summary>
    public void PlayShedSound(AudioClip overrideClip = null, float volumeMultiplier = 1f)
    {
        AudioClip clipToPlay = overrideClip != null ? overrideClip : shedSoundClip;
        if (clipToPlay == null) return;

        EnsureAudioSource();

        float finalVolume = shedSoundVolume * Mathf.Clamp01(volumeMultiplier);
        if (audioSource != null)
        {
            float originalPitch = audioSource.pitch;
            if (randomizePitch)
            {
                audioSource.pitch = UnityEngine.Random.Range(0.97f, 1.03f);
            }

            audioSource.PlayOneShot(clipToPlay, finalVolume);
            audioSource.pitch = originalPitch;
        }
        else
        {
            AudioSource.PlayClipAtPoint(clipToPlay, transform.position, finalVolume);
        }
    }
}