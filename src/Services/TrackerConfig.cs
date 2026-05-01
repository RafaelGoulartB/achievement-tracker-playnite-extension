using System;
using System.IO;
using Newtonsoft.Json;

namespace AchievementTracker.Services
{
    /// <summary>
    /// Configuration for the achievement tracking system.
    /// Persists to {ExtensionDataPath}/achievement_tracker_config.json.
    /// Supports reload without restart. (US-008)
    /// </summary>
    public class TrackerConfig
    {
        private const string DefaultFileName = "achievement_tracker_config.json";

        private readonly string filePath;
        private readonly object lockObj = new object();

        // ── Configurable fields ──

        /// <summary>
        /// Whether real-time tracking is enabled. Default: true.
        /// When false, StartTracking returns early without starting the timer.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Polling interval in seconds. Default: 10, min: 5, max: 120.
        /// </summary>
        public int PollingIntervalSeconds { get; set; }

        /// <summary>
        /// Notification banner auto-dismiss timeout in seconds. Default: 5.
        /// </summary>
        public int NotificationTimeoutSeconds { get; set; }

        /// <summary>
        /// Whether to play a notification sound on unlock. Default: true.
        /// Controls sound playback in AchievementNotificationWindow.ShowAnimated().
        /// </summary>
        public bool ShowNotificationSound { get; set; }

        /// <summary>
        /// Notification volume percentage. Default: 50.0, range: 0.0-100.0.
        /// Used by AchievementNotificationWindow to scale WAV playback volume.
        /// </summary>
        public double NotificationVolumePercent { get; set; }

        /// <summary>
        /// Creates a TrackerConfig that loads from (or creates defaults at) the
        /// given extension data path.
        /// </summary>
        public TrackerConfig(string extensionDataPath = null)
        {
            if (extensionDataPath == null)
            {
                // Use a temporary path for default config creation
                filePath = Path.Combine(Path.GetTempPath(), DefaultFileName);
            }
            else
            {
                filePath = Path.Combine(extensionDataPath, DefaultFileName);
            }
            Load();
        }

        /// <summary>
        /// Loads config from disk. If the file does not exist or is corrupt,
        /// creates a new file with default values.
        /// </summary>
        public void Load()
        {
            lock (lockObj)
            {
                bool createdDefaults = false;

                try
                {
                    if (File.Exists(filePath))
                    {
                        var json = File.ReadAllText(filePath);
                        var loaded = JsonConvert.DeserializeObject<TrackerConfig>(json);
                        if (loaded != null)
                        {
                            Apply(loaded);
                            return;
                        }
                    }
                }
                catch
                {
                    createdDefaults = true;
                }

                if (createdDefaults)
                {
                    Apply(GetDefaults());
                    Save();
                }
            }
        }

        /// <summary>
        /// Applies config values to this instance.
        /// </summary>
        private void Apply(TrackerConfig config)
        {
            this.Enabled = config.Enabled;
            this.PollingIntervalSeconds = config.PollingIntervalSeconds;
            this.NotificationTimeoutSeconds = config.NotificationTimeoutSeconds;
            this.ShowNotificationSound = config.ShowNotificationSound;
            this.NotificationVolumePercent = config.NotificationVolumePercent;
        }

        /// <summary>
        /// Creates the configuration with default values.
        /// </summary>
        private TrackerConfig GetDefaults()
        {
            return new TrackerConfig
            {
                Enabled = true,
                PollingIntervalSeconds = 10,
                NotificationTimeoutSeconds = 5,
                ShowNotificationSound = true,
                NotificationVolumePercent = 100.0
            };
        }

        /// <summary>
        /// Saves the current configuration to disk.
        /// </summary>
        public void Save()
        {
            lock (lockObj)
            {
                try
                {
                    var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                    File.WriteAllText(filePath, json);
                }
                catch
                {
                    // Ignore save failures and try to keep user data if possible
                }
            }
        }

        /// <summary>
        /// Validates and applies the polling interval constraint.
        /// Polling interval must be between 5 and 120 seconds.
        /// </summary>
        public int GetValidatedPollingInterval()
        {
            int interval = PollingIntervalSeconds;
            if (interval < 5) interval = 5;
            if (interval > 120) interval = 120;
            return interval;
        }

        /// <summary>
        /// Toggles the notification sound on/off.
        /// </summary>
        public bool ToggleNotificationSound()
        {
            ShowNotificationSound = !ShowNotificationSound;
            return ShowNotificationSound;
        }
    }
}
