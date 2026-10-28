using System.Collections;
using UnityEngine;

// A big poker chip lying on the table. The chibi can pick it up (E) and throw it (E again).
// A thrown chip that hits a table button hard enough presses it.
[RequireComponent(typeof(Rigidbody))]
public class TableChip : MonoBehaviour
{
    [SerializeField] private Renderer chipRenderer;
    [SerializeField] private Material[] colorChoices;       // One is picked at random, like real casino chips
    [SerializeField] private float pressImpactSpeed = 4f;   // A thrown chip must hit a button at least this fast
    [SerializeField] private float fallLimit = -3f;         // This far below its starting height, the chip fell off the table
    [SerializeField] private float pickupDelay = 0.6f;      // After a throw, the chip can't be caught again for this long
    [SerializeField] private float maxPickupSpeed = 3f;     // A chip still flying or rolling faster than this can't be picked up

    private Rigidbody body;
    private Collider chipCollider;
    private RigidbodyInterpolation normalInterpolation;
    private Transform holder;          // The chibi's hold point while the chip is carried
    private Vector3 homePosition;
    private Quaternion homeRotation;
    private bool isThrown;             // Flying from a throw, until it first hits something
    private float pickupBlockedUntil;

    public bool IsHeld { get; private set; }

    // Lying still (or nearly) and not just thrown
    public bool CanBePickedUp =>
        !IsHeld
        && Time.time >= pickupBlockedUntil
        && (body.isKinematic || body.linearVelocity.magnitude < maxPickupSpeed);

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        chipCollider = GetComponent<Collider>();
        normalInterpolation = body.interpolation;
        homePosition = transform.position;
        homeRotation = transform.rotation;

        if (chipRenderer != null && colorChoices.Length > 0)
        {
            chipRenderer.sharedMaterial = colorChoices[Random.Range(0, colorChoices.Length)];
        }
    }

    private void Update()
    {
        // Fell off the table: put it back where it started
        if (!IsHeld && transform.position.y < homePosition.y + fallLimit)
        {
            ReturnHome();
        }
    }

    // LateUpdate runs after the chibi has moved this frame, so a carried chip sticks exactly to its head
    private void LateUpdate()
    {
        if (IsHeld && holder != null)
        {
            transform.SetPositionAndRotation(holder.position, holder.rotation);
        }
    }

    // Sit on top of the chibi's head, with physics off
    public void PickUp(Transform holdPoint)
    {
        IsHeld = true;
        isThrown = false;
        holder = holdPoint;

        // Physics off while carried. Interpolation off too, or it would keep pulling the chip back to its old spot.
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.None;
        chipCollider.enabled = false;

        transform.SetPositionAndRotation(holder.position, holder.rotation);
    }

    // Fly off with this speed and direction. The thrower is ignored for a moment so the chip doesn't hit them.
    public void Throw(Vector3 velocity, Collider thrower)
    {
        IsHeld = false;
        holder = null;
        pickupBlockedUntil = Time.time + pickupDelay;

        chipCollider.enabled = true;
        body.isKinematic = false;
        body.interpolation = normalInterpolation;
        body.position = transform.position;
        isThrown = true;

        if (thrower != null)
        {
            StartCoroutine(IgnoreThrowerForAMoment(thrower));
        }

        body.linearVelocity = velocity;
        body.angularVelocity = Random.onUnitSphere * 5f;
    }

    private IEnumerator IgnoreThrowerForAMoment(Collider thrower)
    {
        Physics.IgnoreCollision(chipCollider, thrower, true);
        yield return new WaitForSeconds(0.3f);

        if (thrower != null)
        {
            Physics.IgnoreCollision(chipCollider, thrower, false);
        }
    }

    // Back to where the chip started, for example after falling off the table
    public void ReturnHome()
    {
        StopAllCoroutines();
        IsHeld = false;
        holder = null;
        isThrown = false;
        chipCollider.enabled = true;
        body.isKinematic = false;
        body.interpolation = normalInterpolation;

        transform.SetPositionAndRotation(homePosition, homeRotation);
        body.position = homePosition;
        body.rotation = homeRotation;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    // Unity calls this when the chip bumps into something
    private void OnCollisionEnter(Collision collision)
    {
        if (!isThrown)
        {
            return;
        }

        // Only the first thing it hits after a throw counts
        isThrown = false;

        if (collision.relativeVelocity.magnitude < pressImpactSpeed)
        {
            return;
        }

        TableButton button = collision.collider.GetComponentInParent<TableButton>();

        if (button != null)
        {
            button.Press();
        }
    }
}