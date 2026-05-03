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

        // ── Hydra API web fetch ───────────────────────────────────────────
        public string HydraRequestUrl  { get; set; }
        public bool   HydraFetchOk     { get; set; }
        public string HydraFetchMode   { get; set; }   // "Fetching", "NotFound", "Failed"
        public string HydraFetchError  { get; set; }
        public int    HydraAchievementCount { get; set; }
        public string HydraMetadataJson      { get; set; }   // Serialized achievement list
        public string RawHydraJson           { get; set; }   // Raw API response
        public string HydraMode             { get; set; }   // "Using Hydra data", "Hydra unavailable", "LocalOnly", etc.

        // ── Local files scanned ───────────────────────────────────────────
        public List<string> LocalFilesFound   { get; set; } = new List<string>();
        public List<string> LocalFilesChecked { get; set; } = new List<string>();   // checked but missing
        public int          UnlockedLocalCount { get; set; }

        // -- Debug details for mismatch investigation --
        public List<string> LocalUnlocksFound { get; set; } = new List<string>();
        public List<string> MatchHistory      { get; set; } = new List<string>();
        public string       SteamMetadataJson { get; set; }

        // ── Final result ──────────────────────────────────────────────────
        public int    TotalAchievements  { get; set; }
        public string Mode               { get; set; }  // "Online", "LocalOnly", "None"
        public string ScanTime           { get; set; }
    }
}
