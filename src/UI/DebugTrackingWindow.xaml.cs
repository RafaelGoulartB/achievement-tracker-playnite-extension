using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AchievementTracker.Models;
using AchievementTracker.Services;
using Playnite.SDK;
using Playnite.SDK.Models;
using System.Windows.Media;

namespace AchievementTracker.UI
{
    /// <summary>
    /// Debug window for inspecting the live state of the achievement tracking system.
    /// US-001: Accessible from game context menu, receives PlayniteApi and Game as constructor params.
    /// </summary>
    public partial class DebugTrackingWindow : Window
    {
        private readonly IPlayniteAPI playniteApi;
        private readonly Game game;
        private DispatcherTimer refreshTimer;
        private TrackerConfig config;
        private AchievementTrackerManager manager;
        private NotificationHistory notificationHistory;
        private readonly string debugLogPath;

        // Snapshot diff tracking (US-004)
        private int lastDiffCount = 0;

        public DebugTrackingWindow(IPlayniteAPI api, Game game, AchievementTrackerManager manager = null)
        {
            InitializeComponent();
            playniteApi = api;
            this.game = game;
            this.manager = manager;

            debugLogPath = Path.Combine(playniteApi.Paths.ExtensionsDataPath, "achievement_tracker_debug.log");

            if (game == null)
            {
                MessageBox.Show("Select a game first", "Debug Achievement Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
                Dispatcher.InvokeAsync(() => Close());
                return;
            }

            GameNameText.Text = game.Name;
            TrackingStatusText.Text = "Tracking: INACTIVE";
            TrackingStatusText.Foreground = System.Windows.Media.Brushes.Red;
            AppIdText.Text = "—";
            IntervalText.Text = "—";
            LastScanText.Text = "—";

            // Load config for US-007
            EnsureConfig();
            RefreshConfigDisplay();

            UpdateButtonStates();
            LoadExistingLog();
        }

        // ─────────────────────────────────────────────────────────────
        // Window lifecycle
        // ─────────────────────────────────────────────────────────────

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            refreshTimer.Tick += OnRefreshTimerTick;
            refreshTimer.Start();
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            if (refreshTimer != null)
            {
                refreshTimer.Stop();
                refreshTimer = null;
            }
        }

        private void OnRefreshTimerTick(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshStatusHeader();
                AppendNewLogEntries();
                RefreshDiffView();
            });
        }

        // ─────────────────────────────────────────────────────────────
        // US-002: Tracking status header refresh
        // ─────────────────────────────────────────────────────────────

        private void RefreshStatusHeader()
        {
            if (game == null) return;

            // Update AppId and interval from manager if tracking is active
            if (manager != null && manager.IsTracking)
            {
                // Use manager's cached AppId instead of re-scanning
                AppIdText.Text = manager.CurrentAppId ?? "— not detected —";

                // Update config-dependent displays
                if (config != null)
                {
                    IntervalText.Text = config.GetValidatedPollingInterval() + "s";
                }

                TrackingStatusText.Text = "Tracking: ACTIVE";
                TrackingStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;

                // Use manager's last scan time
                var scanTime = manager.LastScanTime;
                if (scanTime.HasValue)
                {
                    LastScanText.Text = scanTime.Value.ToString("HH:mm:ss");
                }
            }
            else
            {
                // Still try to detect AppId for display
                var scanner = new AchievementScanner(playniteApi);
                var dbg = new ScanDebugInfo();
                var appId = scanner.GetSteamAppId(game, dbg);
                AppIdText.Text = appId ?? "—";

                if (config != null)
                {
                    IntervalText.Text = config.GetValidatedPollingInterval() + "s";
                }

                TrackingStatusText.Text = "Tracking: INACTIVE";
                TrackingStatusText.Foreground = System.Windows.Media.Brushes.Red;

                // Even when inactive, show last scan time if available from previous tracking session
                if (manager != null && manager.LastScanTime.HasValue)
                {
                    LastScanText.Text = manager.LastScanTime.Value.ToString("HH:mm:ss");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // US-003: Log file reading
        // ─────────────────────────────────────────────────────────────

        private int lastLineCount = 0;

        private void LoadExistingLog()
        {
            try
            {
                if (File.Exists(debugLogPath))
                {
                    var lines = File.ReadAllLines(debugLogPath);
                    lastLineCount = lines.Length;

                    var linesList = new List<string>(lines);
                    if (linesList.Count > 200)
                    {
                        linesList = linesList.GetRange(linesList.Count - 200, 200);
                        AppendLogText("[... truncated, showing last 200 lines ...]" + Environment.NewLine);
                    }

                    foreach (var line in linesList)
                    {
                        AppendLogLine(line);
                    }
                }
            }
            catch
            {
                AppendLogText("[Could not read debug log file]", LogEntryType.Error);
            }
        }

        private void AppendNewLogEntries()
        {
            try
            {
                if (!File.Exists(debugLogPath)) return;

                // Quick check: has file grown?
                var info = new FileInfo(debugLogPath);
                // Read all lines and check count
                var allLines = File.ReadAllLines(debugLogPath);
                int currentCount = allLines.Length;

                if (currentCount > lastLineCount)
                {
                    for (int i = lastLineCount; i < currentCount; i++)
                    {
                        AppendLogLine(allLines[i]);
                    }
                    lastLineCount = currentCount;
                }
            }
            catch { }
        }

        private void AppendLogLine(string line)
        {
            var entryType = LogEntryType.Info;

            if (line.Contains("ERROR") || line.Contains("FAIL") || line.Contains("failed"))
                entryType = LogEntryType.Error;
            else if (line.Contains("NEW") || line.Contains("unlock") || line.Contains("TEST"))
                entryType = LogEntryType.New;
            else if (line.Contains("diff") || line.Contains("Scan") || line.Contains("poll"))
                entryType = LogEntryType.Diff;

            if (entryType == LogEntryType.New || entryType == LogEntryType.Diff)
            {
                // (refresh header will display updated timestamp from manager on next timer tick)
            }

            var prefix = entryType == LogEntryType.Error ? "[ERROR] " :
                         entryType == LogEntryType.New ? "[NEW   ] " :
                         entryType == LogEntryType.Diff ? "[DIFF  ] " : "[INFO  ] ";

            AppendLogText(prefix + line + Environment.NewLine, entryType);
        }

        private void AppendLogText(string text)
        {
            AppendLogText(text, LogEntryType.Info);
        }

        private void AppendLogText(string text, LogEntryType type)
        {
            var run = new System.Windows.Documents.Run(text);

            switch (type)
            {
                case LogEntryType.New:
                    run.Foreground = System.Windows.Media.Brushes.LightGreen;
                    break;
                case LogEntryType.Diff:
                    run.Foreground = System.Windows.Media.Brushes.LightYellow;
                    break;
                case LogEntryType.Error:
                    run.Foreground = System.Windows.Media.Brushes.Salmon;
                    break;
                default:
                    run.Foreground = System.Windows.Media.Brushes.White;
                    break;
            }

            LogTextBlock.Inlines.Add(run);

            // Auto-scroll to bottom
            LogScrollViewer.Dispatcher.BeginInvoke(new Action(() =>
            {
                LogScrollViewer.ScrollToEnd();
            }));
        }

        private void OnClearLog(object sender, RoutedEventArgs e)
        {
            LogTextBlock.Inlines.Clear();

            try
            {
                if (File.Exists(debugLogPath))
                {
                    File.WriteAllText(debugLogPath, "");
                }
            }
            catch { }

            lastLineCount = 0;
            AppendLogText("[Log cleared]", LogEntryType.Info);
        }

        // ─────────────────────────────────────────────────────────────
        // US-004: Snapshot diff view
        // ─────────────────────────────────────────────────────────────

        private void RefreshDiffView()
        {
            List<SnapshotDiffEntry> entries;

            if (manager != null && manager.IsTracking)
            {
                entries = manager.GetSnapshotDiffInfo();

                // Color assignment
                foreach (var e in entries)
                {
                    e.PreviousColor = e.PreviousState == "Locked" ? "Salmon" : "LightGreen";
                    e.CurrentColor = e.CurrentState == "Unlocked" ? "LightGreen" : "Salmon";
                }

                // Detect if new diffs appeared since last check
                if (entries.Count != lastDiffCount)
                {
                    lastDiffCount = entries.Count;
                    AppendLogText("[DIFF  ] Snapshot diff updated (" + entries.Count + " changes)", LogEntryType.Diff);
                }
            }
            else
            {
                entries = new List<SnapshotDiffEntry>();
            }

            if (entries.Count == 0)
            {
                DiffListBox.Visibility = Visibility.Collapsed;
                DiffPlaceholderText.Visibility = Visibility.Visible;
                DiffPlaceholderText.Text = "No tracking data available";
            }
            else
            {
                DiffListBox.Visibility = Visibility.Visible;
                DiffPlaceholderText.Visibility = Visibility.Collapsed;
                DiffListBox.ItemsSource = null;
                DiffListBox.ItemsSource = entries;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // US-005: Send test notification
        // ─────────────────────────────────────────────────────────────

        private void OnTestNotification(object sender, RoutedEventArgs e)
        {
            if (game == null)
            {
                MessageBox.Show("Select a game first", "Debug Achievement Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TestNotificationBtn.IsEnabled = false;
            TestNotificationBtn.Content = "Scanning...";

            var scanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };

            EventHandler tickHandler = null;
            tickHandler = (s, e2) =>
            {
                try
                {
                    var scanner = new AchievementScanner(playniteApi);
                    var achievements = scanner.ScanForAchievements(game);

                    Achievement target = null;
                    foreach (var ach in achievements)
                    {
                        if (!ach.IsUnlocked)
                        {
                            target = ach;
                            break;
                        }
                    }

                    // If all unlocked, use last as fallback
                    if (target == null && achievements.Count > 0)
                    {
                        target = achievements[achievements.Count - 1];
                    }

                    if (target == null)
                    {
                        MessageBox.Show("No achievements found for this game", "Debug Achievement Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        // Use the same notification pipeline as OnAchievementUnlocked
                        ShowTestNotification(target);

                        // Log entry
                        var logMsg = string.Format("TEST: Sent test notification for achievement '{0}'", target.Name);
                        try
                        {
                            var logPath = Path.Combine(playniteApi.Paths.ExtensionsDataPath, "achievement_tracker_debug.log");
                            File.AppendAllText(logPath, string.Format("[{0}] {1}{2}", DateTime.Now.ToString("HH:mm:ss"), logMsg, Environment.NewLine));
                        }
                        catch { }

                        AppendLogText("[TEST  ] " + logMsg, LogEntryType.New);
                    }
                }
                catch (Exception ex)
                {
                    AppendLogText("[ERROR] Test notification failed: " + ex.Message, LogEntryType.Error);
                }
                finally
                {
                    TestNotificationBtn.IsEnabled = true;
                    TestNotificationBtn.Content = "Send Test Notification";
                    scanTimer.Stop();
                }
            };

            scanTimer.Tick += tickHandler;
            scanTimer.Start();
        }

        private void ShowTestNotification(Achievement achievement)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    var window = new AchievementNotificationWindow(achievement);
                    window.ShowAnimated();
                }
                catch (Exception ex)
                {
                    AppendLogText("[ERROR] Failed to show test notification: " + ex.Message, LogEntryType.Error);
                }
            });
        }

        // ─────────────────────────────────────────────────────────────
        // US-006: Manual tracking start/stop
        // ─────────────────────────────────────────────────────────────

        private void OnStartTracking(object sender, RoutedEventArgs e)
        {
            if (game == null) return;
            if (manager != null && manager.IsTracking) return;

            EnsureManager();

            if (manager != null)
            {
                manager.StartTracking(game);
                AppendLogText("[INFO  ] Tracking started for: " + game.Name, LogEntryType.Info);
                UpdateButtonStates();
            }
        }

        private void OnStopTracking(object sender, RoutedEventArgs e)
        {
            if (manager == null || !manager.IsTracking) return;

            manager.StopTracking();
            AppendLogText("[INFO  ] Tracking stopped.", LogEntryType.Info);
            UpdateButtonStates();
        }

        // ─────────────────────────────────────────────────────────────
        // US-007: Config display and edit
        // ─────────────────────────────────────────────────────────────

        private void EnsureConfig()
        {
            if (config == null)
            {
                try
                {
                    config = new TrackerConfig(playniteApi.Paths.ExtensionsDataPath);
                }
                catch { }
            }
        }

        private void RefreshConfigDisplay()
        {
            EnsureConfig();
            if (config == null) return;

            ConfigEnabledText.Text = config.Enabled ? "Yes" : "No";
            ConfigEnabledText.Foreground = config.Enabled ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.Salmon;

            PollingIntervalBox.Text = config.PollingIntervalSeconds.ToString();
            NotificationTimeoutBox.Text = config.NotificationTimeoutSeconds.ToString();
            ConfigSoundText.Text = config.ShowNotificationSound ? "Yes (reserved)" : "No";
        }

        private void OnReloadConfig(object sender, RoutedEventArgs e)
        {
            EnsureConfig();
            if (config == null) return;

            config.Load();
            RefreshConfigDisplay();
            AppendLogText("[INFO  ] Config reloaded from disk.", LogEntryType.Info);
        }

        private void OnSaveConfig(object sender, RoutedEventArgs e)
        {
            EnsureConfig();
            if (config == null) return;

            // Validate and apply polling interval
            int newInterval;
            if (!int.TryParse(PollingIntervalBox.Text, out newInterval) || newInterval < 5 || newInterval > 120)
            {
                MessageBox.Show("Polling interval must be between 5 and 120 seconds.", "Save Config",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                PollingIntervalBox.Text = config.GetValidatedPollingInterval().ToString();
                return;
            }

            config.PollingIntervalSeconds = newInterval;

            // Validate and apply notification timeout
            int newTimeout;
            if (!int.TryParse(NotificationTimeoutBox.Text, out newTimeout) || newTimeout < 1)
            {
                MessageBox.Show("Notification timeout must be at least 1 second.", "Save Config",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                NotificationTimeoutBox.Text = config.NotificationTimeoutSeconds.ToString();
                return;
            }

            config.NotificationTimeoutSeconds = newTimeout;

            config.Save();
            AppendLogText("[INFO  ] Config saved to disk (interval=" + newInterval + "s, timeout=" + newTimeout + "s).", LogEntryType.Info);
            MessageBox.Show("Configuration saved successfully.", "Save Config",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void EnsureManager()
        {
            if (manager == null)
            {
                // Lazy-init config
                EnsureConfig();

                // Lazy-init notification history
                if (notificationHistory == null)
                {
                    try
                    {
                        notificationHistory = new NotificationHistory(playniteApi.Paths.ExtensionsDataPath);
                    }
                    catch { }
                }

                // Create manager (same as OnGameStarted does)
                try
                {
                    manager = new AchievementTrackerManager(playniteApi, cfg: config, history: notificationHistory);
                }
                catch (Exception ex)
                {
                    AppendLogText("[ERROR] Failed to create manager: " + ex.Message, LogEntryType.Error);
                }
            }
        }

        private void UpdateButtonStates()
        {
            bool isActive = manager != null && manager.IsTracking;
            StartTrackingBtn.IsEnabled = !isActive;
            StopTrackingBtn.IsEnabled = isActive;
        }

        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────

        private enum LogEntryType { Info, New, Diff, Error }
    }
}
