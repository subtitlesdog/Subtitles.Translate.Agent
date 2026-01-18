namespace Subtitles.Translate.Agent.Core.Models;

/// <summary>
/// Token usage summary
/// </summary>
public class TokenUsageSummary
{
    private readonly object _lock = new();

    /// <summary>
    /// All token usage records
    /// </summary>
    public List<TokenUsageRecord> Records { get; set; } = new();

    /// <summary>
    /// Total input token count
    /// </summary>
    public long TotalInputTokens => Records.Sum(r => r.InputTokens);

    /// <summary>
    /// Total output token count
    /// </summary>
    public long TotalOutputTokens => Records.Sum(r => r.OutputTokens);

    /// <summary>
    /// Total token count (Input + Output)
    /// </summary>
    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    /// <summary>
    /// Add a usage record (Thread-safe)
    /// </summary>
    public void AddRecord(string agentName, long inputTokens, long outputTokens, int? batchIndex = null)
    {
        lock (_lock)
        {
            Records.Add(new TokenUsageRecord
            {
                AgentName = agentName,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                BatchIndex = batchIndex,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Get summary information
    /// </summary>
    public string GetSummary()
    {
        return $"Total: {TotalTokens:N0} tokens (Input: {TotalInputTokens:N0}, Output: {TotalOutputTokens:N0})";
    }

    /// <summary>
    /// Summarize by Agent
    /// </summary>
    public Dictionary<string, AgentTokenSummary> GetSummaryByAgent()
    {
        return Records
            .GroupBy(r => r.AgentName)
            .ToDictionary(
                g => g.Key,
                g => new AgentTokenSummary
                {
                    AgentName = g.Key,
                    TotalInputTokens = g.Sum(r => r.InputTokens),
                    TotalOutputTokens = g.Sum(r => r.OutputTokens),
                    CallCount = g.Count()
                }
            );
    }
}

/// <summary>
/// Single token usage record
/// </summary>
public class TokenUsageRecord
{
    /// <summary>
    /// Agent name (e.g., IntroductionGenerator, GlossaryGenerator, Translator)
    /// </summary>
    public string AgentName { get; set; } = string.Empty;

    /// <summary>
    /// Input token count
    /// </summary>
    public long InputTokens { get; set; }

    /// <summary>
    /// Output token count
    /// </summary>
    public long OutputTokens { get; set; }

    /// <summary>
    /// Total token count
    /// </summary>
    public long TotalTokens => InputTokens + OutputTokens;

    /// <summary>
    /// Batch index (used only in translation phase)
    /// </summary>
    public int? BatchIndex { get; set; }

    /// <summary>
    /// Record timestamp
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Agent Token usage summary
/// </summary>
public class AgentTokenSummary
{
    public string AgentName { get; set; } = string.Empty;
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalTokens => TotalInputTokens + TotalOutputTokens;
    public int CallCount { get; set; }
}
