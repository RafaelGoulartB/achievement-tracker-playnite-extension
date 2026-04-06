using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AchievementTracker.Models;

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
        private bool isDismissing;

        public AchievementNotificationWindow(Achievement achievement, int timeoutSeconds = 5)
        {
            this.timeoutSeconds = timeoutSeconds;
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
        }

        /// <summary>
        /// Positions the window at the bottom-right of the primary work area
        /// and starts the fade-in animation. Call this instead of Show().
        /// </summary>
        public void ShowAnimated()
        {
            PositionWindow();
            Show();

            // Fallback: set a fallback icon image if the Uri-based load didn't work
            if (IconImage.Source == null)
            {
                IconImage.Source = GetFallbackIcon();
            }

            // Fade in
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3))
            {
                FillBehavior = FillBehavior.HoldEnd
            };

            fadeIn.Completed += (s, e) =>
            {
                // Start auto-dismiss timer after fade-in completes
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
        /// Positions window at bottom-right corner of primary work area.
        /// </summary>
        private void PositionWindow()
        {
            var workArea = SystemParameters.WorkArea;
            double right = workArea.Right - Width - 10;
            double bottom = workArea.Bottom - Height - 10;

            Left = right;
            Top = bottom;
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

        public static void InitializeDispatcherTimer(AchievementNotificationWindow window)
        {
            // Static helper for when constructor-based timer creation is needed
        }
    }
}
