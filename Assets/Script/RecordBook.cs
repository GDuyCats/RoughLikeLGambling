using UnityEngine;

// The player's best results, saved on this computer so they survive closing the game
public static class RecordBook
{
    private const string BestRoundKey = "Record.BestRound";
    private const string WinsKey = "Record.Wins";
    private const string MostChipsKey = "Record.MostChips";

    public static int BestRound => PlayerPrefs.GetInt(BestRoundKey, 0);
    public static int Wins => PlayerPrefs.GetInt(WinsKey, 0);
    public static int MostChips => PlayerPrefs.GetInt(MostChipsKey, 0);

    public static bool HasAnyRecord => BestRound > 0 || Wins > 0 || MostChips > 0;

    // Called once when a run ends, won or lost
    public static void RecordRunEnd(int roundReached, int chips, bool won)
    {
        if (roundReached > BestRound)
        {
            PlayerPrefs.SetInt(BestRoundKey, roundReached);
        }

        if (chips > MostChips)
        {
            PlayerPrefs.SetInt(MostChipsKey, chips);
        }

        if (won)
        {
            PlayerPrefs.SetInt(WinsKey, Wins + 1);
        }

        PlayerPrefs.Save();
    }
}