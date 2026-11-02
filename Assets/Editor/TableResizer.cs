using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

// Used by Tools > Rearrange Tables. Makes every giant table 1.2 times bigger (30 x 18 becomes 36 x 21.6):
// the felt, the rails, the walkable area, the dealer's seat, the minigame's own spots, the light and the cameras.
// Things keep their size (cards, chips, buttons): they just get more room.
// Safe to run again: a table that already has the new size is left as it is.
public static class TableResizer
{
    private const float OldWidth = 30f;
    private const float RailThickness = 0.6f;
    private static readonly Vector3 BaseFollowOffset = new Vector3(0f, 15f, -10f);
    private const float BaseSweepHalfLength = 15f;

    public static string Resize(TableStation table)
    {
        Transform arena = table.Arena.transform;
        Transform top = arena.Find("ArenaTop");

        if (top == null)
        {
            return $"{table.name}: no ArenaTop found, so the table was not resized.";
        }

        float ratio = TableSpots.TableSize.x / top.localScale.x;
        bool grow = Mathf.Abs(ratio - 1f) > 0.01f;

        // The felt and the rails, always set to the new size
        Undo.RecordObject(top, "Rearrange Tables");
        top.localScale = new Vector3(TableSpots.TableSize.x, top.localScale.y, TableSpots.TableSize.y);
        PlaceRails(arena);

        // The walkable area
        SerializedObject arenaFields = new SerializedObject(table.Arena);
        Vector3 center = arena.position;
        arenaFields.FindProperty("walkMin").vector2Value = new Vector2(center.x + TableSpots.WalkMin.x, center.z + TableSpots.WalkMin.y);
        arenaFields.FindProperty("walkMax").vector2Value = new Vector2(center.x + TableSpots.WalkMax.x, center.z + TableSpots.WalkMax.y);
        arenaFields.ApplyModifiedProperties();

        // The dealer sits back a little
        if (table.Dealer != null)
        {
            SetArenaZ(arena, table.Dealer.transform, TableSpots.DealerZ);
        }

        if (!grow)
        {
            return $"{table.name}: already 36 x 21.6.";
        }

        // The minigame's own spots (card spots, dice spots...) and the old card piles spread out with the table
        foreach (Transform child in arena)
        {
            if (child.name.EndsWith("Spots") || child.name.StartsWith("DeckPile"))
            {
                SpreadOut(arena, child, ratio, child.name.EndsWith("Spots"));
            }
        }

        // The light covers the bigger table
        foreach (Light light in arena.GetComponentsInChildren<Light>(true))
        {
            if (light.type == LightType.Spot)
            {
                Undo.RecordObject(light, "Rearrange Tables");
                float half = Mathf.Atan(Mathf.Tan(light.spotAngle * 0.5f * Mathf.Deg2Rad) * ratio) * Mathf.Rad2Deg;
                light.spotAngle = Mathf.Min(170f, half * 2f);
                light.innerSpotAngle = Mathf.Min(light.spotAngle, light.innerSpotAngle * ratio);
            }
        }

        // Cameras fixed above this table move back to see all of it
        SerializedObject tableFields = new SerializedObject(table);
        MoveCameraBack(arena, tableFields.FindProperty("bossCamera").objectReferenceValue as GameObject, ratio);

        if (table.Minigame == MinigameType.Dice)
        {
            DiceGame dice = Object.FindFirstObjectByType<DiceGame>(FindObjectsInactive.Include);

            if (dice != null)
            {
                MoveCameraBack(arena, new SerializedObject(dice).FindProperty("rollCamera").objectReferenceValue as GameObject, ratio);
            }
        }

        return $"{table.name}: now 36 x 21.6 (was {top.localScale.x / ratio:0} wide).";
    }

    // Once for all tables: the camera that follows the chibi pulls back, and the boss's sweep crosses the wider table
    public static void ScaleSharedThings(List<string> report)
    {
        foreach (CinemachineFollow follow in Object.FindObjectsByType<CinemachineFollow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (follow.name != "ArenaCamera")
            {
                continue;
            }

            Undo.RecordObject(follow, "Rearrange Tables");
            follow.FollowOffset = BaseFollowOffset * TableSpots.Scale;
            report.Add($"ArenaCamera: follows from a little further back ({follow.FollowOffset}).");
        }

        BossCombat combat = Object.FindFirstObjectByType<BossCombat>(FindObjectsInactive.Include);

        if (combat != null)
        {
            SerializedObject fields = new SerializedObject(combat);
            fields.FindProperty("sweepHalfLength").floatValue = BaseSweepHalfLength * TableSpots.Scale;
            fields.ApplyModifiedProperties();
        }
    }

    private static void PlaceRails(Transform arena)
    {
        Transform rails = arena.Find("Rails");

        if (rails == null)
        {
            return;
        }

        float halfX = TableSpots.HalfWidth + RailThickness * 0.5f;
        float halfZ = TableSpots.HalfDepth + RailThickness * 0.5f;
        float longX = TableSpots.TableSize.x + RailThickness * 2f;
        float longZ = TableSpots.TableSize.y + RailThickness * 2f;

        SetRail(rails.Find("RailNorth"), new Vector3(0f, 0f, halfZ), new Vector3(longX, 0f, RailThickness));
        SetRail(rails.Find("RailSouth"), new Vector3(0f, 0f, -halfZ), new Vector3(longX, 0f, RailThickness));
        SetRail(rails.Find("RailWest"), new Vector3(-halfX, 0f, 0f), new Vector3(RailThickness, 0f, longZ));
        SetRail(rails.Find("RailEast"), new Vector3(halfX, 0f, 0f), new Vector3(RailThickness, 0f, longZ));
    }

    // Sets x and z of a rail's place and size; its height stays
    private static void SetRail(Transform rail, Vector3 position, Vector3 size)
    {
        if (rail == null)
        {
            return;
        }

        Undo.RecordObject(rail, "Rearrange Tables");
        rail.localPosition = new Vector3(position.x, rail.localPosition.y, position.z);
        rail.localScale = new Vector3(size.x, rail.localScale.y, size.z);
    }

    // Moves an object (or each of its children) further from the table's middle by this ratio
    private static void SpreadOut(Transform arena, Transform target, float ratio, bool children)
    {
        List<Transform> moving = new List<Transform>();

        if (children)
        {
            foreach (Transform child in target)
            {
                moving.Add(child);
            }
        }
        else
        {
            moving.Add(target);
        }

        foreach (Transform item in moving)
        {
            Undo.RecordObject(item, "Rearrange Tables");
            Vector3 local = arena.InverseTransformPoint(item.position);
            item.position = arena.TransformPoint(new Vector3(local.x * ratio, local.y, local.z * ratio));
        }
    }

    private static void MoveCameraBack(Transform arena, GameObject camera, float ratio)
    {
        if (camera == null)
        {
            return;
        }

        Transform t = camera.transform;
        Undo.RecordObject(t, "Rearrange Tables");
        Vector3 offset = t.position - arena.position;
        t.position = arena.position + offset * ratio;
    }

    private static void SetArenaZ(Transform arena, Transform target, float z)
    {
        Undo.RecordObject(target, "Rearrange Tables");
        Vector3 local = arena.InverseTransformPoint(target.position);
        target.position = arena.TransformPoint(new Vector3(local.x, local.y, z));
    }
}
