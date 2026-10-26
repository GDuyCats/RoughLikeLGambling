using UnityEngine;

// A big physics die. It is thrown onto the table, rolls freely, and the face pointing up is the result.
// It shoves the chibi aside, or squashes it if it lands on its head.
[RequireComponent(typeof(Rigidbody))]
public class TableDie : MonoBehaviour
{
    [SerializeField] private Transform[] faces = new Transform[6];   // faces[0] shows 1 pip, ... faces[5] shows 6
    [SerializeField] private float settledSpeed = 0.05f;             // Below this speed the die counts as stopped
    [SerializeField] private float shoveStrength = 1.2f;             // How hard the die shoves the chibi
    [SerializeField] private float maxShove = 8f;

    private Rigidbody body;

    public bool IsSettled =>
        body.isKinematic
        || (body.linearVelocity.magnitude < settledSpeed && body.angularVelocity.magnitude < settledSpeed);

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    // Place the die showing a value, frozen in place (no physics)
    public void ShowValue(int value, Vector3 position)
    {
        body.isKinematic = true;
        transform.position = position;
        transform.rotation = RotationShowing(value);
    }

    // Throw from a point with a push and a spin. Physics does the rest.
    public void Throw(Vector3 from, Vector3 push, Vector3 spin)
    {
        body.isKinematic = false;

        Quaternion startRotation = Random.rotation;
        transform.SetPositionAndRotation(from, startRotation);
        body.position = from;
        body.rotation = startRotation;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.AddForce(push, ForceMode.Impulse);
        body.AddTorque(spin, ForceMode.Impulse);
    }

    // The dealer kicked the table: the die jumps up and tumbles
    public void Kick(float strength)
    {
        body.isKinematic = false;
        Vector2 sideways = Random.insideUnitCircle * strength * 0.5f;
        body.AddForce(new Vector3(sideways.x, strength, sideways.y), ForceMode.Impulse);
        body.AddTorque(Random.onUnitSphere * strength, ForceMode.Impulse);
    }

    // The value on top, and how flat the die is lying (1 = perfectly flat)
    public int ReadTopValue(out float flatness)
    {
        int bestValue = 1;
        flatness = -1f;

        for (int i = 0; i < faces.Length; i++)
        {
            // Direction from the die's center to this face, in the world
            Vector3 faceDirection = transform.TransformDirection(faces[i].localPosition.normalized);

            // Dot: 1 if the face points straight up, 0 if sideways, -1 if down
            float upness = Vector3.Dot(faceDirection, Vector3.up);

            if (upness > flatness)
            {
                flatness = upness;
                bestValue = i + 1;
            }
        }

        return bestValue;
    }

    // Unity calls this when the die bumps into something
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.rigidbody == null || !collision.rigidbody.TryGetComponent(out PlayerController player))
        {
            return;
        }

        // Landed on the head: the contact is high up on the chibi, and the die is above it
        Vector3 contact = collision.GetContact(0).point;
        bool onHead = contact.y > player.transform.position.y + 0.3f
                      && transform.position.y > player.transform.position.y;

        if (onHead)
        {
            player.Squash();
            return;
        }

        // Hit from the side: shove the chibi away, harder if the die was moving fast
        Vector3 away = player.transform.position - transform.position;
        away.y = 0f;
        float strength = Mathf.Min(collision.relativeVelocity.magnitude * shoveStrength, maxShove);
        player.Knockback(away.normalized * strength);
        player.Daze(0.4f);
    }

    private Quaternion RotationShowing(int value)
    {
        Vector3 faceDirection = faces[value - 1].localPosition.normalized;
        Quaternion faceUp = Quaternion.FromToRotation(faceDirection, Vector3.up);
        return Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up) * faceUp;
    }
}