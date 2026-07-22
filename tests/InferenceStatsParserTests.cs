using LlamaServerLauncher.Models;

public static class InferenceStatsParserTests
{
    public static void Run(Harness h)
    {
        h.Section("InferenceStatsParser");

        var pp = InferenceStatsParser.TryParse(
            "prompt eval time =     306.11 ms /    18 tokens (   17.01 ms per token,    58.80 tokens per second)");
        h.Check("prompt kind", pp is { Kind: InferenceStatKind.Prompt }, pp?.Kind.ToString() ?? "null");
        h.Check("prompt tps", pp is { TokensPerSecond: > 58.7 and < 58.9 }, pp?.TokensPerSecond.ToString() ?? "null");

        var tg = InferenceStatsParser.TryParse(
            "       eval time =    3200.20 ms /   150 tokens (   21.33 ms per token,    46.87 tokens per second)");
        h.Check("gen kind", tg is { Kind: InferenceStatKind.Gen }, tg?.Kind.ToString() ?? "null");
        h.Check("gen tps", tg is { TokensPerSecond: > 46.8 and < 46.9 }, tg?.TokensPerSecond.ToString() ?? "null");

        var prefixed = InferenceStatsParser.TryParse(
            "slot      release: id  0 | task 5 | prompt eval time =   10.0 ms / 2 tokens ( 5.0 ms per token, 200.00 tokens per second)");
        h.Check("prefixed prompt kind", prefixed is { Kind: InferenceStatKind.Prompt }, prefixed?.Kind.ToString() ?? "null");
        h.Check("prefixed prompt tps", prefixed is { TokensPerSecond: > 199.9 and < 200.1 }, prefixed?.TokensPerSecond.ToString() ?? "null");

        var total = InferenceStatsParser.TryParse("      total time =    3506.31 ms /   168 tokens");
        h.Check("total time -> null", total == null, total?.ToString() ?? "null");

        h.Check("unrelated -> null", InferenceStatsParser.TryParse("main: server is listening on 127.0.0.1:8080") == null, "ok");
        h.Check("empty -> null", InferenceStatsParser.TryParse("") == null, "ok");
        h.Check("null -> null", InferenceStatsParser.TryParse(null) == null, "ok");

        var noNumber = InferenceStatsParser.TryParse("eval time reported in tokens per second units");
        h.Check("no number -> null", noNumber == null, noNumber?.ToString() ?? "null");
    }
}
