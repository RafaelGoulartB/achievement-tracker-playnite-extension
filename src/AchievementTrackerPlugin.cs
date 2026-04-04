using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace AchievementTracker
{
    public class AchievementTrackerPlugin : GenericPlugin
    {
        public override Guid Id { get; } = Guid.Parse("11ab4f7c-389f-43e5-9f5b-11c5d9a911ab");

        public AchievementTrackerPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties
            {
                HasSettings = false
            };
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            return new List<GameMenuItem>
            {
                new GameMenuItem
                {
                    Description = "View Achievements",
                    Action = (a) =>
                    {
                        var game = args.Games.FirstOrDefault();
                        if (game != null)
                        {
                            ShowAchievementsWindow(game);
                        }
                    }
                }
            };
        }

        private void ShowAchievementsWindow(Game game)
        {
            var window = new UI.AchievementsWindow(PlayniteApi, game);
            window.ShowDialog();
        }
    }
}
