using System.Collections;
using UnityEngine;

// A black screen that fades in and out for transitions
[RequireComponent(typeof(CanvasGroup))]
public class ScreenFader : MonoBehaviour
{
    private CanvasGroup group;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
    }

    // Screen goes black (also blocks clicks while black)
    public IEnumerator FadeOut(float duration)
    {
        group.blocksRaycasts = true;
        yield return Fade(0f, 1f, duration);
    }

    // Screen comes back
    public IEnumerator FadeIn(float duration)
    {
        yield return Fade(1f, 0f, duration);
        group.blocksRaycasts = false;
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }

        group.alpha = to;
    }
}