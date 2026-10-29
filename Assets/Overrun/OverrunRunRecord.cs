using UnityEngine;

/// Highest round entered, retained across runs and application restarts.
public static class OverrunRunRecord
{
    public const string BestRoundKey = "Overrun.BestRound";
    public static int BestRound => Mathf.Max(0, PlayerPrefs.GetInt(BestRoundKey, 0));
    public static void Record(int round)
    {
        if (round <= BestRound) return;
        PlayerPrefs.SetInt(BestRoundKey, round);
        PlayerPrefs.Save();
    }
}
