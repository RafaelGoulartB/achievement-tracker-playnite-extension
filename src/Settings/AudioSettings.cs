using System.Collections.Generic;
using System.Linq;
using System.IO;
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using AchievementTracker.Models;

namespace AchievementTracker.Settings
{
    /// <summary>
    /// Settings for notification audio playback.
    /// Users can select from available sound files for achievement notifications.
    /// </summary>
    public class AudioSettings
    {
        private static readonly object lockObj = new object();

        /// <summary>
        /// Directory paths to scan for notification sound files.
        /// Default locations include App Data and Plugin directory.
        /// </summary>
        public List<string> SoundDirectories { get; set; } = null!;

        /// <summary>
        /// User-selected sound file path to play for notifications.
        /// Falls back to default if empty or file doesn't exist.
        /// </summary>
        public string SelectedSoundPath { get; set; } = null!;

        /// <summary>
        /// List of discovered sound files for selection in UI.
        /// </summary>
        internal List<AudioSoundFile> DiscoveredSounds { get; set; } = null!;

        /// <summary>
        /// Whether notification sounds are enabled.
        /// </summary>
        public bool EnableSounds { get; set; } = true;

        /// <summary>
        /// List of discovered sound files from configured directories.
        /// Cached to avoid repeated disk I/O.
        /// </summary>
        private List<AudioSoundFile> _discoveredSounds = null!;

        /// <summary>
        /// Default sound path that's used if no custom sound is selected.
        /// </summary>
        public static string DefaultSoundPath => Path.Combine(
            typeof(AudioSettings).Assembly.Location,
            "notification.wav"
        );

        /// <summary>
        /// Default sound directories - used as fallback if config not found.
        /// </summary>
        internal static readonly List<string> DefaultDirectories = new List<string>
        {
            @"%APPDATA%\AchievementTracker\Sounds",
            @"%USERPROFILE%\Music\AchievementTracker",
            @"C:\Sounds\Achievements"
        };

        /// <summary>
        /// Default sound file name.
        /// </summary>
        private const string DefaultFileName = "notification.wav";

        /// <summary>
        /// Gets the configured audio settings, loading from file if needed.
        /// </summary>
        public static AudioSettings Current { get; private set; } = null!;

        /// <summary>
        /// Initializes the static Current instance from the config file.
        /// </summary>
        public static void Initialize(string configPath)
        {
            lock (lockObj)
            {
                // First check extension config
                if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
                {
                    try
                    {
                        var json = File.ReadAllText(configPath);
                        var trackerConfig = JsonConvert.DeserializeObject<TrackerConfig>(json);
                        if (trackerConfig != null)
                        {
                            Current = new AudioSettings
                            {
                                SoundDirectories = trackerConfig.SoundDirectories ?? DefaultDirectories,
                                SelectedSoundPath = trackerConfig.SelectedSoundPath ?? DefaultSoundPath,
                                EnableSounds = trackerConfig.ShowNotificationSound
                            };
                            Current.DiscoveredSounds = trackerConfig.DiscoverSoundFiles();
                            return;
                        }
                    }
                    catch
                    {
                        // Ignore deserialization errors
                    }
                }

                // Use defaults if no config file exists
                Current = new AudioSettings
                {
                    SoundDirectories = DefaultDirectories,
                    SelectedSoundPath = DefaultSoundPath,
                    EnableSounds = true
                };
            }
        }

        /// <summary>
        /// Returns a list of available sound files from discovered directories.
        /// Supports WAV, MP3, OGG formats.
        /// </summary>
        public List<AudioSoundFile> GetAvailableSounds()
        {
            lock (lockObj)
            {
                if (_discoveredSounds != null)
                {
                    return _discoveredSounds;
                }

                var directories = SoundDirectories ?? DefaultDirectories;
                var sounds = new List<AudioSoundFile>();

                foreach (var dir in directories)
                {
                    try
                    {
                        var envVars = dir.Split(new[] { '%' }, StringSplitOptions.RemoveEmptyEntries)
                                          .Select((x, i) => i == 0 ? "" : Environment.GetEnvironmentVariable(x))
                                          .Aggregate((a, b) => a + b);

                        if (!Directory.Exists(envVars))
                        {
                            continue;
                        }

                        string[] supportedExtensions = { ".wav", ".mp3", ".ogg" };

                        foreach (var file in Directory.GetFiles(envVars))
                        {
                            var extension = Path.GetExtension(file).ToLowerInvariant();
                            if (supportedExtensions.Contains(extension))
                            {
                                sounds.Add(new AudioSoundFile
                                {
                                    Name = Path.GetFileName(file),
                                    Path = file
                                });
                            }
                        }
                    }
                    catch
                    {
                        // Ignore errors in directory enumeration
                    }
                }

                // Sort by name for consistent dropdown ordering
                _discoveredSounds = sounds.OrderBy(s => s.Name).ToList();
                return _discoveredSounds;
            }
        }

        /// <summary>
        /// Checks if the selected sound file exists and is valid.
        /// </summary>
        public bool IsValidSelectedSound()
        {
            if (string.IsNullOrEmpty(SelectedSoundPath))
            {
                return false;
            }

            return File.Exists(SelectedSoundPath);
        }

        /// <summary>
        /// Gets the currently active sound path (selected or default).
        /// </summary>
        public string GetActiveSoundPath()
        {
            if (!string.IsNullOrEmpty(SelectedSoundPath) && IsValidSelectedSound())
            {
                return SelectedSoundPath;
            }

            return DefaultSoundPath;
        }

        /// <summary>
        /// Toggles sound enablement and persists to disk.
        /// Returns the new value.
        /// </summary>
        public bool ToggleSounds()
        {
            lock (lockObj)
            {
                EnableSounds = !EnableSounds;
                Save();
                return EnableSounds;
            }
        }

        /// <summary>
        /// Sets the selected sound path and validates it exists.
        /// Returns the new selected path or current if file doesn't exist.
        /// </summary>
        public string SetSelectedSoundPath(string soundPath)
        {
            lock (lockObj)
            {
                if (!string.IsNullOrEmpty(soundPath) && File.Exists(soundPath))
                {
                    SelectedSoundPath = soundPath;
                    Save();
                    return soundPath;
                }

                // Keep current if invalid path
                return SelectedSoundPath;
            }
        }

        /// <summary>
        /// Reloads configuration from disk.
        /// </summary>
        public void Reload(string configPath)
        {
            if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
            {
                try
                {
                    lock (lockObj)
                    {
                        var json = File.ReadAllText(configPath);
                        var loaded = JsonConvert.DeserializeObject<AudioSettings>(json);
                        if (loaded != null)
                        {
                            SoundDirectories = loaded.SoundDirectories ?? DefaultDirectories;
                            SelectedSoundPath = loaded.SelectedSoundPath ?? DefaultSoundPath;
                            EnableSounds = loaded.EnableSounds || true;
                            if (loaded.DiscoveredSounds != null)
                            {
                                DiscoveredSounds = loaded.DiscoveredSounds;
                            }
                            else
                            {
                                DiscoveredSounds = GetAvailableSounds();
                            }
                        }
                    }
                }
                catch
                {
                    // Corrupt config - ignore
                }
            }
        }

        /// <summary>
        /// Saves settings to disk.
        /// </summary>
        public void Save()
        {
            lock (lockObj)
            {
                try
                {
                    var assemblyLocation = Assembly.GetExecutingAssembly().Location;
                    var configPath = Path.Combine(
                        new DirectoryInfo(assemblyLocation).Parent.FullName,
                        "Data", "audio_config.json"
                    );

                    var settings = new
                    {
                        SoundDirectories = SoundDirectories,
                        SelectedSoundPath = SelectedSoundPath,
                        EnableSounds = EnableSounds
                    };

                    var dir = Path.GetDirectoryName(configPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                    File.WriteAllText(configPath, json);
                }
                catch
                {
                    // Silently ignore write errors
                }
            }
        }

        /// <summary>
        /// Resets audio settings to defaults and saves.
        /// </summary>
        public void ResetToDefaults()
        {
            lock (lockObj)
            {
                SoundDirectories = DefaultDirectories;
                SelectedSoundPath = DefaultSoundPath;
                EnableSounds = true;
                Save();
            }
        }
    }
}
