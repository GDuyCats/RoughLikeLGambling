using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// An editor tool: Tools > Set Boss Styles.
// Gives each final boss its own way of fighting, by filling in the fields of its file in Assets/Bosses.
// - The Oracle: mysterious, feints and sweeps, wind-ups all over the place.
// - Dice King: showy, floods the table with chips.
// - Card Shark: quick hands, combos and very fast slams, often parries back.
// Running it again puts these numbers back (any changes made by hand in those files are replaced).
public static class BossStyleBuilder
{
    // One boss's numbers
    private class Style
    {
        public string Name;
        public int Slam, Combo, Feint, Sweep, Chip;          // Attack weights (raffle tickets)
        public int MinChips, MaxChips;
        public float ChipMinGap, ChipMaxGap;
        public float MinWindup, MaxWindup;
        public float HandDropTime;                            // 0 = the BossCombat's normal speed
        public float ParryChance;
        public int MinChain, MaxChain;
        public float ChainMinGap, ChainMaxGap;
        public float OverlapChance;
        public string RageLine;
    }

    [MenuItem("Tools/Set Boss Styles")]
    public static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            Tell("Please stop Play mode first, then run Tools > Set Boss Styles again.");
            return;
        }

        List<string> report = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:FinalBoss"))
        {
            FinalBoss boss = AssetDatabase.LoadAssetAtPath<FinalBoss>(AssetDatabase.GUIDToAssetPath(guid));

            if (boss == null)
            {
                continue;
            }

            Style style = StyleFor(boss.FavoriteGame);
            Apply(boss, style);
            report.Add($"{style.Name}: done.");
        }

        AssetDatabase.SaveAssets();
        Tell(report.Count > 0 ? string.Join("\n", report) : "No Final Boss files were found in the project.");
    }

    private static Style StyleFor(MinigameType game)
    {
        switch (game)
        {
            case MinigameType.HighLow:
                return new Style
                {
                    Name = "The Oracle",
                    Slam = 2, Combo = 1, Feint = 4, Sweep = 3, Chip = 1,
                    MinChips = 2, MaxChips = 3, ChipMinGap = 0.2f, ChipMaxGap = 0.7f,
                    MinWindup = 0.4f, MaxWindup = 1.4f, HandDropTime = 0f,
                    ParryChance = 0.25f,
                    MinChain = 1, MaxChain = 3, ChainMinGap = 0.15f, ChainMaxGap = 0.6f, OverlapChance = 0.2f,
                    RageLine = "I have already seen how this ends."
                };

            case MinigameType.Dice:
                return new Style
                {
                    Name = "Dice King",
                    Slam = 2, Combo = 1, Feint = 0, Sweep = 2, Chip = 5,
                    MinChips = 4, MaxChips = 6, ChipMinGap = 0.18f, ChipMaxGap = 0.35f,
                    MinWindup = 0.7f, MaxWindup = 1f, HandDropTime = 0f,
                    ParryChance = 0.1f,
                    MinChain = 1, MaxChain = 2, ChainMinGap = 0.2f, ChainMaxGap = 0.5f, OverlapChance = 0.3f,
                    RageLine = "ENOUGH! Bow before your King!"
                };

            default:
                return new Style
                {
                    Name = "Card Shark",
                    Slam = 3, Combo = 4, Feint = 2, Sweep = 0, Chip = 1,
                    MinChips = 2, MaxChips = 2, ChipMinGap = 0.25f, ChipMaxGap = 0.4f,
                    MinWindup = 0.25f, MaxWindup = 0.8f, HandDropTime = 0.08f,
                    ParryChance = 0.3f,
                    MinChain = 2, MaxChain = 3, ChainMinGap = 0.1f, ChainMaxGap = 0.3f, OverlapChance = 0.15f,
                    RageLine = "Alright kid. No more playing nice."
                };
        }
    }

    private static void Apply(FinalBoss boss, Style style)
    {
        SerializedObject fields = new SerializedObject(boss);

        fields.FindProperty("displayName").stringValue = style.Name;
        fields.FindProperty("slamWeight").intValue = style.Slam;
        fields.FindProperty("comboWeight").intValue = style.Combo;
        fields.FindProperty("feintWeight").intValue = style.Feint;
        fields.FindProperty("sweepWeight").intValue = style.Sweep;
        fields.FindProperty("chipWeight").intValue = style.Chip;
        fields.FindProperty("minChips").intValue = style.MinChips;
        fields.FindProperty("maxChips").intValue = style.MaxChips;
        fields.FindProperty("chipMinGap").floatValue = style.ChipMinGap;
        fields.FindProperty("chipMaxGap").floatValue = style.ChipMaxGap;
        fields.FindProperty("minWindup").floatValue = style.MinWindup;
        fields.FindProperty("maxWindup").floatValue = style.MaxWindup;
        fields.FindProperty("handDropTime").floatValue = style.HandDropTime;
        fields.FindProperty("parryChance").floatValue = style.ParryChance;
        fields.FindProperty("minChainMoves").intValue = style.MinChain;
        fields.FindProperty("maxChainMoves").intValue = style.MaxChain;
        fields.FindProperty("chainMinGap").floatValue = style.ChainMinGap;
        fields.FindProperty("chainMaxGap").floatValue = style.ChainMaxGap;
        fields.FindProperty("overlapChance").floatValue = style.OverlapChance;
        fields.FindProperty("rageAtHealth").floatValue = 0.5f;
        fields.FindProperty("rageExtraMoves").intValue = 1;
        fields.FindProperty("rageWindupScale").floatValue = 0.8f;
        fields.FindProperty("rageLine").stringValue = style.RageLine;

        fields.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(boss);
    }

    private static void Tell(string message)
    {
        EditorUtility.DisplayDialog("Set Boss Styles", message, "OK");
    }
}
