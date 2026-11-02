using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Used by Tools > Rearrange Tables, on the Blackjack table only. Builds:
// - the shoe (right, dealer's side): dark wood, red acrylic sides, gold trim, a slanted floor, a hood over the mouth,
//   and inside it the card stack, the yellow cut card and the weight that pushes the cards forward,
// - the discard holder (left): a red acrylic box on a wood base,
// - the 5 seat frames printed on the felt.
// The CardShoe script then makes the stack shorter as cards are dealt, fills the holder, and shuffles.
// Safe to run again: the shoe, the holder and the frames are rebuilt each time.
public static class CardShoeBuilder
{
    private const string CardPrefabPath = "Assets/Prefabs/TableCard.prefab";
    private const float Wall = 0.12f;
    private const float InnerLength = 3.9f;   // 416 cards stand 3.3 long, plus room for the weight
    private const float Tilt = 8f;            // The floor slopes down toward the mouth

    public static void Build(Transform arena, BlackjackGame game, List<string> report)
    {
        TableCard cardPrefab = AssetDatabase.LoadAssetAtPath<TableCard>(CardPrefabPath);
        Vector2 card = MeasureCard(cardPrefab);   // x = width (short side), y = length
        Sprite back = CardArt.GetCardBack();
        float pictureScale = back != null ? card.x / back.bounds.size.x : 1f;

        HideOldDeckPiles(arena);

        TableMaterials.RemoveOld(arena, "CardShoe");
        TableMaterials.RemoveOld(arena, "DiscardHolder");

        CardShoe shoe = BuildShoe(arena, card, back, pictureScale, cardPrefab, out Transform mouth);
        Transform[] discardParts = BuildDiscardHolder(arena, card, back, pictureScale);

        SerializedObject shoeFields = new SerializedObject(shoe);
        shoeFields.FindProperty("discardFloor").objectReferenceValue = discardParts[0];
        shoeFields.FindProperty("discardBody").objectReferenceValue = discardParts[1];
        shoeFields.FindProperty("discardTop").objectReferenceValue = discardParts[2].GetComponent<SpriteRenderer>();
        shoeFields.ApplyModifiedPropertiesWithoutUndo();

        Renderer[] frames = BuildSeatFrames(arena, card);

        // The game uses the shoe, the frames, and (for the old code paths) spots at the shoe's mouth and the holder
        SerializedObject gameFields = new SerializedObject(game);
        gameFields.FindProperty("shoe").objectReferenceValue = shoe;
        SerializedProperty frameList = gameFields.FindProperty("seatFrames");
        frameList.arraySize = frames.Length;

        for (int i = 0; i < frames.Length; i++)
        {
            frameList.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];
        }

        gameFields.ApplyModifiedProperties();

        MoveSpot(gameFields.FindProperty("deckSpot").objectReferenceValue as Transform, mouth.position);
        MoveSpot(gameFields.FindProperty("discardSpot").objectReferenceValue as Transform, discardParts[0].position);

        report.Add("Blackjack: an 8-deck shoe with a cut card (right), a discard holder (left), and 5 seat frames on the felt.");
    }

    // ---------- The shoe ----------

    private static CardShoe BuildShoe(Transform arena, Vector2 card, Sprite back, float pictureScale, TableCard cardPrefab, out Transform mouth)
    {
        float innerWidth = card.y + 0.2f;             // The cards stand on their long side
        float innerHeight = card.x + 0.35f;
        float outerWidth = innerWidth + Wall * 2f;

        GameObject rootObject = new GameObject("CardShoe");
        Undo.RegisterCreatedObjectUndo(rootObject, "Rearrange Tables");
        Transform root = rootObject.transform;
        root.SetParent(arena, false);
        root.localPosition = TableSpots.ShoeSpot;
        root.localRotation = Quaternion.Euler(0f, TableSpots.ShoeYaw, 0f);

        // Solid for the chibi
        BoxCollider body = rootObject.AddComponent<BoxCollider>();
        body.center = new Vector3(0f, (innerHeight + 0.8f) * 0.5f, 0f);
        body.size = new Vector3(outerWidth, innerHeight + 0.8f, InnerLength + 0.6f);

        // A flat wood base, with wood skirts on both sides hiding the gap under the slanted floor
        TableMaterials.Box(root, "Base", new Vector3(0f, 0.11f, 0f), new Vector3(outerWidth, 0.22f, InnerLength + 0.6f), TableMaterials.DarkWood);
        TableMaterials.Box(root, "SkirtLeft", new Vector3(-outerWidth * 0.5f + Wall * 0.5f, 0.45f, 0f), new Vector3(Wall, 0.7f, InnerLength + 0.6f), TableMaterials.Wood);
        TableMaterials.Box(root, "SkirtRight", new Vector3(outerWidth * 0.5f - Wall * 0.5f, 0.45f, 0f), new Vector3(Wall, 0.7f, InnerLength + 0.6f), TableMaterials.Wood);

        // Everything above the base slopes: the front (mouth) is low, the back rises
        Transform inner = TableMaterials.Empty(root, "Inner", new Vector3(0f, 0.22f, InnerLength * 0.5f + 0.05f));
        inner.localRotation = Quaternion.Euler(Tilt, 0f, 0f);

        TableMaterials.Box(inner, "Floor", new Vector3(0f, 0.03f, -InnerLength * 0.5f), new Vector3(innerWidth, 0.06f, InnerLength), TableMaterials.Wood);
        TableMaterials.Box(inner, "SideLeft", new Vector3(-(innerWidth + Wall) * 0.5f, innerHeight * 0.5f, -InnerLength * 0.5f), new Vector3(Wall, innerHeight, InnerLength), TableMaterials.Acrylic);
        TableMaterials.Box(inner, "SideRight", new Vector3((innerWidth + Wall) * 0.5f, innerHeight * 0.5f, -InnerLength * 0.5f), new Vector3(Wall, innerHeight, InnerLength), TableMaterials.Acrylic);
        TableMaterials.Box(inner, "Back", new Vector3(0f, innerHeight * 0.5f, -InnerLength - 0.075f), new Vector3(outerWidth, innerHeight, 0.15f), TableMaterials.Wood);
        TableMaterials.Box(inner, "FrontLip", new Vector3(0f, 0.15f, 0.05f), new Vector3(outerWidth, 0.3f, 0.1f), TableMaterials.Gold);
        TableMaterials.Box(inner, "Hood", new Vector3(0f, innerHeight + 0.06f, -0.55f), new Vector3(outerWidth, 0.12f, 1.1f), TableMaterials.Wood);
        TableMaterials.Box(inner, "HoodTrim", new Vector3(0f, innerHeight + 0.06f, 0.02f), new Vector3(outerWidth + 0.02f, 0.14f, 0.06f), TableMaterials.Gold);
        TableMaterials.Box(inner, "RailLeft", new Vector3(-(innerWidth + Wall) * 0.5f, innerHeight + 0.02f, -InnerLength * 0.5f), new Vector3(Wall + 0.03f, 0.05f, InnerLength), TableMaterials.Gold);
        TableMaterials.Box(inner, "RailRight", new Vector3((innerWidth + Wall) * 0.5f, innerHeight + 0.02f, -InnerLength * 0.5f), new Vector3(Wall + 0.03f, 0.05f, InnerLength), TableMaterials.Gold);

        // The card stack: its front sits just behind the lip, halfway up a standing card
        Transform stackFront = TableMaterials.Empty(inner, "StackFront", new Vector3(0f, 0.06f + card.x * 0.5f, -0.15f));
        Transform stackBody = TableMaterials.Box(stackFront, "Stack", new Vector3(0f, 0f, -1.6f), new Vector3(card.y, card.x, 3.2f), TableMaterials.CardEdge);
        SpriteRenderer frontCard = Picture(stackFront, "FrontCard", new Vector3(0f, 0f, 0.006f), Quaternion.Euler(0f, 0f, 90f), back, pictureScale);
        Transform cutCard = TableMaterials.Box(stackFront, "CutCard", new Vector3(0f, 0.06f, -0.8f), new Vector3(card.y + 0.06f, card.x + 0.12f, 0.03f), TableMaterials.CutCard);

        Transform pusher = TableMaterials.Empty(stackFront, "Weight", new Vector3(0f, -card.x * 0.5f + 0.35f, -3.2f));
        TableMaterials.Box(pusher, "Block", new Vector3(0f, 0f, -0.175f), new Vector3(innerWidth - 0.1f, 0.7f, 0.35f), TableMaterials.Wood);
        TableMaterials.Ball(pusher, "Knob", new Vector3(0f, 0.45f, -0.175f), 0.35f, TableMaterials.Gold);

        // Cards come out of the mouth, low at the front
        mouth = TableMaterials.Empty(inner, "Mouth", new Vector3(0f, 0.12f, 0.35f));

        // Once it comes out, the cut card lies flat next to the shoe
        Transform cutCardOut = TableMaterials.Box(root, "CutCardOut", new Vector3(outerWidth * 0.5f + card.x * 0.5f + 0.4f, 0.02f, 0.4f), new Vector3(card.x, 0.02f, card.y), TableMaterials.CutCard);
        cutCardOut.localRotation = Quaternion.Euler(0f, 12f, 0f);
        cutCardOut.gameObject.SetActive(false);

        CardShoe shoe = rootObject.AddComponent<CardShoe>();
        SerializedObject fields = new SerializedObject(shoe);
        fields.FindProperty("cardPrefab").objectReferenceValue = cardPrefab;
        fields.FindProperty("stackFront").objectReferenceValue = stackFront;
        fields.FindProperty("stackBody").objectReferenceValue = stackBody;
        fields.FindProperty("frontCard").objectReferenceValue = frontCard;
        fields.FindProperty("pusher").objectReferenceValue = pusher;
        fields.FindProperty("cutCard").objectReferenceValue = cutCard;
        fields.FindProperty("cutCardOut").objectReferenceValue = cutCardOut;
        fields.FindProperty("mouth").objectReferenceValue = mouth;
        fields.ApplyModifiedPropertiesWithoutUndo();
        return shoe;
    }

    // ---------- The discard holder ----------

    // Returns the floor, the pile box and the card back on top of the pile
    private static Transform[] BuildDiscardHolder(Transform arena, Vector2 card, Sprite back, float pictureScale)
    {
        float innerWidth = card.x + 0.25f;
        float innerLength = card.y + 0.25f;
        float outerWidth = innerWidth + Wall * 2f;
        float outerLength = innerLength + Wall * 2f;
        const float wallHeight = 2.8f;
        const float baseHeight = 0.15f;

        GameObject rootObject = new GameObject("DiscardHolder");
        Undo.RegisterCreatedObjectUndo(rootObject, "Rearrange Tables");
        Transform root = rootObject.transform;
        root.SetParent(arena, false);
        root.localPosition = TableSpots.DiscardSpot;
        root.localRotation = Quaternion.Euler(0f, TableSpots.DiscardYaw, 0f);

        BoxCollider body = rootObject.AddComponent<BoxCollider>();
        body.center = new Vector3(0f, (wallHeight + baseHeight) * 0.5f, 0f);
        body.size = new Vector3(outerWidth, wallHeight + baseHeight, outerLength);

        TableMaterials.Box(root, "Base", new Vector3(0f, baseHeight * 0.5f, 0f), new Vector3(outerWidth + 0.2f, baseHeight, outerLength + 0.2f), TableMaterials.DarkWood);
        TableMaterials.Box(root, "Felt", new Vector3(0f, baseHeight + 0.005f, 0f), new Vector3(innerWidth, 0.01f, innerLength), TableMaterials.BlackFelt);

        float wallY = baseHeight + wallHeight * 0.5f;
        float sideX = (innerWidth + Wall) * 0.5f;
        float endZ = (innerLength + Wall) * 0.5f;

        TableMaterials.Box(root, "WallBack", new Vector3(0f, wallY, endZ), new Vector3(outerWidth, wallHeight, Wall), TableMaterials.Acrylic);
        TableMaterials.Box(root, "WallLeft", new Vector3(-sideX, wallY, 0f), new Vector3(Wall, wallHeight, innerLength), TableMaterials.Acrylic);
        TableMaterials.Box(root, "WallRight", new Vector3(sideX, wallY, 0f), new Vector3(Wall, wallHeight, innerLength), TableMaterials.Acrylic);
        TableMaterials.Box(root, "WallFront", new Vector3(0f, baseHeight + 0.45f, -endZ), new Vector3(outerWidth, 0.9f, Wall), TableMaterials.Acrylic);

        // Gold along the top edges
        float top = baseHeight + wallHeight + 0.02f;
        TableMaterials.Box(root, "TrimBack", new Vector3(0f, top, endZ), new Vector3(outerWidth + 0.03f, 0.05f, Wall + 0.03f), TableMaterials.Gold);
        TableMaterials.Box(root, "TrimLeft", new Vector3(-sideX, top, 0f), new Vector3(Wall + 0.03f, 0.05f, innerLength), TableMaterials.Gold);
        TableMaterials.Box(root, "TrimRight", new Vector3(sideX, top, 0f), new Vector3(Wall + 0.03f, 0.05f, innerLength), TableMaterials.Gold);
        TableMaterials.Box(root, "TrimFront", new Vector3(0f, baseHeight + 0.92f, -endZ), new Vector3(outerWidth + 0.03f, 0.05f, Wall + 0.03f), TableMaterials.Gold);

        Transform floor = TableMaterials.Empty(root, "Floor", new Vector3(0f, baseHeight + 0.01f, 0f));
        Transform pile = TableMaterials.Box(floor, "Pile", new Vector3(0f, 0.05f, 0f), new Vector3(card.x, 0.1f, card.y), TableMaterials.CardEdge);
        pile.gameObject.SetActive(false);
        SpriteRenderer pileTop = Picture(floor, "PileTop", new Vector3(0f, 0.103f, 0f), Quaternion.Euler(90f, 0f, 0f), back, pictureScale);
        pileTop.gameObject.SetActive(false);

        return new[] { floor, pile, pileTop.transform };
    }

    // ---------- Seat frames ----------

    // Five rounded frames printed on the felt, one per seat. The game puts them on the seats and lights them up.
    private static Renderer[] BuildSeatFrames(Transform arena, Vector2 card)
    {
        TableMaterials.RemoveOld(arena, "SeatFrames");
        Transform group = TableMaterials.Empty(arena, "SeatFrames", Vector3.zero);
        Undo.RegisterCreatedObjectUndo(group.gameObject, "Rearrange Tables");

        // Big enough for two cards fanned on the seat, with a little room around them
        float width = card.x + TableSpots.SeatCardSpread + 0.7f;
        float length = card.y + 0.6f;
        Mesh mesh = SaveMesh(RoundedFrame(width, length, 0.09f, 0.35f), "SeatFrame");

        Renderer[] frames = new Renderer[5];

        for (int i = 0; i < frames.Length; i++)
        {
            int seat = i - 2;
            Quaternion turn = Quaternion.Euler(0f, -seat * TableSpots.SeatAngle, 0f);
            Vector3 position = TableSpots.ArcCenter + turn * (Vector3.back * TableSpots.SeatRadius);

            GameObject frame = new GameObject($"SeatFrame{i}");
            frame.transform.SetParent(group, false);
            frame.transform.localPosition = new Vector3(position.x, 0.008f, position.z);
            frame.transform.localRotation = turn;
            frame.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer renderer = frame.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = TableMaterials.FrameLine;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            frames[i] = renderer;
        }

        return frames;
    }

    // An outline of a rectangle with rounded corners, lying flat, centered on its object
    private static Mesh RoundedFrame(float width, float length, float line, float corner)
    {
        List<Vector3> outline = new List<Vector3>();
        float halfX = width * 0.5f - corner;
        float halfZ = length * 0.5f - corner;
        Vector2[] centers =
        {
            new Vector2(halfX, halfZ), new Vector2(-halfX, halfZ), new Vector2(-halfX, -halfZ), new Vector2(halfX, -halfZ)
        };

        for (int c = 0; c < 4; c++)
        {
            for (int step = 0; step <= 8; step++)
            {
                float angle = (c * 90f + step * 90f / 8f) * Mathf.Deg2Rad;
                outline.Add(new Vector3(centers[c].x + Mathf.Cos(angle), 0f, centers[c].y + Mathf.Sin(angle)));
            }
        }

        List<Vector3> points = new List<Vector3>();
        List<int> triangles = new List<int>();
        int count = outline.Count;

        for (int i = 0; i < count; i++)
        {
            Vector3 cornerCenter = new Vector3(centers[i / 9].x, 0f, centers[i / 9].y);
            Vector3 direction = (outline[i] - cornerCenter).normalized;
            points.Add(cornerCenter + direction * (corner - line * 0.5f));
            points.Add(cornerCenter + direction * (corner + line * 0.5f));
        }

        for (int i = 0; i < count; i++)
        {
            int a = i * 2;
            int b = ((i + 1) % count) * 2;
            triangles.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
        }

        // Face up
        for (int i = 0; i < triangles.Count; i += 3)
        {
            Vector3 normal = Vector3.Cross(points[triangles[i + 1]] - points[triangles[i]], points[triangles[i + 2]] - points[triangles[i]]);

            if (normal.y < 0f)
            {
                (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
            }
        }

        Mesh mesh = new Mesh();
        mesh.SetVertices(points);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------- Helpers ----------

    // A card's size on the table, from the card prefab (x = width, y = length)
    private static Vector2 MeasureCard(TableCard cardPrefab)
    {
        Vector2 size = new Vector2(1.75f, 2.45f);

        if (cardPrefab == null)
        {
            return size;
        }

        SpriteRenderer face = cardPrefab.GetComponentInChildren<SpriteRenderer>();

        if (face == null || face.sprite == null)
        {
            return size;
        }

        // The face lies flat (turned 90 degrees), so the picture's height runs along the table
        Vector3 pictureSize = face.sprite.bounds.size;
        Vector3 scale = face.transform.localScale;
        return new Vector2(pictureSize.x * scale.x, pictureSize.y * scale.y);
    }

    private static SpriteRenderer Picture(Transform parent, string name, Vector3 position, Quaternion rotation, Sprite sprite, float scale)
    {
        GameObject picture = new GameObject(name);
        picture.transform.SetParent(parent, false);
        picture.transform.localPosition = position;
        picture.transform.localRotation = rotation;
        picture.transform.localScale = Vector3.one * scale;

        SpriteRenderer renderer = picture.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        return renderer;
    }

    private static void MoveSpot(Transform spot, Vector3 position)
    {
        if (spot != null)
        {
            Undo.RecordObject(spot, "Rearrange Tables");
            spot.position = position;
        }
    }

    // The old flat card piles on this table are replaced by the shoe and the holder
    private static void HideOldDeckPiles(Transform arena)
    {
        foreach (Transform child in arena.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.StartsWith("DeckPile") && child.gameObject.activeSelf)
            {
                Undo.RecordObject(child.gameObject, "Rearrange Tables");
                child.gameObject.SetActive(false);
            }
        }
    }

    private static Mesh SaveMesh(Mesh mesh, string name)
    {
        const string folder = "Assets/Materials/Blackjack";

        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder("Assets/Materials", "Blackjack");
        }

        string path = $"{folder}/{name}.asset";

        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null)
        {
            AssetDatabase.DeleteAsset(path);
        }

        mesh.name = name;
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }
}
