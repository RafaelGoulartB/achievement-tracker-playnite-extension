namespace AchievementTracker.Models
{
    /// <summary>
    /// Data item for the snapshot diff view in the debug window.
    /// Represents a single achievement's state change between scan cycles.
    /// </summary>
    public class SnapshotDiffEntry
    {
        public string AchievementName { get; set; }
        public string PreviousState { get; set; }
        public string CurrentState { get; set; }
        public string Source { get; set; }
        public string PreviousColor { get; set; }
        public string CurrentColor { get; set; }
    }
}
