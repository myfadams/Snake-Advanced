using UnityEngine;
using UnityEngine.EventSystems;

public class ButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private float hoverScale = 1.05f;
    [SerializeField] private float animationSpeed = 10f;
    private AudioSource hoverSound;
    

    private Vector3 originalScale;
    private Vector3 targetScale;

    private void Awake()
    {
        originalScale = transform.localScale;
        targetScale = originalScale;
        hoverSound=gameObject.GetComponent<AudioSource>();
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
        hoverSound.Play();
        
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        targetScale = originalScale;
        hoverSound.Stop();
    }
}