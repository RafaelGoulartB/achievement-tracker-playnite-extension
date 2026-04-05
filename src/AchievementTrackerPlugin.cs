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
                    Action = (a) =>
                    {
                        var game = args.Games.FirstOrDefault();
                        if (game != null)
                        {
                            var window = new AchievementsWindow(PlayniteApi, game);
                            window.ShowDialog();
                        }
                    }
                }
            };
        }
    }
}
