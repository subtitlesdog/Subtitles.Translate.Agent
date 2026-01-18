using Subtitles.Translate.Agent.Core.Agents;
using Subtitles.Translate.Agent.Core.Models;

namespace Subtitles.Translate.Agent.Core.Configuration;

public class AgentSystemConfig
{

    public AgentSystemConfig()
    {
        
    }


    public void AddDefaultConfig(AgentConfig config)
    {
        Agents["Default"] = config;
    }

    public void AddConfig(string agentName, AgentConfig config)
    {
        Agents[agentName] = config;
    }

    /// <summary>
    /// Get configuration for specified Agent, returns Default config if not found
    /// </summary>
    /// <param name="agentName">Agent name</param>
    /// <returns>Agent configuration</returns>
    public AgentConfig GetConfig(string agentName)
    {
        if (Agents.TryGetValue(agentName, out var config))
        {
            return config;
        }

        if (Agents.TryGetValue("Default", out var defaultConfig))
        {
            return defaultConfig;
        }

        throw new InvalidOperationException($"Configuration for Agent '{agentName}' not found, and no default configuration is set.");
    }

    /// <summary>
    /// Agent configuration collection (Key: Agent Name)
    /// </summary>
    public Dictionary<string, AgentConfig> Agents { get; set; } = new();
}
