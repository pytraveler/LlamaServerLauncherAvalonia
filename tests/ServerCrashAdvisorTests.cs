using LlamaServerLauncher.Models;

public static class ServerCrashAdvisorTests
{
    public static void Run(Harness h)
    {
        h.Section("ServerCrashAdvisor.IsPinnedMemoryFailure");
        h.Check("real 1MiB line",
            ServerCrashAdvisor.IsPinnedMemoryFailure("0.04.737.572 D ggml_cuda_host_malloc: failed to allocate 1.00 MiB of pinned memory: resource already mapped"), "ok");
        h.Check("real 203MiB line",
            ServerCrashAdvisor.IsPinnedMemoryFailure("0.23.170.305 D ggml_cuda_host_malloc: failed to allocate 203.78 MiB of pinned memory: resource already mapped"), "ok");
        h.Check("null -> false", !ServerCrashAdvisor.IsPinnedMemoryFailure(null), "ok");
        h.Check("normal line -> false", !ServerCrashAdvisor.IsPinnedMemoryFailure("load_tensors: loading model tensors"), "ok");
        h.Check("pinned mention alone -> false", !ServerCrashAdvisor.IsPinnedMemoryFailure("using pinned memory for faster transfers"), "ok");

        h.Section("ServerCrashAdvisor.IsCudaInitFailure");
        h.Check("real cuda error line",
            ServerCrashAdvisor.IsCudaInitFailure("0.22.574.241 E CUDA error: shared object initialization failed"), "ok");
        h.Check("other cuda error -> false", !ServerCrashAdvisor.IsCudaInitFailure("CUDA error: out of memory"), "ok");
        h.Check("null -> false", !ServerCrashAdvisor.IsCudaInitFailure(null), "ok");

        h.Section("ServerCrashAdvisor.ShouldSuggestDisableNoMmap");
        var pinned = "ggml_cuda_host_malloc: failed to allocate 1.00 MiB of pinned memory: resource already mapped";
        var cuda = "E CUDA error: shared object initialization failed";
        h.Check("pinned + no-mmap -> suggest", ServerCrashAdvisor.ShouldSuggestDisableNoMmap(pinned, true), "ok");
        h.Check("cuda + no-mmap -> suggest", ServerCrashAdvisor.ShouldSuggestDisableNoMmap(cuda, true), "ok");
        h.Check("pinned + mmap on -> no", !ServerCrashAdvisor.ShouldSuggestDisableNoMmap(pinned, false), "ok");
        h.Check("normal + no-mmap -> no", !ServerCrashAdvisor.ShouldSuggestDisableNoMmap("main: model loaded", true), "ok");
    }
}
