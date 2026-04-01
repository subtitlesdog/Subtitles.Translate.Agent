using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Subtitles.Translate.Agent.Core.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Subtitles.Translate.Agent.Core.Agents
{
    /// <summary>
    /// Step 5: 时间轴调整 Agent
    /// 根据中文字幕的阅读长度，微调字幕的结束时间
    /// </summary>
    public class Step5_TimingAdjusterAgent : AgentBase
    {
        public const string AgentName = nameof(Step5_TimingAdjusterAgent);
        private readonly AIAgent _agent;
        private int _currentBatchIndex;

        /// <summary>
        /// 舒适阅读速度（字/秒）
        /// </summary>
        private const double ComfortableCps = 5.0;

        /// <summary>
        /// CPS 阈值，超过此值需要延长
        /// </summary>
        private const double CpsThreshold = 7.0;

        /// <summary>
        /// 最小帧间隙（毫秒）
        /// </summary>
        private const double MinGapMs = 50;

        /// <summary>
        /// 默认最大延长时间（毫秒）- 用于最后一句
        /// </summary>
        private const double DefaultMaxExtensionMs = 1000;

        /// <summary>
        /// 短句最小时长（毫秒）
        /// </summary>
        private const double ShortLineMinDurationMs = 1000;

        public Step5_TimingAdjusterAgent(WorkflowContext context)
            : base(context, AgentName)
        {
            // 创建 ChatClient 并初始化 Agent
            var client = CreateChatClient(_agentConfig.ModelId);
            _agent = client.CreateAIAgent().AsBuilder().Build();
        }

        /// <summary>
        /// 执行时间轴调整（全部使用 AI）
        /// </summary>
        public override async Task ExecuteAsync()
        {
            _logger?.LogInformation("===== {AgentName} 开始执行 (AI 模式) =====", AgentName);

            var paragraphs = _context.Subtitle.Paragraphs;
            var translatedItems = _context.TranslatedItems;
            int totalCount = Math.Min(paragraphs.Count, translatedItems.Count);

            // 初始化结果
            _context.TimingAdjustments = new List<TimingAdjustmentItem>();
            _context.TimingStatistics = new TimingStatistics();

            // 使用配置的 BatchSize，默认 20
            int batchSize = _context.Request.BatchSize > 0 ? _context.Request.BatchSize : 20;
            _logger?.LogDebug("待处理字幕数: {Count}, 批次大小: {BatchSize}", totalCount, batchSize);

            int startIndex = 0;
            int batchIndex = 0;

            while (startIndex < totalCount)
            {
                int count = Math.Min(batchSize, totalCount - startIndex);

                // 带重试机制处理批次
                await ExecuteBatchWithRetryAsync(_context, startIndex, count, batchIndex);

                startIndex += count;
                batchIndex++;
            }

            _logger?.LogInformation("===== {AgentName} 执行完成，延长 {Extended} 条，保持 {Kept} 条 =====",
                AgentName, _context.TimingStatistics.ExtendedCount, _context.TimingStatistics.KeptCount);
        }


        private async Task ExecuteBatchWithRetryAsync(WorkflowContext context, int startIndex, int count, int batchIndex)
        {
            int maxRetries = context.MaxRetries;
            int retryCount = 0;

            while (retryCount <= maxRetries)
            {
                try
                {
                    await ProcessBatchAsync(context, startIndex, count, batchIndex);
                    return;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    if (retryCount <= maxRetries)
                    {
                        _logger?.LogWarning(ex, "批次 {BatchIndex} 时间轴调整失败，正在进行第 {RetryCount}/{MaxRetries} 次重试...",
                            batchIndex, retryCount, maxRetries);
                        await Task.Delay(1000 * retryCount);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        private async Task ProcessBatchAsync(WorkflowContext context, int startIndex, int count, int batchIndex)
        {
            _currentBatchIndex = batchIndex;
            var prompt = GetPrompt(context, startIndex, count);

            _logger?.LogInformation("正在调用 LLM (模型: {ModelId}, 批次: {BatchIndex})...", _agentConfig.ModelId, batchIndex);
            var response = await _agent.RunAsync(prompt);

            // 记录 Token 使用量
            RecordTokenUsage(AgentName, response.Usage, batchIndex);

            // 解析响应
            var adjustments = ParseJsonResponse<List<TimingAdjustmentItem>>(response.Text);

            if (adjustments != null && adjustments.Count > 0)
            {
                // 应用调整到原始字幕
                ApplyAdjustments(context, adjustments);
                context.TimingAdjustments.AddRange(adjustments);
            }
            else
            {
                _logger?.LogWarning("{AgentName} AI 响应解析失败或为空 (批次 {BatchIndex})", AgentName, batchIndex);
                // 抛出异常以触发重试
                throw new InvalidOperationException($"AI 响应解析失败 (批次 {batchIndex})");
            }
        }

        /// <summary>
        /// 生成 AI 时间轴调整提示词
        /// </summary>
        public string GetPrompt(WorkflowContext context, int startIndex, int count)
        {
            var paragraphs = context.Subtitle.Paragraphs;
            var translatedItems = context.TranslatedItems;

            // 构建输入数据
            var inputList = new List<object>();

            int endIndex = startIndex + count;

            for (int i = startIndex; i < endIndex && i < translatedItems.Count && i < paragraphs.Count; i++)
            {
                var paragraph = paragraphs[i];
                var translation = translatedItems[i];

                var startTimeMs = paragraph.StartTime.TotalMilliseconds;
                var endTimeMs = paragraph.EndTime.TotalMilliseconds;

                double? nextLineStartMs = null;
                // 注意：这里需要获取全局的下一句，不仅仅是 batch 内的
                if (i + 1 < paragraphs.Count)
                {
                    nextLineStartMs = paragraphs[i + 1].StartTime.TotalMilliseconds;
                }

                inputList.Add(new
                {
                    id = i.ToString(),
                    text = translation.Translation,
                    start_time = FormatTimeCode(startTimeMs),
                    end_time = FormatTimeCode(endTimeMs),
                    next_line_start_time = nextLineStartMs.HasValue ? FormatTimeCode(nextLineStartMs.Value) : null
                });
            }

            var inputData = JsonSerializer.Serialize(inputList, DefaultJsonOptions);

            var prompt = $$"""
# Role 
 你是一位字幕时间轴专家（Subtitle Timing Specialist）。 
 **核心任务**：根据文本长度优化字幕的`end_time`，确保观众有足够阅读时间。 
 **核心原则**：只改结束时间（end_time），绝不触碰开始时间（start_time），绝不导致重叠。 
 
 # Inputs 
 {{inputData}} 
 (包含字段: id, text, start_time, end_time, next_line_start_time) 
 
 # Rules (Simplified Logic) 
 
 请对每一行执行以下**阅读舒适度检查**： 
 
 1.  **Density Check (密度检查)** 
     * 观察 `text` 的长度和当前 `duration` (`end` - `start`)。 
     * 如果文本较长但持续时间很短（例如：超过 10 个字符但少于 1.5 秒），标记为 **"Rushed (局促)"**。 
     * 如果文本极短（如 "No.", "好。"）但时间很长，标记为 **"Loose (松散)"**，通常无需处理。 
 
 2.  **Gap Filling Strategy (空隙填充策略)** 
     * 如果判定为 **"Rushed"**，检查当前行与下一行之间是否有**空隙 (Gap)**。 
     * `Max_End_Time` = `next_line_start_time` - **50ms** (保留 50毫秒 安全缓冲)。 
     * **Action**: 将 `end_time` 延长至 `Max_End_Time`，以最大化阅读时间。 
     * *注意*：如果这是最后一行（无 next_line），且文本很长，可酌情延长 1-2 秒。 
 
 3.  **Minimum Duration Constraint (最短时常约束)** 
     * 除单词（如 "Hi", "是"）外，尽量保证每一行字幕至少停留 **1.2秒 (1200ms)**。 
     * 如果有空隙，优先满足 1.2秒 的停留时间。 
 
 # Constraints 
 * **Language Agnostic**: 无论是中文、英文还是混合语言，统一基于“字符看起来很多”这一视觉标准判断。 
 * **Safety First**: 修改后的 `end_time` **严格禁止** 大于或等于 `next_line_start_time`。 
 
 # Output Requirements 
 1.  **Format**: 纯 JSON 数组，无 Markdown。 
 2.  **Data**: 必须包含 `action` ("KEEP" 或 "EXTEND") 和 `reason`。 
 
 # Output Example 
 [ 
   { 
     "id": "1", 
     "text": "This implies a significant geopolitical shift.", 
     "original_end": "00:00:02,500", 
     "adjusted_end": "00:00:03,100", 
     "action": "EXTEND", 
     "reason": "文本较长(42 chars)，利用后方空隙延长 600ms 以提升阅读体验。" 
   }, 
   { 
     "id": "2", 
     "text": "No.", 
     "original_end": "00:00:04,000", 
     "adjusted_end": "00:00:04,000", 
     "action": "KEEP", 
     "reason": "文本极短，当前时长已足够。" 
   } 
 ] 
""";

            return prompt;
        }

        /// <summary>
        /// 格式化时间码
        /// </summary>
        private static string FormatTimeCode(double totalMs)
        {
            var ts = TimeSpan.FromMilliseconds(totalMs);
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
        }

        /// <summary>
        /// 解析时间码
        /// </summary>
        private static double ParseTimeCode(string timeCode)
        {
            // 格式: HH:MM:SS,mmm
            var pattern = @"(\d{2}):(\d{2}):(\d{2})[,.](\d{3})";
            var match = Regex.Match(timeCode, pattern);
            if (match.Success)
            {
                var hours = int.Parse(match.Groups[1].Value);
                var minutes = int.Parse(match.Groups[2].Value);
                var seconds = int.Parse(match.Groups[3].Value);
                var ms = int.Parse(match.Groups[4].Value);
                return new TimeSpan(0, hours, minutes, seconds, ms).TotalMilliseconds;
            }
            return 0;
        }



        /// <summary>
        /// 应用时间轴调整到原始字幕
        /// </summary>
        private void ApplyAdjustments(WorkflowContext context, List<TimingAdjustmentItem> adjustments)
        {
            var paragraphs = _context.Subtitle.Paragraphs;
            context.TimingStatistics = new TimingStatistics();

            foreach (var adjustment in adjustments)
            {
                if (int.TryParse(adjustment.Id, out var index) && index < paragraphs.Count)
                {
                    context.TimingStatistics.TotalProcessed++;

                    if (adjustment.IsExtended)
                    {
                        var newEndMs = ParseTimeCode(adjustment.AdjustedEndTime);
                        var originalEndMs = paragraphs[index].EndTime.TotalMilliseconds;

                        if (newEndMs > originalEndMs)
                        {
                            paragraphs[index].EndTime.TotalMilliseconds = newEndMs;
                            context.TimingStatistics.ExtendedCount++;
                            context.TimingStatistics.TotalExtensionMs += (long)(newEndMs - originalEndMs);
                        }
                        else
                        {
                            context.TimingStatistics.KeptCount++;
                        }
                    }
                    else
                    {
                        context.TimingStatistics.KeptCount++;
                    }
                }
            }
        }
    }

    #region Step5 Result Models

    /// <summary>
    /// 单条时间轴调整结果
    /// </summary>
    public class TimingAdjustmentItem
    {
        /// <summary>
        /// 字幕序号
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 润色后文本
        /// </summary>
        [JsonPropertyName("text")]
        public string PolishedText { get; set; } = string.Empty;

        /// <summary>
        /// 原始时长（毫秒）
        /// </summary>
        [JsonPropertyName("original_duration_ms")]
        public int OriginalDurationMs { get; set; }

        /// <summary>
        /// 字符数（不含标点）
        /// </summary>
        [JsonPropertyName("char_count")]
        public int CharCount { get; set; }

        /// <summary>
        /// 操作类型：KEEP（保持）或 EXTEND（延长）
        /// </summary>
        [JsonPropertyName("action")]
        public string Action { get; set; } = "KEEP";

        /// <summary>
        /// 原始结束时间
        /// </summary>
        [JsonPropertyName("original_end")]
        public string OriginalEndTime { get; set; } = string.Empty;

        /// <summary>
        /// 调整后的结束时间
        /// </summary>
        [JsonPropertyName("adjusted_end")]
        public string AdjustedEndTime { get; set; } = string.Empty;

        /// <summary>
        /// 调整原因
        /// </summary>
        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// 是否进行了延长
        /// </summary>
        [JsonIgnore]
        public bool IsExtended => Action?.ToUpperInvariant() == "EXTEND";

        /// <summary>
        /// 原始 CPS
        /// </summary>
        [JsonIgnore]
        public double OriginalCps => OriginalDurationMs > 0
            ? CharCount / (OriginalDurationMs / 1000.0)
            : 0;
    }

    /// <summary>
    /// 时间轴调整统计信息
    /// </summary>
    public class TimingStatistics
    {
        /// <summary>
        /// 总处理数
        /// </summary>
        public int TotalProcessed { get; set; }

        /// <summary>
        /// 延长数
        /// </summary>
        public int ExtendedCount { get; set; }

        /// <summary>
        /// 保持不变数
        /// </summary>
        public int KeptCount { get; set; }

        /// <summary>
        /// 总延长时长（毫秒）
        /// </summary>
        public long TotalExtensionMs { get; set; }
    }

    #endregion
}