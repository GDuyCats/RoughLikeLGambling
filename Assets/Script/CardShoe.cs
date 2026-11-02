using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The Blackjack shoe and discard holder, like a real casino:
// - 8 decks shuffled together sit in the shoe; the stack you see gets shorter as cards are dealt,
// - a yellow cut card sits about 3/4 of the way in; once it comes out, the shoe is shuffled after that hand,
// - used cards go face down into the discard holder, which fills up,
// - the shuffle is animated: both piles come to the middle, two riffles, a bridge, the cut card goes in,
//   and the new stack slides back into the shoe.
// The shoe and holder themselves (wood, red acrylic, gold) are made by Tools > Rearrange Tables.
public class CardShoe : MonoBehaviour
{
    private const int CardsPerDeck = 52;

    [Header("Rules")]
    [SerializeField] private int decks = 8;
    [SerializeField, Range(0.5f, 0.9f)] private float penetration = 0.75f;   // The cut card sits this deep in the shoe

    [Header("Shoe Parts")]
    [SerializeField] private TableCard cardPrefab;          // Used to measure a card, and for the cards flying in the shuffle
    [SerializeField] private Transform stackFront;          // The front of the card stack, halfway up
    [SerializeField] private Transform stackBody;           // A box: the cards seen from the side
    [SerializeField] private SpriteRenderer frontCard;      // The card back you see at the front of the stack
    [SerializeField] private Transform pusher;              // The weight that pushes the stack toward the front
    [SerializeField] private Transform cutCard;             // The yellow cut card inside the stack
    [SerializeField] private Transform cutCardOut;          // The cut card lying next to the shoe once it came out
    [SerializeField] private Transform mouth;               // Dealt cards come out here

    [Header("Discard Parts")]
    [SerializeField] private Transform discardFloor;        // The floor of the discard holder
    [SerializeField] private Transform discardBody;         // A box: the pile of used cards
    [SerializeField] private SpriteRenderer discardTop;     // A card back on top of the pile

    [Header("Look")]
    [SerializeField] private float shoeCardThickness = 0.008f;   // One card standing in the shoe
    [SerializeField] private float pileCardThickness = 0.006f;   // One card lying in a pile
    [SerializeField] private int looseTopCards = 6;              // The last cards thrown in stay as real cards, a little crooked
    [SerializeField] private float slideOutDistance = 1.4f;

    private readonly List<int> cards = new List<int>();
    private readonly List<TableCard> discardTops = new List<TableCard>();
    private int next;              // Index of the next card to deal
    private int cutAt;             // Dealing this card brings the cut card out
    private int discardCount;      // Cards that landed in the holder
    private int discardReserved;   // Cards on their way to the holder (or landed)

    private RunManager run;
    private Vector2 cardSize = new Vector2(1.75f, 2.45f);   // Width, length (measured in Awake)
    private float spriteScale = 1f;                         // Makes a card-back picture the size of a card
    private Sprite cardBack;
    private Material edgeMaterial;

    public int Remaining => cards.Count - next;
    public bool NeedsShuffle => next >= cutAt;
    public int TotalCards => decks * CardsPerDeck;

    private void Awake()
    {
        cardBack = CardArt.GetCardBack();
        MeasureCard();

        if (stackBody != null)
        {
            edgeMaterial = stackBody.GetComponent<Renderer>().sharedMaterial;
        }

        SetUpPicture(frontCard, true);
        SetUpPicture(discardTop, false);
        ResetShoe();

        // Every run starts with a full, freshly shuffled shoe
        run = FindFirstObjectByType<RunManager>();

        if (run != null)
        {
            run.RunStarted += ResetShoe;
        }
    }

    private void OnDestroy()
    {
        if (run != null)
        {
            run.RunStarted -= ResetShoe;
        }
    }

    // ---------- Dealing ----------

    // A fresh shoe: every card back in, shuffled, the discard holder empty (no animation)
    public void ResetShoe()
    {
        foreach (TableCard card in discardTops)
        {
            if (card != null)
            {
                Destroy(card.gameObject);
            }
        }

        discardTops.Clear();
        discardCount = 0;
        discardReserved = 0;
        ShuffleOrder();
        UpdateShoe();
        UpdateDiscard();
    }

    public int Draw()
    {
        // Should never happen (the cut card comes first), but a shoe never runs dry
        if (next >= cards.Count)
        {
            ShuffleOrder();
        }

        int card = cards[next];
        next++;
        UpdateShoe();
        return card;
    }

    // A face-down card appears at the shoe's mouth, lying flat
    public TableCard SpawnCard(TableCard prefab)
    {
        TableCard card = Instantiate(prefab, mouth.position, FlatRotation(mouth));
        card.SetSprite(cardBack);
        return card;
    }

    // The dealer pulls the card out of the shoe
    public IEnumerator SlideOut(TableCard card)
    {
        Vector3 forward = mouth.forward;
        forward.y = 0f;
        yield return card.SlideTo(mouth.position + forward.normalized * slideOutDistance, 0.12f);
    }

    // ---------- Discard holder ----------

    // Where the next used card lands. Call Land() when it gets there.
    public Vector3 ReserveDiscardSpot(out Quaternion rotation)
    {
        int index = discardReserved;
        discardReserved++;
        rotation = Quaternion.Euler(0f, discardFloor.eulerAngles.y + Random.Range(-7f, 7f), 0f);
        return discardFloor.position + Vector3.up * (index * pileCardThickness + 0.01f);
    }

    public void Land(TableCard card)
    {
        discardCount++;
        discardTops.Add(card);

        while (discardTops.Count > looseTopCards)
        {
            if (discardTops[0] != null)
            {
                Destroy(discardTops[0].gameObject);
            }

            discardTops.RemoveAt(0);
        }

        UpdateDiscard();
    }

    // Cards cleared off the table at once (the table was reset): they count as discarded
    public void AddDiscardedInstantly(int count)
    {
        discardCount += count;
        discardReserved += count;
        UpdateDiscard();
    }

    // ---------- Shuffle ----------

    public IEnumerator ShuffleRoutine(Vector3 workSpot)
    {
        Quaternion flat = Quaternion.identity;

        // 1. The discard pile and what is left in the shoe come to the middle of the table
        Pack fromDiscard = new Pack(this, discardFloor.position, FlatRotation(discardFloor), discardCount);
        Vector3 shoeBottom = stackFront.position + Vector3.down * cardSize.x * 0.5f;
        Pack fromShoe = new Pack(this, shoeBottom, FlatRotation(stackFront), Remaining);

        ClearDiscard();
        next = cards.Count;
        UpdateShoe();

        int total = TotalCards;
        float landOnTop = fromDiscard.Height;
        StartCoroutine(fromDiscard.FlyTo(workSpot, flat, 0.55f, 1.5f));
        yield return fromShoe.FlyTo(workSpot + Vector3.up * landOnTop, flat, 0.55f, 2f);
        fromShoe.Destroy();
        fromDiscard.SetCount(total);
        yield return Wait(0.15f);

        // 2. Two riffles
        Pack deck = fromDiscard;

        for (int i = 0; i < 2; i++)
        {
            deck = new Pack(this, workSpot, flat, 0);
            fromDiscard.Destroy();
            yield return Riffle(workSpot, total, 12f, 0f, 22, 0.035f, deck);
            fromDiscard = deck;
        }

        // 3. The bridge: both halves rise and bend, and the cards rain into one stack
        Pack bridged = new Pack(this, workSpot, flat, 0);
        deck.Destroy();
        yield return Riffle(workSpot, total, 25f, 0.6f, 16, 0.022f, bridged);
        yield return bridged.Squash();

        // 4. The cut card goes in, about a quarter of the way up from the bottom (3/4 deep from the top)
        GameObject slip = MakeCutCardSlip(bridged, 1f - penetration);
        yield return SlideIn(slip.transform, bridged);

        // 5. The new stack goes back into the shoe
        Vector3 into = stackFront.position + Vector3.down * cardSize.x * 0.5f;
        yield return bridged.FlyTo(into, FlatRotation(stackFront), 0.45f, 1.5f);
        bridged.Destroy();

        ShuffleOrder();
        UpdateShoe();
    }

    // Splits a pile into two halves left and right, tilts them, and flies cards from both into the middle in turn
    private IEnumerator Riffle(Vector3 spot, int total, float tilt, float lift, int flyers, float gap, Pack middle)
    {
        int leftCount = total / 2;
        Pack left = new Pack(this, spot, Quaternion.identity, leftCount);
        Pack right = new Pack(this, spot, Quaternion.identity, total - leftCount);
        float apart = cardSize.x * 0.5f + 0.9f;

        StartCoroutine(left.FlyTo(spot + Vector3.left * apart + Vector3.up * lift, Quaternion.Euler(0f, 0f, -tilt), 0.22f, 0.3f));
        yield return right.FlyTo(spot + Vector3.right * apart + Vector3.up * lift, Quaternion.Euler(0f, 0f, tilt), 0.22f, 0.3f);

        AudioManager.Play(SoundId.Shuffle);

        int perFlyer = Mathf.Max(1, total / flyers);

        for (int i = 0; i < flyers; i++)
        {
            Pack from = i % 2 == 0 ? left : right;
            int moved = i == flyers - 1 ? from.Count : Mathf.Min(perFlyer, from.Count);

            from.SetCount(from.Count - moved);
            StartCoroutine(FlyCard(from.TopPosition, middle, moved));
            yield return Wait(gap);
        }

        // The last of both halves drops in
        middle.SetCount(middle.Count + left.Count + right.Count);
        left.Destroy();
        right.Destroy();
        yield return Wait(0.15f);
    }

    // One card picture drops from a half into the middle pile, and the middle pile grows when it lands
    private IEnumerator FlyCard(Vector3 from, Pack middle, int count)
    {
        GameObject flyer = MakePicture("ShuffleCard", from, Quaternion.identity);
        Vector3 to = middle.TopPosition;
        const float duration = 0.14f;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float progress = t / duration;
            flyer.transform.position = Vector3.Lerp(from, to, progress) + Vector3.up * Mathf.Sin(progress * Mathf.PI) * 0.4f;
            yield return null;
        }

        Destroy(flyer);
        middle.SetCount(middle.Count + count);
    }

    private GameObject MakeCutCardSlip(Pack pack, float fromBottom)
    {
        GameObject slip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slip.name = "CutCard";
        Destroy(slip.GetComponent<Collider>());
        slip.GetComponent<Renderer>().sharedMaterial = cutCard.GetComponent<Renderer>().sharedMaterial;
        slip.transform.SetParent(pack.Root, false);
        slip.transform.localScale = new Vector3(cardSize.x + 0.05f, 0.03f, cardSize.y + 0.05f);
        slip.transform.localPosition = new Vector3(cardSize.x * 1.5f, pack.Height * fromBottom, 0f);
        return slip;
    }

    private IEnumerator SlideIn(Transform slip, Pack pack)
    {
        Vector3 start = slip.localPosition;
        Vector3 end = new Vector3(0f, start.y, 0f);

        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            slip.localPosition = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t / 0.3f));
            yield return null;
        }

        slip.localPosition = end;
        yield return Wait(0.1f);
    }

    // ---------- Visuals ----------

    private void ShuffleOrder()
    {
        cards.Clear();

        for (int deck = 0; deck < decks; deck++)
        {
            for (int card = 0; card < CardsPerDeck; card++)
            {
                cards.Add(card);
            }
        }

        // Fisher-Yates: every order is equally likely
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }

        next = 0;
        int spread = cards.Count / 20;
        cutAt = Mathf.RoundToInt(cards.Count * penetration) + Random.Range(-spread, spread + 1);
    }

    // The stack in the shoe: as long as the cards left, with the cut card where it sits
    private void UpdateShoe()
    {
        int left = Remaining;
        float length = Mathf.Max(0.001f, left * shoeCardThickness);

        stackBody.gameObject.SetActive(left > 0);
        stackBody.localScale = new Vector3(cardSize.y, cardSize.x, length);
        stackBody.localPosition = new Vector3(0f, 0f, -length * 0.5f);
        frontCard.gameObject.SetActive(left > 0);

        if (pusher != null)
        {
            pusher.localPosition = new Vector3(0f, pusher.localPosition.y, -length);
        }

        bool cutInside = next < cutAt && left > 0;
        cutCard.gameObject.SetActive(cutInside);
        cutCard.localScale = new Vector3(cardSize.y + 0.06f, cardSize.x + 0.12f, 0.03f);
        cutCard.localPosition = new Vector3(0f, 0.06f, -(cutAt - next) * shoeCardThickness);

        if (cutCardOut != null)
        {
            cutCardOut.gameObject.SetActive(!cutInside && left > 0);
        }
    }

    private void UpdateDiscard()
    {
        float height = Mathf.Max(0, discardCount - discardTops.Count) * pileCardThickness;
        bool show = height > 0.001f;

        discardBody.gameObject.SetActive(show);
        discardBody.localScale = new Vector3(cardSize.x, Mathf.Max(0.001f, height), cardSize.y);
        discardBody.localPosition = new Vector3(0f, height * 0.5f, 0f);

        discardTop.gameObject.SetActive(show);
        discardTop.transform.localPosition = new Vector3(0f, height + 0.003f, 0f);
    }

    private void ClearDiscard()
    {
        foreach (TableCard card in discardTops)
        {
            if (card != null)
            {
                Destroy(card.gameObject);
            }
        }

        discardTops.Clear();
        discardCount = 0;
        discardReserved = 0;
        UpdateDiscard();
    }

    // How big a card is on the table, measured from a real card
    private void MeasureCard()
    {
        if (cardPrefab == null || cardBack == null)
        {
            return;
        }

        TableCard sample = Instantiate(cardPrefab, new Vector3(0f, -500f, 0f), Quaternion.identity);
        sample.SetSprite(cardBack);
        Vector2 measured = sample.Size;
        Destroy(sample.gameObject);

        if (measured.x > 0.1f && measured.y > 0.1f)
        {
            cardSize = measured;
        }

        spriteScale = cardSize.x / Mathf.Max(0.01f, cardBack.bounds.size.x);
    }

    // A card-back picture sized like a real card: standing in the shoe (turned on its long side), or lying flat
    private void SetUpPicture(SpriteRenderer picture, bool standing)
    {
        if (picture == null)
        {
            return;
        }

        picture.sprite = cardBack;
        picture.transform.localScale = Vector3.one * spriteScale;
        picture.transform.localRotation = standing ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);
    }

    private GameObject MakePicture(string name, Vector3 position, Quaternion rotation)
    {
        GameObject picture = new GameObject(name);
        picture.transform.SetPositionAndRotation(position, rotation * Quaternion.Euler(90f, 0f, 0f));
        picture.transform.localScale = Vector3.one * spriteScale;
        picture.AddComponent<SpriteRenderer>().sprite = cardBack;
        return picture;
    }

    private static Quaternion FlatRotation(Transform from)
    {
        return Quaternion.Euler(0f, from.eulerAngles.y, 0f);
    }

    private static IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            yield return null;
        }
    }

    // A pile of cards made just for the shuffle: a box with a card back on top
    private class Pack
    {
        private readonly CardShoe shoe;
        private readonly Transform body;
        private readonly Transform top;

        public Transform Root { get; }
        public int Count { get; private set; }
        public float Height => Count * shoe.pileCardThickness;
        public Vector3 TopPosition => Root.position + Root.up * (Height + 0.01f);

        public Pack(CardShoe shoe, Vector3 position, Quaternion rotation, int count)
        {
            this.shoe = shoe;
            GameObject root = new GameObject("ShufflePack");
            Root = root.transform;
            Root.SetPositionAndRotation(position, rotation);

            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(box.GetComponent<Collider>());
            box.GetComponent<Renderer>().sharedMaterial = shoe.edgeMaterial;
            body = box.transform;
            body.SetParent(Root, false);

            GameObject picture = new GameObject("Top");
            picture.AddComponent<SpriteRenderer>().sprite = shoe.cardBack;
            top = picture.transform;
            top.SetParent(Root, false);
            top.localRotation = Quaternion.Euler(90f, 0f, 0f);
            top.localScale = Vector3.one * shoe.spriteScale;

            SetCount(count);
        }

        public void SetCount(int count)
        {
            Count = Mathf.Max(0, count);
            float height = Mathf.Max(0.001f, Height);
            body.localScale = new Vector3(shoe.cardSize.x, height, shoe.cardSize.y);
            body.localPosition = new Vector3(0f, height * 0.5f, 0f);
            top.localPosition = new Vector3(0f, height + 0.003f, 0f);
            Root.gameObject.SetActive(Count > 0);
        }

        public IEnumerator FlyTo(Vector3 position, Quaternion rotation, float duration, float arc)
        {
            Vector3 startPosition = Root.position;
            Quaternion startRotation = Root.rotation;

            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float progress = Mathf.SmoothStep(0f, 1f, t / duration);
                Root.position = Vector3.Lerp(startPosition, position, progress) + Vector3.up * Mathf.Sin(progress * Mathf.PI) * arc;
                Root.rotation = Quaternion.Slerp(startRotation, rotation, progress);
                yield return null;
            }

            Root.SetPositionAndRotation(position, rotation);
        }

        // A little bounce, like squaring the cards up
        public IEnumerator Squash()
        {
            for (float t = 0f; t < 0.2f; t += Time.deltaTime)
            {
                float squash = 1f - Mathf.Sin(t / 0.2f * Mathf.PI) * 0.12f;
                Root.localScale = new Vector3(1f / squash, squash, 1f / squash);
                yield return null;
            }

            Root.localScale = Vector3.one;
        }

        public void Destroy()
        {
            if (Root != null)
            {
                Object.Destroy(Root.gameObject);
            }
        }
    }
}
