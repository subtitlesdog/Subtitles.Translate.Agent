using Nikse.SubtitleEdit.Core.Common;

namespace Subtitles.Translate.Agent.Core.Models
{
    /// <summary>
    /// Subtitle translation request
    /// </summary>
    public class SubtitleTranslationRequest
    {
        /// <summary>
        /// Subtitle file link/path
        /// </summary>
        public required string SubtitleUrl { get; set; }

        #region Language Configuration
        /// <summary>
        /// Target language
        /// </summary>
        public string TargetLanguage { get; set; } = "简体中文";
        #endregion

        #region Translation Batch Configuration
        /// <summary>
        /// Number of subtitle lines per translation batch
        /// </summary>
        public int BatchSize { get; set; } = 20;

        /// <summary>
        /// Sliding window step (Overlap lines = BatchSize - Step)
        /// </summary>
        public int SlideStep { get; set; } = 10;
        #endregion

        #region Quality Control
        /// <summary>
        /// Whether to enable final polishing
        /// </summary>
        public bool EnablePolishing { get; set; } = true;

        /// <summary>
        /// Whether to enable review (Step 4)
        /// </summary>
        public bool EnableReview { get; set; } = true;

        /// <summary>
        /// Whether to enable timing adjustment
        /// </summary>
        public bool EnableTimingAdjustment { get; set; } = true;

        /// <summary>
        /// Maximum number of retries
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// LLM API retry count
        /// </summary>
        public int LLMRetries { get; set; } = 3;

        /// <summary>
        /// Number of preceding context lines
        /// </summary>
        public int PrecedingContextLines { get; set; } = 2;

        /// <summary>
        /// Number of following context lines
        /// </summary>
        public int FollowingContextLines { get; set; } = 2;
        #endregion
    }
}
