using System.Collections;
using UnityEngine;

// A playing card lying on a table. It can fly to a new spot and turn over.
public class TableCard : MonoBehaviour
{
    [SerializeField] private SpriteRenderer face;
    [SerializeField] private float moveTime = 0.4f;    // How long a card takes to fly to its spot
    [SerializeField] private float arcHeight = 1.5f;   // How high the card lifts while flying
    [SerializeField] private float flipTime = 0.25f;

    private Vector3 baseScale;

    private void Awake()
    {
        baseScale = face.transform.localScale;
    }

    public void SetSprite(Sprite sprite)
    {
        face.sprite = sprite;
    }

    // Fly in an arc to the target position
    public IEnumerator MoveTo(Vector3 target)
    {
        AudioManager.Play(SoundId.CardDeal);
        Vector3 start = transform.position;

        for (float t = 0f; t < moveTime; t += Time.deltaTime)
        {
            float progress = t / moveTime;
            Vector3 position = Vector3.Lerp(start, target, progress);

            // Sin goes 0 → 1 → 0 across the flight, so the card lifts in the middle
            position.y += Mathf.Sin(progress * Mathf.PI) * arcHeight;
            transform.position = position;
            yield return null;
        }

        transform.position = target;
    }

    // Squash the card, swap the picture, stretch it back: looks like turning it over
    public IEnumerator FlipTo(Sprite sprite)
    {
        AudioManager.Play(SoundId.CardFlip);
        float halfTime = flipTime / 2f;

        for (float t = 0f; t < halfTime; t += Time.deltaTime)
        {
            face.transform.localScale = new Vector3(baseScale.x * (1f - t / halfTime), baseScale.y, baseScale.z);
            yield return null;
        }

        face.sprite = sprite;

        for (float t = 0f; t < halfTime; t += Time.deltaTime)
        {
            face.transform.localScale = new Vector3(baseScale.x * (t / halfTime), baseScale.y, baseScale.z);
            yield return null;
        }

        face.transform.localScale = baseScale;
    }
}