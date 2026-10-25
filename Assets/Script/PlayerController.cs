using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Moves the player on the casino floor with WASD / arrow keys, or by clicking where to go
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float turnSpeed = 720f;          // Degrees per second when turning to face a direction
    [SerializeField] private float stopDistance = 0.2f;       // How close to a click target counts as "arrived"
    [SerializeField] private bool moveWithLeftClick = true;   // Right click always works, like TFT
    [SerializeField] private Camera viewCamera;               // Leave empty to use the main camera

    [Header("Walkable Area (X and Z)")]
    [SerializeField] private Vector2 areaMin = new Vector2(-9.5f, -5.5f);
    [SerializeField] private Vector2 areaMax = new Vector2(9.5f, 5.5f);

    private Rigidbody body;
    private Vector2 defaultAreaMin;   // The casino floor's walkable area, to restore after leaving a table
    private Vector2 defaultAreaMax;
    private Vector2 keyboardInput;

    // The "?" means this can also be null: no click target right now
    private Vector3? clickTarget;

    // Other scripts can switch movement off, for example while sitting at a table
    public bool CanMove { get; set; } = true;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        defaultAreaMin = areaMin;
        defaultAreaMax = areaMax;
        if (viewCamera == null)
        {
            viewCamera = Camera.main;
        }
    }

    // Update runs every frame: the best place to read input
    private void Update()
    {
        if (!CanMove)
        {
            keyboardInput = Vector2.zero;
            clickTarget = null;
            return;
        }

        keyboardInput = ReadKeyboard();

        if (keyboardInput != Vector2.zero)
        {
            clickTarget = null;
        }
        else if (WasMoveClickPressed() && TryGetMouseGroundPoint(out Vector3 groundPoint))
        {
            clickTarget = groundPoint;
        }
    }

    // FixedUpdate runs at a steady rate: the best place to move physics objects
    private void FixedUpdate()
    {
        // While hopping, the hop moves the player instead of physics
        if (body.isKinematic)
        {
            return;
        }
        // Cancel any spin caused by collisions. Only this script should turn the player.
        body.angularVelocity = Vector3.zero;

        Vector3 direction = Vector3.zero;

        if (keyboardInput != Vector2.zero)
        {
            direction = GetCameraRelativeDirection(keyboardInput);
        }
        else if (clickTarget.HasValue)
        {
            Vector3 toTarget = clickTarget.Value - body.position;
            toTarget.y = 0f;   // Only care about distance across the floor

            if (toTarget.magnitude <= stopDistance)
            {
                clickTarget = null;
            }
            else
            {
                direction = toTarget.normalized;
            }
        }

        // Move across the floor but keep the current vertical speed (gravity)
        Vector3 velocity = direction * moveSpeed;
        velocity.y = body.linearVelocity.y;
        body.linearVelocity = velocity;

        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime));
        }

        KeepInsideArea();
    }

    private Vector2 ReadKeyboard()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return Vector2.zero;
        }

        float x = 0f;
        float y = 0f;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) y += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) y -= 1f;

        return new Vector2(x, y);
    }

    // W always means "away from the camera", whichever way the camera is facing
    private Vector3 GetCameraRelativeDirection(Vector2 input)
    {
        Vector3 forward = viewCamera.transform.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 right = viewCamera.transform.right;
        right.y = 0f;
        right.Normalize();

        return (forward * input.y + right * input.x).normalized;
    }

    private bool WasMoveClickPressed()
    {
        Mouse mouse = Mouse.current;

        if (mouse == null)
        {
            return false;
        }

        if (mouse.rightButton.wasPressedThisFrame)
        {
            return true;
        }

        // Ignore left clicks on buttons and other UI
        return moveWithLeftClick && mouse.leftButton.wasPressedThisFrame && !IsPointerOverUI();
    }

    private bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    // Shoot an invisible ray from the camera through the mouse, and find where it hits the floor
    private bool TryGetMouseGroundPoint(out Vector3 point)
    {
        Ray ray = viewCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane ground = new Plane(Vector3.up, Vector3.zero);

        if (ground.Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }

        point = Vector3.zero;
        return false;
    }

    // Stop the player from walking off the floor
    private void KeepInsideArea()
    {
        Vector3 position = body.position;
        Vector3 clamped = new Vector3(
            Mathf.Clamp(position.x, areaMin.x, areaMax.x),
            position.y,
            Mathf.Clamp(position.z, areaMin.y, areaMax.y));

        if (clamped != position)
        {
            body.position = clamped;
            clickTarget = null;
        }
    }

    
    // Used when the chibi moves onto a giant table, and back to the casino floor
    public void SetWalkArea(Vector2 min, Vector2 max)
    {
        areaMin = min;
        areaMax = max;
    }

    public void ResetWalkArea()
    {
        areaMin = defaultAreaMin;
        areaMax = defaultAreaMax;
    }

    // Move instantly, for example while the screen is black
    public void TeleportTo(Vector3 position)
    {
        body.position = position;
        transform.position = position;
        body.linearVelocity = Vector3.zero;
        clickTarget = null;
    }

    // A small jump in an arc. Physics is paused (kinematic) so it doesn't fight the jump.
    public IEnumerator HopTo(Vector3 target, float height, float duration)
    {
        body.isKinematic = true;
        Vector3 start = transform.position;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float progress = t / duration;
            Vector3 position = Vector3.Lerp(start, target, progress);
            position.y += Mathf.Sin(progress * Mathf.PI) * height;
            transform.position = position;
            yield return null;
        }

        transform.position = target;
        body.isKinematic = false;
        body.linearVelocity = Vector3.zero;
    }
}