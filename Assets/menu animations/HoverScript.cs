using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Animates button scale on hover and plays a low-volume hover sound effect.
/// Button click/press sounds are intentionally omitted per configuration.
/// </summary>
public class ButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Scale Animation")]
    [SerializeField] private float hoverScale = 1.05f;
    [SerializeField] private float animationSpeed = 10f;

    [Header("Sound Settings")]
    [Tooltip("Playback volume for the button hover sound effect (set low, e.g. 0.45).")]
    [Range(0f, 1f)]
    [SerializeField] private float hoverVolume = 0.45f;

    [Tooltip("Optional hover audio clip to play if the AudioSource clip is not assigned.")]
    [SerializeField] private AudioClip hoverSoundClip;

    private AudioSource hoverSound;
    private Vector3 originalScale;
    private Vector3 targetScale;

    public float HoverVolume
    {
        get => hoverVolume;
        set
        {
            hoverVolume = Mathf.Clamp01(value);
            if (hoverSound != null)
            {
                hoverSound.volume = hoverVolume;
            }
        }
    }

    public AudioClip HoverSoundClip
    {
        get => hoverSoundClip;
        set
        {
            hoverSoundClip = value;
            if (hoverSound != null && hoverSound.clip == null)
            {
                hoverSound.clip = hoverSoundClip;
            }
        }
    }

    private static AudioClip defaultHoverClip;

    private void Awake()
    {
        originalScale = transform.localScale;
        targetScale = originalScale;
        EnsureAudioSource();
    }

    private void OnEnable()
    {
        EnsureAudioSource();
    }

    private void OnDisable()
    {
        targetScale = originalScale;
        transform.localScale = originalScale;
        if (hoverSound != null && hoverSound.isPlaying)
        {
            hoverSound.Stop();
        }
    }

    private void OnValidate()
    {
        hoverVolume = Mathf.Clamp01(hoverVolume);
        if (hoverSound != null)
        {
            hoverSound.playOnAwake = false;
            hoverSound.volume = hoverVolume;
        }
    }

    private void EnsureAudioSource()
    {
        if (hoverSound == null)
        {
            hoverSound = GetComponent<AudioSource>();
        }

        // Cache any available clip as the project-wide default hover clip
        if (hoverSoundClip != null && defaultHoverClip == null)
        {
            defaultHoverClip = hoverSoundClip;
        }
        else if (hoverSound != null && hoverSound.clip != null && defaultHoverClip == null)
        {
            defaultHoverClip = hoverSound.clip;
        }

        // If no AudioSource exists on this GameObject, add one if a clip or fallback exists
        if (hoverSound == null && (hoverSoundClip != null || defaultHoverClip != null))
        {
            hoverSound = gameObject.AddComponent<AudioSource>();
            hoverSound.playOnAwake = false;
        }

        if (hoverSound != null)
        {
            hoverSound.playOnAwake = false;
            hoverSound.volume = hoverVolume;

            if (hoverSound.clip == null)
            {
                if (hoverSoundClip != null)
                {
                    hoverSound.clip = hoverSoundClip;
                }
                else if (defaultHoverClip != null)
                {
                    hoverSound.clip = defaultHoverClip;
                }
            }
        }
    }

    private void Update()
    {
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            targetScale,
            animationSpeed * Time.unscaledDeltaTime
        );
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        targetScale = originalScale * hoverScale;

        EnsureAudioSource();
        if (hoverSound != null && hoverSound.clip != null)
        {
            hoverSound.volume = hoverVolume;
            hoverSound.Play();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        targetScale = originalScale;

        if (hoverSound != null && hoverSound.isPlaying)
        {
            hoverSound.Stop();
        }
    }
}