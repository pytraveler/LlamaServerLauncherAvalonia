using System.Collections.Generic;
using LlamaServerLauncher.Models;
using LlamaServerLauncher.Resources;

namespace LlamaServerLauncher.ViewModels;

public class McpImportSource
{
    public McpImportSource(string profileName, List<McpServerEntry> servers)
    {
        ProfileName = profileName;
        Servers = servers;
    }

    public string ProfileName { get; }

    public List<McpServerEntry> Servers { get; }

    public string Display =>
        string.Format(LocalizedStrings.Instance.McpImportProfileItem, ProfileName, Servers.Count);
}
