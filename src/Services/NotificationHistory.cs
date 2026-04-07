using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace AchievementTracker.Services
{
    /// <summary>
    /// Persists notified achievements to a JSON file so the system
    /// remembers what was already notified and does not double-notify
    /// on game re-launch. (US-007)
    /// </summary>
    public class NotificationHistory
    {
        // { "appId": { "achievementId": "2026-04-05T23:10:00" } }
        private Dictionary<string, Dictionary<string, string>> data;

        private readonly string filePath;
        private readonly object lockObj = new object();

        public NotificationHistory(string extensionDataPath)
        {
            filePath = Path.Combine(extensionDataPath, "achievement_tracker_notifications.json");
            Load();
        }

        /// <summary>
        /// Returns true if the achievement was already notified for the given app.
        /// </summary>
        public bool IsAlreadyNotified(string appId, string achievementId)
        {
            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(achievementId))
                return false;

            lock (lockObj)
            {
                Dictionary<string, string> appHistory;
                if (data.TryGetValue(appId, out appHistory))
                {
                    return appHistory.ContainsKey(achievementId);
                }
                return false;
            }
        }

        /// <summary>
        /// Records that the achievement was notified for the given app,
        /// and persists the change to disk immediately.
        /// </summary>
        public void RecordNotification(string appId, string achievementId, DateTime? time)
        {
            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(achievementId))
                return;

            var timestamp = time != null
                ? time.Value.ToString("O")
                : DateTime.Now.ToString("O");

            lock (lockObj)
            {
                Dictionary<string, string> appHistory;
                if (!data.TryGetValue(appId, out appHistory))
                {
                    appHistory = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    data[appId] = appHistory;
                }

                appHistory[achievementId] = timestamp;
            }

            Save();
        }

        /// <summary>
        /// Loads notification history from disk.
        /// If file does not exist, initializes empty history.
        /// </summary>
        private void Load()
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    var loaded = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(json);
                    data = loaded ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // If file is corrupt or unreadable, start fresh
                data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Writes current history to disk as JSON.
        /// </summary>
        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch
            {
                // Silently ignore — missing write permission, disk full, etc.
            }
        }
    }
}
