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
        /// Whether to play a system sound on notification. Default: false.
        /// Controls System.Media.SystemSounds.Asterisk.Play() in ShowAnimated().
        /// </summary>
        public bool ShowNotificationSound { get; set; }

        /// <summary>
        /// Creates a TrackerConfig that loads from (or creates defaults at) the
        /// given extension data path.
        /// </summary>
        public TrackerConfig(string extensionDataPath)
        {
            filePath = Path.Combine(extensionDataPath, DefaultFileName);
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
                    createdDefaults = true;
                }
                catch
                {
                    // Corrupt or unreadable — use defaults
                    createdDefaults = true;
                }

                SetDefaults();

                if (createdDefaults)
                {
                    Save();
                }
            }
        }

        /// <summary>
        /// Writes the current config to disk as JSON.
        /// </summary>
        public void Save()
        {
            lock (lockObj)
            {
                try
                {
                    var dir = Path.GetDirectoryName(filePath);
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                    File.WriteAllText(filePath, json);
                }
                catch
                {
                    // Silently ignore — missing write permission, disk full, etc.
                }
            }
        }

        /// <summary>
        /// Validates and clamps PollingIntervalSeconds to the allowed range (5-120).
        /// Returns the corrected value.
        /// </summary>
        public int GetValidatedPollingInterval()
        {
            int val = PollingIntervalSeconds;
            if (val < 5) val = 5;
            if (val > 120) val = 120;
            return val;
        }

        /// <summary>
        /// Resets all fields to their default values and saves.
        /// </summary>
        public void ResetToDefaults()
        {
            lock (lockObj)
            {
                SetDefaults();
                Save();
            }
        }

        // ── Private helpers ──

        /// <summary>
        /// Toggles ShowNotificationSound and saves to disk immediately.
        /// Returns the new value.
        /// </summary>
        public bool ToggleNotificationSound()
        {
            lock (lockObj)
            {
                ShowNotificationSound = !ShowNotificationSound;
                Save();
                return ShowNotificationSound;
            }
        }

        private void SetDefaults()
        {
            Enabled = true;
            PollingIntervalSeconds = 10;
            NotificationTimeoutSeconds = 5;
            ShowNotificationSound = false;
        }

        private void Apply(TrackerConfig other)
        {
            Enabled = other.Enabled;
            PollingIntervalSeconds = Clamp(other.PollingIntervalSeconds, 5, 120);
            NotificationTimeoutSeconds = Math.Max(1, other.NotificationTimeoutSeconds);
            ShowNotificationSound = other.ShowNotificationSound;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
