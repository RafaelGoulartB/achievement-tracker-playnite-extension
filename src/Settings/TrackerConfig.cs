using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Playnite.SDK;

namespace AchievementTracker.Settings
{
    /// <summary>
    /// Configuration for the Achievement Tracker plugin.
    /// Stores polling interval, enable/disable flag, notification timeout, and sound settings (US-008).
    /// </summary>
    public class TrackerConfig
    {
        private readonly object lockObj = new object();
        private readonly string configPathTemplate;

        /// <summary>
        /// Whether the tracker is enabled. Default: true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Polling interval in seconds. Must be between 5 and 120. Default: 10.
        /// </summary>
        public int PollingIntervalSeconds { get; set; } = 10;

        /// <summary>
        /// How long to wait before dismissing notification. Default: 5 seconds.
        /// </summary>
        public int NotificationTimeoutSeconds { get; set; } = 5;

        /// <summary>
        /// Whether notification sound is enabled. Default: true.
        /// </summary>
        public bool ShowNotificationSound { get; set; } = true;

        /// <summary>
        /// Volume percent for notification sound (0-100). Default: 50.
        /// </summary>
        public double NotificationVolumePercent { get; set; } = 50.0;

        /// <summary>
        /// Directory to scan for notification sound files. Default: plugin resources folder.
        /// </summary>
        public List<string> SoundDirectories { get; set; } = null!;

        /// <summary>
        /// Path to the selected sound file for notifications.
        /// </summary>
        public string SelectedSoundPath { get; set; } = null!;

        private IPlayniteAPI playniteApi;

        /// <summary>
        /// Initializes config with the given Playnite path.
        /// </summary>
        public TrackerConfig(IPlayniteAPI api, string extensionDataPath)
        {
            playniteApi = api;
            var baseDir = Path.Combine(api.Paths.ExtensionsDataPath, "AchievementTracker");
            SoundDirectories = null!;
            SelectedSoundPath = null!;
        }

        /// <summary>
        /// Initializes config with Playnite path only (used from AudioSettings.Initialize).
        /// </summary>
        public TrackerConfig(string extensionDataPath)
        {
            configPathTemplate = Path.Combine(extensionDataPath, "achievement_tracker_config.json");
            try
            {
                var baseDir = Path.Combine(Path.GetFullPath(extensionDataPath), "AchievementTracker");
                if (!Directory.Exists(baseDir))
                {
                    Directory.CreateDirectory(baseDir);
                }
                SoundDirectories = null!;
                SelectedSoundPath = null!;
            }
            catch { }
        }

        /// <summary>
        /// Validates the polling interval to be within acceptable bounds.
        /// </summary>
        public int GetValidatedPollingInterval()
        {
            if (PollingIntervalSeconds < 5)
            {
                return 5;
            }
            if (PollingIntervalSeconds > 120)
            {
                return 120;
            }
            return PollingIntervalSeconds;
        }

        /// <summary>
        /// Toggles notification sound and returns the new value.
        /// </summary>
        public bool ToggleNotificationSound()
        {
            ShowNotificationSound = !ShowNotificationSound;
            Save();
            return ShowNotificationSound;
        }

        /// <summary>
        /// Loads config from disk.
        /// </summary>
        public void Load()
        {
            lock (lockObj)
            {
                try
                {
                    if (File.Exists(configPathTemplate))
                    {
                        var json = File.ReadAllText(configPathTemplate);
                        var loaded = JsonConvert.DeserializeObject<TrackerConfig>(json);
                        if (loaded != null)
                        {
                            Enabled = loaded.Enabled;
                            PollingIntervalSeconds = loaded.PollingIntervalSeconds > 0 ? loaded.PollingIntervalSeconds : PollingIntervalSeconds;
                            NotificationTimeoutSeconds = loaded.NotificationTimeoutSeconds > 0 ? loaded.NotificationTimeoutSeconds : NotificationTimeoutSeconds;
                        }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Saves config to disk.
        /// </summary>
        public void Save()
        {
            lock (lockObj)
            {
                try
                {
                    var configDir = Path.Combine(Path.GetFullPath(Path.GetDirectoryName(configPathTemplate)), "AchievementTracker");
                    if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);

                    var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                    File.WriteAllText(configPathTemplate, json);
                }
                catch { }
            }
        }

        /// <summary>
        /// Resets to default values and saves.
        /// </summary>
        public void Reset()
        {
            Enabled = true;
            PollingIntervalSeconds = 10;
            NotificationTimeoutSeconds = 5;
            ShowNotificationSound = true;
            NotificationVolumePercent = 50.0;
            Save();
        }

        /// <summary>
        /// Discover available sound files from configured directories.
        /// </summary>
        public List<Models.AudioSoundFile> DiscoverSoundFiles()
        {
            lock (lockObj)
            {
                var sounds = new List<Models.AudioSoundFile>();

                if (string.IsNullOrEmpty(SelectedSoundPath))
                {
                    // Default to plugin resources folder
                    try
                    {
                        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                        var resources = assembly.GetManifestResourceNames();
                        foreach (var r in resources)
                        {
                            if (r.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || r.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                            {
                                sounds.Add(new Models.AudioSoundFile
                                {
                                    Name = Path.GetFileName(r),
                                    Path = r
                                });
                            }
                        }
                    }
                    catch { }
                }

                // Discover files from directories (same logic as AudioSettings)
                var dirs = SoundDirectories ?? GetDefaultSoundDirectories();
                foreach (var dir in dirs)
                {
                    try
                    {
                        var envPath = dir.Replace("%APPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
                        if (!Directory.Exists(envPath)) continue;

                        var exts = new[] { ".wav", ".mp3", ".ogg" };
                        foreach (var file in Directory.GetFiles(envPath))
                        {
                            var ext = Path.GetExtension(file).ToLowerInvariant();
                            if (exts.Contains(ext))
                            {
                                sounds.Add(new Models.AudioSoundFile
                                {
                                    Name = Path.GetFileName(file),
                                    Path = file
                                });
                            }
                        }
                    }
                    catch { }
                }

                return sounds.OrderBy(s => s.Name).ToList();
            }
        }

        /// <summary>
        /// Gets the default sound directories.
        /// </summary>
        private List<string> GetDefaultSoundDirectories()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return new List<string>
            {
                Path.Combine(appData, "AchievementTracker", "Sounds"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "AchievementTracker"),
                @"C:\Sounds\Achievements"
            };
        }
    }
}