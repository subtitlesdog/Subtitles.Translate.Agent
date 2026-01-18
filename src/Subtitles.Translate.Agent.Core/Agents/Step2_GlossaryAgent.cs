using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Subtitles.Translate.Agent.Core.Models;
using System.Text;
using System.Text.Json.Serialization;

namespace Subtitles.Translate.Agent.Core.Agents
{
    /// <summary>
    /// Step 2: Glossary Agent
    /// Based on subtitle text and plot analysis, extract proper nouns and build a bilingual glossary
    /// </summary>
    public class Step2_GlossaryAgent : AgentBase
    {
        public const string AgentName = nameof(Step2_GlossaryAgent);
        private readonly AIAgent _agent;

        public Step2_GlossaryAgent(WorkflowContext context)
            : base(context, AgentName)
        {
            // Create ChatClient and initialize Agent
            var client = CreateChatClient();
            _agent = client.CreateAIAgent().AsBuilder().Build();
        }

        public override async Task ExecuteAsync()
        {
            _logger?.LogInformation("===== {AgentName} started execution =====", AgentName);

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
            _context.Step2_GlossaryRaw = response.Text;
            _logger?.LogDebug("Raw response length: {Length} chars", response.Text?.Length ?? 0);

            // Use base class method to extract and parse JSON
            _context.Step2_GlossaryResult = ParseJsonResponse<Step2_GlossaryResult>(response.Text ?? string.Empty);

            // Output parsing result summary
            if (_context.Step2_GlossaryResult != null)
            {
                _logger?.LogDebug("Parsing successful:");
                _logger?.LogDebug(_context.Step2_GlossaryResult.ToMarkdown());
            }
            else
            {
                throw new Exception("Step 2: Result Is null");
            }


            _logger?.LogInformation("===== {AgentName} execution completed =====", AgentName);
        }

        public string GetPrompt()
        {
            var prompt = $$"""
# Role
You are a terminology management expert with 20 years of experience (Terminology Manager). Your specialty is building precise **Bilingual Controlled Vocabularies**, ensuring high consistency in proper nouns, character names, and industry terms, fitting the reading habits of the target audience.

# Task
Based on the provided **[Original Subtitle Text]** and **[Step 1 Translation Guidance]**, extract all key entities and build a **JSON Format Glossary**.

# Inputs
1. **Translation Strategy (from Step 1)**:
{{_context.Step1_DirectorResult!.ToMarkdown()}}
*(Note: Use `Summary` and `Style Instructions` from Step 1 to determine the style of translated names. For example: if "Fantasy", names should lean towards classical; if "Hard Sci-Fi", names should be accurate and professional.)*

2. **Subtitle Content**:
{{_context.FormattedSubtitle}}

# Goals
You need to identify the following three types of entities and generate corresponding standard translations in the target language:

1.  **Characters**:
    * Extract all names and code names.
    * **Critical Task 1**: Must infer **Gender** based on context and Step 1's `pronoun_rules`.
    * **Critical Task 2**: Identify different forms of address for the same character (e.g., "William" and "Bill") and associate them in the JSON.

2.  **Locations**:
    * Extract cities, landmarks, fictional realms.
    * **Strategy**: Use standard translations for real places (New York -> 纽约); use transliteration or semantic translation for fictional places based on Step 1's tone (Rivendell -> 瑞文戴尔).

3.  **Specific Terms (Proper/Industry Terms)**:
    * Extract organizations, special items, magic spells, tech concepts, specific industry jargon.
    * **Strategy**: Since Step 1 has defined `category` (e.g., Medical Drama), ensure extracted term translations meet professional standards for that domain.

# Analysis Requirements (JSON Fields)

Please generate a JSON object containing the following fields:

1.  **character_map**:
    * `source_name`: Standard name in original text (capitalized).
    * `aliases`: [Array] Other names for this character appearing in the text (nicknames, surnames, code names).
    * `target_name`: Standard {{_context.Request.TargetLanguage}} translation.
    * `gender`: "male" / "female" / "unknown" / "object".
    * `context_note`: Short note (identity, relationship) to help subsequent translation understand why this name was chosen.

2.  **location_map**:
    * `source_term`: Original location name.
    * `target_term`: Standard {{_context.Request.TargetLanguage}} translation.
    * `type`: Location category (Real/Fictional/Micro-location).

3.  **terminology_map**:
    * `source_term`: Original term.
    * `target_term`: Standard {{_context.Request.TargetLanguage}} translation.
    * `domain`: Domain (based on Step 1 category, e.g., "Magic", "Cybernetics", "Legal").
    * `definition`: Short explanation to prevent ambiguity.

# Constraints
1.  **Exclusion**: Strictly forbid extracting common words (e.g., "morning", "officer", "teacher") unless they are part of a proper noun (e.g., "Officer Judy").
2.  **Context-Aware**: If a word is both a name and a common word (e.g., "Summer"), judge whether to extract based on plot.
3.  **JSON Only**: Output must be pure JSON, no text other than Markdown tags.
4.  **Empty Handling**: If a category of entities does not exist, return an empty array `[]`.

# Output Example
{
  "character_map": [
    {
      "source_name": "William Butcher",
      "aliases": ["Billy", "Butcher"],
      "target_name": "威廉·布彻",
      "gender": "male",
      "context_note": "One of the protagonists, violent temper, usually called Billy"
    }
  ],
  "location_map": [
    {
      "source_term": "Vought Tower",
      "target_term": "沃特大厦",
      "type": "Fictional Landmark"
    }
  ],
  "terminology_map": [
    {
      "source_term": "Compound V",
      "target_term": "五号化合物",
      "domain": "Bio-Tech",
      "definition": "Drug in the show that gives people superpowers"
    }
  ]
}
""";

            return prompt;
        }
    }

    #region Step2 Result Models

    /// <summary>
    /// Step 2 Glossary Analysis Result
    /// </summary>
    public class Step2_GlossaryResult
    {
        /// <summary>
        /// Character Map
        /// </summary>
        [JsonPropertyName("character_map")]
        public List<CharacterEntry> CharacterMap { get; set; } = new();

        /// <summary>
        /// Location Map
        /// </summary>
        [JsonPropertyName("location_map")]
        public List<LocationEntry> LocationMap { get; set; } = new();

        /// <summary>
        /// Terminology Map
        /// </summary>
        [JsonPropertyName("terminology_map")]
        public List<TerminologyEntry> TerminologyMap { get; set; } = new();

        /// <summary>
        /// Convert entity class to Markdown format for LLM
        /// </summary>
        public string ToMarkdown()
        {
            var sb = new StringBuilder();

            // 1. Characters
            if (CharacterMap != null && CharacterMap.Any())
            {
                sb.AppendLine("- Character Mapping");
                foreach (var charItem in CharacterMap)
                {
                    // Format: - **Source** (aka Alias): Target (Gender: X, Note: Y)
                    var aliasPart = (charItem.Aliases != null && charItem.Aliases.Any())
                                    ? $" (aka: {string.Join(", ", charItem.Aliases)})"
                                    : "";
                    var notePart = string.IsNullOrWhiteSpace(charItem.ContextNote)
                                   ? ""
                                   : $", Note: {charItem.ContextNote}";

                    sb.AppendLine($"    - **{charItem.SourceName}**{aliasPart}: {charItem.TargetName} (Gender: {charItem.Gender}{notePart})");
                }
                sb.AppendLine();
            }

            // 2. Locations
            if (LocationMap != null && LocationMap.Any())
            {
                sb.AppendLine("- Place Name Mapping");
                foreach (var loc in LocationMap)
                {
                    // Format: - **Source**: Target (Type: X)
                    sb.AppendLine($"    - **{loc.SourceTerm}**: {loc.TargetTerm} (Type: {loc.Type})");
                }
                sb.AppendLine();
            }

            // 3. Terminology
            if (TerminologyMap != null && TerminologyMap.Any())
            {
                sb.AppendLine("- Terminology Table");
                foreach (var term in TerminologyMap)
                {
                    // Format: - **Source**: Target (Domain: X, Def: Y)
                    var defPart = string.IsNullOrWhiteSpace(term.Definition) ? "" : $", Def: {term.Definition}";
                    sb.AppendLine($"    - **{term.SourceTerm}**: {term.TargetTerm} (Domain: {term.Domain}{defPart})");
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Character Entry
    /// </summary>
    public class CharacterEntry
    {
        /// <summary>
        /// Original name (full spelling)
        /// </summary>
        [JsonPropertyName("source_name")]
        public string SourceName { get; set; } = string.Empty;

        /// <summary>
        /// Other names for this character (nicknames, surnames, code names)
        /// </summary>
        [JsonPropertyName("aliases")]
        public List<string> Aliases { get; set; } = new();

        /// <summary>
        /// Standard translation
        /// </summary>
        [JsonPropertyName("target_name")]
        public string TargetName { get; set; } = string.Empty;

        /// <summary>
        /// Gender: male / female / unknown / object (non-human entity)
        /// </summary>
        [JsonPropertyName("gender")]
        public string Gender { get; set; } = "unknown";

        /// <summary>
        /// Short note (identity, relationship)
        /// </summary>
        [JsonPropertyName("context_note")]
        public string ContextNote { get; set; } = string.Empty;
    }

    /// <summary>
    /// Location Entry
    /// </summary>
    public class LocationEntry
    {
        /// <summary>
        /// Original location name
        /// </summary>
        [JsonPropertyName("source_term")]
        public string SourceTerm { get; set; } = string.Empty;

        /// <summary>
        /// Standard translation
        /// </summary>
        [JsonPropertyName("target_term")]
        public string TargetTerm { get; set; } = string.Empty;

        /// <summary>
        /// Category (Real/Fictional/Micro-location)
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
    }

    /// <summary>
    /// Terminology Entry
    /// </summary>
    public class TerminologyEntry
    {
        /// <summary>
        /// Original term
        /// </summary>
        [JsonPropertyName("source_term")]
        public string SourceTerm { get; set; } = string.Empty;

        /// <summary>
        /// Standard translation
        /// </summary>
        [JsonPropertyName("target_term")]
        public string TargetTerm { get; set; } = string.Empty;

        /// <summary>
        /// Domain
        /// </summary>
        [JsonPropertyName("domain")]
        public string Domain { get; set; } = string.Empty;

        /// <summary>
        /// Short definition
        /// </summary>
        [JsonPropertyName("definition")]
        public string Definition { get; set; } = string.Empty;
    }

    #endregion
}
