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

        /// <summary>
        /// Classifies achievement into visual rarity tier for notification styling.
        /// Gold: rarity <= 5%, Silver: 5% < rarity <= 25%, Bronze: rarity > 25% or unknown (0).
        /// </summary>
        public string RarityTier
        {
            get
            {
                if (Rarity <= 0) return "Bronze";
                if (Rarity <= 5.0) return "Gold";
                if (Rarity <= 25.0) return "Silver";
                return "Bronze";
            }
        }
    }
}
