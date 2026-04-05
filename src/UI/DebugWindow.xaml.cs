using System;
using System.Text;
using System.Windows;
using AchievementTracker.Services;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace AchievementTracker.UI
{
    public partial class DebugWindow : Window
    {
        private readonly IPlayniteAPI playniteApi;
        private readonly Game game;
        private AchievementScanner scanner;

        public DebugWindow(IPlayniteAPI api, Game game)
        {
            InitializeComponent();
            playniteApi = api;
            this.game   = game;

            GameTitle.Text = game.Name;
            RunScan();
        }

        private void RunScan()
        {
            scanner = new AchievementScanner(playniteApi);
            var achievements = scanner.ScanForAchievements(game);
            var dbg = scanner.LastDebug;

            if (dbg == null)
            {
                AppIdText.Text = "No debug info available";
                return;
            }

            // AppId
            AppIdText.Text       = dbg.DetectedAppId ?? "— not detected —";
            AppIdSourceText.Text = dbg.AppIdSource   ?? "—";

            // Steam request
            SteamUrlText.Text    = dbg.SteamRequestUrl ?? "— no request made (AppId missing) —";
            SteamCountText.Text  = dbg.SteamAchievementCount.ToString();
            SteamErrorText.Text  = dbg.SteamFetchError ?? "";

            if (string.IsNullOrEmpty(dbg.SteamRequestUrl))
            {
                SteamResultText.Text      = "⚠ Skipped";
                SteamResultText.Foreground = System.Windows.Media.Brushes.Orange;
            }
            else if (dbg.SteamFetchOk)
            {
                SteamResultText.Text      = $"✅ {dbg.SteamFetchMode} — {dbg.SteamAchievementCount} items";
                SteamResultText.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
            else
            {
                SteamResultText.Text      = $"❌ FAILED ({dbg.SteamFetchMode})";
                SteamResultText.Foreground = System.Windows.Media.Brushes.Salmon;
            }

            // Local files found
            LocalFilesFoundBox.Text = dbg.LocalFilesFound.Count > 0
                ? string.Join(Environment.NewLine, dbg.LocalFilesFound)
                : "(none)";

            // Local files checked but missing
            LocalFilesCheckedBox.Text = dbg.LocalFilesChecked.Count > 0
                ? string.Join(Environment.NewLine, dbg.LocalFilesChecked)
                : "(none)";

            // Summary
            ModeText.Text     = dbg.Mode ?? "—";
            TotalText.Text    = $"{dbg.TotalAchievements} ({achievements.Count} returned)";
            UnlockedText.Text = dbg.UnlockedLocalCount.ToString();
            ScanTimeText.Text = dbg.ScanTime ?? "—";
        }

        private void OnRefresh(object sender, RoutedEventArgs e) => RunScan();

        private void OnCopy(object sender, RoutedEventArgs e)
        {
            var dbg = scanner?.LastDebug;
            if (dbg == null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"=== Achievement Tracker Debug — {game.Name} ===");
            sb.AppendLine($"Scan Time      : {dbg.ScanTime}");
            sb.AppendLine($"Steam AppId    : {dbg.DetectedAppId ?? "not found"}");
            sb.AppendLine($"AppId Source   : {dbg.AppIdSource}");
            sb.AppendLine($"Steam URL      : {dbg.SteamRequestUrl}");
            sb.AppendLine($"Steam Fetch OK : {dbg.SteamFetchOk}");
            sb.AppendLine($"Steam Count    : {dbg.SteamAchievementCount}");
            sb.AppendLine($"Steam Error    : {dbg.SteamFetchError}");
            sb.AppendLine($"Mode           : {dbg.Mode}");
            sb.AppendLine($"Total          : {dbg.TotalAchievements}");
            sb.AppendLine($"Local Unlocked : {dbg.UnlockedLocalCount}");
            sb.AppendLine();
            sb.AppendLine("--- LOCAL FILES FOUND ---");
            foreach (var f in dbg.LocalFilesFound) sb.AppendLine(f);
            sb.AppendLine();
            sb.AppendLine("--- CHECKED (NOT FOUND) ---");
            foreach (var f in dbg.LocalFilesChecked) sb.AppendLine(f);

            Clipboard.SetText(sb.ToString());
            MessageBox.Show("Debug info copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
