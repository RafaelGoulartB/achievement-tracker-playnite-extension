using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AchievementTracker.Models;
using AchievementTracker.Services;
using AchievementTracker.Settings;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace AchievementTracker.UI
{
    public partial class DebugWindow : Window
    {
        private readonly IPlayniteAPI playniteApi;
        private readonly Game game;
        private AchievementScanner scanner;
        private AudioSettings audioSettings = new AudioSettings();

        public DebugWindow(IPlayniteAPI api, Game game)
        {
            InitializeComponent();
            playniteApi = api;
            this.game   = game;

            GameTitle.Text = game.Name;
            LoadAudioSettings();
            RunScan();
        }

        private void LoadAudioSettings()
        {
            var paths = AudioSettings.DefaultDirectories ?? new List<string>();
            SoundDirectoriesText.Text = string.Join("\n• ", paths);
            AvailableSoundsText.Text = "Loading available sounds...";
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
            SteamMetadataBox.Text = dbg.SteamMetadataJson ?? "(none)";

            // Hydra request
            var hydraJson = dbg.RawHydraJson ?? dbg.HydraMetadataJson;
            HydraMetadataBox.Text = hydraJson ?? "(none)";

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

            // Local IDs/Names found
            LocalUnlocksBox.Text = dbg.LocalUnlocksFound.Count > 0
                ? string.Join(Environment.NewLine, dbg.LocalUnlocksFound)
                : "(none)";

            // Match details
            MatchHistoryBox.Text = dbg.MatchHistory.Count > 0
                ? string.Join(Environment.NewLine, dbg.MatchHistory)
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
            sb.AppendLine();
            sb.AppendLine("--- STEAM METADATA (JSON) ---");
            sb.AppendLine(dbg.SteamMetadataJson ?? "(none)");
            sb.AppendLine();
            sb.AppendLine($"Total          : {dbg.TotalAchievements}");
            sb.AppendLine($"Local Unlocked : {dbg.UnlockedLocalCount}");
            sb.AppendLine();
            sb.AppendLine("--- LOCAL FILES FOUND ---");
            foreach (var f in dbg.LocalFilesFound) sb.AppendLine(f);
            sb.AppendLine();
            sb.AppendLine("--- CHECKED (NOT FOUND) ---");
            foreach (var f in dbg.LocalFilesChecked) sb.AppendLine(f);
            sb.AppendLine();
            sb.AppendLine("--- LOCAL UNLOCKS (IDs/Names) ---");
            foreach (var s in dbg.LocalUnlocksFound) sb.AppendLine(s);
            sb.AppendLine();
            sb.AppendLine("--- MATCH HISTORY ---");
            foreach (var s in dbg.MatchHistory) sb.AppendLine(s);

            Clipboard.SetText(sb.ToString());
            MessageBox.Show("Debug info copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnPreviewSound(object sender, RoutedEventArgs e)
        {
            PreviewAudioSound();
        }

        private void OnSoundEnabledChanged(object sender, RoutedEventArgs e)
        {
            audioSettings.EnableSounds = SoundEnabledCheckBox.IsChecked.Value;
            SaveAudioSettings();
        }

        private void OnSoundSelectedChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SoundFileComboBox.SelectedItem != null)
            {
                var selectedSound = (AudioSoundFile)SoundFileComboBox.SelectedItem;
                audioSettings.SetSelectedSoundPath(selectedSound.Path);
            }
            SaveAudioSettings();
        }

        private void OnLoadSoundFiles(object sender, RoutedEventArgs e)
        {
            LoadAvailableSounds();
        }

        private void PreviewAudioSound()
        {
            if (audioSettings.EnableSounds && audioSettings.SelectedSoundPath != null && File.Exists(audioSettings.SelectedSoundPath))
            {
                try
                {
                    var soundPath = audioSettings.SelectedSoundPath;
                    var volume = (float)(50.0 / 100.0); // Default 50% volume
                    using (var reader = new NAudio.Wave.AudioFileReader(soundPath))
                    {
                        reader.Volume = volume;
                        using (var output = new NAudio.Wave.WaveOutEvent())
                        {
                            output.Init(reader);
                            output.Play();
                            while (output.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                            {
                                Thread.Sleep(50);
                            }
                        }
                    }
                    AvailableSoundsText.Text = $"Previewed: {Path.GetFileName(soundPath)}";
                }
                catch (Exception ex)
                {
                    AvailableSoundsText.Text = $"Error: {ex.Message}";
                    AvailableSoundsText.Foreground = Brushes.Orange;
                }
            }
        }

        private void LoadAvailableSounds()
        {
            var sounds = audioSettings.GetAvailableSounds();
            SoundFileComboBox.ItemsSource = sounds;

            var discoveredFiles = sounds.Select(s => s.Path).ToList();

            DiscoveredFilesText.Text = string.Join("\n• ", discoveredFiles);
            AvailableSoundsText.Text = $"Found {sounds.Count} sound files";
            AvailableSoundsText.Foreground = Brushes.Green;

            // Auto-select first available sound if no selection exists
            if (SoundFileComboBox.Items.Count > 0 && SoundFileComboBox.SelectedItem == null && sounds.Count > 0)
            {
                SoundFileComboBox.SelectedItem = sounds[0];
            }

            SoundEnabledCheckBox.IsEnabled = true;
        }

        private void SaveAudioSettings()
        {
            // Settings already saved via binding when selection changes
        }
    }
}
