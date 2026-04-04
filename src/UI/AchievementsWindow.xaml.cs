using System.Windows;
using AchievementTracker.Services;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace AchievementTracker.UI
{
    public partial class AchievementsWindow : Window
    {
        private readonly IPlayniteAPI playniteApi;
        private readonly Game game;

        public AchievementsWindow(IPlayniteAPI api, Game game)
        {
            InitializeComponent();
            playniteApi = api;
            this.game = game;

            LoadData();
        }

        private void LoadData()
        {
            GameTitleText.Text = $"{game.Name} - Achievements";

            var scanner = new AchievementScanner(playniteApi);
            var achievements = scanner.ScanForAchievements(game);

            DebugInfoText.Text = $"Detected Steam AppId: {scanner.DetectedId ?? "None Found"} | Found {achievements.Count} items";

            if (achievements.Count > 0)
            {
                AchievementsList.ItemsSource = achievements;
                EmptyStateText.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyStateText.Visibility = Visibility.Visible;
            }
        }
    }
}
