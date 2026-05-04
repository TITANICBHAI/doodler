using System;
using UnityEngine;

namespace DoodleClimb.Game
{
    /// <summary>
    /// Persists and retrieves the daily-best score.
    /// Key format: "dc_db_YYYY-M-D" (matches the Expo app's localStorage key).
    /// Resets automatically on the next calendar day.
    /// </summary>
    public static class DailyBestTracker
    {
        private static string Key =>
            $"dc_db_{DateTime.Now.Year}-{DateTime.Now.Month}-{DateTime.Now.Day}";

        public static int GetDailyBest()
        {
            return PlayerPrefs.GetInt(Key, 0);
        }

        public static bool TrySaveDailyBest(int score)
        {
            if (score <= GetDailyBest()) return false;
            PlayerPrefs.SetInt(Key, score);
            PlayerPrefs.Save();
            return true;
        }
    }
}
