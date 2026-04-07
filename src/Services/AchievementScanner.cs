using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Text;
using System.Globalization;
using System.Xml.Linq;
using AchievementTracker.Models;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace AchievementTracker.Services
{
    /// <summary>
    /// Online-first achievement scanner:
    ///   1. Detect Steam AppId
    ///   2. Fetch ALL achievements from Steam Community XML (names, descriptions, icons)
    ///   3. Scan local emulator files for unlock status (Goldberg, CODEX, FLT, etc.)
    ///   4. Merge: every Steam achievement is listed; local files only set IsUnlocked + UnlockTime
    /// </summary>
    public class AchievementScanner
    {
        private readonly IPlayniteAPI playniteApi;
        public string       DetectedId { get; private set; }
        public ScanDebugInfo LastDebug  { get; private set; }

        public AchievementScanner(IPlayniteAPI api)
        {
            playniteApi = api;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        }

        private void Log(string msg)
        {
            try
            {
                var path = Path.Combine(playniteApi.Paths.ExtensionsDataPath, "achievement_tracker_debug.log");
                File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        // PUBLIC API
        // ─────────────────────────────────────────────────────────────────────

        public List<Achievement> ScanForAchievements(Game game)
        {
            var dbg = new ScanDebugInfo { ScanTime = DateTime.Now.ToString("HH:mm:ss") };
            LastDebug = dbg;

            // ── STEP 1: AppId ──────────────────────────────────────────────────
            DetectedId = GetSteamAppId(game, dbg);
            dbg.DetectedAppId = DetectedId;

            // ── STEP 2: Online fetch ───────────────────────────────────────────
            var masterList = new List<Achievement>();
            if (!string.IsNullOrEmpty(DetectedId))
            {
                masterList = FetchSteamAchievements(DetectedId, dbg);
                if (masterList.Count > 0)
                {
                    dbg.SteamMetadataJson = Newtonsoft.Json.JsonConvert.SerializeObject(masterList, Newtonsoft.Json.Formatting.Indented);
                }
            }

            // ── STEP 3: Local achievement collection ───────────────────────────
            var localAchievements = BuildLocalOnlyList(game, DetectedId, dbg);
            dbg.UnlockedLocalCount = localAchievements.Count(a => a.IsUnlocked);

            // Populate debug list of local items found
            dbg.LocalUnlocksFound = localAchievements
                .OrderBy(l => l.Name)
                .Select(l => l.IsUnlocked ? $"[UNLOCKED] {l.Name} ({l.Id})" : $"[LOCKED] {l.Name} ({l.Id})")
                .ToList();

            // ── STEP 4: If Steam returned nothing, local-only fallback ─────────
            if (masterList.Count == 0)
            {
                dbg.Mode = "LocalOnly";
                var localList = BuildLocalOnlyList(game, DetectedId, dbg);
                dbg.SteamMetadataJson = Newtonsoft.Json.JsonConvert.SerializeObject(localList, Newtonsoft.Json.Formatting.Indented);
                dbg.TotalAchievements = localList.Count;
                return localList
                    .OrderBy(a => a.IsUnlocked ? 0 : 1)
                    .ThenBy(a => a.Name)
                    .ToList();
            }

            // ── STEP 5: Merge unlock status into master list ───────────────────
            dbg.Mode = "Online";
            Log($"Merging {localAchievements.Count} local entries into {masterList.Count} Steam achievements...");
            
            foreach (var ach in masterList)
            {
                string status = $"ID: '{ach.Id}' | Name: '{ach.Name}' -> ";
                
                // 1. Match by technical ID
                var match = localAchievements.FirstOrDefault(l => l.Id.Equals(ach.Id, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    if (match.IsUnlocked) { ach.IsUnlocked = true; ach.UnlockTime = match.UnlockTime; }
                    
                    // Fill in missing metadata from local match (common for hidden Steam achievements)
                    if (string.IsNullOrEmpty(ach.Description) || ach.Description.Equals("Hidden Achievement", StringComparison.OrdinalIgnoreCase))
                        if (!string.IsNullOrEmpty(match.Description)) ach.Description = match.Description;
                    
                    if (ach.Rarity == 0 && match.Rarity > 0) ach.Rarity = match.Rarity;
                    
                    dbg.MatchHistory.Add(status + "[MATCH BY ID]");
                    continue;
                }

                // 2. Match by exact Name
                match = localAchievements.FirstOrDefault(l => l.Name.Equals(ach.Name, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    if (match.IsUnlocked) { ach.IsUnlocked = true; ach.UnlockTime = match.UnlockTime; }
                    
                    if (string.IsNullOrEmpty(ach.Description) || ach.Description.Equals("Hidden Achievement", StringComparison.OrdinalIgnoreCase))
                        if (!string.IsNullOrEmpty(match.Description)) ach.Description = match.Description;
                    
                    if (ach.Rarity == 0 && match.Rarity > 0) ach.Rarity = match.Rarity;
                    
                    dbg.MatchHistory.Add(status + "[MATCH BY NAME]");
                    continue;
                }

                // 3. Match by Normalized Name
                var normSteamName = Normalize(ach.Name);
                match = localAchievements.FirstOrDefault(l => Normalize(l.Name) == normSteamName);
                if (match != null)
                {
                    if (match.IsUnlocked) { ach.IsUnlocked = true; ach.UnlockTime = match.UnlockTime; }
                    
                    if (string.IsNullOrEmpty(ach.Description) || ach.Description.Equals("Hidden Achievement", StringComparison.OrdinalIgnoreCase))
                        if (!string.IsNullOrEmpty(match.Description)) ach.Description = match.Description;
                    
                    if (ach.Rarity == 0 && match.Rarity > 0) ach.Rarity = match.Rarity;
                    
                    dbg.MatchHistory.Add(status + $"[MATCH BY NORM NAME: {normSteamName}]");
                    continue;
                }

                // 4. Match by Normalized Description
                if (!string.IsNullOrEmpty(ach.Description))
                {
                    var normSteamDesc = Normalize(ach.Description);
                    match = localAchievements.FirstOrDefault(l => !string.IsNullOrEmpty(l.Description) && Normalize(l.Description) == normSteamDesc);
                    if (match != null)
                    {
                        if (match.IsUnlocked) { ach.IsUnlocked = true; ach.UnlockTime = match.UnlockTime; }
                        
                        if (ach.Rarity == 0 && match.Rarity > 0) ach.Rarity = match.Rarity;
                        
                        dbg.MatchHistory.Add(status + $"[MATCH BY NORM DESC]");
                        continue;
                    }
                }

                dbg.MatchHistory.Add(status + "[NO MATCH]");
            }

            // Apply global sorting: Unlocked first, then by commonality (higher percentage first)
            masterList = masterList
                .OrderByDescending(a => a.IsUnlocked)
                .ThenByDescending(a => a.Rarity)
                .ToList();

            dbg.SteamMetadataJson = Newtonsoft.Json.JsonConvert.SerializeObject(masterList, Newtonsoft.Json.Formatting.Indented);
            dbg.TotalAchievements = masterList.Count;
            return masterList;
        }

        // ─────────────────────────────────────────────────────────────────────
        // STEAM APP ID DETECTION
        // ─────────────────────────────────────────────────────────────────────

        public string GetSteamAppId(Game game, ScanDebugInfo dbg = null)
        {
            // 1. GameId numeric
            if (!string.IsNullOrEmpty(game.GameId) && game.GameId.All(char.IsDigit))
            {
                if (dbg != null) dbg.AppIdSource = $"GameId = {game.GameId}";
                return game.GameId;
            }

            // 2. steam_appid.txt
            if (!string.IsNullOrEmpty(game.InstallDirectory) && Directory.Exists(game.InstallDirectory))
            {
                var direct = TryReadAppIdFile(Path.Combine(game.InstallDirectory, "steam_appid.txt"));
                if (direct != null)
                {
                    if (dbg != null) dbg.AppIdSource = $"steam_appid.txt (root) = {direct}";
                    return direct;
                }
                var found = TryFindAppIdFile(game.InstallDirectory, "steam_appid.txt");
                if (found != null)
                {
                    if (dbg != null) dbg.AppIdSource = $"steam_appid.txt (subdir) = {found}";
                    return found;
                }
            }

            // 3. Links
            if (game.Links != null)
            {
                foreach (var link in game.Links)
                {
                    if (string.IsNullOrEmpty(link.Url)) continue;
                    var m = Regex.Match(link.Url, @"steampowered\.com/app/(\d+)");
                    if (m.Success)
                    {
                        if (dbg != null) dbg.AppIdSource = $"Link URL = {link.Url}";
                        return m.Groups[1].Value;
                    }
                }
            }

            // 4. INI files
            if (!string.IsNullOrEmpty(game.InstallDirectory) && Directory.Exists(game.InstallDirectory))
            {
                string src;
                var id = ScanIniFilesForAppId(game.InstallDirectory, out src);
                if (id != null)
                {
                    if (dbg != null) dbg.AppIdSource = $"INI file: {src}";
                    return id;
                }
            }

            if (dbg != null) dbg.AppIdSource = "Not found";
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Online fetch
        // ─────────────────────────────────────────────────────────────────────

        private List<Achievement> FetchSteamAchievements(string appId, ScanDebugInfo dbg)
        {
            var list = new List<Achievement>();
            var url  = $"https://steamcommunity.com/stats/{appId}/achievements?xml=1&l=english";
            if (dbg != null) dbg.SteamRequestUrl = url;

            try
            {
                using (var wc = new WebClient())
                {
                    wc.Encoding = System.Text.Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.UserAgent] =
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Safari/537.36";

                    var content = wc.DownloadString(url);

                    if (content.Trim().StartsWith("<achievement") || content.Contains("<stats>"))
                    {
                        // — XML Path —————————————————————————————————————————─
                        if (dbg != null) dbg.SteamFetchMode = "XML";
                        var doc = XDocument.Parse(content);
                        foreach (var item in doc.Descendants("achievement"))
                        {
                            var id = item.Element("apiname")?.Value;
                            if (string.IsNullOrEmpty(id)) continue;

                            list.Add(new Achievement
                            {
                                Id          = id,
                                Name        = item.Element("name")?.Value ?? id,
                                Description = item.Element("description")?.Value ?? "",
                                IsHidden    = item.Element("hidden")?.Value == "1",
                                IconUrl     = item.Element("iconClosed")?.Value,
                                IsUnlocked  = false
                            });
                        }
                    }
                    else if (content.Contains("<div class=\"achieveRow ") || content.Contains("<body"))
                    {
                        // — HTML Path (Fallback) —————————————————————————————─
                        if (dbg != null) dbg.SteamFetchMode = "HTML (Scraped)";
                        
                        // Scrape row by row using Regex
                        var rowMatches = Regex.Matches(content, @"<div class=""achieveRow\s*"".*?>(.*?)<div style=""clear:\s*both;""></div>", RegexOptions.Singleline);
                        foreach (Match m in rowMatches)
                        {
                            var rowHtml = m.Groups[1].Value;
                            var imgMatch = Regex.Match(rowHtml, @"<img src=""(.*?)"".*?/>");
                            var nameMatch = Regex.Match(rowHtml, @"<h3>(.*?)</h3>");
                            var descMatch = Regex.Match(rowHtml, @"<h5>(.*?)</h5>");

                            if (!nameMatch.Success) continue;

                            var name = WebUtility.HtmlDecode(nameMatch.Groups[1].Value).Trim();
                            var desc = descMatch.Success ? WebUtility.HtmlDecode(descMatch.Groups[1].Value).Trim() : "";
                            var icon = imgMatch.Success ? imgMatch.Groups[1].Value : null;

                            var percentMatch = Regex.Match(rowHtml, @"<div class=""achievePercent"">(.*?)%</div>");
                            var perc = percentMatch.Success ? double.Parse(percentMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;

                            list.Add(new Achievement
                            {
                                Id          = name, // fallback to name as ID
                                Name        = name,
                                Description = desc,
                                IsHidden    = desc.Equals("Hidden Achievement", StringComparison.OrdinalIgnoreCase),
                                Rarity      = perc,
                                IconUrl     = icon,
                                IsUnlocked  = false
                            });
                        }
                    }
                    else
                    {
                        if (dbg != null) dbg.SteamFetchMode = "Unknown Response Type";
                    }

                    if (dbg != null) { dbg.SteamFetchOk = true; dbg.SteamAchievementCount = list.Count; }
                }
            }
            catch (Exception ex)
            {
                if (dbg != null) { dbg.SteamFetchOk = false; dbg.SteamFetchError = ex.Message; dbg.SteamFetchMode = "Failed"; }
            }
            return list;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Local unlock status collection
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a dictionary of locally unlocked achievement IDs by scanning
        /// all supported emulator sources (CodexFiles, INIs, Goldberg, etc.).
        /// </summary>
        /// <param name="game">The game to scan.</param>
        /// <param name="appId">Known Steam AppId (can be null, reduces external scans).</param>
        /// <param name="dbg">Optional debug info to populate.</param>
        /// <returns>Dictionary mapping achievement ID to unlock timestamp (null if unknown).</returns>
        public Dictionary<string, DateTime?> CollectLocalUnlockStatus(Game game, string appId, ScanDebugInfo dbg = null)
        {
            var unlockedIds = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
            var idToNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CollectLocalUnlockStatusInternal(game, appId, unlockedIds, idToNameMap, dbg);
            return unlockedIds;
        }

        /// <summary>
        /// Polls local emulator files for newly unlocked achievements since the last poll.
        /// Compares current local state against the provided previous snapshot.
        /// </summary>
        /// <param name="game">The game to scan.</param>
        /// <param name="appId">Known Steam AppId.</param>
        /// <param name="previousLocalSnapshot">Previous poll's unlock state dictionary.</param>
        /// <param name="cachedAchievements">Dictionary of known achievement IDs to full Achievement objects (from Steam/master list).</param>
        /// <param name="outCurrentSnapshot">Updated local unlock state dictionary (outputs for caller to store).</param>
        /// <returns>List of full Achievement objects for newly unlocked items.</returns>
        public List<Achievement> PollLocalUnlocks(Game game, string appId,
            Dictionary<string, DateTime?> previousLocalSnapshot,
            Dictionary<string, Achievement> cachedAchievements,
            out Dictionary<string, DateTime?> outCurrentSnapshot)
        {
            var dbg = new ScanDebugInfo { ScanTime = DateTime.Now.ToString("HH:mm:ss") };
            var currentLocal = CollectLocalUnlockStatus(game, appId, dbg);
            outCurrentSnapshot = currentLocal;

            var newUnlocks = new List<Achievement>();
            foreach (var kvp in currentLocal)
            {
                // Skip if already seen in previous local poll
                if (previousLocalSnapshot.ContainsKey(kvp.Key)) continue;

                // Try to resolve full metadata from the cached master list
                Achievement fullAch = null;
                Achievement cachedAch;
                if (cachedAchievements != null && cachedAchievements.TryGetValue(kvp.Key, out cachedAch))
                {
                    fullAch = new Achievement
                    {
                        Id = cachedAch.Id,
                        Name = cachedAch.Name,
                        Description = cachedAch.Description,
                        IsUnlocked = true,
                        UnlockTime = kvp.Value ?? DateTime.Now,
                        IsHidden = cachedAch.IsHidden,
                        Rarity = cachedAch.Rarity,
                        IconUrl = cachedAch.IconUrl
                    };
                }

                // Fallback: create stub from ID if not found in cache
                if (fullAch == null)
                {
                    fullAch = new Achievement
                    {
                        Id = kvp.Key,
                        Name = kvp.Key,
                        Description = "",
                        IsUnlocked = true,
                        UnlockTime = kvp.Value ?? DateTime.Now
                    };
                }

                newUnlocks.Add(fullAch);
            }

            return newUnlocks;
        }

        private void CollectLocalUnlockStatusInternal(Game game, string appId,
            Dictionary<string, DateTime?> unlockedIds,
            Dictionary<string, string> idToNameMap,
            ScanDebugInfo dbg)
        {
            if (!string.IsNullOrEmpty(game.InstallDirectory) && Directory.Exists(game.InstallDirectory))
            {
                var dir = game.InstallDirectory;

                foreach (var f in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    var n = Path.GetFileName(f).ToLowerInvariant();
                    if (n.EndsWith(".json") && (n.Contains("achiev") || n.Contains("stats") || n.Contains("score") || n.Contains("clair")))
                    {
                        dbg?.LocalFilesFound.Add(f);
                        MergeGoldbergUnlocks(f, unlockedIds, idToNameMap);
                    }
                    else if (n.EndsWith(".ini") && (n.Contains("achiev") || n.Contains("stats") || n.Contains("emu") || n.Contains("context") || n.Contains("codex")))
                    {
                        dbg?.LocalFilesFound.Add(f);
                        MergeIniUnlocks(f, unlockedIds);
                    }
                }

                foreach (var fltDir in Directory.GetDirectories(dir, "achievements", SearchOption.AllDirectories)
                                                  .Where(d => d.IndexOf("flt", StringComparison.OrdinalIgnoreCase) >= 0))
                    foreach (var f in Directory.GetFiles(fltDir))
                    {
                        dbg?.LocalFilesFound.Add(f);
                        var name = Path.GetFileName(f);
                        if (!unlockedIds.ContainsKey(name))
                            unlockedIds[name] = new FileInfo(f).CreationTime;
                    }
            }

            if (string.IsNullOrEmpty(appId)) return;

            foreach (var entry in GetExternalAchievementPathsWithStatus(appId))
            {
                if (entry.Value)
                {
                    dbg?.LocalFilesFound.Add(entry.Key);
                    if (entry.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        MergeGoldbergUnlocks(entry.Key, unlockedIds, idToNameMap);
                    else
                        MergeIniUnlocks(entry.Key, unlockedIds);
                }
                else
                {
                    dbg?.LocalFilesChecked.Add(entry.Key);
                }
            }

            foreach (var f in GetSteamLibraryCacheFiles(appId))
            {
                dbg?.LocalFilesFound.Add(f);
                MergeSteamLibraryCache(f, unlockedIds);
            }
        }

        private void CollectLocalUnlockStatus(Game game, string appId,
            Dictionary<string, DateTime?> unlockedIds,
            Dictionary<string, string> idToNameMap,
            ScanDebugInfo dbg)
        {
            CollectLocalUnlockStatusInternal(game, appId, unlockedIds, idToNameMap, dbg);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Local-only fallback
        // ─────────────────────────────────────────────────────────────────────

        private List<Achievement> BuildLocalOnlyList(Game game, string appId, ScanDebugInfo dbg)
        {
            var raw = new List<Achievement>();

            if (!string.IsNullOrEmpty(game.InstallDirectory) && Directory.Exists(game.InstallDirectory))
            {
                var dir = game.InstallDirectory;
                foreach (var f in Directory.GetFiles(dir, "achievements.json", SearchOption.AllDirectories))
                { raw.AddRange(ParseGoldbergJson(f)); dbg?.LocalFilesFound.Add(f); }
                foreach (var f in Directory.GetFiles(dir, "*.ini", SearchOption.AllDirectories).Where(IsAchievementIni))
                { raw.AddRange(ParseEmuIni(f)); dbg?.LocalFilesFound.Add(f); }
                foreach (var fltDir in Directory.GetDirectories(dir, "achievements", SearchOption.AllDirectories)
                                                  .Where(d => d.IndexOf("flt", StringComparison.OrdinalIgnoreCase) >= 0))
                    foreach (var f in Directory.GetFiles(fltDir))
                    {
                        var info = new FileInfo(f);
                        raw.Add(new Achievement { Id = info.Name, Name = info.Name,
                            Description = "", IsUnlocked = true, UnlockTime = info.CreationTime });
                        dbg?.LocalFilesFound.Add(f);
                    }
            }

            if (!string.IsNullOrEmpty(appId))
            {
                foreach (var entry in GetExternalAchievementPathsWithStatus(appId))
                {
                    if (entry.Value)
                    {
                        dbg?.LocalFilesFound.Add(entry.Key);
                        if (entry.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                            raw.AddRange(ParseGoldbergJson(entry.Key));
                        else
                            raw.AddRange(ParseEmuIni(entry.Key));
                    }
                    else dbg?.LocalFilesChecked.Add(entry.Key);
                }
                foreach (var f in GetSteamLibraryCacheFiles(appId))
                { raw.AddRange(ParseSteamLibraryCache(f)); dbg?.LocalFilesFound.Add(f); }
            }

            return raw.GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
                      .Select(g =>
                      {
                          var unlocked = g.FirstOrDefault(a => a.IsUnlocked);
                          var best = unlocked ?? g.First();
                          best.IsUnlocked = unlocked != null;
                          best.UnlockTime = unlocked?.UnlockTime;
                          return best;
                      })
                      .ToList();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Merge helpers (update status dict only)
        // ─────────────────────────────────────────────────────────────────────

        private void MergeGoldbergUnlocks(string filePath, 
            Dictionary<string, DateTime?> dict,
            Dictionary<string, string> idToNameMap = null)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                var token = JToken.Parse(content);
                ScanJsonForAchievements(token, dict, idToNameMap);
            }
            catch { }
        }

        private void ScanJsonForAchievements(JToken token, 
            Dictionary<string, DateTime?> dict, 
            Dictionary<string, string> idToNameMap,
            string propertyName = null)
        {
            if (token == null) return;

            if (token is JObject obj)
            {
                // Try to identify if this object is an achievement entry
                // Common keys: "name"/"strID", "earned"/"bAchieved", "displayName"/"strName"
                // If no name/ID found in object, fallback to property name
                var id   = obj["name"]?.ToString() ?? obj["strID"]?.ToString() ?? propertyName;
                var name = obj["displayName"]?["english"]?.ToString() ?? obj["displayName"]?.ToString() ?? obj["strName"]?.ToString() ?? propertyName;
                
                // If it has an ID, we can use it for mapping
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                {
                    if (idToNameMap != null) idToNameMap[id] = name;
                }

                // Check if earned
                bool isEarned = false;
                var earnedToken = obj["earned"] ?? obj["bAchieved"];
                if (earnedToken != null)
                {
                    if (earnedToken.Type == JTokenType.Boolean) isEarned = (bool)earnedToken;
                    else if (earnedToken.Type == JTokenType.Integer) isEarned = (long)earnedToken != 0;
                    else if (earnedToken.Type == JTokenType.String)
                    {
                        var earnedStr = earnedToken.ToString();
                        isEarned = earnedStr.Equals("true", StringComparison.OrdinalIgnoreCase) || earnedStr == "1";
                    }
                }

                bool isHidden = obj["hidden"]?.ToObject<bool>() ?? obj["bHidden"]?.ToObject<bool>() ?? false;

                if (isEarned)
                {
                    long ts = 0;
                    var timeToken = obj["earned_time"] ?? obj["rtUnlocked"];
                    if (timeToken != null)
                    {
                        if (timeToken.Type == JTokenType.Integer) ts = (long)timeToken;
                    }

                    var t = ts > 0 ? DateTimeOffset.FromUnixTimeSeconds(ts).DateTime : (DateTime?)null;
                    
                    if (!string.IsNullOrEmpty(id))
                    {
                        if (!dict.ContainsKey(id)) { dict[id] = t; Log($"Found local unlock (ID): {id}"); }
                    }
                    if (!string.IsNullOrEmpty(name))
                    {
                        if (!dict.ContainsKey(name)) { dict[name] = t; Log($"Found local unlock (Name): {name}"); }
                    }
                }

                // Continue recursion into properties even if this was an achievement (might be nested containers)
                foreach (var prop in obj.Properties())
                {
                    ScanJsonForAchievements(prop.Value, dict, idToNameMap, prop.Name);
                }
            }
            else if (token is JArray arr)
            {
                foreach (var item in arr)
                {
                    ScanJsonForAchievements(item, dict, idToNameMap);
                }
            }
        }

        private void MergeIniUnlocks(string filePath, Dictionary<string, DateTime?> dict)
        {
            try
            {
                string currentSection = null;
                var sectionData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var fileTime = File.GetLastWriteTime(filePath);

                void FlushSection()
                {
                    if (currentSection == null) return;
                    if (sectionData.TryGetValue("Achieved", out var achVal))
                    {
                        if (achVal == "1" || achVal.Equals("true", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!dict.ContainsKey(currentSection))
                            {
                                DateTime? t = fileTime;
                                if (sectionData.TryGetValue("UnlockTime", out var ts2)
                                    && long.TryParse(ts2, out var tsVal) && tsVal > 0)
                                    t = DateTimeOffset.FromUnixTimeSeconds(tsVal).DateTime;
                                dict[currentSection] = t;
                            }
                        }
                        return;
                    }
                    if (currentSection.Equals("Achievements", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var kvp in sectionData)
                            if (kvp.Value == "1" || kvp.Value.Equals("true", StringComparison.OrdinalIgnoreCase))
                                if (!dict.ContainsKey(kvp.Key))
                                    dict[kvp.Key] = fileTime;
                    }
                }

                foreach (var raw in File.ReadAllLines(filePath))
                {
                    var line = raw.Trim();
                    if (string.IsNullOrEmpty(line) || line[0] == ';' || line[0] == '#') continue;
                    if (line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        FlushSection();
                        currentSection = line.Substring(1, line.Length - 2);
                        sectionData.Clear();
                        continue;
                    }
                    var eq = line.IndexOf('=');
                    if (eq > 0 && currentSection != null)
                        sectionData[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                FlushSection();
            }
            catch { }
        }

        private void MergeSteamLibraryCache(string filePath, Dictionary<string, DateTime?> dict)
        {
            try
            {
                var jArray = JArray.Parse(File.ReadAllText(filePath));
                foreach (var tuple in jArray)
                {
                    if (tuple[0]?.ToString() != "achievements") continue;
                    var data = tuple[1]?["data"];
                    if (data == null) continue;
                    MergeSteamVec(data["vecHighlight"], dict);
                    MergeSteamVec(data["vecAchievedHidden"], dict);
                }
            }
            catch { }
        }

        private void MergeSteamVec(JToken token, Dictionary<string, DateTime?> dict)
        {
            if (!(token is JArray arr)) return;
            foreach (var item in arr)
            {
                var id = item["strID"]?.ToString();
                if (string.IsNullOrEmpty(id)) continue;
                if (!(item["bAchieved"]?.ToObject<bool>() ?? false)) continue;
                var ts = item["rtUnlocked"]?.ToObject<long>() ?? 0;
                if (!dict.ContainsKey(id))
                    dict[id] = ts > 0 ? DateTimeOffset.FromUnixTimeSeconds(ts).DateTime : (DateTime?)null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Full parsers (used in local-only fallback)
        // ─────────────────────────────────────────────────────────────────────

        private List<Achievement> ParseGoldbergJson(string filePath)
        {
            var list = new List<Achievement>();
            try
            {
                var token = JToken.Parse(File.ReadAllText(filePath));
                if (token is JArray arr)
                {
                    foreach (var item in arr)
                    {
                        var id   = item["name"]?.ToString(); if (string.IsNullOrEmpty(id)) continue;
                        var earn = item["earned"]?.ToObject<bool>() ?? false;
                        var ts   = item["earned_time"]?.ToObject<long>() ?? 0;
                        var name = item["displayName"]?["english"]?.ToString() ?? id;
                        var desc = item["description"]?["english"]?.ToString() ?? "";
                        var icon = item["icon"]?.ToString();
                        string iconPath = null;
                        if (!string.IsNullOrEmpty(icon))
                            iconPath = Path.IsPathRooted(icon) ? icon
                                     : Path.Combine(Path.GetDirectoryName(filePath) ?? "", icon);
                        var hid  = item["hidden"]?.ToObject<bool>() ?? item["bHidden"]?.ToObject<bool>() ?? false;
                        var rar  = item["flAchieved"]?.ToObject<double>() ?? 0;
                        list.Add(new Achievement { Id = id, Name = name, Description = desc,
                            IsUnlocked = earn, IsHidden = hid, Rarity = rar,
                            UnlockTime = earn && ts > 0 ? DateTimeOffset.FromUnixTimeSeconds(ts).DateTime : (DateTime?)null,
                            IconUrl    = iconPath });
                    }
                }
                else if (token is JObject obj)
                {
                    foreach (var prop in obj.Properties())
                    {
                        var earn = prop.Value["earned"]?.ToObject<bool>() ?? false;
                        var ts   = prop.Value["earned_time"]?.ToObject<long>() ?? 0;
                        var hid  = prop.Value["hidden"]?.ToObject<bool>() ?? prop.Value["bHidden"]?.ToObject<bool>() ?? false;
                        var name = prop.Value["strName"]?.ToString() ?? prop.Value["displayName"]?.ToString() ?? prop.Name;
                        var dsc  = prop.Value["strDescription"]?.ToString() ?? prop.Value["description"]?.ToString() ?? "";
                        var rar  = prop.Value["flAchieved"]?.ToObject<double>() ?? 0;
                        list.Add(new Achievement { Id = prop.Name, Name = name, Description = dsc,
                            IsUnlocked = earn, IsHidden = hid, Rarity = rar,
                            UnlockTime = earn && ts > 0 ? DateTimeOffset.FromUnixTimeSeconds(ts).DateTime : (DateTime?)null });
                    }
                }
            }
            catch { }
            return list;
        }

        private List<Achievement> ParseEmuIni(string filePath)
        {
            var list = new List<Achievement>();
            try
            {
                string currentSection = null;
                var sectionData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var fileTime = File.GetLastWriteTime(filePath);

                void FlushSection()
                {
                    if (currentSection == null) return;
                    if (sectionData.TryGetValue("Achieved", out var achVal))
                    {
                        var unlocked = achVal == "1" || achVal.Equals("true", StringComparison.OrdinalIgnoreCase);
                        DateTime? t = unlocked ? fileTime : (DateTime?)null;
                        if (unlocked && sectionData.TryGetValue("UnlockTime", out var tsStr)
                            && long.TryParse(tsStr, out var ts) && ts > 0)
                            t = DateTimeOffset.FromUnixTimeSeconds(ts).DateTime;
                        list.Add(new Achievement { Id = currentSection, Name = currentSection, Description = "",
                            IsUnlocked = unlocked, UnlockTime = t });
                        return;
                    }
                    if (currentSection.Equals("Achievements", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var kvp in sectionData)
                        {
                            var unlocked = kvp.Value == "1" || kvp.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                            list.Add(new Achievement { Id = kvp.Key, Name = kvp.Key, Description = "",
                                IsUnlocked = unlocked, UnlockTime = unlocked ? fileTime : (DateTime?)null });
                        }
                    }
                }

                foreach (var raw in File.ReadAllLines(filePath))
                {
                    var line = raw.Trim();
                    if (string.IsNullOrEmpty(line) || line[0] == ';' || line[0] == '#') continue;
                    if (line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        FlushSection(); currentSection = line.Substring(1, line.Length - 2); sectionData.Clear(); continue;
                    }
                    var eq = line.IndexOf('=');
                    if (eq > 0 && currentSection != null)
                        sectionData[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                FlushSection();
            }
            catch { }
            return list;
        }

        private List<Achievement> ParseSteamLibraryCache(string filePath)
        {
            var list = new List<Achievement>();
            try
            {
                var jArray = JArray.Parse(File.ReadAllText(filePath));
                foreach (var tuple in jArray)
                {
                    if (tuple[0]?.ToString() != "achievements") continue;
                    var data = tuple[1]?["data"]; if (data == null) continue;
                    ParseSteamVec(data["vecHighlight"],      list, true);
                    ParseSteamVec(data["vecAchievedHidden"], list, true);
                    ParseSteamVec(data["vecUnachieved"],     list, false);
                }
            }
            catch { }
            return list;
        }

        private void ParseSteamVec(JToken token, List<Achievement> list, bool isUnlocked)
        {
            if (!(token is JArray arr)) return;
            foreach (var item in arr)
            {
                var id = item["strID"]?.ToString(); if (string.IsNullOrEmpty(id)) continue;
                var ts = item["rtUnlocked"]?.ToObject<long>() ?? 0;
                list.Add(new Achievement {
                    Id = id, Name = item["strName"]?.ToString() ?? id, Description = item["strDescription"]?.ToString() ?? "",
                    IsUnlocked = isUnlocked,
                    UnlockTime = isUnlocked && ts > 0 ? DateTimeOffset.FromUnixTimeSeconds(ts).DateTime : (DateTime?)null,
                    IconUrl    = item["strImage"]?.ToString() });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static bool IsAchievementIni(string path)
        {
            var lower = path.ToLowerInvariant();
            return lower.Contains("steam_emu") || lower.Contains("achievements")
                || lower.Contains("codex")    || lower.Contains("rune")
                || lower.Contains("achiev");
        }

        private static string TryReadAppIdFile(string path)
        {
            if (!File.Exists(path)) return null;
            var c = File.ReadAllText(path).Trim();
            return c.Length > 0 && c.All(char.IsDigit) ? c : null;
        }

        private static string TryFindAppIdFile(string dir, string name)
        {
            try
            {
                var files = Directory.GetFiles(dir, name, SearchOption.AllDirectories);
                return files.Length > 0 ? TryReadAppIdFile(files[0]) : null;
            }
            catch { return null; }
        }

        private static string ScanIniFilesForAppId(string dir, out string source)
        {
            source = null;
            try
            {
                var candidates = Directory.GetFiles(dir, "*.ini", SearchOption.AllDirectories)
                    .Where(f => { var n = Path.GetFileName(f).ToLowerInvariant();
                        return n == "steam_emu.ini" || n == "context.ini" || n == "config.ini"
                            || n.Contains("steam") || n.Contains("codex"); });

                foreach (var file in candidates)
                {
                    try
                    {
                        foreach (var line in File.ReadAllLines(file))
                        {
                            var m = Regex.Match(line.Trim(), @"^\s*AppId\s*=\s*(\d+)", RegexOptions.IgnoreCase);
                            if (m.Success)
                            {
                                source = file;
                                return m.Groups[1].Value;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        // Returns Dictionary<path, exists> to avoid ValueTuple (not available in net462 without package)
        private Dictionary<string, bool> GetExternalAchievementPathsWithStatus(string appId)
        {
            var pub     = Path.Combine(Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public", "Documents");
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var local   = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var prog    = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var docs    = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var paths = new[]
            {
                Path.Combine(pub,     "Steam", "CODEX",        appId, "achievements.ini"),
                Path.Combine(pub,     "Steam", "CODEX",        appId, "achievements.json"),
                Path.Combine(roaming, "Steam", "CODEX",        appId, "achievements.ini"),
                Path.Combine(pub,     "Steam", "RUNE",         appId, "achievements.ini"),
                Path.Combine(pub,     "OnlineFix", appId, "Stats", "Achievements.ini"),
                Path.Combine(pub,     "OnlineFix", appId, "Achievements.ini"),
                Path.Combine(roaming, "Goldberg SteamEmu Saves", appId, "achievements.json"),
                Path.Combine(roaming, "GSE Saves",               appId, "achievements.json"),
                Path.Combine(roaming, "EMPRESS", "remote", appId, "achievements.json"),
                Path.Combine(pub,     "EMPRESS", appId, "remote", appId, "achievements.json"),
                Path.Combine(prog,    "RLD!",  appId, "achievements.ini"),
                Path.Combine(prog,    "Steam", "Player", appId, "stats", "achievements.ini"),
                Path.Combine(prog,    "Steam", "RLD!",   appId, "stats", "achievements.ini"),
                Path.Combine(prog,    "Steam", "dodi",   appId, "stats", "achievements.ini"),
                Path.Combine(docs,    "SKIDROW", appId, "SteamEmu", "UserStats", "achiev.ini"),
                Path.Combine(docs,    "Player",  appId, "SteamEmu", "UserStats", "achiev.ini"),
                Path.Combine(local,   "SKIDROW", appId, "SteamEmu", "UserStats", "achiev.ini"),
                Path.Combine(roaming, "CreamAPI",      appId, "stats", "CreamAPI.Achievements.cfg"),
                Path.Combine(roaming, "SmartSteamEmu", appId, "User",  "Achievements.ini"),
            };

            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in paths) result[p] = File.Exists(p);
            return result;
        }

        private IEnumerable<string> GetSteamLibraryCacheFiles(string appId)
        {
            var steamPath = GetSteamPath();
            if (string.IsNullOrEmpty(steamPath)) yield break;
            var userdata = Path.Combine(steamPath, "userdata");
            if (!Directory.Exists(userdata)) yield break;
            foreach (var d in Directory.GetDirectories(userdata))
            {
                var f = Path.Combine(d, "config", "librarycache", appId + ".json");
                if (File.Exists(f)) yield return f;
            }
        }

        private string GetSteamPath()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    return key?.GetValue("SteamPath")?.ToString();
            }
            catch { return null; }
        }

        // ─────────────────────────────────────────────────────────────────────
        // NORMALIZATION
        // ─────────────────────────────────────────────────────────────────────

        private string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            // 1. Remove accents
            var s = RemoveAccents(input);
            // 2. Lowercase
            s = s.ToLowerInvariant();
            // 3. Keep only alphanumeric
            s = Regex.Replace(s, @"[^a-z0-9]", "");
            return s;
        }

        private string RemoveAccents(string text)
        {
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
