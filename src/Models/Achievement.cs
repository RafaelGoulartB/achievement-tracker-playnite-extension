using System;

namespace AchievementTracker.Models
{
    public class Achievement
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsUnlocked { get; set; }
        public DateTime? UnlockTime { get; set; }
        
        // Optional icon URL or local path
        public string IconUrl { get; set; }
    }
}
