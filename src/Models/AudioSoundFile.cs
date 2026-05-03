using System.IO;

namespace AchievementTracker.Models
{
    /// <summary>
    /// Represents an available notification sound file.
    /// </summary>
    public class AudioSoundFile
    {
        /// <summary>
        /// File name (e.g., "achievement.wav").
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Full file system path to the sound file.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// File size in bytes (cached to avoid repeated I/O).
        /// </summary>
        private long? _fileSize;

        /// <summary>
        /// Gets the file size, caching the result.
        /// </summary>
        public long? FileSize
        {
            get
            {
                if (_fileSize.HasValue)
                {
                    return _fileSize;
                }

                try
                {
                    _fileSize = new FileInfo(Path).Length;
                    return _fileSize;
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
