using TMPro;
using UnityEngine;

// Bends a TextMeshPro text along an arc, like the words printed on a real blackjack table.
// The text lies flat on the felt. Its center sits on the arc's lowest point (closest to the player),
// and the arc's center is "radius" away toward the dealer, so both ends of the text curve up toward the dealer.
// Works in the editor too, so the bent text shows in the Scene view.
[ExecuteAlways]
[RequireComponent(typeof(TMP_Text))]
public class ArcText : MonoBehaviour
{
    [SerializeField] private float radius = 6f;   // Distance from the arc's center to the bottom of the letters

    private TMP_Text text;
    private bool needsRefresh;

    public float Radius
    {
        get => radius;
        set
        {
            radius = value;
            needsRefresh = true;
        }
    }

    private void OnEnable()
    {
        text = GetComponent<TMP_Text>();
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        Refresh();
    }

    private void OnDisable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);

        // Straight again (for example when this component is removed)
        if (text != null)
        {
            text.ForceMeshUpdate();
        }
    }

    // Changing the radius in the Inspector: bend again on the next frame (not allowed inside OnValidate)
    private void OnValidate()
    {
        needsRefresh = true;
    }

    private void Update()
    {
        if (needsRefresh)
        {
            needsRefresh = false;
            Refresh();
        }
    }

    private void Refresh()
    {
        if (text == null || !isActiveAndEnabled)
        {
            return;
        }

        // Rebuilding the text calls OnTextChanged, which bends it
        text.ForceMeshUpdate();
    }

    // TextMeshPro just rebuilt the letters (straight): bend them
    private void OnTextChanged(Object changed)
    {
        if (changed == text)
        {
            Bend();
        }
    }

    private void Bend()
    {
        if (radius <= 0.01f)
        {
            return;
        }

        TMP_TextInfo info = text.textInfo;

        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo character = info.characterInfo[i];

            if (!character.isVisible)
            {
                continue;
            }

            Vector3[] vertices = info.meshInfo[character.materialReferenceIndex].vertices;
            int first = character.vertexIndex;

            // Where the letter sits on the straight line: its middle, on the baseline
            float middle = (vertices[first].x + vertices[first + 2].x) * 0.5f;
            Vector3 anchor = new Vector3(middle, character.baseLine, 0f);

            // Walking "middle" along the arc gives this angle; the letter turns by the same angle
            float angle = middle / radius;
            Vector3 onArc = new Vector3(Mathf.Sin(angle) * radius, radius - Mathf.Cos(angle) * radius, 0f);
            Quaternion turn = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);

            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 fromAnchor = vertices[first + corner] - anchor;
                vertices[first + corner] = onArc + new Vector3(0f, character.baseLine, 0f) + turn * fromAnchor;
            }
        }

        text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
    }
}
