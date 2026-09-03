using System.Linq;
using LlamaServerLauncher.Models;

public static class CommandImportTests
{
    public static void Run(Harness h)
    {
        RunLines(h);
        RunSingleLine(h);
        RunPastedBlocks(h);
        RunRejects(h);
    }

    private static void RunLines(Harness h)
    {
        h.Section("CommandImport: turning pasted text into one command");

        var joined = CommandImport.LogicalLines("llama-server \\\n  -m a.gguf \\\n  -ngl 99");
        h.Check("a backslash at the end of a line joins the next one",
            joined.Count == 1 && joined[0] == "llama-server -m a.gguf -ngl 99",
            joined.Count == 1 ? joined[0] : joined.Count.ToString());

        var caret = CommandImport.LogicalLines("llama-server ^\n  -m a.gguf");
        h.Check("so does a caret, the way cmd continues a line",
            caret.Count == 1 && caret[0] == "llama-server -m a.gguf", string.Join(" | ", caret));

        var backtick = CommandImport.LogicalLines("llama-server `\n  -m a.gguf");
        h.Check("and a backtick, the way PowerShell does",
            backtick.Count == 1 && backtick[0] == "llama-server -m a.gguf", string.Join(" | ", backtick));

        var windowsDir = CommandImport.LogicalLines("llama-server --models-dir C:\\models\\\n-ngl 99");
        h.Check("a path that ends in a separator is not a continuation",
            windowsDir.Count == 2, string.Join(" | ", windowsDir));

        var noisy = CommandImport.LogicalLines("```bash\n#!/bin/sh\n# run it\nllama-server -m a.gguf\n```");
        h.Check("fences, shebangs and comments are dropped",
            noisy.Count == 1 && noisy[0] == "llama-server -m a.gguf", string.Join(" | ", noisy));

        var prompts = CommandImport.LogicalLines("$ llama-server -m a.gguf\nPS C:\\llama> llama-server -m b.gguf");
        h.Check("shell prompts are not part of the command",
            prompts.Count == 2 && prompts[0].StartsWith("llama-server") && prompts[1].StartsWith("llama-server"),
            string.Join(" | ", prompts));

        h.Check("nothing in, nothing out", CommandImport.LogicalLines(null).Count == 0, "empty");
    }

    private static void RunSingleLine(Harness h)
    {
        h.Section("CommandImport: finding the command in a line");

        var plain = CommandImport.FromText("llama-server -m a.gguf -c 8192");
        h.Check("the arguments come back without the program",
            plain.Tokens.SequenceEqual(new[] { "-m", "a.gguf", "-c", "8192" }),
            string.Join(" ", plain.Tokens));
        h.Check("a bare program name is not an executable path",
            plain.ExecutablePath == null, plain.ExecutablePath ?? "null");

        var quoted = CommandImport.FromText("\"C:\\llama\\llama-server.exe\" -m \"C:\\models\\a b.gguf\" -ngl 99");
        h.Check("a quoted windows path is taken as the executable",
            quoted.ExecutablePath == "C:\\llama\\llama-server.exe", quoted.ExecutablePath ?? "null");
        h.Check("and a quoted model path survives its spaces",
            quoted.Tokens.Contains("C:\\models\\a b.gguf"), string.Join(" ", quoted.Tokens));

        var relative = CommandImport.FromText("./llama-server -m a.gguf");
        h.Check("a relative program path is not worth importing",
            relative.ExecutablePath == null && relative.HasCommand, relative.ExecutablePath ?? "null");

        var env = CommandImport.FromText("CUDA_VISIBLE_DEVICES=0 llama-server -m a.gguf -ngl 99");
        h.Check("what comes before the program is not an argument",
            env.Tokens.SequenceEqual(new[] { "-m", "a.gguf", "-ngl", "99" }), string.Join(" ", env.Tokens));

        var bare = CommandImport.FromText("-m a.gguf -ngl 99 -c 4096");
        h.Check("a pasted argument string is a command too",
            bare.HasCommand && bare.Tokens.Count == 6, string.Join(" ", bare.Tokens));

        var piped = CommandImport.FromText("llama-server -m a.gguf > server.log 2>&1");
        h.Check("a redirect is not an argument",
            piped.Tokens.SequenceEqual(new[] { "-m", "a.gguf" }), string.Join(" ", piped.Tokens));

        var chained = CommandImport.FromText("llama-server -m a.gguf && echo done");
        h.Check("neither is what follows the command",
            chained.Tokens.SequenceEqual(new[] { "-m", "a.gguf" }), string.Join(" ", chained.Tokens));

        var upper = CommandImport.FromText("D:\\LLAMA\\LLAMA-SERVER.EXE -m a.gguf");
        h.Check("the program is recognised whatever its case",
            upper.ExecutablePath == "D:\\LLAMA\\LLAMA-SERVER.EXE", upper.ExecutablePath ?? "null");
    }

    private static void RunPastedBlocks(Harness h)
    {
        h.Section("CommandImport: the shapes people actually paste");

        var readme = CommandImport.FromText(
            "```\n" +
            "llama-server \\\n" +
            "    -m /models/qwen.gguf \\\n" +
            "    -c 32768 \\\n" +
            "    -ngl 99 \\\n" +
            "    --host 0.0.0.0 --port 8080\n" +
            "```");
        h.Check("a fenced multi-line block reads as one command",
            readme.Tokens.SequenceEqual(new[]
            {
                "-m", "/models/qwen.gguf", "-c", "32768", "-ngl", "99",
                "--host", "0.0.0.0", "--port", "8080"
            }),
            string.Join(" ", readme.Tokens));

        var script = CommandImport.FromText(
            "#!/bin/bash\n" +
            "# my launcher\n" +
            "MODEL=/models/a.gguf\n" +
            "cd /opt/llama.cpp\n" +
            "./llama-server -m /models/a.gguf -ngl 99 ;\n");
        h.Check("a command found below other lines is still the command",
            script.Tokens.SequenceEqual(new[] { "-m", "/models/a.gguf", "-ngl", "99" }),
            string.Join(" ", script.Tokens));

        var two = CommandImport.FromText("llama-server -m first.gguf\nllama-server -m second.gguf");
        h.Check("the first command wins when there are several",
            two.Tokens.Contains("first.gguf"), string.Join(" ", two.Tokens));

        var afterJunk = CommandImport.FromText("some prose about the model\nllama-server -m a.gguf -fa on");
        h.Check("prose above the command is ignored",
            afterJunk.Tokens.SequenceEqual(new[] { "-m", "a.gguf", "-fa", "on" }), string.Join(" ", afterJunk.Tokens));
    }

    private static void RunRejects(Harness h)
    {
        h.Section("CommandImport: what is not a command");

        h.Check("empty text", !CommandImport.FromText("").HasCommand, "none");
        h.Check("null text", !CommandImport.FromText(null).HasCommand, "none");
        h.Check("prose alone", !CommandImport.FromText("just some words about llama-server").HasCommand, "none");
        h.Check("a command without a model", !CommandImport.FromText("llama-server -c 4096 -ngl 99").HasCommand, "none");
        h.Check("a model flag with no value",
            !CommandImport.FromText("llama-server -m -ngl 99").HasCommand, "none");
        h.Check("a models directory needs no value",
            CommandImport.FromText("llama-server --models-dir /models").HasCommand, "taken");
        h.Check("a hugging face repo counts as a model",
            CommandImport.FromText("llama-server -hf ggml-org/gemma-3-4b-it-GGUF").HasCommand, "taken");
    }
}
