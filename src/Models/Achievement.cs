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
        public bool IsHidden { get; set; }
        public double Rarity { get; set; }
        
        // Return placeholder if hidden and not unlocked
        public string DisplayDescription => (IsHidden && !IsUnlocked) ? "Hidden Achievement" : Description;
        
        // Optional icon URL or local path
        public string IconUrl { get; set; }
    }
}
