using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AchievementTracker.Services;
using AchievementTracker.UI;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AchievementTracker
{
    public class AchievementTrackerPlugin : GenericPlugin
    {
        public override Guid Id { get; } = Guid.Parse("11ab4f7c-389f-43e5-9f5b-11c5d9a911ab");

        // Keep track of created controls to push game context to them
        private AchievementTrackerControl _currentControl;

        // Achievement tracking manager for real-time unlock detection
        private AchievementTrackerManager _trackerManager;

        // Notification history for deduplication across sessions (US-007)
        private AchievementTracker.Services.NotificationHistory _notificationHistory;

        // Config system for polling interval, enable/disable, and notification timeout (US-008)
        private AchievementTracker.Settings.TrackerConfig _config;

        public AchievementTrackerPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            AddCustomElementSupport(new AddCustomElementSupportArgs
            {
                ElementList = new List<string> { "MainControl" },
                SourceName = "AchievementTracker"
            });
        }

        /// <summary>
        /// Loads config from disk. Creates default config if file does not exist.
        /// Called on first game launch (lazy-init). Supports reload without restart. (US-008)
        /// </summary>
        private void LoadConfig()
        {
            try
            {
                _config = new AchievementTracker.Settings.TrackerConfig(PlayniteApi, PlayniteApi.Paths.ExtensionsDataPath);
            }
            catch (Exception ex)
            {
                Log(string.Format("Failed to load config, using defaults: {0}", ex.Message));
                _config = new AchievementTracker.Settings.TrackerConfig(PlayniteApi, PlayniteApi.Paths.ExtensionsDataPath);
            }
        }

        /// <summary>
        /// Opens the config file in Explorer for manual editing. (US-008)
        /// </summary>
        public void OpenSettings()
        {
            if (_config == null)
            {
                LoadConfig();
            }

            if (_config != null)
            {
                _config.Save();
            }

            try
            {
                var configPath = System.IO.Path.Combine(
                    PlayniteApi.Paths.ExtensionsDataPath,
                    "achievement_tracker_config.json");
                var dir = System.IO.Path.GetDirectoryName(configPath);
                if (!System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }

                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + configPath + "\"");
            }
            catch (Exception ex)
            {
                Log(string.Format("Failed to open settings: {0}", ex.Message));
            }
        }

        public override Control GetGameViewControl(GetGameViewControlArgs args)
        {
            Log($"GetGameViewControl called: args.Name='{args.Name}'");

            if (args.Name == "MainControl")
            {
                var control = new AchievementTrackerControl(PlayniteApi);
                _currentControl = control;

                // If there's already a selected game, load it immediately
                var selected = PlayniteApi.MainView?.SelectedGames?.FirstOrDefault();
                if (selected != null)
                {
                    Log($"Already has selected game: {selected.Name}");
                    control.SetGame(selected);
                }

                // Pass config if already initialized
                if (_config != null)
                {
                    control.SetConfig(_config);
                }

                return control;
            }
            return null;
        }

        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            var game = args.NewValue?.FirstOrDefault();
            Log($"OnGameSelected: {game?.Name ?? "NULL"}, control={(_currentControl != null ? "exists" : "null")}");

            if (_currentControl != null)
            {
                _currentControl.Dispatcher.Invoke(() =>
                {
                    _currentControl.SetGame(game);
                });
            }
        }

        private void Log(string message)
        {
            try
            {
                System.IO.File.AppendAllText(@"C:\Games\_PLAYNITE\plugin_debug.log",
                    $"{DateTime.Now:HH:mm:ss} | {message}{Environment.NewLine}");
            }
            catch { }
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            return new List<GameMenuItem>
            {
                new GameMenuItem
                {
                    Description = "View Achievements (Window)",
                    MenuSection = "Achievement Tracker",
                    Action = (a) =>
                    {
                        var game = args.Games.FirstOrDefault();
                        if (game != null)
                        {
                            var window = new AchievementsWindow(PlayniteApi, game);
                            window.ShowDialog();
                        }
                    }
                },
                new GameMenuItem
                {
                    Description = "Debug Info",
                    MenuSection = "Achievement Tracker",
                    Action = (a) =>
                    {
                        var game = args.Games.FirstOrDefault();
                        if (game != null)
                        {
                            var window = new DebugWindow(PlayniteApi, game);
                            window.ShowDialog();
                        }
                    }
                },
                new GameMenuItem
                {
                    Description = "Debug Achievement Tracker",
                    MenuSection = "Achievement Tracker",
                    Action = (a) =>
                    {
                        var game = args.Games.FirstOrDefault();
                        if (game != null)
                        {
                            var window = new DebugTrackingWindow(PlayniteApi, game, _trackerManager, _config);
                            window.ShowDialog();
                        }
                    }
                }
            };
        }
        /// <summary>
        /// Called by Playnite when a game is launched.
        /// Automatically starts achievement tracking for the launched game.
        /// If tracking is already active for another game, it stops that first.
        /// </summary>
        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            var game = args?.Game ?? PlayniteApi?.MainView?.SelectedGames?.FirstOrDefault();
            if (game == null) return;

            Log(string.Format("OnGameStarted: {0}", game.Name));

            // Lazy-init the config on first use (US-008)
            if (_config == null)
            {
                LoadConfig();
            }

            // Lazy-init the notification history on first use (US-007)
            if (_notificationHistory == null)
            {
                _notificationHistory = new AchievementTracker.Services.NotificationHistory(
                    PlayniteApi.Paths.ExtensionsDataPath);
            }

            // Lazy-init the manager on first use, passing config and history
            if (_trackerManager == null)
            {
                _trackerManager = new AchievementTrackerManager(PlayniteApi, cfg: _config, history: _notificationHistory);
            }

            // StartTracking already calls StopTracking internally if active
            _trackerManager.StartTracking(game);

            // Push config to control so sound settings appear in UI
            if (_currentControl != null && _config != null)
            {
                _currentControl.Dispatcher.Invoke(() =>
                {
                    _currentControl.SetConfig(_config);
                });
            }

            // Wire up achievement unlock event for future notification display
            _trackerManager.AchievementUnlocked += OnAchievementUnlocked;
        }

        /// <summary>
        /// Called by Playnite when a game is closed.
        /// Stops achievement tracking and unwires event handlers.
        /// </summary>
        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            var game = args?.Game;
            Log(string.Format("OnGameStopped: {0}", game != null ? game.Name : "NULL"));

            if (_trackerManager != null)
            {
                _trackerManager.AchievementUnlocked -= OnAchievementUnlocked;
                _trackerManager.StopTracking();
            }
        }

        /// <summary>
        /// Called when the tracking manager detects a newly unlocked achievement.
        /// Shows a banner notification for each unlock via Dispatcher.Invoke on the UI thread.
        /// (US-006)
        /// </summary>
        private void OnAchievementUnlocked(object sender, List<AchievementTracker.Models.Achievement> newUnlocks)
        {
            foreach (var ach in newUnlocks)
            {
                Log(string.Format("Achievement unlocked: {0} - {1}", ach.Name, ach.Description));

                // Show notification on the UI thread via WPF Application dispatcher (US-006)
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ShowAchievementNotification(ach);
                });
            }
        }

        /// <summary>
        /// Displays an AchievementNotificationWindow for the given achievement.
        /// Each window manages its own fade-in, timeout, and fade-out lifecycle.
        /// </summary>
        private void ShowAchievementNotification(AchievementTracker.Models.Achievement achievement)
        {
            try
            {
                bool playSound = _config != null && _config.ShowNotificationSound;
                double volume = _config != null ? _config.NotificationVolumePercent : 50.0;
                var window = new AchievementNotificationWindow(achievement, playSound: playSound, notificationVolumePercent: volume);
                window.ShowAnimated();
            }
            catch (Exception ex)
            {
                Log(string.Format("Failed to show achievement notification: {0}", ex.Message));
            }
        }

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            yield return new SidebarItem
            {
                Title = "Achievement Progress Library",
                Type = SiderbarItemType.View,
                Icon = new System.Windows.Shapes.Path
                {
                    Data = System.Windows.Media.Geometry.Parse("M18,2H16V1H8V2H6C4.9,2 4,2.9 4,4V9C4,11.39 5.81,13.35 8.11,13.9C8.61,15.1 9.42,16.14 10.45,16.89L10,20H8V22H16V20H14L13.55,16.89C14.58,16.14 15.39,15.1 15.89,13.9C18.19,13.35 20,11.39 20,9V4C20,2.9 19.1,2 18,2M6,9V4H8V9C8,10.1 7.1,11 6,11C4.9,11 4,10.1 4,9M18,9C18,10.1 17.1,11 16,11C14.9,11 14,10.1 14,9V4H16V9"),
                    Fill = System.Windows.Media.Brushes.White,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    Width = 20,
                    Height = 20
                },
                Opened = () => new SidebarView(PlayniteApi)
            };
        }
    }
}
