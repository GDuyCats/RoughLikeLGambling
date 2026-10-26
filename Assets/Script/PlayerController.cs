using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Moves the chibi with WASD / arrow keys or by left clicking where to go.
// Left clicking a table button makes the chibi run over and stomp on it.
// Also handles hops onto tables, pushes from dice, and getting squashed or dizzy.
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float turnSpeed = 720f;           // Degrees per second when turning to face a direction
    [SerializeField] private float stopDistance = 0.2f;        // How close to a click target counts as "arrived"
    [SerializeField] private float knockbackDecay = 12f;       // How fast a push from a die fades
    [SerializeField] private bool moveWithLeftClick = true;
    [SerializeField] private bool moveWithRightClick = false;  // Off: right click is reserved for parrying
    [SerializeField] private Camera viewCamera;                // Leave empty to use the main camera

    [Header("Walkable Area (X and Z)")]
    [SerializeField] private Vector2 areaMin = new Vector2(-9.5f, -5.5f);
    [SerializeField] private Vector2 areaMax = new Vector2(9.5f, 5.5f);

    [Header("Table Buttons")]
    [SerializeField] private float pressStartDistance = 2.2f;  // Distance from the button's center where the chibi hops on (the button blocks walking)
    [SerializeField] private float pressHopHeight = 1.2f;
    [SerializeField] private float pressHopTime = 0.3f;

    [Header("Hit Reactions")]
    [SerializeField] private Transform squashPivot;            // The visual is squashed around the feet
    [SerializeField] private float squashAmount = 0.3f;        // Height while squashed (1 = normal)
    [SerializeField] private float squashTime = 1f;
    [SerializeField] private float dizzyTime = 0.8f;

    private Rigidbody body;
    private float halfHeight;         // Half the chibi's height, so it lands with its feet on top of things
    private Vector2 defaultAreaMin;   // The casino floor's walkable area, restored after leaving a table
    private Vector2 defaultAreaMax;
    private Vector2 keyboardInput;
    private Vector3? clickTarget;     // The "?" means this can be null: no click target right now
    private TableButton pressTarget;  // The button the chibi is running to, if any
    private Vector3 knockbackVelocity;
    private bool isStunned;           // Squashed or dizzy: can't walk for a moment
    private bool isPressing;          // Busy hopping onto a button
    private Coroutine reactionRoutine;

    // Other scripts can switch movement off, for example during a transition
    public bool CanMove { get; set; } = true;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        halfHeight = GetComponent<Collider>().bounds.extents.y;
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
        if (!CanMove || isStunned || isPressing)
        {
            keyboardInput = Vector2.zero;
            clickTarget = null;
            pressTarget = null;
            return;
        }

        keyboardInput = ReadKeyboard();

        if (keyboardInput != Vector2.zero)
        {
            // Walking with the keyboard cancels any click target, including a button
            clickTarget = null;
            pressTarget = null;
        }
        else if (WasMoveClickPressed())
        {
            HandleClick();
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
            toTarget.y = 0f;

            if (pressTarget != null && !pressTarget.CanPress)
            {
                // The button got locked while running (for example the hand ended): give up
                pressTarget = null;
                clickTarget = null;
            }
            else if (pressTarget != null && toTarget.magnitude <= pressStartDistance)
            {
                // Close enough: hop onto the button
                StartCoroutine(PressRoutine(pressTarget));
                pressTarget = null;
                clickTarget = null;
            }
            else if (toTarget.magnitude <= stopDistance)
            {
                clickTarget = null;
            }
            else
            {
                direction = toTarget.normalized;
            }
        }

        // Walking, plus any push from a die (which fades out quickly)
        Vector3 velocity = direction * moveSpeed + knockbackVelocity;
        velocity.y = body.linearVelocity.y;
        body.linearVelocity = velocity;
        knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, knockbackDecay * Time.fixedDeltaTime);

        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime));
        }

        KeepInsideArea();
    }

    // A click on a table button means "go press it". A click anywhere else means "walk there".
    private void HandleClick()
    {
        if (TryGetClickedButton(out TableButton button))
        {
            pressTarget = button;
            Vector3 buttonPosition = button.transform.position;
            clickTarget = new Vector3(buttonPosition.x, body.position.y, buttonPosition.z);
        }
        else if (TryGetMouseGroundPoint(out Vector3 groundPoint))
        {
            pressTarget = null;
            clickTarget = groundPoint;
        }
    }

    // Hop up onto the top of the button, and press it when landing.
    // Afterwards the chibi stands on the button; walking off the edge, gravity brings it down.
    private IEnumerator PressRoutine(TableButton button)
    {
        isPressing = true;

        Vector3 top = button.TopCenter + Vector3.up * halfHeight;
        yield return HopTo(top, pressHopHeight, pressHopTime);

        button.Press();
        isPressing = false;
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

        if (moveWithRightClick && mouse.rightButton.wasPressedThisFrame)
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

    // Shoot an invisible ray from the camera through the mouse, and see if it hits a table button
    private bool TryGetClickedButton(out TableButton button)
    {
        button = null;
        Ray ray = viewCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

        // "Collide" lets the ray hit trigger colliders too, which is what the buttons use
        if (Physics.Raycast(ray, out RaycastHit hit, 500f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            button = hit.collider.GetComponentInParent<TableButton>();
        }

        return button != null && button.CanPress;
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

    // Stop the player from walking off the floor or the table
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
            pressTarget = null;
        }
    }

    // ---------- Tables ----------

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
        knockbackVelocity = Vector3.zero;
        clickTarget = null;
        pressTarget = null;
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

    // ---------- Hit reactions ----------

    // Something (like a rolling die) shoves the chibi across the table
    public void Knockback(Vector3 push)
    {
        push.y = 0f;
        knockbackVelocity += push;
    }

    // A die landed on the chibi's head: flattened like a pancake, then pops back up and wobbles
    public void Squash()
    {
        StartReaction(SquashRoutine());
    }

    // A die bumped into the chibi from the side: a short dizzy wobble
    public void Daze(float duration)
    {
        StartReaction(DizzyRoutine(duration));
    }

    private void StartReaction(IEnumerator routine)
    {
        if (squashPivot == null)
        {
            return;
        }

        if (reactionRoutine != null)
        {
            StopCoroutine(reactionRoutine);
        }

        squashPivot.localScale = Vector3.one;
        squashPivot.localRotation = Quaternion.identity;
        reactionRoutine = StartCoroutine(routine);
    }

    private IEnumerator SquashRoutine()
    {
        isStunned = true;
        clickTarget = null;
        pressTarget = null;

        // Flatten fast, and get wider like a squashed rubber toy
        yield return ScaleVisual(new Vector3(1.4f, squashAmount, 1.4f), 0.08f);
        yield return new WaitForSeconds(squashTime);

        // Pop back up, a little too tall at first, then settle
        yield return ScaleVisual(new Vector3(0.85f, 1.3f, 0.85f), 0.1f);
        yield return ScaleVisual(Vector3.one, 0.1f);

        yield return DizzyRoutine(dizzyTime);
    }

    private IEnumerator DizzyRoutine(float duration)
    {
        isStunned = true;
        clickTarget = null;
        pressTarget = null;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            // Wobble in a little circle, like stars spinning around the head
            squashPivot.localRotation = Quaternion.Euler(Mathf.Sin(t * 20f) * 10f, 0f, Mathf.Cos(t * 17f) * 10f);
            yield return null;
        }

        squashPivot.localRotation = Quaternion.identity;
        isStunned = false;
        reactionRoutine = null;
    }

    private IEnumerator ScaleVisual(Vector3 target, float duration)
    {
        Vector3 start = squashPivot.localScale;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            squashPivot.localScale = Vector3.Lerp(start, target, t / duration);
            yield return null;
        }

        squashPivot.localScale = target;
    }
}