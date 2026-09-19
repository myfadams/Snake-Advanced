using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private float smoothSpeed = 5f;

    private Vector3 offset;

    private void Start()
    {
        if (player != null)
        {
            offset = transform.position - player.position;
        }
    }

    private void LateUpdate()
    {
        if (player == null)
            return;

        Vector3 targetPosition = player.position + offset;

        transform.position = Vector3.Lerp(
            transform.position,
            targetPosition,
            smoothSpeed * Time.deltaTime
        );
    }
}