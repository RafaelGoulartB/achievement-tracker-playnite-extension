using System;
using System.Collections.Generic;

namespace AchievementTracker.Models
{
    public class ScanDebugInfo
    {
        // ── AppId detection ───────────────────────────────────────────────
        public string DetectedAppId    { get; set; }
        public string AppIdSource      { get; set; }   // where it was found

        // ── Steam web fetch ───────────────────────────────────────────────
        public string SteamRequestUrl  { get; set; }
        public bool   SteamFetchOk     { get; set; }
        public string SteamFetchMode   { get; set; }   // "XML", "HTML", "Failed"
        public string SteamFetchError  { get; set; }
        public int    SteamAchievementCount { get; set; }

        // ── Local files scanned ───────────────────────────────────────────
        public List<string> LocalFilesFound   { get; set; } = new List<string>();
        public List<string> LocalFilesChecked { get; set; } = new List<string>();   // checked but missing
        public int          UnlockedLocalCount { get; set; }

        // ── Final result ──────────────────────────────────────────────────
        public int    TotalAchievements  { get; set; }
        public string Mode               { get; set; }  // "Online", "LocalOnly", "None"
        public string ScanTime           { get; set; }
    }
}
