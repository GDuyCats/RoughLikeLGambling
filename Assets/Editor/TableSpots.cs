using UnityEngine;

// Every spot on the giant tables, in one place, for the editor tools (Tools > Rearrange Tables).
// All spots are in an arena's own space: x = left/right, z = toward the dealer, the felt at y = 0.
// The tables are 36 x 21.6 (they were 30 x 18: everything was spread out by 1.2).
public static class TableSpots
{
    public const float Scale = 1.2f;                       // How much bigger than the first tables
    public static readonly Vector2 TableSize = new Vector2(30f * Scale, 18f * Scale);
    public static float HalfWidth => TableSize.x * 0.5f;   // 18
    public static float HalfDepth => TableSize.y * 0.5f;   // 10.8

    // Walking: up to 1.2 from the sides and the back rail, 0.5 from the front edge
    public static readonly Vector2 WalkMin = new Vector2(-16.8f, -10.3f);
    public static readonly Vector2 WalkMax = new Vector2(16.8f, 9.6f);

    // The player's side
    public static readonly Vector3 PileSpot = new Vector3(0f, 0f, -8.8f);
    public static readonly Vector2 SquareSize = new Vector2(5.2f, 3f);
    public static readonly Vector3 SpawnSpot = new Vector3(0f, 0f, -6.2f);

    // The two control panels: three columns, two rows. Action panel on the left, bet panel on the right.
    public static readonly float[] PanelColumns = { 8.6f, 10.6f, 12.6f };   // Distance from the middle
    public static readonly float[] PanelRows = { -6.7f, -8.6f };
    public const float PanelCenter = 10.6f;
    public const float PlateZ = -7.65f;
    public static readonly Vector2 PlateSize = new Vector2(6.4f, 4f);
    public static readonly Vector3 BetBoardSpot = new Vector3(PanelCenter, 0.02f, -4.9f);
    public static readonly Vector3 TimerSpot = new Vector3(-PanelCenter, 0.02f, -4.9f);

    // Message signs
    public const float MessageZ = -3.4f;
    public const float BlackjackMessageZ = -4.5f;
    public const float BlackjackSignWidth = 14f;

    // The dealer
    public const float DealerZ = 7.2f;
    public static readonly Vector3 DealerHandSpot = new Vector3(7.6f, 0f, 8.2f);   // The right hand; the left one mirrors it
    public static readonly Vector3 RackSpot = new Vector3(0f, 0f, 8.6f);

    // Bet circles
    public static Vector3 BetSpotFor(MinigameType game)
    {
        return game switch
        {
            MinigameType.HighLow => new Vector3(-4.2f, 0f, -1.1f),
            MinigameType.Dice => new Vector3(-5.4f, 0f, -1.1f),
            _ => new Vector3(-6.2f, 0f, -3.6f)   // Blackjack: left of the seats, inside the half-moon
        };
    }

    // ---------- Blackjack ----------

    public static readonly Vector3 ArcCenter = new Vector3(0f, 0f, 9f);   // Middle of the half-moon's straight side
    public const float HalfMoonRadius = 14.9f;
    public const float SeatRadius = 10.3f;                     // The middle seat sits at z = 9 - 10.3 = -1.3
    public const float SeatAngle = 28f;
    public const float SeatCardSpread = 1f;
    public static readonly Vector3 DealerRowStart = new Vector3(-1f, 0f, 5.5f);
    public static readonly Vector3 PlayerRowStart = new Vector3(-1f, 0f, ArcCenter.z - SeatRadius);   // + half a card spacing (1) = the middle seat
    public static readonly Vector3 ShoeSpot = new Vector3(12.6f, 0f, 6.2f);
    public const float ShoeYaw = -100f;                        // The shoe's mouth points left, toward the dealer's hands
    public static readonly Vector3 DiscardSpot = new Vector3(-12.6f, 0f, 6.2f);
    public const float DiscardYaw = 12f;
}
