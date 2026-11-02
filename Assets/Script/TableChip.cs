using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A big poker chip lying on the table. The chibi can pick it up (E) and throw it (E again).
// A thrown chip that hits a table button hard enough presses it.
// Chips belong to someone: the player's own pile (see PlayerChipPile), or the dealer's tray.
// Taking a chip from the dealer's tray is stealing, and the dealer gathers it back after a while.
[RequireComponent(typeof(Rigidbody))]
public class TableChip : MonoBehaviour
{
    private static readonly List<Collider> railColliders = new List<Collider>();
    private static bool railsFound;

    [SerializeField] private Renderer chipRenderer;
    [SerializeField] private Material[] colorChoices;       // One is picked at random, like real casino chips
    [SerializeField] private float pressImpactSpeed = 4f;   // A thrown chip must hit a button at least this fast
    [SerializeField] private float fallLimit = -3f;         // This far below its starting height, the chip fell off the table
    [SerializeField] private float pickupDelay = 0.6f;      // After a throw, the chip can't be caught again for this long
    [SerializeField] private float maxPickupSpeed = 3f;     // A chip still flying or rolling faster than this can't be picked up
    [SerializeField] private float minDealerHitSpeed = 2f;  // A chip must hit a dealer at least this fast to bother them
    [SerializeField] private float minClinkSpeed = 0.8f;    // Slower bumps than this make no sound
    [SerializeField] private float loudClinkSpeed = 8f;     // Bumps this fast (or faster) play at full volume

    [Header("Dealer's Chips")]
    [SerializeField] private bool belongsToDealer;          // Part of the dealer's tray: taking it is stealing
    [SerializeField] private float dealerReturnDelay = 5f;  // A stolen chip lying still this long flies back to the tray

    private Rigidbody body;
    private Collider chipCollider;
    private RigidbodyInterpolation normalInterpolation;
    private Transform holder;          // The chibi's hold point while the chip is carried
    private Vector3 homePosition;
    private Quaternion homeRotation;
    private bool isThrown;             // Flying from a throw, until it first hits something
    private float pickupBlockedUntil;
    private float restingAwaySince = -1f;
    private bool isFlying;

    public bool IsHeld { get; private set; }
    public PlayerChipPile Pile { get; set; }   // Set for the player's own chips
    public int Value { get; set; }             // What a player's chip is worth

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

        // The rails' tall invisible walls are only there to keep the dice in.
        // Thrown chips fly over them (to reach the dealer behind the table).
        foreach (Collider rail in GetRailColliders())
        {
            if (rail != null && rail != chipCollider)
            {
                Physics.IgnoreCollision(chipCollider, rail, true);
            }
        }

        if (chipRenderer != null && colorChoices.Length > 0)
        {
            chipRenderer.sharedMaterial = colorChoices[Random.Range(0, colorChoices.Length)];
        }
    }

    private void Update()
    {
        if (isFlying)
        {
            return;
        }

        // Fell off the table: the player's chips go back to their pile, others back where they started
        if (!IsHeld && transform.position.y < homePosition.y + fallLimit)
        {
            ReturnHome();
            return;
        }

        if (belongsToDealer)
        {
            WatchForDealerReturn();
        }
    }

    // The rails sit on the "Ignore Raycast" layer. They are looked up once, not for every new chip.
    private static List<Collider> GetRailColliders()
    {
        if (!railsFound || railColliders.Exists(rail => rail == null))
        {
            railColliders.Clear();
            int railLayer = LayerMask.NameToLayer("Ignore Raycast");

            foreach (Collider other in FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (other.gameObject.layer == railLayer)
                {
                    railColliders.Add(other);
                }
            }

            railsFound = true;
        }

        return railColliders;
    }

    // A stolen chip lying still away from the tray: after a while the dealer takes it back
    private void WatchForDealerReturn()
    {
        bool away = (transform.position - homePosition).sqrMagnitude > 0.25f;
        bool resting = !IsHeld && (body.isKinematic || body.linearVelocity.sqrMagnitude < 0.04f);

        if (!away || !resting)
        {
            restingAwaySince = -1f;
            return;
        }

        if (restingAwaySince < 0f)
        {
            restingAwaySince = Time.time;
        }
        else if (Time.time - restingAwaySince > dealerReturnDelay)
        {
            StartCoroutine(FlyBackToTray());
        }
    }

    private IEnumerator FlyBackToTray()
    {
        isFlying = true;
        restingAwaySince = -1f;
        body.isKinematic = true;
        chipCollider.enabled = false;

        Vector3 from = transform.position;
        const float flightTime = 0.5f;

        for (float t = 0f; t < flightTime; t += Time.deltaTime)
        {
            float progress = t / flightTime;
            transform.position = Vector3.Lerp(from, homePosition, progress) + Vector3.up * Mathf.Sin(progress * Mathf.PI) * 1.5f;
            yield return null;
        }

        isFlying = false;
        ReturnHome();
    }

    // A chip in the dealer's rack: taking it is stealing, and it flies back to the rack after a while
    public void MakeDealerChip()
    {
        belongsToDealer = true;
    }

    // Use this look instead of a random color (the player's chips are colored by value)
    public void SetLook(Material material)
    {
        if (chipRenderer != null && material != null)
        {
            chipRenderer.sharedMaterial = material;
        }
    }

    // Lying in a pile: physics off so the pile never topples, but it can still be bumped into and picked up
    public void HoldStill()
    {
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.None;
        homePosition = transform.position;
        homeRotation = transform.rotation;
    }

    // Just a moving picture from now on: no physics, can't be picked up (for chips flying around the table)
    public void MakeDecoration()
    {
        StopAllCoroutines();
        IsHeld = false;
        holder = null;
        isThrown = false;
        isFlying = true;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.None;
        chipCollider.enabled = false;
        enabled = false;
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
        AudioManager.Play(SoundId.ChipPickup);

        if (Pile != null)
        {
            Pile.OnChipTaken(this);
        }

        if (belongsToDealer)
        {
            TellDealerAboutTheft();
        }
    }

    // The nearest dealer saw that
    private void TellDealerAboutTheft()
    {
        TableDealer nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (TableDealer dealer in FindObjectsByType<TableDealer>(FindObjectsSortMode.None))
        {
            float distance = (dealer.transform.position - transform.position).sqrMagnitude;

            if (distance < nearestDistance)
            {
                nearest = dealer;
                nearestDistance = distance;
            }
        }

        if (nearest != null)
        {
            nearest.OnChipStolen();
        }
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
        AudioManager.Play(SoundId.ChipThrow);
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

    // Back to where the chip started, for example after falling off the table.
    // The player's own chips go back to their pile instead.
    public void ReturnHome()
    {
        if (Pile != null)
        {
            IsHeld = false;
            holder = null;
            Pile.OnChipLost(this);
            return;
        }

        StopAllCoroutines();
        isFlying = false;
        restingAwaySince = -1f;
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
        // Clink! Louder when the chip hits harder. Tiny bumps (like a stack settling) make no sound.
        float impact = collision.relativeVelocity.magnitude;

        if (impact > minClinkSpeed)
        {
            AudioManager.Play(SoundId.ChipHit, Mathf.Clamp01(impact / loudClinkSpeed));
        }

        // A dealer hit on the head flinches and grumbles (just for fun, it changes nothing).
        // This counts even if the chip grazed something on the way.
        TableDealer dealer = collision.collider.GetComponentInParent<TableDealer>();

        if (dealer != null)
        {
            if (impact > minDealerHitSpeed)
            {
                dealer.OnChipHit();
            }

            isThrown = false;
            return;
        }

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