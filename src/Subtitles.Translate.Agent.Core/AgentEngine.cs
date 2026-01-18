using Amazon.Auth.AccessControlPolicy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Subtitles.Translate.Agent.Core.Agents;
using Subtitles.Translate.Agent.Core.Configuration;
using Subtitles.Translate.Agent.Core.Models;
using Subtitles.Translate.Agent.Core.Utils;
using System.Transactions;

namespace Subtitles.Translate.Agent.Core;



/// <summary>
/// Subtitle translation workflow engine, responsible for coordinating agents to complete translation tasks
/// </summary>
public class AgentEngine
{
    private readonly WorkflowContext _context;
    private readonly Microsoft.Extensions.Logging.ILogger? _logger;

    /// <summary>
    /// Initialize translation engine
    /// </summary>
    /// <param name="request">Subtitle translation request</param>
    /// <param name="agentSystemConfig">Agent system configuration</param>
    /// <param name="loggerFactory">Logger factory (optional)</param>
    public AgentEngine(SubtitleTranslationRequest request,
        AgentSystemConfig agentSystemConfig, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(agentSystemConfig);

        // Detect file encoding and parse subtitles
        var detectedEncoding = FileEncodingUtility.DetectEncoding(request.SubtitleUrl);
        Console.WriteLine($"File encoding detected: {detectedEncoding.EncodingName}");
        var subtitle = Subtitle.Parse(request.SubtitleUrl, detectedEncoding);

        // Initialize context
        _context = new WorkflowContext
        {
            Request = request,
            Subtitle = subtitle,
            AgentSystemConfig = agentSystemConfig,
            LoggerFactory = loggerFactory,
            MaxRetries = request.MaxRetries // Initialize from request, can be modified independently later
        };
        _logger = loggerFactory?.CreateLogger<AgentEngine>();
    }

    /// <summary>
    /// Run translation workflow
    /// </summary>
    /// <returns>Workflow context, including translation results and status</returns>
    public async Task<WorkflowContext> RunAsync()
    {
        await ExecuteWithRetryAsync("Translation Workflow", RunWorkflowCoreAsync);
        return _context;
    }

    /// <summary>
    /// Core workflow logic (skips completed steps during retry)
    /// </summary>
    private async Task RunWorkflowCoreAsync()
    {
        // Step 1: Director analysis
        await RunStageAsync<Step1_DirectorAgent>(WorkflowStage.IntroductionCompleted);

        // Step 2: Glossary generation
        await RunStageAsync<Step2_GlossaryAgent>(WorkflowStage.GlossaryCompleted);

        // Step 3: Translation (includes Step 4 Review)
        // Note: Translator Agent has an additional batch-level retry mechanism internally
        await RunStageAsync<Step3_TranslatorAgent>(WorkflowStage.TranslationCompleted);
    }

    /// <summary>
    /// Execute workflow stage (generic method)
    /// </summary>
    private async Task RunStageAsync<TAgent>(WorkflowStage completedStage) where TAgent : AgentBase
    {
        if (_context.CurrentStage >= completedStage)
            return;

        var agent = (AgentBase)Activator.CreateInstance(typeof(TAgent), _context)!;
        await agent.ExecuteAsync();

        _context.CurrentStage = completedStage;
    }

    /// <summary>
    /// Execution method with retry mechanism
    /// </summary>
    /// <param name="stageName">Stage name (for logging)</param>
    /// <param name="action">Action to execute</param>
    private async Task ExecuteWithRetryAsync(string stageName, Func<Task> action)
    {

        //_logger?.LogError("Original mismatch! {Source} {LogText}", "Test Source", $"Original mismatch: Translation original, Polished original=");


        int maxRetries = _context.MaxRetries;
        int retryCount = 0;
        Exception? lastException = null;

        while (retryCount <= maxRetries)
        {
            try
            {
                await action();
                return; // Return on success
            }
            catch (Exception ex)
            {
                lastException = ex;
                retryCount++;

                if (retryCount <= maxRetries)
                {
                    _logger?.LogWarning(ex, "{StageName} execution failed, retrying {RetryCount}/{MaxRetries}...",
                        stageName, retryCount, maxRetries);

                    // Exponential backoff delay
                    int delayMs = Math.Min(1000 * (int)Math.Pow(2, retryCount - 1), 30000);
                    await Task.Delay(delayMs);
                }
                else
                {
                    _logger?.LogError(ex, "{StageName} execution failed, reached max retries {MaxRetries}",
                        stageName, maxRetries);
                    throw new InvalidOperationException(
                        $"{stageName} execution failed, retried {maxRetries} times. Error: {ex.Message}", ex);
                }
            }
        }
    }

    /// <summary>
    /// Create engine from existing context (for resuming)
    /// </summary>
    private AgentEngine(WorkflowContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Resume workflow from saved context
    /// </summary>
    /// <param name="context">Previously saved workflow context</param>
    /// <returns>Workflow context after resuming execution</returns>
    public static Task<WorkflowContext> ResumeAsync(WorkflowContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var engine = new AgentEngine(context);
        return engine.RunAsync();
    }

    /// <summary>
    /// Generate translated SRT subtitle content
    /// </summary>
    /// <returns>SRT format subtitle content</returns>
    public string GenerateTranslatedSrt()
    {
        var translatedSubtitle = new Subtitle();

        // Copy header information
        if (_context.Subtitle.Header != null)
        {
            translatedSubtitle.Header = _context.Subtitle.Header;
        }

        var paragraphs = _context.Subtitle.Paragraphs;
        var translations = _context.TranslatedItems;

        for (int i = 0; i < paragraphs.Count; i++)
        {
            var p = paragraphs[i];

            // Create new paragraph, preserving original timeline
            var newP = new Paragraph(p)
            {
                Text = p.Text // Default to original text in case there is no translation
            };

            // Try to match translation
            // Assume TranslatedItems corresponds one-to-one with Paragraphs (in order)
            if (translations != null && i < translations.Count)
            {
                var t = translations[i];
                // Could also check if t.Id matches p.ID, but p.ID generation rule is uncertain, and Step3 generates in order
                if (!string.IsNullOrEmpty(t.Translation))
                {
                    newP.Text = t.Translation;
                }
            }

            translatedSubtitle.Paragraphs.Add(newP);
        }

        // Convert to SRT format
        return translatedSubtitle.ToText(new SubRip());
    }
    /// <summary>
    /// Generate bilingual SRT subtitle content
    /// </summary>
    /// <returns>SRT format subtitle content</returns>
    public string GenerateBilingualSrt()
    {
        var translatedSubtitle = new Subtitle();

        // Copy header information
        if (_context.Subtitle.Header != null)
        {
            translatedSubtitle.Header = _context.Subtitle.Header;
        }

        var paragraphs = _context.Subtitle.Paragraphs;
        var translations = _context.TranslatedItems;

        for (int i = 0; i < paragraphs.Count; i++)
        {
            var p = paragraphs[i];

            // Create new paragraph, preserving original timeline
            var newP = new Paragraph(p)
            {
                Text = p.Text // Default to original text in case there is no translation
            };

            // Try to match translation
            // Assume TranslatedItems corresponds one-to-one with Paragraphs (in order)
            if (translations != null && i < translations.Count)
            {
                var t = translations[i];
                // Could also check if t.Id matches p.ID, but p.ID generation rule is uncertain, and Step3 generates in order
                if (!string.IsNullOrEmpty(t.Translation))
                {
                    newP.Text += p.Text + "\n" + t.Translation;
                }
            }

            translatedSubtitle.Paragraphs.Add(newP);
        }

        // Convert to SRT format
        return translatedSubtitle.ToText(new SubRip());
    }
}
