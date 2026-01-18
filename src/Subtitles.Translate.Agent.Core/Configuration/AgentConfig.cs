using Subtitles.Translate.Agent.Core.Configuration;

namespace Subtitles.Translate.Agent.Core.Configuration;

public class AgentConfig
{
    public required string ModelId { get; set; } = "gpt-4o";
    public required string Endpoint { get; set; }
    public required string ApiKey { get; set; }
}
