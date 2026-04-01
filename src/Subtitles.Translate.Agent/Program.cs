using Microsoft.Extensions.Logging;
using Mscc.GenerativeAI;
using Subtitles.Translate.Agent.Core;
using Subtitles.Translate.Agent.Core.Configuration;
using Subtitles.Translate.Agent.Core.Models;
using Subtitles.Translate.Agent.Core.Agents;
using System.Text;
using System.Linq;
using System.Reflection;

// 1. Set console encoding to prevent garbled characters
Console.OutputEncoding = Encoding.UTF8;

// 2. Create logger factory
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .SetMinimumLevel(LogLevel.Information)
        .AddConsole();
});

// Parse command line arguments
string filePath = string.Empty;
string targetLanguage = "简体中文";
string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
string modelId = "gpt-4o";
string endpoint = "https://api.openai.com/v1";
bool generateBilingual = false;
bool showHelp = false;
bool showVersion = false;
string? outputPath = null;

int batchSize = 20;
int slideStep = 10;
int precedingContextLines = 2;
int followingContextLines = 2;
bool enableReview = true;
bool enablePolishing = true;
bool enableTimingAdjustment = true;
int maxRetries = 3;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i].ToLower())
    {
        case "-f":
        case "--file":
            if (i + 1 < args.Length) filePath = args[++i];
            break;
        case "-l":
        case "--lang":
            if (i + 1 < args.Length) targetLanguage = args[++i];
            break;
        case "-k":
        case "--key":
            if (i + 1 < args.Length) apiKey = args[++i];
            break;
        case "-m":
        case "--model":
            if (i + 1 < args.Length) modelId = args[++i];
            break;
        case "-e":
        case "--endpoint":
            if (i + 1 < args.Length) endpoint = args[++i];
            break;
        case "-b":
        case "--bilingual":
            generateBilingual = true;
            break;
        case "-o":
        case "--output":
            if (i + 1 < args.Length) outputPath = args[++i];
            break;
        case "--batch-size":
            if (i + 1 < args.Length && int.TryParse(args[++i], out var parsedBatchSize)) batchSize = parsedBatchSize;
            break;
        case "--slide-step":
            if (i + 1 < args.Length && int.TryParse(args[++i], out var parsedSlideStep)) slideStep = parsedSlideStep;
            break;
        case "--preceding":
            if (i + 1 < args.Length && int.TryParse(args[++i], out var parsedPreceding)) precedingContextLines = parsedPreceding;
            break;
        case "--following":
            if (i + 1 < args.Length && int.TryParse(args[++i], out var parsedFollowing)) followingContextLines = parsedFollowing;
            break;
        case "--no-review":
            enableReview = false;
            break;
        case "--no-polish":
            enablePolishing = false;
            break;
        case "--no-timing-adjust":
            enableTimingAdjustment = false;
            break;
        case "--max-retries":
            if (i + 1 < args.Length && int.TryParse(args[++i], out var parsedMaxRetries)) maxRetries = parsedMaxRetries;
            break;
        case "-v":
        case "--version":
            showVersion = true;
            break;
        case "-h":
        case "--help":
            showHelp = true;
            break;
    }
}

if (showVersion)
{
    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
    Console.WriteLine(version);
    return;
}

if (showHelp)
{
    Console.WriteLine("Usage: Subtitles.Translate.Agent [options]");
    Console.WriteLine("Options:");
    Console.WriteLine("  -f, --file <path>        Subtitle file local path (required)");
    Console.WriteLine("  -l, --lang <language>    Target language (default: 简体中文)");
    Console.WriteLine("  -k, --key <key>          API Key (required, or set OPENAI_API_KEY env var)");
    Console.WriteLine("  -m, --model <model>      Model ID (default: gpt-4o)");
    Console.WriteLine("  -e, --endpoint <url>     API Endpoint (default: https://api.openai.com/v1)");
    Console.WriteLine("  -b, --bilingual          Generate bilingual subtitles");
    Console.WriteLine("  -o, --output <path>      Output file path (.srt). Default: <input>.<lang>.srt");
    Console.WriteLine("      --batch-size <n>     Subtitle lines per batch (default: 20)");
    Console.WriteLine("      --slide-step <n>     Sliding window step (default: 10)");
    Console.WriteLine("      --preceding <n>      Preceding context lines (default: 2)");
    Console.WriteLine("      --following <n>      Following context lines (default: 2)");
    Console.WriteLine("      --no-review          Disable Step4 review");
    Console.WriteLine("      --no-polish          Disable polishing (if applicable)");
    Console.WriteLine("      --no-timing-adjust   Disable timing adjustment");
    Console.WriteLine("      --max-retries <n>    Max retries for workflow/batches (default: 3)");
    Console.WriteLine("  -v, --version            Show version");
    Console.WriteLine("  -h, --help               Show command line help");
    return;
}

bool interactiveMode = args.Length == 0;

// Fallback to interactive mode only when launched without args
if (interactiveMode && (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)))
{
    while (true)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("Please enter subtitle file local path (drag and drop supported): ");
        Console.ResetColor();

        var input = Console.ReadLine();
        filePath = input?.Trim('"') ?? string.Empty;

        if (File.Exists(filePath)) break;

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("❌ File does not exist, please re-enter.");
        Console.ResetColor();
    }
}

if (interactiveMode)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write($"Please enter target language (default: {targetLanguage}): ");
    Console.ResetColor();

    var inputLang = Console.ReadLine()?.Trim();
    if (!string.IsNullOrWhiteSpace(inputLang))
    {
        targetLanguage = inputLang;
    }
}

if (!interactiveMode)
{
    if (string.IsNullOrWhiteSpace(filePath))
    {
        Console.Error.WriteLine("Missing required argument: --file <path>");
        Console.Error.WriteLine("Run with --help to see all options.");
        Environment.ExitCode = 2;
        return;
    }

    if (!File.Exists(filePath))
    {
        Console.Error.WriteLine($"Input file does not exist: {filePath}");
        Environment.ExitCode = 2;
        return;
    }
}

if (interactiveMode && string.IsNullOrWhiteSpace(apiKey))
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("⚠️  Missing API Key configuration detected");
    Console.ResetColor();

    while (true)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("Please enter your API Key: ");
        Console.ResetColor();

        string? inputKey = Console.ReadLine()?.Trim();

        if (!string.IsNullOrWhiteSpace(inputKey))
        {
            apiKey = inputKey;
            break;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("❌ API Key cannot be empty, please re-enter.");
        Console.ResetColor();
    }
}

if (!interactiveMode && string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Missing API Key. Use --key <key> or set OPENAI_API_KEY env var.");
    Environment.ExitCode = 2;
    return;
}

var request = new SubtitleTranslationRequest
{
    SubtitleUrl = filePath,
    TargetLanguage = targetLanguage,
    BatchSize = batchSize,
    SlideStep = slideStep,
    PrecedingContextLines = precedingContextLines,
    FollowingContextLines = followingContextLines,
    EnableReview = enableReview,
    EnablePolishing = enablePolishing,
    EnableTimingAdjustment = enableTimingAdjustment,
    MaxRetries = maxRetries
};

var systemConfig = new AgentSystemConfig();
systemConfig.AddDefaultConfig(new AgentConfig
{
    ModelId = modelId,
    ApiKey = apiKey,
    Endpoint = endpoint
});

try
{
    Console.WriteLine("Initializing engine...");
    var agentEngine = new AgentEngine(request, systemConfig, loggerFactory);

    Console.WriteLine("🚀 Starting translation workflow...");
    var result = await agentEngine.RunAsync();

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"✅ Workflow completed, current stage: {result.CurrentStage}");
    Console.ResetColor();

    string srtContent = generateBilingual 
        ? agentEngine.GenerateBilingualSrt() 
        : agentEngine.GenerateTranslatedSrt();

    if (!string.IsNullOrWhiteSpace(srtContent))
    {
        string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        string safeLang = NormalizeFileToken(targetLanguage);
        string defaultFileName = $"{fileNameWithoutExt}.{safeLang}.srt";
        string resolvedOutputPath = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(directory, defaultFileName)
            : outputPath;

        Console.WriteLine($"Saving file to: {resolvedOutputPath}");
        await File.WriteAllTextAsync(resolvedOutputPath, srtContent, Encoding.UTF8);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"🎉 Subtitle saved successfully!");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("⚠️ Warning: Generated subtitle content is empty.");
        Console.ResetColor();
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"💥 Error occurred: {ex.Message}");
    Console.ResetColor();
    Environment.ExitCode = 1;
}

// Only wait for key press if it was run without arguments (interactive mode)
if (interactiveMode)
{
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
}

static string NormalizeFileToken(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return "lang";
    }

    var invalid = Path.GetInvalidFileNameChars();
    var sb = new StringBuilder(value.Length);

    foreach (var ch in value.Trim())
    {
        if (invalid.Contains(ch))
        {
            continue;
        }

        if (char.IsWhiteSpace(ch))
        {
            sb.Append('_');
            continue;
        }

        sb.Append(ch);
    }

    var result = sb.ToString().Trim('_');
    return string.IsNullOrWhiteSpace(result) ? "lang" : result;
}
