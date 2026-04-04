using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AchievementTracker.Models;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using Microsoft.Win32;

namespace AchievementTracker.Services
{
    public class AchievementScanner
    {
        private readonly IPlayniteAPI playniteApi;
        public string DetectedId { get; private set; }

        public AchievementScanner(IPlayniteAPI api)
        {
            playniteApi = api;
        }

        public string GetSteamAppId(Game game)
        {
            // 1. Check if GameId is numeric
            if (!string.IsNullOrEmpty(game.GameId) && game.GameId.All(char.IsDigit))
            {
                return game.GameId;
            }

            // 2. Check steam_appid.txt in install directory
            if (!string.IsNullOrEmpty(game.InstallDirectory) && Directory.Exists(game.InstallDirectory))
            {
                var appIdPath = Path.Combine(game.InstallDirectory, "steam_appid.txt");
                if (File.Exists(appIdPath))
                {
                    var content = File.ReadAllText(appIdPath).Trim();
                    if (!string.IsNullOrEmpty(content) && content.All(char.IsDigit))
                    {
                        return content;
                    }
                }
                
                // Also search in subdirectories (sometimes it's in a subfolder)
                var files = Directory.GetFiles(game.InstallDirectory, "steam_appid.txt", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    var content = File.ReadAllText(files[0]).Trim();
                    if (!string.IsNullOrEmpty(content) && content.All(char.IsDigit)) return content;
                }
            }

            // 3. Check Links for Steam store URL
            if (game.Links != null)
            {
                foreach (var link in game.Links)
                {
                    if (link.Url.Contains("steampowered.com/app/"))
                    {
                        var parts = link.Url.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        var id = parts.FirstOrDefault(p => p.All(char.IsDigit));
                        if (!string.IsNullOrEmpty(id)) return id;
                    }
                }
            }

            return null;
        }

        public List<Achievement> ScanForAchievements(Game game)
        {
            var achievements = new List<Achievement>();
            DetectedId = GetSteamAppId(game);
            
            try 
            {
                var filesToParse = new List<string>();

                // 1. Scan in Install Directory (Always check here)
                if (!string.IsNullOrEmpty(game.InstallDirectory) && Directory.Exists(game.InstallDirectory))
                {
                    var files = Directory.GetFiles(game.InstallDirectory, "*.*", SearchOption.AllDirectories);
                    
                    var gFiles = files.Where(f => f.EndsWith("achievements.json", StringComparison.OrdinalIgnoreCase));
                    var iFiles = files.Where(f => f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) && 
                                               (f.IndexOf("steam_emu", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                                f.IndexOf("achievements", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                                f.IndexOf("codex", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                                f.IndexOf("rune", StringComparison.OrdinalIgnoreCase) >= 0));
                    
                    filesToParse.AddRange(gFiles);
                    filesToParse.AddRange(iFiles);

                    var fltPaths = Directory.GetDirectories(game.InstallDirectory, "achievements", SearchOption.AllDirectories)
                                            .Where(d => d.IndexOf("flt", StringComparison.OrdinalIgnoreCase) >= 0);
                    foreach (var fltPath in fltPaths)
                    {
                        foreach (var file in Directory.GetFiles(fltPath))
                        {
                            var info = new FileInfo(file);
                            achievements.Add(new Achievement 
                            {
                                Id = info.Name,
                                Name = info.Name,
                                Description = "FLT Unlocked Achievement",
                                IsUnlocked = true,
                                UnlockTime = info.CreationTime
                            });
                        }
                    }
                }

                // 2. Scan in External Crack Directories using DetectedId
                if (!string.IsNullOrEmpty(DetectedId))
                {
                    filesToParse.AddRange(GetExternalAchievementFiles(DetectedId));
                    filesToParse.AddRange(GetSteamAchievementFiles(DetectedId));
                }
                
                // Remove duplicates and process gathered files
                foreach (var file in filesToParse.Distinct())
                {
                    if (file.IndexOf("librarycache", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        achievements.AddRange(ParseSteamLibraryJson(file));
                    }
                    else if (file.EndsWith("achievements.json", StringComparison.OrdinalIgnoreCase))
                    {
                        achievements.AddRange(ParseGoldbergJson(file));
                    }
                    else if (file.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
                    {
                        achievements.AddRange(ParseEmuIni(file));
                    }
                }

                // Group by Id and merge: take the one with a non-Id name (if possible) and the highest IsUnlocked
                return achievements
                    .GroupBy(a => a.Id)
                    .Select(g => {
                        var bestMetadata = g.OrderBy(a => a.Name == a.Id ? 1 : 0).First();
                        var anyUnlocked = g.FirstOrDefault(a => a.IsUnlocked);
                        
                        return new Achievement
                        {
                            Id = g.Key,
                            Name = bestMetadata.Name,
                            Description = bestMetadata.Description,
                            IsUnlocked = anyUnlocked != null,
                            UnlockTime = anyUnlocked?.UnlockTime,
                            IconUrl = bestMetadata.IconUrl
                        };
                    })
                    .OrderBy(a => a.IsUnlocked ? 0 : 1) // Unlocked first
                    .ThenBy(a => a.Name)
                    .ToList();
            }
            catch (Exception ex)
            {
                playniteApi.Dialogs.ShowErrorMessage("Error scanning achievements: " + ex.Message);
            }

            return achievements;
        }

        private IEnumerable<string> GetExternalAchievementFiles(string objectId)
        {
            var pathsToCheck = new List<string>();
            var publicDocs = Environment.GetEnvironmentVariable("PUBLIC") != null 
                             ? Path.Combine(Environment.GetEnvironmentVariable("PUBLIC"), "Documents") 
                             : @"C:\Users\Public\Documents";
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            // Codex / Rune
            pathsToCheck.Add(Path.Combine(publicDocs, "Steam", "CODEX", objectId, "achievements.ini"));
            pathsToCheck.Add(Path.Combine(publicDocs, "Steam", "CODEX", objectId, "achievements.json"));
            pathsToCheck.Add(Path.Combine(appData, "Steam", "CODEX", objectId, "achievements.ini"));
            pathsToCheck.Add(Path.Combine(publicDocs, "Steam", "RUNE", objectId, "achievements.ini"));

            // OnlineFix
            pathsToCheck.Add(Path.Combine(publicDocs, "OnlineFix", objectId, "Stats", "Achievements.ini"));
            pathsToCheck.Add(Path.Combine(publicDocs, "OnlineFix", objectId, "Achievements.ini"));

            // Goldberg
            pathsToCheck.Add(Path.Combine(appData, "Goldberg SteamEmu Saves", objectId, "achievements.json"));
            pathsToCheck.Add(Path.Combine(appData, "GSE Saves", objectId, "achievements.json"));
            
            // Empress
            pathsToCheck.Add(Path.Combine(appData, "EMPRESS", "remote", objectId, "achievements.json"));
            pathsToCheck.Add(Path.Combine(publicDocs, "EMPRESS", objectId, "remote", objectId, "achievements.json"));

            // RLD
            pathsToCheck.Add(Path.Combine(programData, "RLD!", objectId, "achievements.ini"));
            pathsToCheck.Add(Path.Combine(programData, "Steam", "Player", objectId, "stats", "achievements.ini"));
            pathsToCheck.Add(Path.Combine(programData, "Steam", "RLD!", objectId, "stats", "achievements.ini"));
            pathsToCheck.Add(Path.Combine(programData, "Steam", "dodi", objectId, "stats", "achievements.ini"));

            // Skidrow
            pathsToCheck.Add(Path.Combine(myDocs, "SKIDROW", objectId, "SteamEmu", "UserStats", "achiev.ini"));
            pathsToCheck.Add(Path.Combine(myDocs, "Player", objectId, "SteamEmu", "UserStats", "achiev.ini"));
            pathsToCheck.Add(Path.Combine(localAppData, "SKIDROW", objectId, "SteamEmu", "UserStats", "achiev.ini"));

            // CreamApi
            pathsToCheck.Add(Path.Combine(appData, "CreamAPI", objectId, "stats", "CreamAPI.Achievements.cfg"));

            // SmartSteamEmu
            pathsToCheck.Add(Path.Combine(appData, "SmartSteamEmu", objectId, "User", "Achievements.ini"));

            return pathsToCheck.Where(File.Exists);
        }

        private string GetSteamPath()
        {
            try 
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    return key?.GetValue("SteamPath")?.ToString();
                }
            }
            catch { return null; }
        }

        private IEnumerable<string> GetSteamAchievementFiles(string appId)
        {
            var detectedPaths = new List<string>();
            var steamPath = GetSteamPath();
            if (string.IsNullOrEmpty(steamPath)) return detectedPaths;

            var userDataPath = Path.Combine(steamPath, "userdata");
            if (!Directory.Exists(userDataPath)) return detectedPaths;

            try 
            {
                foreach (var userDir in Directory.GetDirectories(userDataPath))
                {
                    var cacheFile = Path.Combine(userDir, "config", "librarycache", appId + ".json");
                    if (File.Exists(cacheFile))
                    {
                        detectedPaths.Add(cacheFile);
                    }
                }
            }
            catch { }
            return detectedPaths;
        }

        private List<Achievement> ParseSteamLibraryJson(string filePath)
        {
            var list = new List<Achievement>();
            try
            {
                var content = File.ReadAllText(filePath);
                var jArray = JArray.Parse(content);
                
                // Steam librarycache JSON is an array of [key, value] tuples
                foreach (var tuple in jArray)
                {
                    if (tuple[0]?.ToString() == "achievements")
                    {
                        var dataNode = tuple[1]?["data"];
                        if (dataNode != null)
                        {
                            // Process both highlighted and hidden/unachieved
                            ProcessSteamAchievementList(dataNode["vecHighlight"], list);
                            ProcessSteamAchievementList(dataNode["vecAchievedHidden"], list);
                            ProcessSteamAchievementList(dataNode["vecUnachieved"], list);
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        private void ProcessSteamAchievementList(JToken token, List<Achievement> list)
        {
            if (token == null || !(token is JArray arr)) return;
            foreach (var item in arr)
            {
                var id = item["strID"]?.ToString();
                if (string.IsNullOrEmpty(id)) continue;

                var achieved = item["bAchieved"]?.ToObject<bool>() ?? false;
                var unlockTime = item["rtUnlocked"]?.ToObject<long>() ?? 0;

                list.Add(new Achievement
                {
                    Id = id,
                    Name = item["strName"]?.ToString() ?? id,
                    Description = item["strDescription"]?.ToString() ?? "",
                    IsUnlocked = achieved,
                    UnlockTime = achieved && unlockTime > 0 ? DateTimeOffset.FromUnixTimeSeconds(unlockTime).DateTime : (DateTime?)null,
                    IconUrl = item["strImage"]?.ToString()
                });
            }
        }

        private List<Achievement> ParseGoldbergJson(string filePath)
        {
            var list = new List<Achievement>();
            try
            {
                var json = File.ReadAllText(filePath);
                var jToken = JToken.Parse(json);

                if (jToken is JArray jArray)
                {
                    foreach (var item in jArray)
                    {
                        var id = item["name"]?.ToString();
                        var earned = item["earned"] != null && item["earned"].ToObject<bool>();
                        var earnedTime = item["earned_time"]?.ToObject<long>() ?? 0;
                        
                        var displayName = item["displayName"]?["english"]?.ToString() ?? id;
                        var desc = item["description"]?["english"]?.ToString() ?? "Hidden or No Description";
                        var icon = item["icon"]?.ToString();
                        
                        string resolvedIcon = null;
                        if (!string.IsNullOrEmpty(icon))
                        {
                            if (Path.IsPathRooted(icon)) resolvedIcon = icon;
                            else resolvedIcon = Path.Combine(Path.GetDirectoryName(filePath), icon);
                        }
                        
                        list.Add(new Achievement
                        {
                            Id = id,
                            Name = displayName,
                            Description = desc,
                            IsUnlocked = earned,
                            UnlockTime = earned ? DateTimeOffset.FromUnixTimeSeconds(earnedTime).DateTime : (DateTime?)null,
                            IconUrl = resolvedIcon
                        });
                    }
                }
                else if (jToken is JObject jObj)
                {
                    foreach(var prop in jObj.Properties())
                    {
                        var name = prop.Name;
                        var achieved = prop.Value["earned"]?.ToObject<bool>() ?? false;
                        var earnedTime = prop.Value["earned_time"]?.ToObject<long>() ?? 0;

                        list.Add(new Achievement
                        {
                            Id = name,
                            Name = name,
                            Description = "Steam Emu Achievement",
                            IsUnlocked = achieved,
                            UnlockTime = achieved ? DateTimeOffset.FromUnixTimeSeconds(earnedTime).DateTime : (DateTime?)null
                        });
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
                var content = File.ReadAllLines(filePath);
                string currentSection = null;
                var sectionData = new Dictionary<string, string>();

                foreach (var line in content)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#")) continue;

                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        // Process previous section if it was an achievement
                        if (currentSection != null && sectionData.Count > 0)
                        {
                            ProcessIniSection(currentSection, sectionData, list, filePath);
                        }

                        currentSection = trimmed.Substring(1, trimmed.Length - 2);
                        sectionData.Clear();
                        continue;
                    }

                    if (currentSection != null && trimmed.Contains("="))
                    {
                        var parts = trimmed.Split(new[] { '=' }, 2);
                        if (parts.Length == 2)
                        {
                            sectionData[parts[0].Trim()] = parts[1].Trim();
                        }
                    }
                }

                // Final section
                if (currentSection != null && sectionData.Count > 0)
                {
                    ProcessIniSection(currentSection, sectionData, list, filePath);
                }
            }
            catch { }
            return list;
        }

        private void ProcessIniSection(string sectionName, Dictionary<string, string> data, List<Achievement> list, string filePath)
        {
            // Case 1: Codex style - each achievement is a section
            if (data.ContainsKey("Achieved"))
            {
                bool unlocked = data["Achieved"] == "1" || data["Achieved"].Equals("true", StringComparison.OrdinalIgnoreCase);
                long unlockTime = 0;
                if (data.ContainsKey("UnlockTime")) long.TryParse(data["UnlockTime"], out unlockTime);

                list.Add(new Achievement
                {
                    Id = sectionName,
                    Name = sectionName, // Needs metadata for pretty name
                    Description = "Codex Achievement",
                    IsUnlocked = unlocked,
                    UnlockTime = unlocked ? (unlockTime > 0 ? DateTimeOffset.FromUnixTimeSeconds(unlockTime).DateTime : File.GetLastWriteTime(filePath)) : (DateTime?)null
                });
            }
            // Case 2: Standard emu style - [Achievements] section with key=value
            else if (sectionName.Equals("Achievements", StringComparison.OrdinalIgnoreCase))
            {
                foreach(var kvp in data)
                {
                    bool unlocked = kvp.Value == "1" || kvp.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    list.Add(new Achievement
                    {
                        Id = kvp.Key,
                        Name = kvp.Key,
                        Description = "Emulator Achievement",
                        IsUnlocked = unlocked,
                        UnlockTime = unlocked ? File.GetLastWriteTime(filePath) : (DateTime?)null
                    });
                }
            }
        }
    }
}
