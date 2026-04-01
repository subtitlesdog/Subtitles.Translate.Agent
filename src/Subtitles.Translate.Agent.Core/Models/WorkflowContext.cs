using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nikse.SubtitleEdit.Core.Common;
using Subtitles.Translate.Agent.Core.Agents;
using Subtitles.Translate.Agent.Core.Configuration;

namespace Subtitles.Translate.Agent.Core.Models;

public class WorkflowContext
{

    public string Id = Guid.NewGuid().ToString();

    public required SubtitleTranslationRequest Request { get; set; }

    /// <summary>
    /// Maximum retry attempts (default 3)
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Original subtitle
    /// </summary>
    public required Subtitle Subtitle { get; set; }


    /// <summary>
    /// Agent system configuration
    /// </summary>
    public required AgentSystemConfig AgentSystemConfig { get; set; }

    /// <summary>
    /// Logger factory (used to create Agent loggers)
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// Current workflow stage
    /// </summary>
    public WorkflowStage CurrentStage { get; set; } = WorkflowStage.Initialized;

    /// <summary>
    /// Formatted subtitle content
    /// </summary>
    public string FormattedSubtitle { get; set; } = string.Empty;

    /// <summary>
    /// AI generated global context (raw text)
    /// </summary>
    public string Step1_DirectorRaw { get; set; } = string.Empty;

    /// <summary>
    /// Step 1 Director analysis result (parsed structured data)
    /// </summary>
    public Step1_DirectorResult? Step1_DirectorResult { get; set; }

    /// <summary>
    /// Step 2 Glossary (raw text)
    /// </summary>
    public string Step2_GlossaryRaw { get; set; } = string.Empty;

    /// <summary>
    /// Step 2 Glossary analysis result (parsed structured data)
    /// </summary>
    public Step2_GlossaryResult? Step2_GlossaryResult { get; set; }

    /// <summary>
    /// Translation result list
    /// </summary>
    public List<TranslationItem> TranslatedItems { get; set; } = new();

    /// <summary>
    /// Translation progress
    /// </summary>
    public TranslationProgress? TranslationProgress { get; set; }

    /// <summary>
    /// Review statistics
    /// </summary>
    public ReviewStatistics ReviewStatistics { get; set; } = new();

    /// <summary>
    /// Timing adjustments list
    /// </summary>
    public List<TimingAdjustmentItem> TimingAdjustments { get; set; } = new();

    /// <summary>
    /// Timing adjustments statistics
    /// </summary>
    public TimingStatistics TimingStatistics { get; set; } = new();

    /// <summary>
    /// Token usage summary
    /// </summary>
    public TokenUsageSummary TokenUsage { get; set; } = new();
}
