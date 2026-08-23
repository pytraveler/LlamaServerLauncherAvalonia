using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using LlamaServerLauncher.Models;

public static class McpConfigTests
{
    public static void Run(Harness h)
    {
        BuildJson(h);
        ParseConfig(h);
        Validate(h);
        CommandLine(h);
        ToolsResponse(h);
        LogProblems(h);
    }

    private static McpServerEntry Entry(string name, string command, string args = "", bool enabled = true)
    {
        return new McpServerEntry { Name = name, Command = command, ArgsText = args, Enabled = enabled };
    }

    private static void BuildJson(Harness h)
    {
        h.Section("McpConfigDocument.BuildJson");

        var entry = Entry("fs", "npx", "-y @modelcontextprotocol/server-filesystem \"C:\\my data\"");
        entry.WorkingDirectory = "C:\\work";
        entry.EnvText = "TOKEN=abc\n# comment\nEMPTY_LINE_BELOW=1\n\n";
        entry.TimeoutMs = 5000;

        var json = McpConfigDocument.BuildJson(new[] { entry });
        var root = JsonNode.Parse(json)!.AsObject();
        var server = root["mcpServers"]!["fs"]!.AsObject();

        h.Check("command written", server["command"]!.GetValue<string>() == "npx", json);

        var args = server["args"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
        h.Check("args become an array", args.Count == 3, string.Join(" | ", args));
        h.Check("quoted arg keeps its spaces", args[2] == "C:\\my data", args[2]);
        h.Check("backslashes are not doubled", !args[2].Contains("\\\\"), args[2]);

        var env = server["env"]!.AsObject();
        h.Check("env pair parsed", env["TOKEN"]!.GetValue<string>() == "abc", json);
        h.Check("comment line skipped", env["# comment"] == null, json);
        h.Check("env pair count", env.Count == 2, $"count={env.Count}");

        h.Check("cwd written", server["cwd"]!.GetValue<string>() == "C:\\work", json);
        h.Check("timeout written as timeout_ms", server["timeout_ms"]!.GetValue<int>() == 5000, json);

        var mixed = McpConfigDocument.BuildJson(new[]
        {
            Entry("ok", "cmd"),
            Entry("no-command", ""),
            Entry("", "cmd")
        });
        var mixedServers = JsonNode.Parse(mixed)!["mcpServers"]!.AsObject();
        h.Check("entries without name or command are skipped", mixedServers.Count == 1, mixed);

        var noArgs = JsonNode.Parse(McpConfigDocument.BuildJson(new[] { Entry("bare", "cmd") }))!
            ["mcpServers"]!["bare"]!.AsObject();
        h.Check("empty optional fields are omitted",
            noArgs["args"] == null && noArgs["env"] == null && noArgs["cwd"] == null && noArgs["timeout_ms"] == null,
            noArgs.ToJsonString());
    }

    private static void ParseConfig(Harness h)
    {
        h.Section("McpConfigDocument.Parse");

        const string cursor = """
        {
          "mcpServers": {
            "fs": {
              "command": "npx",
              "args": ["-y", "server-filesystem", "C:\\my data"],
              "env": { "TOKEN": "abc" },
              "cwd": "C:\\work",
              "timeout_ms": 4000
            },
            "off": { "command": "other", "disabled": true }
          }
        }
        """;

        var parsed = McpConfigDocument.Parse(cursor);
        h.Check("both servers read", parsed.Count == 2, $"count={parsed.Count}");

        var fs = parsed[0];
        h.Check("name from the key", fs.Name == "fs", fs.Name);
        h.Check("command read", fs.Command == "npx", fs.Command);
        h.Check("arg with spaces is quoted back", fs.ArgsText.Contains("\"C:\\my data\""), fs.ArgsText);
        h.Check("env formatted as KEY=VALUE", fs.EnvText == "TOKEN=abc", fs.EnvText);
        h.Check("cwd read", fs.WorkingDirectory == "C:\\work", fs.WorkingDirectory);
        h.Check("timeout read", fs.TimeoutMs == 4000, $"{fs.TimeoutMs}");
        h.Check("disabled entry stays off", !parsed[1].Enabled, "off");

        var bare = McpConfigDocument.Parse("""{ "fs": { "command": "npx" } }""");
        h.Check("bare server map accepted", bare.Count == 1 && bare[0].Command == "npx", $"count={bare.Count}");

        var unrelated = McpConfigDocument.Parse("""{ "theme": "dark" }""");
        h.Check("unrelated json yields nothing", unrelated.Count == 0, $"count={unrelated.Count}");

        var missing = McpConfigDocument.Parse("""{ "other": { "a": 1 } }""");
        h.Check("object without command is not a server", missing.Count == 0, $"count={missing.Count}");

        bool threw = false;
        try { McpConfigDocument.Parse("{ not json"); } catch (System.FormatException) { threw = true; }
        h.Check("invalid json throws FormatException", threw, "ok");

        // args survive a write-read cycle unchanged
        var entry = Entry("fs", "npx", "-y \"C:\\my data\" plain");
        var round = McpConfigDocument.Parse(McpConfigDocument.BuildJson(new[] { entry }));
        h.Check("args round trip", round[0].ArgsText == "-y \"C:\\my data\" plain", round[0].ArgsText);
    }

    private static void Validate(Harness h)
    {
        h.Section("McpConfigDocument.Validate");

        var clean = McpConfigDocument.Validate(new[] { Entry("a", "cmd"), Entry("b", "cmd") });
        h.Check("clean list has no issues", clean.Count == 0, $"count={clean.Count}");

        var duplicate = McpConfigDocument.Validate(new[] { Entry("a", "cmd"), Entry("A", "cmd") });
        h.Check("duplicate name reported (case-insensitive)",
            duplicate.Any(i => i.Kind == McpIssueKind.DuplicateName), $"count={duplicate.Count}");

        var noCommand = McpConfigDocument.Validate(new[] { Entry("a", "  ") });
        h.Check("empty command reported", noCommand.Any(i => i.Kind == McpIssueKind.EmptyCommand), "ok");

        var noName = McpConfigDocument.Validate(new[] { Entry("", "cmd") });
        h.Check("empty name reported", noName.Any(i => i.Kind == McpIssueKind.EmptyName), "ok");

        var badTimeout = new McpServerEntry { Name = "a", Command = "cmd", TimeoutMs = 0 };
        h.Check("non-positive timeout reported",
            McpConfigDocument.Validate(new[] { badTimeout }).Any(i => i.Kind == McpIssueKind.InvalidTimeout), "ok");

        var disabled = McpConfigDocument.Validate(new[] { Entry("", "", enabled: false) });
        h.Check("disabled entries are not validated", disabled.Count == 0, $"count={disabled.Count}");

        var config = new ServerConfiguration { McpEnabled = true, McpServers = new List<McpServerEntry> { Entry("a", "cmd") } };
        h.Check("usable when enabled with a server", McpConfigDocument.HasUsableServers(config), "ok");

        config.McpEnabled = false;
        h.Check("not usable while the switch is off", !McpConfigDocument.HasUsableServers(config), "ok");

        config.McpEnabled = true;
        config.McpServers = new List<McpServerEntry> { Entry("a", "cmd", enabled: false) };
        h.Check("not usable with only disabled servers", !McpConfigDocument.HasUsableServers(config), "ok");
    }

    private static void CommandLine(Harness h)
    {
        h.Section("CommandLineBuilder MCP flag");

        var config = new ServerConfiguration
        {
            ModelPath = "C:\\models\\m.gguf",
            McpEnabled = true,
            McpServers = new List<McpServerEntry> { Entry("fs", "npx") },
            McpConfigPath = "C:\\data\\mcp\\Profile One.json"
        };

        var args = CommandLineBuilder.Build(config);
        h.Check("flag emitted", args.Contains("--mcp-servers-config"), args);
        h.Check("path quoted and escaped like other paths",
            args.Contains("--mcp-servers-config \"C:\\\\data\\\\mcp\\\\Profile One.json\""), args);

        var withoutPath = CommandLineBuilder.Build(new ServerConfiguration { ModelPath = "m.gguf" });
        h.Check("no flag without a generated config", !withoutPath.Contains("--mcp-servers-config"), withoutPath);

        var unsupported = CommandLineBuilder.Build(config, new HashSet<string>(new[] { "-m", "--host", "--port" }));
        h.Check("flag skipped when the build does not support it",
            !unsupported.Contains("--mcp-servers-config"), unsupported);

        var overridden = config.Clone();
        overridden.CustomArguments = "--mcp-servers-config C:\\other.json";
        var overriddenArgs = CommandLineBuilder.Build(overridden);
        h.Check("custom argument wins", overriddenArgs.Contains("C:\\other.json"), overriddenArgs);
        h.Check("generated path dropped when overridden",
            !overriddenArgs.Contains("Profile One.json"), overriddenArgs);

        var clone = config.Clone();
        clone.McpServers[0].Name = "changed";
        h.Check("clone deep-copies the server list", config.McpServers[0].Name == "fs", config.McpServers[0].Name);

        var source = Entry("fs", "npx", "-y pkg");
        h.Check("a clone compares equal to its source", source.Clone().SameAs(source), "ok");
        h.Check("a real difference is still seen", !source.SameAs(Entry("fs", "other")), "ok");

        var parsed = ServerConfigurationExtensions.ParseFromCommandLine(args);
        h.Check("round-trip parse unescapes the path",
            parsed != null && parsed.McpConfigPath == "C:\\data\\mcp\\Profile One.json",
            parsed?.McpConfigPath ?? "null");
        if (parsed != null)
        {
            var rebuilt = CommandLineBuilder.Build(parsed);
            h.Check("second build emits the same flag",
                rebuilt.Contains("--mcp-servers-config \"C:\\\\data\\\\mcp\\\\Profile One.json\""), rebuilt);
            h.Check("no quadruple backslashes after a round trip",
                !rebuilt.Contains("\\\\\\\\"), rebuilt);
        }

        var docker = config.Clone();
        docker.RunInDocker = true;
        var dockerCommand = CommandLineBuilder.BuildDockerCommand(docker);
        h.Check("config directory mounted for docker", dockerCommand.Contains(":/mcp"), dockerCommand);
        h.Check("container path used inside docker",
            dockerCommand.Contains("--mcp-servers-config \"/mcp/Profile One.json\""), dockerCommand);
    }

    private static void ToolsResponse(Harness h)
    {
        h.Section("ServerToolsResponse.Parse");

        const string json = """
        [
          { "tool": "read_file", "display_name": "Read file", "type": "server" },
          { "tool": "fs_list_directory", "display_name": "List", "type": "mcp" },
          { "display_name": "no id" },
          "not an object"
        ]
        """;

        var tools = ServerToolsResponse.Parse(json);
        h.Check("well-formed entries kept", tools.Count == 2, $"count={tools.Count}");
        h.Check("server tool typed", !tools[0].IsMcp, tools[0].Type);
        h.Check("mcp tool typed", tools[1].IsMcp, tools[1].Type);
        h.Check("display name read", tools[0].DisplayName == "Read file", tools[0].DisplayName);
        h.Check("garbage yields nothing", ServerToolsResponse.Parse("{ nope").Count == 0, "ok");
        h.Check("empty yields nothing", ServerToolsResponse.Parse("").Count == 0, "ok");
    }

    private static void LogProblems(Harness h)
    {
        h.Section("ServerLogFilter.TryGetMcpProblem");

        h.Check("spawn failure detected",
            ServerLogFilter.TryGetMcpProblem("srv    load: MCP warmup: failed to spawn 'fs': No such file") != null, "ok");
        h.Check("startup failure detected",
            ServerLogFilter.TryGetMcpProblem("MCP starting failed: failed to parse MCP config JSON") != null, "ok");
        h.Check("dead server detected",
            ServerLogFilter.TryGetMcpProblem("MCP 'fs' is no longer alive: transport closed") != null, "ok");
        h.Check("skipped entry detected",
            ServerLogFilter.TryGetMcpProblem("MCP server 'fs' has no command, skipping") != null, "ok");
        h.Check("message starts at MCP",
            ServerLogFilter.TryGetMcpProblem("srv  x: MCP 'fs' is no longer alive: closed")!.StartsWith("MCP 'fs'"), "ok");

        h.Check("success line is not a problem",
            ServerLogFilter.TryGetMcpProblem("MCP warmup: 'fs' discovered 4 tools") == null, "ok");
        h.Check("unrelated line ignored",
            ServerLogFilter.TryGetMcpProblem("main: model loaded") == null, "ok");
        h.Check("null ignored", ServerLogFilter.TryGetMcpProblem(null) == null, "ok");
    }
}
