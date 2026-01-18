using Microsoft.Extensions.Logging;
using Mscc.GenerativeAI;
using Subtitles.Translate.Agent.Core;
using Subtitles.Translate.Agent.Core.Configuration;
using Subtitles.Translate.Agent.Core.Models;
using Subtitles.Translate.Agent.Core.Agents;
using System.Text;

// 1. Set console encoding to prevent garbled characters
Console.OutputEncoding = Encoding.UTF8;

// 2. Create logger factory
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .SetMinimumLevel(LogLevel.Information)
        .AddConsole();
});

// ==========================================
// Interaction section: Get user input
// ==========================================

string filePath = string.Empty;

// Loop until user inputs a valid file path
while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("Please enter subtitle file local path (drag and drop supported): ");
    Console.ResetColor();

    var input = Console.ReadLine();
    // Remove double quotes that might be generated when dragging files
    filePath = input?.Trim('"') ?? string.Empty;

    if (File.Exists(filePath))
    {
        break;
    }

    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("❌ File does not exist, please re-enter.");
    Console.ResetColor();
}

Console.ForegroundColor = ConsoleColor.Cyan;
Console.Write("Please enter target language (Default: Simplified Chinese): ");
Console.ResetColor();
string? targetLanguage = Console.ReadLine();
if (string.IsNullOrWhiteSpace(targetLanguage))
{
    targetLanguage = "Simplified Chinese";
}

Console.ForegroundColor = ConsoleColor.Cyan;
Console.Write("Please enter API endpoint (Default: https://api.openai.com/v1): ");
Console.ResetColor();
string? endpointInput = Console.ReadLine();
string endpoint = NormalizeEndpoint(endpointInput);

Console.ForegroundColor = ConsoleColor.Cyan;
Console.Write("Please enter model id (Default: gpt-oss-120b): ");
Console.ResetColor();
string? modelId = Console.ReadLine();
if (string.IsNullOrWhiteSpace(modelId))
{
    modelId = "gpt-oss-120b";
}
modelId = modelId.Trim();

string apiKey = "";
// Check if Key is empty
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine();
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


// ==========================================
// Core logic: Initialization and execution
// ==========================================

var request = new SubtitleTranslationRequest
{
    SubtitleUrl = filePath,
    TargetLanguage = targetLanguage,
    MaxRetries = 3
};

// Configure Agent system parameters
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

    // Create engine
    var agentEngine = new AgentEngine(request, systemConfig, loggerFactory);

    Console.WriteLine("🚀 Starting translation workflow...");

    // Execute workflow
    var result = await agentEngine.RunAsync();

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"✅ Workflow completed, current stage: {result.CurrentStage}");
    Console.ResetColor();

    // ==========================================
    // Output section: Call engine built-in methods to generate and save
    // ==========================================

    // 1. Use engine built-in method to generate SRT content
    // If bilingual subtitles are needed, use agentEngine.GenerateBilingualSrt();
    string srtContent = agentEngine.GenerateTranslatedSrt();

    if (!string.IsNullOrWhiteSpace(srtContent))
    {
        // 2. Build output path
        string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);

        // Filename format: source_filename.target_language.srt
        string newFileName = $"{fileNameWithoutExt}.{targetLanguage}.srt";
        string outputPath = Path.Combine(directory, newFileName);

        // 3. Write to file
        Console.WriteLine($"Saving file to: {outputPath}");
        await File.WriteAllTextAsync(outputPath, srtContent, Encoding.UTF8);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"🎉 Subtitle saved successfully!");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("⚠️ Warning: Generated subtitle content is empty, possibly due to no valid results from translation process.");
        Console.ResetColor();
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"💥 Error occurred: {ex.Message}");
    Console.ResetColor();
}

Console.WriteLine("Press any key to exit...");
Console.ReadKey();

static string NormalizeEndpoint(string? input)
{
    const string defaultEndpoint = "https://api.openai.com/v1";
    string endpoint = string.IsNullOrWhiteSpace(input) ? defaultEndpoint : input.Trim();

    endpoint = endpoint.TrimEnd('/');
    if (!endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
    {
        endpoint += "/v1";
    }

    return endpoint;
}
