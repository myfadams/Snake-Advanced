
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
            // Remember the camera's starting position relative to the player
            offset = transform.position - player.position;
        }
    }

    private void LateUpdate()
    {
        if (player == null)
            return;

        // Follow the Player's position
        Vector3 targetPosition = player.position + offset;

        transform.position = Vector3.Lerp(
            transform.position,
            targetPosition,
            smoothSpeed * Time.deltaTime
        );
    }
}
