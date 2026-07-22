using LlamaServerLauncher.Models;

public static class ServerLogFilterTests
{
    public static void Run(Harness h)
    {
        h.Section("ServerLogFilter.IsPollingNoise (filtered)");
        h.Check("idle slots", ServerLogFilter.IsPollingNoise("srv  update_slots: all slots are idle"), "ok");
        h.Check("get /slots access log", ServerLogFilter.IsPollingNoise("srv  log_server_r: done request: GET /slots 127.0.0.1 200"), "ok");
        h.Check("get /health access log", ServerLogFilter.IsPollingNoise("srv  log_server_r: done request: GET /health 127.0.0.1 503"), "ok");

        h.Section("ServerLogFilter.IsPollingNoise (kept)");
        h.Check("null kept", !ServerLogFilter.IsPollingNoise(null), "ok");
        h.Check("empty kept", !ServerLogFilter.IsPollingNoise(""), "ok");
        h.Check("model loaded kept", !ServerLogFilter.IsPollingNoise("main: model loaded"), "ok");
        h.Check("listening kept", !ServerLogFilter.IsPollingNoise("main: server is listening on http://127.0.0.1:8088"), "ok");
        h.Check("chat completions kept", !ServerLogFilter.IsPollingNoise("srv  log_server_r: done request: POST /v1/chat/completions 127.0.0.1 200"), "ok");
        h.Check("real slot work kept", !ServerLogFilter.IsPollingNoise("slot update_slots: id 0 | task 12 | prompt processing progress"), "ok");
        h.Check("kv line kept", !ServerLogFilter.IsPollingNoise("llama_model_loader: - kv 0: general.architecture str = qwen3"), "ok");
    }
}
