using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Moves the chibi with WASD / arrow keys or by left clicking where to go.
// Left clicking a table button makes the chibi run over and stomp on it.
// Space jumps; Space again in the air smashes down (presses a button underneath and shoves things nearby).
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

    [Header("Jump and Smash")]
    [SerializeField] private float jumpSpeed = 9f;
    [SerializeField] private float extraGravity = 15f;         // Added to normal gravity so jumps feel snappy, not floaty
    [SerializeField] private float groundCheckDistance = 0.15f;
    [SerializeField] private float smashHangTime = 0.12f;      // The chibi freezes in the air for a moment before smashing
    [SerializeField] private float smashSpeed = 25f;
    [SerializeField] private float smashRadius = 3f;           // Loose things inside this circle get shoved
    [SerializeField] private float smashPushForce = 6f;
    [SerializeField] private CinemachineImpulseSource smashShake;
    [SerializeField] private GameObject smashRingPrefab;       // A flat ring that grows and fades on landing (optional)

    [Header("Chips")]
    [SerializeField] private Transform holdPoint;              // Above the head: where a picked-up chip sits
    [SerializeField] private float pickupRange = 1.8f;
    [SerializeField] private float throwHorizontalSpeed = 12f; // Roughly how fast a thrown chip travels across the table

    [Header("Hit Reactions")]
    [SerializeField] private Transform squashPivot;            // The visual is squashed around the feet
    [SerializeField] private float squashAmount = 0.3f;        // Height while squashed (1 = normal)
    [SerializeField] private float squashTime = 1f;
    [SerializeField] private float dizzyTime = 0.8f;
    [SerializeField] private float dazeTime = 0.8f;            // How long a bump from the side leaves the chibi dizzy
    [SerializeField] private float stunImmunityTime = 0.6f;    // After recovering, new bumps can't stun again for this long
    [SerializeField] private float dizzySoundDelay = 0.25f;    // The dizzy sound starts a moment after the hit, so the hit is heard first

    private Rigidbody body;
    private Collider ownCollider;
    private TableChip heldChip;       // The chip on the chibi's head, if any
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
    private bool isSquashed;          // Flattened right now (a bigger reaction than being dizzy)
    private float stunImmuneUntil;    // Bumps before this time shove the chibi but don't stun it again
    private AudioSource voiceSource;  // The chibi's own speaker, so its dizzy sound never overlaps and can be stopped

    private bool spacePressed;        // Read in Update, used in FixedUpdate
    private bool isGrounded;
    private bool canSmash;            // True after a jump, until landing
    private float lastJumpTime;
    private bool isSmashing;
    private float smashHangUntil;

    // Other scripts can switch movement off, for example during a transition
    public bool CanMove { get; set; } = true;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        ownCollider = GetComponent<Collider>();
        halfHeight = ownCollider.bounds.extents.y;

        voiceSource = gameObject.AddComponent<AudioSource>();
        voiceSource.playOnAwake = false;
        voiceSource.spatialBlend = 0f;
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
        if (!CanMove || isStunned || isPressing || isSmashing)
        {
            keyboardInput = Vector2.zero;
            clickTarget = null;
            pressTarget = null;
            return;
        }

        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            spacePressed = true;
        }

        // E: throw the chip you hold, or pick one up
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (heldChip != null)
            {
                ThrowHeldChip();
            }
            else
            {
                TryPickUpChip();
            }
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

        isGrounded = CheckGrounded();

        if (isSmashing)
        {
            UpdateSmash();
            KeepInsideArea();
            return;
        }

        // Landing ends the chance to smash (the short delay stops a fresh jump from counting as landed)
        if (isGrounded && Time.time > lastJumpTime + 0.1f)
        {
            canSmash = false;
        }

        if (spacePressed)
        {
            spacePressed = false;

            if (isGrounded)
            {
                Jump();
            }
            else if (canSmash)
            {
                StartSmash();
                return;
            }
        }

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

        // Extra gravity in the air for a snappy jump
        if (!isGrounded)
        {
            body.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);
        }

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

    // ---------- Jump and smash ----------

    // A sphere just under the chibi's feet: touching anything solid means standing on something
    private bool CheckGrounded()
    {
        const float radius = 0.3f;
        float distance = halfHeight - radius + groundCheckDistance;

        return Physics.SphereCast(body.position, radius, Vector3.down, out _, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    private void Jump()
    {
        Vector3 velocity = body.linearVelocity;
        velocity.y = jumpSpeed;
        body.linearVelocity = velocity;

        isGrounded = false;
        canSmash = true;
        lastJumpTime = Time.time;
        AudioManager.Play(SoundId.Jump);
    }

    private void StartSmash()
    {
        isSmashing = true;
        canSmash = false;
        smashHangUntil = Time.time + smashHangTime;
        clickTarget = null;
        pressTarget = null;
        body.linearVelocity = Vector3.zero;
    }

    // Freeze in the air for a moment, then shoot straight down until something stops the fall
    private void UpdateSmash()
    {
        if (Time.time < smashHangUntil)
        {
            body.linearVelocity = Vector3.zero;
            return;
        }

        if (isGrounded)
        {
            body.linearVelocity = Vector3.zero;
            isSmashing = false;
            OnSmashLanded();
            return;
        }

        body.linearVelocity = Vector3.down * smashSpeed;
    }

    private void OnSmashLanded()
    {
        Vector3 feet = body.position + Vector3.down * halfHeight;
        AudioManager.Play(SoundId.Smash);

        // A table button right under the chibi gets pressed
        if (Physics.Raycast(body.position, Vector3.down, out RaycastHit hit, halfHeight + 0.5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            TableButton button = hit.collider.GetComponentInParent<TableButton>();

            if (button != null)
            {
                button.Press();
            }
        }

        // Loose things nearby (like dice) get shoved away. Each one only once.
        HashSet<Rigidbody> shoved = new HashSet<Rigidbody>();

        foreach (Collider nearby in Physics.OverlapSphere(feet, smashRadius))
        {
            Rigidbody other = nearby.attachedRigidbody;

            if (other == null || other == body || other.isKinematic || !shoved.Add(other))
            {
                continue;
            }

            other.AddExplosionForce(smashPushForce, feet, smashRadius, 0.5f, ForceMode.VelocityChange);
        }

        if (smashShake != null)
        {
            smashShake.GenerateImpulseWithForce(0.6f);
        }

        if (smashRingPrefab != null)
        {
            StartCoroutine(SmashRingRoutine(feet));
        }

        // A quick squish on landing, unless the chibi is already squashed or dizzy
        if (squashPivot != null && reactionRoutine == null)
        {
            StartCoroutine(LandingSquishRoutine());
        }
    }

    // A flat ring grows out from the landing spot and fades away
    private IEnumerator SmashRingRoutine(Vector3 feet)
    {
        GameObject ring = Instantiate(smashRingPrefab, feet + Vector3.up * 0.05f, Quaternion.identity);
        Renderer ringRenderer = ring.GetComponent<Renderer>();
        Color startColor = ringRenderer.material.color;
        float height = ring.transform.localScale.y;
        const float duration = 0.35f;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float progress = t / duration;
            float size = Mathf.Lerp(0.5f, smashRadius * 2f, progress);
            ring.transform.localScale = new Vector3(size, height, size);

            Color color = startColor;
            color.a = Mathf.Lerp(startColor.a, 0f, progress);
            ringRenderer.material.color = color;
            yield return null;
        }

        Destroy(ring);
    }

    private IEnumerator LandingSquishRoutine()
    {
        yield return ScaleVisual(new Vector3(1.3f, 0.6f, 1.3f), 0.05f);
        yield return ScaleVisual(Vector3.one, 0.12f);
    }

    // ---------- Chips ----------

    // Pick up the nearest chip in reach. From a stack, the one on top.
    private void TryPickUpChip()
    {
        if (holdPoint == null)
        {
            return;
        }

        TableChip best = null;

        foreach (Collider nearby in Physics.OverlapSphere(body.position, pickupRange))
        {
            TableChip chip = nearby.GetComponentInParent<TableChip>();

            if (chip == null || !chip.CanBePickedUp)
            {
                continue;
            }

            if (best == null || chip.transform.position.y > best.transform.position.y)
            {
                best = chip;
            }
        }

        if (best != null)
        {
            best.PickUp(holdPoint);
            heldChip = best;
        }
    }

    // Lob the chip in an arc so it lands where the mouse points
    private void ThrowHeldChip()
    {
        Vector3 start = heldChip.transform.position;

        if (!TryGetMouseWorldPoint(out Vector3 target))
        {
            target = body.position + transform.forward * 5f;
        }

        Vector3 flatDistance = target - start;
        flatDistance.y = 0f;
        float flightTime = Mathf.Clamp(flatDistance.magnitude / throwHorizontalSpeed, 0.35f, 1.2f);

        // The speed that reaches the target after flightTime seconds, with gravity pulling it down on the way
        Vector3 velocity = (target - start) / flightTime - 0.5f * Physics.gravity * flightTime;

        heldChip.Throw(velocity, ownCollider);
        heldChip = null;
    }

    // Where the mouse points: the first solid thing under it (like a button), or else the table surface
    private bool TryGetMouseWorldPoint(out Vector3 point)
    {
        Ray ray = viewCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (Physics.Raycast(ray, out RaycastHit hit, 500f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && hit.collider != ownCollider)
        {
            point = hit.point;
            return true;
        }

        return TryGetMouseGroundPoint(out point);
    }

    // ---------- Input helpers ----------

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

    // Shoot an invisible ray from the camera through the mouse, and see if it passes through a table button.
    // The ray looks through everything in the way (like the chibi standing in front of the button),
    // so a button you can see is a button you can click.
    private bool TryGetClickedButton(out TableButton button)
    {
        button = null;
        Ray ray = viewCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit[] hits = Physics.RaycastAll(ray, 500f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        float closest = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == ownCollider || hit.distance >= closest)
            {
                continue;
            }

            TableButton found = hit.collider.GetComponentInParent<TableButton>();

            if (found != null && found.CanPress)
            {
                button = found;
                closest = hit.distance;
            }
        }

        return button != null;
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

        // Physics speed can only be set while physics is on
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
        }

        knockbackVelocity = Vector3.zero;
        clickTarget = null;
        pressTarget = null;
        isSmashing = false;
        canSmash = false;

        // Leaving the table: a held chip goes back to where it came from
        if (heldChip != null)
        {
            heldChip.ReturnHome();
            heldChip = null;
        }
    }

    // A small jump in an arc. Physics is paused (kinematic) so it doesn't fight the jump.
    public IEnumerator HopTo(Vector3 target, float height, float duration)
    {
        AudioManager.Play(SoundId.TableHop);
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
        body.position = target;
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
        // Already flattened, or just recovered: no new reaction
        if (isSquashed || Time.time < stunImmuneUntil)
        {
            return;
        }

        AudioManager.Play(SoundId.Squash);
        StartReaction(SquashRoutine());
    }

    // A die bumped into the chibi from the side: a dizzy wobble
    public void Daze()
    {
        // Already stunned, or just recovered: the push still happens, but no new stun and no new sound
        if (isStunned || Time.time < stunImmuneUntil)
        {
            return;
        }

        AudioManager.Play(SoundId.Knockback);
        StartReaction(DizzyRoutine(dazeTime));
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

        // A new reaction cuts off the old one's dizzy sound
        voiceSource.Stop();
        squashPivot.localScale = Vector3.one;
        squashPivot.localRotation = Quaternion.identity;
        reactionRoutine = StartCoroutine(routine);
    }

    private IEnumerator SquashRoutine()
    {
        isStunned = true;
        isSquashed = true;
        clickTarget = null;
        pressTarget = null;

        // Flatten fast, and get wider like a squashed rubber toy
        yield return ScaleVisual(new Vector3(1.4f, squashAmount, 1.4f), 0.08f);
        yield return new WaitForSeconds(squashTime);

        // Pop back up, a little too tall at first, then settle
        yield return ScaleVisual(new Vector3(0.85f, 1.3f, 0.85f), 0.1f);
        yield return ScaleVisual(Vector3.one, 0.1f);

        isSquashed = false;
        yield return DizzyRoutine(dizzyTime);
    }

    private IEnumerator DizzyRoutine(float duration)
    {
        isStunned = true;
        clickTarget = null;
        pressTarget = null;
        bool soundStarted = false;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            // Hit first, then the dizzy birds. Played on the chibi's own speaker,
            // so a new dizzy sound replaces the old one instead of piling up.
            if (!soundStarted && t >= dizzySoundDelay)
            {
                soundStarted = true;
                AudioManager.PlayOn(voiceSource, SoundId.Dizzy);
            }

            // Wobble in a little circle, like stars spinning around the head
            squashPivot.localRotation = Quaternion.Euler(Mathf.Sin(t * 20f) * 10f, 0f, Mathf.Cos(t * 17f) * 10f);
            yield return null;
        }

        // Recovered: the wobble, the stun and the dizzy sound all end together
        squashPivot.localRotation = Quaternion.identity;
        voiceSource.Stop();
        isStunned = false;
        isSquashed = false;
        stunImmuneUntil = Time.time + stunImmunityTime;
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