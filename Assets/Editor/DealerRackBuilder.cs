using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

// Used by Tools > Rearrange Tables. On every table, the dealer's old chip stacks become a real chip rack:
// a wood tray with five slots split by gold dividers, filled at runtime with the same chips the player has
// (see DealerChipRack). The dealer's resting hands move to the sides of the rack.
// Safe to run again: the rack is rebuilt each time.
public static class DealerRackBuilder
{
    private const string ChipPrefabPath = "Assets/Prefabs/TableChip.prefab";
    private const float SlotSpacing = 1.45f;

    public static string Build(TableStation table, TMP_FontAsset font)
    {
        Transform arena = table.Arena.transform;
        Transform tray = arena.Find("PlayerChips/DealerTray");

        if (tray == null)
        {
            return $"{table.name}: no dealer tray found (run Tools > Set Up Player Chips first).";
        }

        // The old loose stacks go: the rack makes its own chips
        int removed = 0;

        for (int i = tray.childCount - 1; i >= 0; i--)
        {
            Transform child = tray.GetChild(i);

            if (child.name != "Rack")
            {
                Undo.DestroyObjectImmediate(child.gameObject);
                removed++;
            }
        }

        TableMaterials.RemoveOld(tray, "Rack");
        BuildRack(tray);

        DealerChipRack rack = tray.GetComponent<DealerChipRack>();

        if (rack == null)
        {
            rack = Undo.AddComponent<DealerChipRack>(tray.gameObject);
        }

        SerializedObject fields = new SerializedObject(rack);
        fields.FindProperty("chipPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TableChip>(ChipPrefabPath);
        fields.FindProperty("font").objectReferenceValue = font;
        fields.FindProperty("slotSpacing").floatValue = SlotSpacing;
        fields.ApplyModifiedProperties();

        MoveHands(table, arena);
        return $"{table.name}: dealer chip rack ({removed} old chip stacks removed), dealer's hands moved to the sides.";
    }

    private static void BuildRack(Transform tray)
    {
        const float width = SlotSpacing * 5f + 0.35f;
        const float depth = 1.9f;
        const float floor = 0.15f;
        const float wall = 0.12f;

        GameObject rackObject = new GameObject("Rack");
        Undo.RegisterCreatedObjectUndo(rackObject, "Rearrange Tables");
        Transform rack = rackObject.transform;
        rack.SetParent(tray, false);

        // Solid, low: the chibi bumps into it (and can climb on it with a jump)
        BoxCollider body = rackObject.AddComponent<BoxCollider>();
        body.center = new Vector3(0f, 0.2f, 0f);
        body.size = new Vector3(width, 0.4f, depth);

        TableMaterials.Box(rack, "Base", new Vector3(0f, floor * 0.5f, 0f), new Vector3(width, floor, depth), TableMaterials.DarkWood);
        TableMaterials.Box(rack, "Felt", new Vector3(0f, floor + 0.005f, 0f), new Vector3(width - wall * 2f, 0.01f, depth - wall * 2f), TableMaterials.BlackFelt);
        TableMaterials.Box(rack, "Back", new Vector3(0f, 0.25f, (depth - wall) * 0.5f), new Vector3(width, 0.5f, wall), TableMaterials.Wood);
        TableMaterials.Box(rack, "Front", new Vector3(0f, 0.15f, -(depth - wall) * 0.5f), new Vector3(width, 0.3f, wall), TableMaterials.Wood);
        TableMaterials.Box(rack, "Left", new Vector3(-(width - wall) * 0.5f, 0.25f, 0f), new Vector3(wall, 0.5f, depth), TableMaterials.Wood);
        TableMaterials.Box(rack, "Right", new Vector3((width - wall) * 0.5f, 0.25f, 0f), new Vector3(wall, 0.5f, depth), TableMaterials.Wood);
        TableMaterials.Box(rack, "TrimBack", new Vector3(0f, 0.52f, (depth - wall) * 0.5f), new Vector3(width + 0.02f, 0.04f, wall + 0.02f), TableMaterials.Gold);
        TableMaterials.Box(rack, "TrimFront", new Vector3(0f, 0.32f, -(depth - wall) * 0.5f), new Vector3(width + 0.02f, 0.04f, wall + 0.02f), TableMaterials.Gold);

        // Gold dividers between the five slots
        for (int i = 1; i < 5; i++)
        {
            float x = (i - 2.5f) * SlotSpacing;
            TableMaterials.Box(rack, $"Divider{i}", new Vector3(x, 0.3f, 0f), new Vector3(0.05f, 0.3f, depth - wall * 2f), TableMaterials.Gold);
        }
    }

    // The resting hands sit on both sides of the rack, clear of the dealer's cards and total
    private static void MoveHands(TableStation table, Transform arena)
    {
        if (table.Dealer == null)
        {
            return;
        }

        Vector3 spot = TableSpots.DealerHandSpot;
        PlaceHand(arena, table.Dealer.LeftHand, new Vector3(-spot.x, 0f, spot.z));
        PlaceHand(arena, table.Dealer.RightHand, spot);
    }

    private static void PlaceHand(Transform arena, Transform hand, Vector3 spot)
    {
        if (hand == null)
        {
            return;
        }

        Undo.RecordObject(hand, "Rearrange Tables");
        Vector3 local = arena.InverseTransformPoint(hand.position);
        hand.position = arena.TransformPoint(new Vector3(spot.x, local.y, spot.z));
    }
}
