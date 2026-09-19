
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private Transform head;
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float turnSpeed = 10f;

    private void Update()
    {
        MoveForward();
        TurnHeadWithMouse();
    }

    private void MoveForward()
    {
        // Move the entire snake in the direction the head is facing
        transform.position += head.forward * moveSpeed * Time.deltaTime;
    }

    private void TurnHeadWithMouse()
    {
        if (Mouse.current == null || head == null)
            return;

        // Get mouse position on the screen
        Vector2 mousePosition = Mouse.current.position.ReadValue();

        // Shoot a ray from the camera through the mouse
        Ray ray = Camera.main.ScreenPointToRay(mousePosition);

        // Invisible horizontal ground plane at Y = 0
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

        if (groundPlane.Raycast(ray, out float distance))
        {
            // Get the mouse's position in the game world
            Vector3 mouseWorldPosition = ray.GetPoint(distance);

            // Direction from the head toward the mouse
            Vector3 direction = mouseWorldPosition - head.position;

            // Only rotate around the Y axis
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.01f)
            {
                // Calculate the rotation toward the mouse
                Quaternion targetRotation = Quaternion.LookRotation(direction);

                // Smoothly rotate the head
                head.rotation = Quaternion.Slerp(
                    head.rotation,
                    targetRotation,
                    turnSpeed * Time.deltaTime
                );
            }
        }
    }
}