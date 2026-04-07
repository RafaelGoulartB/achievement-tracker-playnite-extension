using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AchievementTracker.Models;

namespace AchievementTracker.Services
{
    /// <summary>
    /// Manages real-time achievement tracking for a game.
    /// When tracking starts, a background timer repeatedly scans for achievements,
    /// comparing each result against the previous snapshot to detect new unlocks.
    /// </summary>
    public class AchievementTrackerManager
    {
        private readonly IPlayniteAPI playniteApi;
        private Timer pollTimer;
        private readonly object lockObj = new object();
        private bool isScanning; // reentrancy guard

        // Tracking state
        private Game currentGame;
        private string currentAppId;
        private Dictionary<string, bool> previousSnapshot; // achievementId -> wasUnlocked
        private Dictionary<string, Achievement> achievementCache; // full achievement data (Steam master list)
        private List<SnapshotDiffEntry> _diffHistory; // last scan's diff results

        // Default polling interval: 10 seconds
        private readonly TrackerConfig config;

        // Notification history for deduplication across sessions (US-007)
        private readonly NotificationHistory notificationHistory;

        /// <summary>
        /// Event raised when a new achievement unlock is detected and should be notified.
        /// Receives the list of newly unlocked achievements from the scan cycle.
        /// </summary>
        public event EventHandler<List<Achievement>> AchievementUnlocked;

        public Game CurrentGame
        {
            get { lock (lockObj) { return currentGame; } }
        }

        public bool IsTracking
        {
            get { lock (lockObj) { return pollTimer != null; } }
        }

        public string CurrentAppId
        {
            get { lock (lockObj) { return currentAppId; } }
        }

        public DateTime? LastScanTime
        {
            get { lock (lockObj) { return _lastScanTime; } }
        }

        private DateTime? _lastScanTime;

        public AchievementTrackerManager(IPlayniteAPI api, TrackerConfig cfg = null,
            NotificationHistory history = null)
        {
            playniteApi = api;
            config = cfg;
            previousSnapshot = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            achievementCache = new Dictionary<string, Achievement>(StringComparer.OrdinalIgnoreCase);
            notificationHistory = history;
        }

        /// <summary>
        /// Start tracking achievements for the given game.
        /// Stops any existing tracking before starting fresh.
        /// </summary>
        public void StartTracking(Game game)
        {
            if (game == null) return;

            // US-009: If tracking is disabled in config, skip
            if (config != null && !config.Enabled)
            {
                return;
            }

            // Stop existing tracking if active
            StopTracking();

            lock (lockObj)
            {
                currentGame = game;
            }

            // Detect Steam AppId; skip tracking if not found
            var scanner = new AchievementScanner(playniteApi);
            var scanDebug = new ScanDebugInfo { ScanTime = DateTime.Now.ToString("HH:mm:ss") };
            var appId = scanner.GetSteamAppId(game, scanDebug);

            if (string.IsNullOrEmpty(appId))
            {
                LogError(string.Format(
                    "Skipping tracking for '{0}' - no Steam AppId detected.",
                    game.Name ?? "Unknown Game"));
                lock (lockObj) { currentGame = null; }
                return;
            }

            lock (lockObj)
            {
                currentAppId = appId;
            }

            // Perform initial scan to populate baseline snapshot and achievement cache
            PerformInitialScan();

            // Create and start the polling timer
            int intervalMs = GetPollingIntervalMs();
            pollTimer = new Timer(PollTimerCallback, null, intervalMs, intervalMs);

            Log(string.Format(
                "Started tracking for '{0}' (AppId: {1}, interval: {2}s)",
                game.Name, appId, intervalMs / 1000));
        }

        /// <summary>
        /// Stop tracking. Disposes timer and clears state.
        /// Safe to call when tracking is not active.
        /// </summary>
        public void StopTracking()
        {
            Timer timerToDispose = null;

            lock (lockObj)
            {
                timerToDispose = pollTimer;
                pollTimer = null;
                currentGame = null;
                currentAppId = null;
                previousSnapshot = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                achievementCache = new Dictionary<string, Achievement>(StringComparer.OrdinalIgnoreCase);
                isScanning = false;
                _lastScanTime = null;
            }

            if (timerToDispose != null)
            {
                timerToDispose.Dispose();
                Log("Stopped tracking.");
            }
        }

        /// <summary>
        /// Perform one scan cycle and return newly unlocked achievements.
        /// Compares current state against the previous snapshot.
        /// Returns full Achievement objects with name, description, icon, rarity, etc.
        /// </summary>
        public List<Achievement> TrackOnce()
        {
            var newUnlocks = new List<Achievement>();

            Game game;
            lock (lockObj)
            {
                game = currentGame;
            }

            if (game == null) return newUnlocks;

            var scanner = new AchievementScanner(playniteApi);
            var achievements = scanner.ScanForAchievements(game);

            // Update the achievement cache with latest metadata
            UpdateAchievementCache(achievements);

            // Build current unlock snapshot
            var currentSnapshot = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var ach in achievements)
            {
                string key = GetAchievementKey(ach);
                if (!string.IsNullOrEmpty(key))
                {
                    currentSnapshot[key] = ach.IsUnlocked;
                }
            }

            // Build diff for debug window
            var diffEntries = new List<SnapshotDiffEntry>();

            // Compare: find achievements that were previously locked and are now unlocked
            foreach (var kvp in currentSnapshot)
            {
                string name = ResolveAchievementName(kvp.Key);
                bool nowUnlocked = kvp.Value;
                bool wasUnlocked;
                bool wasPreviouslyKnown = previousSnapshot.TryGetValue(kvp.Key, out wasUnlocked);

                if (nowUnlocked)
                {
                    if (!wasPreviouslyKnown)
                    {
                        // New entry that is already unlocked -> treat as new detect
                        Achievement fullAch = ResolveAchievement(kvp.Key);
                        newUnlocks.Add(fullAch);
                    }
                    else if (!wasUnlocked)
                    {
                        // Was locked, now unlocked -> new achievement!
                        Achievement fullAch = ResolveAchievement(kvp.Key);
                        newUnlocks.Add(fullAch);

                        diffEntries.Add(new SnapshotDiffEntry
                        {
                            AchievementName = name,
                            PreviousState = "Locked",
                            CurrentState = "Unlocked",
                            Source = "Tracked"
                        });
                    }
                }
                else if (wasPreviouslyKnown && wasUnlocked)
                {
                    diffEntries.Add(new SnapshotDiffEntry
                    {
                        AchievementName = name,
                        PreviousState = "Unlocked",
                        CurrentState = "Locked",
                        Source = "Tracked"
                    });
                }
            }

            _diffHistory = diffEntries;

            // Update previous snapshot
            previousSnapshot = currentSnapshot;

            return newUnlocks;
        }

        // ─────────────────────────────────────────────────────────────
        // SNAPSHOT DIFF FOR DEBUG WINDOW
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a list of achievements whose unlock state changed between
        /// the previous and current snapshots, with source attribution
        /// (Steam/Local) for use in the debug diff view.
        /// </summary>
        public List<SnapshotDiffEntry> GetSnapshotDiffInfo()
        {
            return _diffHistory ?? new List<SnapshotDiffEntry>();
        }

        private string ResolveAchievementName(string key)
        {
            Achievement ach;
            if (achievementCache.TryGetValue(key, out ach) && !string.IsNullOrEmpty(ach.Name))
            {
                return ach.Name;
            }
            return key;
        }

        // ─────────────────────────────────────────────────────────────
        // PRIVATE
        // ─────────────────────────────────────────────────────────────

        private void PerformInitialScan()
        {
            Game game;
            lock (lockObj)
            {
                game = currentGame;
            }

            if (game == null) return;

            var scanner = new AchievementScanner(playniteApi);
            var achievements = scanner.ScanForAchievements(game);

            previousSnapshot = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            achievementCache = new Dictionary<string, Achievement>(StringComparer.OrdinalIgnoreCase);

            foreach (var ach in achievements)
            {
                string key = GetAchievementKey(ach);
                if (!string.IsNullOrEmpty(key))
                {
                    previousSnapshot[key] = ach.IsUnlocked;
                    achievementCache[key] = ach;
                }
            }

            lock (lockObj)
            {
                _lastScanTime = DateTime.Now;
            }
        }

        private void UpdateAchievementCache(List<Achievement> achievements)
        {
            foreach (var ach in achievements)
            {
                string key = GetAchievementKey(ach);
                if (!string.IsNullOrEmpty(key))
                {
                    achievementCache[key] = ach;
                }
            }
        }

        private Achievement ResolveAchievement(string key)
        {
            Achievement ach;
            if (achievementCache.TryGetValue(key, out ach))
            {
                // Return a copy so the cache entry isn't mutated
                return new Achievement
                {
                    Id = ach.Id,
                    Name = ach.Name,
                    Description = ach.Description,
                    IsUnlocked = ach.IsUnlocked,
                    UnlockTime = DateTime.Now,
                    IsHidden = ach.IsHidden,
                    Rarity = ach.Rarity,
                    IconUrl = ach.IconUrl
                };
            }

            // Fallback: stub if not found in cache
            return new Achievement
            {
                Id = key,
                Name = key,
                Description = "",
                IsUnlocked = true,
                UnlockTime = DateTime.Now
            };
        }

        private string GetAchievementKey(Achievement ach)
        {
            if (!string.IsNullOrEmpty(ach.Id)) return ach.Id;
            if (!string.IsNullOrEmpty(ach.Name)) return ach.Name;
            return null;
        }

        private void Log(string msg)
        {
            try
            {
                var path = System.IO.Path.Combine(
                    playniteApi.Paths.ExtensionsDataPath,
                    "achievement_tracker_debug.log");
                System.IO.File.AppendAllText(path,
                    string.Format("[{0}] {1}{2}", DateTime.Now.ToString("HH:mm:ss"), msg, Environment.NewLine));
            }
            catch { }
        }

        private void LogError(string msg)
        {
            Log("ERROR: " + msg);
        }

        /// <summary>
        /// Returns the polling interval in milliseconds from config,
        /// or uses the default of 10 seconds if config is unavailable.
        /// </summary>
        private int GetPollingIntervalMs()
        {
            if (config != null)
            {
                return config.GetValidatedPollingInterval() * 1000;
            }
            return 10000; // default 10s
        }

        private void PollTimerCallback(object state)
        {
            // Reentrancy guard: skip if a scan is already running
            bool taken = false;
            lock (lockObj)
            {
                if (pollTimer == null) return;  // tracking was stopped
                if (currentGame == null) return;
                if (isScanning)
                {
                    taken = true;
                }
                else
                {
                    isScanning = true;
                }
            }

            if (taken)
            {
                Log("Skipping scan cycle (previous scan still running).");
                return;
            }

            try
            {
                lock (lockObj)
                {
                    _lastScanTime = DateTime.Now;
                }

                var newUnlocks = TrackOnce();

                // Filter out already-notified achievements (US-009 / US-007)
                var toNotify = new List<Achievement>();
                foreach (var ach in newUnlocks)
                {
                    string key = GetAchievementKey(ach);
                    if (!string.IsNullOrEmpty(key) && notificationHistory != null &&
                        notificationHistory.IsAlreadyNotified(currentAppId, key))
                    {
                        continue; // already notified in a previous session
                    }
                    toNotify.Add(ach);
                }

                if (toNotify.Count > 0)
                {
                    // Record each notification before firing the event (US-009 / US-007)
                    foreach (var ach in toNotify)
                    {
                        if (notificationHistory != null)
                        {
                            string nkey = GetAchievementKey(ach);
                            if (!string.IsNullOrEmpty(nkey))
                            {
                                notificationHistory.RecordNotification(currentAppId, nkey, ach.UnlockTime);
                            }
                        }
                    }

                    // Raise event to be handled by UI layer (e.g., show notification)
                    var handler = AchievementUnlocked;
                    if (handler != null)
                    {
                        handler(this, new List<Achievement>(toNotify));
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(string.Format("Error during scan cycle: {0}", ex.Message));
            }
            finally
            {
                lock (lockObj)
                {
                    isScanning = false;
                }
            }
        }
    }

    /// <summary>
    /// Comparer to avoid duplicate Achievement entries in newUnlocks lists.
    /// </summary>
    internal class AchievementIdComparer : System.Collections.Generic.IEqualityComparer<Achievement>
    {
        public bool Equals(Achievement x, Achievement y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;
            if (!string.IsNullOrEmpty(x.Id) && !string.IsNullOrEmpty(y.Id))
                return x.Id.Equals(y.Id, StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(x.Name) && !string.IsNullOrEmpty(y.Name))
                return x.Name.Equals(y.Name, StringComparison.OrdinalIgnoreCase);
            return false;
        }

        public int GetHashCode(Achievement obj)
        {
            if (obj == null) return 0;
            if (!string.IsNullOrEmpty(obj.Id)) return StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Id);
            if (!string.IsNullOrEmpty(obj.Name)) return StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name);
            return 0;
        }
    }
}
