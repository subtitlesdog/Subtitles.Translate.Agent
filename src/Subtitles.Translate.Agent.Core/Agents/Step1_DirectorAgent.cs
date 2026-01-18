using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Subtitles.Translate.Agent.Core.Models;
using Subtitles.Translate.Agent.Core.Utils;
using System.Text;
using System.Text.Json.Serialization;

namespace Subtitles.Translate.Agent.Core.Agents
{
    /// <summary>
    /// Step 1: Director Agent
    /// Responsible for analyzing video content, establishing translation guidelines and style guides
    /// </summary>
    public class Step1_DirectorAgent : AgentBase
    {
        public const string AgentName = nameof(Step1_DirectorAgent);
        private readonly AIAgent _agent;

        public Step1_DirectorAgent(WorkflowContext context)
            : base(context, AgentName)
        {
            // Create ChatClient and initialize Agent
            var client = CreateChatClient();
            _agent = client.CreateAIAgent().AsBuilder().Build();
        }

        public override async Task ExecuteAsync()
        {
            _logger?.LogInformation("===== {AgentName} started execution =====", AgentName);
            if (_context.Subtitle == null)
            {
                throw new Exception("No subtitle content was recognized");
            }
            // Format subtitle
            _context.FormattedSubtitle = SubtitleFormatUtility.ToCustomFormat(_context.Subtitle);
            _logger?.LogDebug("Subtitle formatting completed, total {Count} subtitles, target language: {TargetLanguage}",
                _context.Subtitle.Paragraphs.Count,
                _context.Request.TargetLanguage);

            var prompt = GetPrompt();
            _logger?.LogDebug("Prompt length: {Length} chars", prompt.Length);

            _logger?.LogInformation("Calling LLM (Model: {ModelId})...", _agentConfig.ModelId);
            var response = await _agent.RunAsync(prompt);
            _logger?.LogInformation("LLM response completed");

            // Record Token usage to context
            RecordTokenUsage(AgentName, response.Usage);
            _logger?.LogDebug("Token Usage - Input: {Input}, Output: {Output}, Total: {Total}",
                response.Usage?.InputTokenCount ?? 0,
                response.Usage?.OutputTokenCount ?? 0,
                response.Usage?.TotalTokenCount ?? 0);

            // Save raw response
            _context.Step1_DirectorRaw = response.Text;
            _logger?.LogDebug("Raw response length: {Length} chars", response.Text?.Length ?? 0);

            // Use base class method to extract and parse JSON
            _context.Step1_DirectorResult = ParseJsonResponse<Step1_DirectorResult>(response.Text ?? string.Empty);

            // Output parsing result summary
            if (_context.Step1_DirectorResult != null)
            {
                var result = _context.Step1_DirectorResult;
                _logger?.LogDebug("Parsing successful:");
                _logger?.LogDebug(_context.Step1_DirectorResult.ToMarkdown());
            }
            else
            {
                throw new Exception("Step 1: Result Is null");
            }

            _logger?.LogInformation("===== {AgentName} execution completed =====", AgentName);
        }

        public string GetPrompt()
        {
            var prompt = $$"""
# Role
You are a senior Multimedia Localization Specialist. Your core capability is to quickly analyze various types of video texts (movies, news, tech tutorials, Vlogs, documentaries, etc.) to provide a precise **Context Framework** and **Style Guide** for the translation team.

# Task
Read the provided video subtitle text and generate a structured **JSON Translation Guidance Document**. This document will serve as the "Global Context" input for the subsequent AI translation process.

# Goal
Your goal is not to "translate", but to establish "translation guidelines". You need to address the following core issues based on the content type:
1.  **Register**: Is it solemn (news/academic), relaxed and humorous (short video), or intimate/flirtatious (adult)?
2.  **Audience**: Who is the content for? This determines the choice of pronouns (e.g., "you" informal vs formal) and the level of professional terminology.
3.  **Core Intent**: Is the purpose to educate, entertain, persuade, or document?

# Analysis Requirements (JSON Fields)

Please generate a JSON object containing the following fields:

* `category`: Specific category of the video (e.g., Python Tutorial, Cyberpunk Movie, Political News, Daily Vlog, Adult/Drama, Movie, TV Series).
* `overall_tone`: 3-5 adjectives summarizing the overall tone (e.g., objective, passionate, slang-heavy, seductive).
* `style_instruction`: Specific instructions for translation. Clearly state the writing style to be adopted (e.g., "Use rigorous written language, avoid colloquialisms" or "Use a lot of current internet slang, keep it grounded").
* `pronoun_rules`: Clarify relationships between characters/speakers and address strategies (e.g., "Speaker is a teacher, use friendly 'everyone' or equal 'you' for audience", "Couples use intimate addresses").
* `summary`: Concisely summarize the main content. If it's a drama, summarize the plot direction; if it's teaching/news, summarize the core theme and conclusion.
* `background_setting`: Supplement necessary background information (e.g., time and place, specific software versions involved, specific social events).

# Constraints
1.  **JSON Only**: Output must be pure JSON format, strictly forbidding any explanatory text other than Markdown tags (` ```json `).
2.  **English Keys**: JSON Keys must be in English.
3.  **Target Language Values**: Please use {{_context.Request.TargetLanguage}} for JSON Value content, so that subsequent translation steps can understand directly.

# Subtitle Content
{{_context.FormattedSubtitle}}

# Output Example
{
    "category": "Tech Review",
    "overall_tone": ["Fast-paced", "Sharp", "Humorous"],
    "style_instruction": "Maintain a YouTuber-like colloquial style. Professional terms (like high refresh rate, ray tracing) must be accurate, but conjunctions can be casual to reflect the blogger's personal opinion.",
    "pronoun_rules": "Blogger refers to self as 'I', calls audience 'bros' or 'everyone', maintaining a sense of community.",
    "summary": "Blogger is comparing camera functions of iPhone 16 and Samsung S25. First half complains about iPhone ghosting, second half praises Samsung telephoto, finally suggests photography enthusiasts choose Samsung.",
    "background_setting": "After the 2025 autumn new product launch, there is huge controversy in the market regarding the two flagships."
}
""";

            return prompt;
        }
    }

    #region Step1 Result Models

    /// <summary>
    /// Step 1 Director Analysis Result
    /// </summary>
    public class Step1_DirectorResult
    {
        /// <summary>
        /// Specific category of the video
        /// </summary>
        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// 3-5 adjectives summarizing overall tone
        /// </summary>
        [JsonPropertyName("overall_tone")]
        public List<string> OverallTone { get; set; } = new();

        /// <summary>
        /// Specific instructions for translation, clarifying writing style
        /// </summary>
        [JsonPropertyName("style_instruction")]
        public string StyleInstruction { get; set; } = string.Empty;

        /// <summary>
        /// Clarify relationships and address strategies between characters/speakers
        /// </summary>
        [JsonPropertyName("pronoun_rules")]
        public string PronounRules { get; set; } = string.Empty;


        /// <summary>
        /// Concisely summarize content main idea
        /// </summary>
        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        /// <summary>
        /// Supplement necessary background information
        /// </summary>
        [JsonPropertyName("background_setting")]
        public string BackgroundSetting { get; set; } = string.Empty;



        /// <summary>
        /// Convert entity class to Markdown format for LLM
        /// </summary>
        public string ToMarkdown()
        {
            var sb = new StringBuilder();

            //sb.AppendLine("Style Guide");
            sb.AppendLine($"- **Category**: {Category}");
            sb.AppendLine($"- **Overall Tone**: {string.Join(", ", OverallTone)}");
            sb.AppendLine($"- **Style Instructions**: {StyleInstruction}");
            sb.AppendLine($"- **Address Strategy**: {PronounRules}");
            sb.AppendLine($"- **Summary**: {Summary}");
            sb.AppendLine($"- **Background Info**: {BackgroundSetting}");

            return sb.ToString();
        }
    }


    #endregion
}
