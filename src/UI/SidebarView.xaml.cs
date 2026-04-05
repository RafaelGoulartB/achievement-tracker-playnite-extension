using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using AchievementTracker.Services;

namespace AchievementTracker.UI
{
    public partial class SidebarView : UserControl
    {
        private readonly IPlayniteAPI playniteApi;
        public ObservableCollection<GameProgress> Games { get; set; } = new ObservableCollection<GameProgress>();

        public SidebarView(IPlayniteAPI api)
        {
            InitializeComponent();
            playniteApi = api;
            GamesGrid.ItemsSource = Games;
            DataContext = this;

            LoadInstalledGames();
        }

        private async void LoadInstalledGames()
        {
            GlobalProgressBar.Visibility = System.Windows.Visibility.Visible;
            TotalGamesText.Text = "Scanning library...";

            var installedGames = playniteApi.Database.Games
                .Where(g => g.IsInstalled)
                .OrderBy(g => g.Name)
                .ToList();

            TotalGamesText.Text = $"Found {installedGames.Count} installed games. Scanning achievements...";

            var scanner = new AchievementScanner(playniteApi);
            var results = new List<GameProgress>();

            await Task.Run(() =>
            {
                foreach (var game in installedGames)
                {
                    try
                    {
                        var achievements = scanner.ScanForAchievements(game);
                        if (achievements.Count > 0)
                        {
                            var unlocked = achievements.Count(a => a.IsUnlocked);
                            var total = achievements.Count;
                            var perc = total > 0 ? (double)unlocked / total * 100.0 : 0;

                            results.Add(new GameProgress
                            {
                                GameId = game.Id.ToString(),
                                GameName = game.Name,
                                CoverPath = string.IsNullOrEmpty(game.CoverImage) ? null : playniteApi.Database.GetFullFilePath(game.CoverImage),
                                Percentage = perc,
                                UnlockedCount = unlocked,
                                TotalCount = total
                            });
                        }
                    }
                    catch { }
                }
            });

            // Sort by percentage descending
            var sorted = results.OrderByDescending(r => r.Percentage).ThenBy(r => r.GameName).ToList();

            Games.Clear();
            foreach (var r in sorted)
            {
                Games.Add(r);
            }

            TotalGamesText.Text = $"Showing {Games.Count} games with achievements.";
            GlobalProgressBar.Visibility = System.Windows.Visibility.Collapsed;
        }
    }

    public class GameProgress
    {
        public string GameId { get; set; }
        public string GameName { get; set; }
        public string CoverPath { get; set; }
        public double Percentage { get; set; }
        public int UnlockedCount { get; set; }
        public int TotalCount { get; set; }
        public string StatusText => $"{Percentage:0.#}% ({UnlockedCount}/{TotalCount})";
        public double PercentageRemaining => 100.0 - Percentage;
        
        public double ProgressBarWidth => 144.0 * (Percentage / 100.0); // 160 card width - 16 padding
    }
}
