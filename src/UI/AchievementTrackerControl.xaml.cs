using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AchievementTracker.Services;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace AchievementTracker.UI
{
    /// <summary>
    /// Plain UserControl (NOT PluginUserControl) to avoid Playnite SDK auto-collapse.
    /// Game context is set externally by AchievementTrackerPlugin.GetGameViewControl.
    /// </summary>
    public partial class AchievementTrackerControl : UserControl
    {
        private readonly IPlayniteAPI playniteApi;
        private readonly AchievementScanner scanner;

        public AchievementTrackerControl(IPlayniteAPI api)
        {
            InitializeComponent();
            this.playniteApi = api;
            this.scanner = new AchievementScanner(api);
            Log("Control created");
        }

        private void Log(string message)
        {
            try
            {
                var logPath = System.IO.Path.Combine(playniteApi.Paths.ExtensionsDataPath, "achievement_tracker_debug.log");
                var logDir = System.IO.Path.GetDirectoryName(logPath);
                if (!System.IO.Directory.Exists(logDir)) System.IO.Directory.CreateDirectory(logDir);
                System.IO.File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss} | {message}{Environment.NewLine}");
            }
            catch { }
        }

        public void SetGame(Game game)
        {
            Log($"SetGame called: {game?.Name ?? "NULL"}");
            if (game == null)
            {
                StatusText.Text = "No game selected";
                StatusText.Visibility = Visibility.Visible;
                return;
            }

            StatusText.Text = $"Scanning: {game.Name}...";
            StatusText.Visibility = Visibility.Visible;
            AchievementsListBox.Visibility = Visibility.Collapsed;
            UnlockCountText.Text = "0/0";
            PercentText.Text = " (0%)";
            ProgressBarFill.Width = 0;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var achievements = scanner.ScanForAchievements(game);
                    Log($"Scan complete: {achievements?.Count ?? 0} achievements for {game.Name}");

                    this.Dispatcher.Invoke(() =>
                    {
                        AchievementsListBox.ItemsSource = achievements;

                        if (achievements == null || achievements.Count == 0)
                        {
                            string msg = $"No achievements found for {game.Name}";
                            if (!string.IsNullOrEmpty(scanner.DetectedId))
                                msg += $" (SteamId: {scanner.DetectedId})";
                            
                            StatusText.Text = msg;
                            StatusText.Visibility = Visibility.Visible;
                            AchievementsListBox.Visibility = Visibility.Collapsed;
                            UnlockCountText.Text = "0/0";
                            PercentText.Text = " (0%)";
                            ProgressBarFill.Width = 0;
                        }
                        else
                        {
                            StatusText.Visibility = Visibility.Collapsed;
                            
                            int unlocked = achievements.Count(a => a.IsUnlocked);
                            int total = achievements.Count;
                            double percent = total > 0 ? (double)unlocked / total * 100 : 0;

                            UnlockCountText.Text = $"{unlocked}/{total}";
                            PercentText.Text = $" ({percent:0}%)";
                            AchievementsListBox.Visibility = Visibility.Visible;

                            // Update progress bar width after layout pass
                            AchievementsListBox.UpdateLayout();
                            if (ProgressBarFill.Parent is Grid parentGrid)
                            {
                                double maxW = parentGrid.ActualWidth;
                                if (maxW > 0)
                                    ProgressBarFill.Width = maxW * (percent / 100);
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    Log($"ERROR: {ex.Message}\n{ex.StackTrace}");
                    this.Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = "Error: " + ex.Message;
                        StatusText.Visibility = Visibility.Visible;
                        AchievementsListBox.Visibility = Visibility.Collapsed;
                    });
                }
            });
        }
    }
}
