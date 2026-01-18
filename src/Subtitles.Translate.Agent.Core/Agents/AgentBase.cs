using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Subtitles.Translate.Agent.Core.Configuration;
using Subtitles.Translate.Agent.Core.Models;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace Subtitles.Translate.Agent.Core.Agents
{
    /// <summary>
    /// Agent base class, providing common logging, tracing, and JSON parsing functionalities
    /// </summary>
    public abstract class AgentBase
    {
        /// <summary>
        /// Default JSON serialization options (supports Chinese, case-insensitive property names)
        /// </summary>
        protected static readonly JsonSerializerOptions DefaultJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            WriteIndented = true
        };

        /// <summary>
        /// Workflow context
        /// </summary>
        protected readonly WorkflowContext _context;

        /// <summary>
        /// Agent configuration
        /// </summary>
        protected readonly AgentConfig _agentConfig;

        /// <summary>
        /// Agent name
        /// </summary>
        protected readonly string _agentName;

        /// <summary>
        /// Logger factory (retrieved from context)
        /// </summary>
        protected ILoggerFactory? LoggerFactory => _context.LoggerFactory;

        /// <summary>
        /// Logger instance for current Agent
        /// </summary>
        protected readonly ILogger? _logger;

        public AgentBase(WorkflowContext context, string agentName)
        {
            _context = context;
            _agentName = agentName;
            _agentConfig = context.AgentSystemConfig.GetConfig(agentName);
            _logger = context.LoggerFactory?.CreateLogger(agentName);

            var _tracerProviderBuilder = Sdk.CreateTracerProviderBuilder()
                .AddSource(context.Id)
                .AddConsoleExporter();

        }



        public abstract Task ExecuteAsync();


        /// <summary>
        /// Create ChatClient
        /// </summary>
        /// <param name="modelId">Model ID (optional, defaults to ModelId in configuration)</param>
        /// <returns>ChatClient</returns>
        protected IChatClient CreateChatClient(string? modelId = null)
        {
            var options = new OpenAIClientOptions()
            {
                Endpoint = new Uri(_agentConfig.Endpoint),
                RetryPolicy = new ClientRetryPolicy(_context.Request.LLMRetries, true, LoggerFactory)
            };

            return new OpenAIClient(new ApiKeyCredential(_agentConfig.ApiKey), options)
                .GetChatClient(modelId ?? _agentConfig.ModelId)
                .AsIChatClient();
        }

        /// <summary>
        /// Record Token usage to context
        /// </summary>
        /// <param name="agentName">Agent name</param>
        /// <param name="usage">Usage details from response</param>
        /// <param name="batchIndex">Batch index (optional)</param>
        protected void RecordTokenUsage(string agentName, UsageDetails? usage, int? batchIndex = null)
        {
            if (usage == null) return;

            _context.TokenUsage.AddRecord(
                agentName,
                usage.InputTokenCount ?? 0,
                usage.OutputTokenCount ?? 0,
                batchIndex);
        }

        #region JSON Parsing Methods

        /// <summary>
        /// Extract JSON from AI response text and deserialize to specified type
        /// </summary>
        /// <typeparam name="T">Target entity type</typeparam>
        /// <param name="responseText">Raw text returned by AI (may contain Markdown code blocks or other irrelevant text)</param>
        /// <param name="options">Optional JSON serialization options, defaults to DefaultJsonOptions</param>
        /// <returns>Deserialized entity object; returns default(T) if extraction or parsing fails</returns>
        /// <remarks>
        /// Supports the following JSON formats:
        /// <list type="bullet">
        ///   <item>```json ... ``` or ``` ... ``` Markdown code blocks</item>
        ///   <item>JSON object { ... } or array [ ... ] embedded directly in text (supports nesting)</item>
        ///   <item>Entire response is pure JSON text</item>
        /// </list>
        /// </remarks>
        /// <example>
        /// <code>
        /// var result = ParseJsonResponse&lt;MyModel&gt;(aiResponse);
        /// if (result != null)
        /// {
        ///     // Parsing successful
        /// }
        /// </code>
        /// </example>
        protected static T? ParseJsonResponse<T>(string responseText, JsonSerializerOptions? options = null) where T : class
        {
            if (string.IsNullOrWhiteSpace(responseText))
                return default;

            try
            {
                // Attempt to extract JSON content
                var jsonContent = ExtractJsonFromText(responseText);

                if (string.IsNullOrEmpty(jsonContent))
                    return default;

                return JsonSerializer.Deserialize<T>(jsonContent, options ?? DefaultJsonOptions);
            }
            catch (JsonException)
            {
                // JSON parsing failed, return default
                return default;
            }
        }

        /// <summary>
        /// Extract JSON from AI response text and deserialize to specified type (with error message output)
        /// </summary>
        /// <typeparam name="T">Target entity type</typeparam>
        /// <param name="responseText">Raw text returned by AI</param>
        /// <param name="result">Result object upon successful parsing</param>
        /// <param name="errorMessage">Error message upon parsing failure</param>
        /// <param name="options">Optional JSON serialization options</param>
        /// <returns>Whether parsing was successful</returns>
        /// <example>
        /// <code>
        /// if (TryParseJsonResponse&lt;MyModel&gt;(aiResponse, out var result, out var error))
        /// {
        ///     // Use result
        /// }
        /// else
        /// {
        ///     Console.WriteLine($"Parsing failed: {error}");
        /// }
        /// </code>
        /// </example>
        protected static bool TryParseJsonResponse<T>(string responseText, out T? result, out string? errorMessage, JsonSerializerOptions? options = null) where T : class
        {
            result = default;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(responseText))
            {
                errorMessage = "Response text is empty";
                return false;
            }

            try
            {
                var jsonContent = ExtractJsonFromText(responseText);

                if (string.IsNullOrEmpty(jsonContent))
                {
                    errorMessage = "Unable to extract JSON content from response";
                    return false;
                }

                result = JsonSerializer.Deserialize<T>(jsonContent, options ?? DefaultJsonOptions);

                if (result == null)
                {
                    errorMessage = "JSON deserialization result is null";
                    return false;
                }

                return true;
            }
            catch (JsonException ex)
            {
                errorMessage = $"JSON parsing error: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Use regular expressions to extract JSON content from text
        /// </summary>
        /// <param name="text">Text containing JSON</param>
        /// <returns>Extracted JSON string; returns null if not found</returns>
        /// <remarks>
        /// Attempt extraction in the following priority:
        /// <list type="number">
        ///   <item>Match ```json ... ``` or ``` ... ``` Markdown code blocks</item>
        ///   <item>If the entire text starts with { and ends with }, or starts with [ and ends with ], return directly</item>
        ///   <item>Automatically detect outermost JSON structure (object or array), returning the earliest match</item>
        /// </list>
        /// </remarks>
        protected static string? ExtractJsonFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // Mode 1: Match ```json ... ``` or ``` ... ``` Markdown code blocks
            var codeBlockPattern = @"```(?:json)?\s*\n?([\s\S]*?)\n?```";
            var codeBlockMatch = Regex.Match(text, codeBlockPattern);
            if (codeBlockMatch.Success)
            {
                return codeBlockMatch.Groups[1].Value.Trim();
            }

            // Mode 2: If the entire text looks like JSON, return directly
            var trimmedText = text.Trim();
            if ((trimmedText.StartsWith('{') && trimmedText.EndsWith('}')) ||
                (trimmedText.StartsWith('[') && trimmedText.EndsWith(']')))
            {
                return trimmedText;
            }

            // Mode 3: Automatically detect outermost structure (object or array), take the one with smaller Index
            var jsonObjectPattern = @"\{(?:[^{}]|(?<open>\{)|(?<-open>\}))*(?(open)(?!))\}";
            var jsonArrayPattern = @"\[(?:[^\[\]]|(?<open>\[)|(?<-open>\]))*(?(open)(?!))\]";

            var objectMatch = Regex.Match(text, jsonObjectPattern, RegexOptions.Singleline);
            var arrayMatch = Regex.Match(text, jsonArrayPattern, RegexOptions.Singleline);

            if (objectMatch.Success && arrayMatch.Success)
            {
                // Both matched, return the one that appears earlier
                // Note: If one is a subset of another (e.g., array inside object), the earlier one is naturally the outer structure
                return objectMatch.Index < arrayMatch.Index ? objectMatch.Value : arrayMatch.Value;
            }
            else if (objectMatch.Success)
            {
                return objectMatch.Value;
            }
            else if (arrayMatch.Success)
            {
                return arrayMatch.Value;
            }

            return null;
        }

        /// <summary>
        /// Use regular expressions to extract JSON array content from text
        /// </summary>
        /// <param name="text">Text containing JSON array</param>
        /// <returns>Extracted JSON array string; returns null if not found</returns>
        protected static string? ExtractJsonArrayFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // Mode 1: Match ```json ... ``` or ``` ... ``` Markdown code blocks
            var codeBlockPattern = @"```(?:json)?\s*\n?([\s\S]*?)\n?```";
            var codeBlockMatch = Regex.Match(text, codeBlockPattern);
            if (codeBlockMatch.Success)
            {
                return codeBlockMatch.Groups[1].Value.Trim();
            }

            // Mode 2: Directly match JSON array [ ... ] (supports nesting)
            var jsonArrayPattern = @"\[(?:[^\[\]]|(?<open>\[)|(?<-open>\]))*(?(open)(?!))\]";
            var jsonMatch = Regex.Match(text, jsonArrayPattern, RegexOptions.Singleline);
            if (jsonMatch.Success)
            {
                return jsonMatch.Value;
            }

            // Mode 3: If the entire text looks like a JSON array, return directly
            var trimmedText = text.Trim();
            if (trimmedText.StartsWith('[') && trimmedText.EndsWith(']'))
            {
                return trimmedText;
            }

            return null;
        }

        #endregion
    }
}
