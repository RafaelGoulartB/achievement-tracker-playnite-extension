using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AchievementTracker.UI;

namespace AchievementTracker
{
    public class AchievementTrackerPlugin : GenericPlugin
    {
        public override Guid Id { get; } = Guid.Parse("11ab4f7c-389f-43e5-9f5b-11c5d9a911ab");

        // Keep track of created controls to push game context to them
        private AchievementTrackerControl _currentControl;

        public AchievementTrackerPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties
            {
                HasSettings = false
            };

            AddCustomElementSupport(new AddCustomElementSupportArgs
            {
                ElementList = new List<string> { "MainControl" },
                SourceName = "AchievementTracker"
            });
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
                }
            };
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
