using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AchievementTracker.Models;
using NAudio.Wave;

using System.Threading;

namespace AchievementTracker.UI
{
    /// <summary>
    /// Banner notification window displayed at bottom-right corner.
    /// Fades in, shows achievement details, then fades out and closes.
    /// Acceptance Criteria:
    /// - WindowStyle=None, AllowsTransparency=True, Topmost=True, ShowInTaskbar=False, Background=Transparent
    /// - Positioned at bottom-right of primary screen (SystemParameters.WorkArea)
    /// - Auto-dismisses after configurable timeout (default: 5 seconds) via DispatcherTimer
    /// - Fades in (0->1 in 0.3s) and fades out (1->0 in 0.5s) before closing
    /// - Click dismisses immediately
    /// </summary>
    public partial class AchievementNotificationWindow : Window
    {
        private DispatcherTimer dismissTimer;
        private readonly int timeoutSeconds;
        private readonly bool playSound;
        private readonly double notificationVolumePercent;
        private bool isDismissing;

        public AchievementNotificationWindow(Achievement achievement, int timeoutSeconds = 5, bool playSound = false, double notificationVolumePercent = 50.0)
        {
            this.timeoutSeconds = timeoutSeconds;
            this.playSound = playSound;
            this.notificationVolumePercent = notificationVolumePercent;
            InitializeComponent();

            // Populate content
            NameTextBlock.Text = achievement.Name ?? "Unknown";
            DescriptionTextBlock.Text = achievement.DisplayDescription ?? "";
            if (achievement.Rarity > 0)
            {
                RarityTextBlock.Text = string.Format("Rarity: {0:0.#}% of players", achievement.Rarity);
            }

            // Load icon image from URL or local path
            if (!string.IsNullOrEmpty(achievement.IconUrl))
            {
                try
                {
                    var uri = new Uri(achievement.IconUrl);
                    IconImage.Source = new BitmapImage(uri);
                }
                catch
                {
                    // Fallback: image remains as default dark box
                }
            }

            // Apply rarity-tier styling
            ApplyRarityTier(achievement.RarityTier);
        }

        /// <summary>
        /// Applies visual styling based on achievement rarity tier.
        /// Gold: gold border (#FFD700), gold header, trophy indicator.
        /// Silver: silver border (#C0C0C0), silver header.
        /// Bronze: bronze border (#CD7F32), bronze header.
        /// </summary>
        private void ApplyRarityTier(string tier)
        {
            string borderColor;
            string headerColor;
            bool showTrophy = false;

            if (tier == "Gold")
            {
                borderColor = "#FFD700";
                headerColor = "#FFD700";
                showTrophy = true;
            }
            else if (tier == "Silver")
            {
                borderColor = "#C0C0C0";
                headerColor = "#C0C0C0";
            }
            else
            {
                borderColor = "#CD7F32";
                headerColor = "#CD7F32";
            }

            MainBorder.BorderBrush = (Brush)new BrushConverter().ConvertFromString(borderColor);
            HeaderTextBlock.Foreground = (Brush)new BrushConverter().ConvertFromString(headerColor);

            if (showTrophy)
            {
                TrophyIndicator.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Positions the window at the top-left of the primary work area
        /// and starts the fade-in animation. Call this instead of Show().
        /// </summary>
        public void ShowAnimated()
        {
            // Play notification sound if enabled
            if (playSound)
            {
                PlayNotificationSound();
            }

            Show();

            // Fallback: set a fallback icon image if the Uri-based load didn't work
            if (IconImage.Source == null)
            {
                IconImage.Source = GetFallbackIcon();
            }

            // Reposition after layout pass has resolved dimensions
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SizeToContent = SizeToContent.Manual;
                Width = ActualWidth;
                Height = ActualHeight;
                Left = 10;
                Top = 10;
            }));

            // Fade in
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3))
            {
                FillBehavior = FillBehavior.HoldEnd
            };

            fadeIn.Completed += (s, e) =>
            {
                StartFadeOutTimer();
            };

            MainBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        /// <summary>
        /// Handles clicks on the notification by dismissing immediately.
        /// </summary>
        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            DismissImmediately();
        }

        private void StartFadeOutTimer()
        {
            if (timeoutSeconds <= 0)
            {
                BeginFadeOut();
                return;
            }

            dismissTimer = new DispatcherTimer();
            dismissTimer.Interval = TimeSpan.FromSeconds(timeoutSeconds);
            dismissTimer.Tick += (s, e) =>
            {
                dismissTimer.Stop();
                BeginFadeOut();
            };
            dismissTimer.Start();
        }

        private void DismissImmediately()
        {
            if (isDismissing) return;
            isDismissing = true;

            if (dismissTimer != null)
            {
                dismissTimer.Stop();
            }

            BeginFadeOut();
        }

        private void BeginFadeOut()
        {
            if (isDismissing) return;
            isDismissing = true;

            if (dismissTimer != null)
            {
                dismissTimer.Stop();
            }

            // Fade out animation
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.5))
            {
                FillBehavior = FillBehavior.HoldEnd
            };

            fadeOut.Completed += (s, e) =>
            {
                Close();
            };

            MainBorder.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        /// <summary>
        /// Positions the window at the top-left corner of primary work area.
        /// </summary>
        private void PositionWindow()
        {
            Left = 10;
            Top = 10;
        }

        /// <summary>
        /// Resolves the path to achievement.wav relative to the extension DLL.
        /// Returns the full path if the file exists, null otherwise.
        /// </summary>
        private static string ResolveWavPath()
        {
            try
            {
                var assemblyLocation = Assembly.GetExecutingAssembly().Location;
                var assemblyDir = Path.GetDirectoryName(assemblyLocation);
                var wavPath = Path.Combine(assemblyDir, "resources", "achievement.wav");
                if (File.Exists(wavPath))
                {
                    return wavPath;
                }
            }
            catch
            {
                // Ignore resolution errors
            }
            return null;
        }

        /// <summary>
        /// Plays the achievement notification WAV file at the configured volume.
        /// Runs on a background thread to avoid blocking UI animations.
        /// Skips playback entirely when volume is 0 or file is missing.
        /// Uses NAudio for clean volume control without dependencies on system volume.
        /// </summary>
        internal static void PlayNotificationSound(string wavPath, double volumePercent)
        {
            // Skip if volume is 0 or file doesn't exist
            if (volumePercent <= 0.0)
            {
                return;
            }

            if (string.IsNullOrEmpty(wavPath) || !File.Exists(wavPath))
            {
                return;
            }

            // Run on background thread to avoid blocking UI
            Thread soundThread = new Thread(() =>
            {
                try
                {
                    var volume = (float)(volumePercent / 100.0);
                    using (var reader = new AudioFileReader(wavPath))
                    {
                        reader.Volume = volume;
                        using (var output = new WaveOutEvent())
                        {
                            output.Init(reader);
                            output.Play();
                            // Wait for playback to complete
                            while (output.PlaybackState == PlaybackState.Playing)
                            {
                                Thread.Sleep(50);
                            }
                        }
                    }
                }
                catch
                {
                    // Graceful fallback: sound errors are silently ignored
                }
            });
            soundThread.IsBackground = true;
            soundThread.Start();
        }

        /// <summary>
        /// Plays the achievement notification sound at the configured volume.
        /// </summary>
        private void PlayNotificationSound()
        {
            var wavPath = ResolveWavPath();
            PlayNotificationSound(wavPath, notificationVolumePercent);
        }

        /// <summary>
        /// Creates a simple fallback trophy icon for when no achievement icon is available.
        /// </summary>
        private ImageSource GetFallbackIcon()
        {
            var drawingVisual = new DrawingVisual();
            using (var drawingContext = drawingVisual.RenderOpen())
            {
                // Background
                drawingContext.DrawGeometry(
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333")),
                    null,
                    new RectangleGeometry(new Rect(0, 0, 48, 48), 6, 6));

                // Trophy text indicator
                var formattedText = new FormattedText(
                    "*",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    20,
                    Brushes.Gold,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                var x = (48 - formattedText.Width) / 2;
                var y = (48 - formattedText.Height) / 3.5;
                drawingContext.DrawText(formattedText, new Point(x, y));
            }

            var bitmap = new RenderTargetBitmap(48, 48, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(drawingVisual);
            return bitmap;
        }

    }
}
