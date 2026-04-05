using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
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
            }

            // ── STEP 3: Local unlock status ────────────────────────────────────
            var unlockedIds = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
            CollectLocalUnlockStatus(game, DetectedId, unlockedIds, dbg);
            dbg.UnlockedLocalCount = unlockedIds.Count;

            // ── STEP 4: If Steam returned nothing, local-only fallback ─────────
            if (masterList.Count == 0)
            {
                dbg.Mode = "LocalOnly";
                var localList = BuildLocalOnlyList(game, DetectedId, dbg);
                dbg.TotalAchievements = localList.Count;
                return localList
                    .OrderBy(a => a.IsUnlocked ? 0 : 1)
                    .ThenBy(a => a.Name)
                    .ToList();
            }

            // ── STEP 5: Merge unlock status into master list ───────────────────
            dbg.Mode = "Online";
            foreach (var ach in masterList)
            {
                if (unlockedIds.TryGetValue(ach.Id, out var t))
                {
                    ach.IsUnlocked = true;
                    ach.UnlockTime = t;
                }
            }

            dbg.TotalAchievements = masterList.Count;
            return masterList
                .OrderBy(a => a.IsUnlocked ? 0 : 1)
                .ThenBy(a => a.Name)
                .ToList();
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

                            list.Add(new Achievement
                            {
                                Id          = name, // fallback to name as ID
                                Name        = name,
                                Description = desc,
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

        private void CollectLocalUnlockStatus(Game game, string appId,
            Dictionary<string, DateTime?> unlockedIds, ScanDebugInfo dbg)
        {
            if (!string.IsNullOrEmpty(game.InstallDirectory) && Directory.Exists(game.InstallDirectory))
            {
                var dir = game.InstallDirectory;

                foreach (var f in Directory.GetFiles(dir, "achievements.json", SearchOption.AllDirectories))
                {
                    if (f.IndexOf("steam_settings", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    dbg?.LocalFilesFound.Add(f);
                    MergeGoldbergUnlocks(f, unlockedIds);
                }

                foreach (var f in Directory.GetFiles(dir, "*.ini", SearchOption.AllDirectories)
                                            .Where(IsAchievementIni))
                {
                    dbg?.LocalFilesFound.Add(f);
                    MergeIniUnlocks(f, unlockedIds);
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
                        MergeGoldbergUnlocks(entry.Key, unlockedIds);
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

        private void MergeGoldbergUnlocks(string filePath, Dictionary<string, DateTime?> dict)
        {
            try
            {
                var token = JToken.Parse(File.ReadAllText(filePath));
                if (token is JArray arr)
                {
                    foreach (var item in arr)
                    {
                        var id = item["name"]?.ToString();
                        if (string.IsNullOrEmpty(id)) continue;
                        if (!(item["earned"]?.ToObject<bool>() ?? false)) continue;
                        var ts = item["earned_time"]?.ToObject<long>() ?? 0;
                        if (!dict.ContainsKey(id))
                            dict[id] = ts > 0 ? DateTimeOffset.FromUnixTimeSeconds(ts).DateTime : (DateTime?)null;
                    }
                }
                else if (token is JObject obj)
                {
                    foreach (var prop in obj.Properties())
                    {
                        if (!(prop.Value["earned"]?.ToObject<bool>() ?? false)) continue;
                        var ts = prop.Value["earned_time"]?.ToObject<long>() ?? 0;
                        if (!dict.ContainsKey(prop.Name))
                            dict[prop.Name] = ts > 0 ? DateTimeOffset.FromUnixTimeSeconds(ts).DateTime : (DateTime?)null;
                    }
                }
            }
            catch { }
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
                        list.Add(new Achievement { Id = id, Name = name, Description = desc,
                            IsUnlocked = earn,
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
                        list.Add(new Achievement { Id = prop.Name, Name = prop.Name, Description = "",
                            IsUnlocked = earn,
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
    }
}
