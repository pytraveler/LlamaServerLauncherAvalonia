# Parameter Optimization — Guide

This window automatically searches for llama.cpp runtime settings that make **your** model run faster on **your** hardware, then lets you apply the best one to your profile.

You don't need to understand the internals to use it — pick a model, click **Run**, and apply the result. The notes below are for the curious.

---

## What it actually does

Modern llama.cpp has many performance knobs (GPU layers, batch sizes, flash attention, cache types, MoE CPU offload, and so on). The fast combination is hardware- and model-specific, and trying them all by hand is slow.

This tool treats it as a **hyperparameter optimization (HPO)** problem: it repeatedly benchmarks different configurations and learns which directions improve throughput, spending most of its attempts near the promising settings.

The optimization engine is a from-scratch **C# port** adapted from two open-source projects (see the **About** dialog for links):

- **llama-optimus** — the idea and recipe of tuning llama.cpp throughput with an optimizer.
- **Optuna** — the optimization framework whose sampler design is ported here.

---

## How a run is structured

A run is split into **stages** (shown in the *Stage* column of the trials table):

1. **Coarse search** — broad exploration of the search space to find promising regions.
2. **Focused search** — more trials concentrated around the best regions found so far.
3. **Confirmation** — the best candidate is re-measured (optionally several times) to make sure the win is real and not measurement noise.

Each row in the table is one **trial**: a single configuration that was benchmarked. The `★` marks the best trial so far, and the *t/s* column is its measured throughput (tokens per second).

### The sampler

Trials are not random guesses. After a few warm-up samples, a **TPE sampler** (Tree-structured Parzen Estimator, ported from Optuna) models which parameter values tend to produce good vs. bad results and biases new trials toward the good ones. That's why later trials usually beat earlier ones.

---

## Benchmark backends

There are two ways a configuration can be measured, selected by the **Use HTTP benchmark** checkbox:

- **llama-bench** (default) — fast, runs the standard `llama-bench` tool.
- **Real server over HTTP** (variant B) — launches an actual `llama-server` and measures it via HTTP. This is the **only** way to benchmark vision models (`--mmproj`) and full-context scenarios, which `llama-bench` can't load.

---

## Key settings

- **Metric** — what to maximize: token generation (*tg*), prompt processing (*pp*), or their *mean*.
- **Trials** — how many configurations to try. More trials = better results but longer runs.
- **Repeat** — how many times to re-measure each configuration (reduces noise).
- **N tokens** — workload size used for the benchmark.
- **Warmup runs** / **No warmup** — discard the first measurement(s), which are often slower due to caches warming up.
- **NGL max** — upper bound for the GPU-layers search.
- **Override mode** — controls tensor-override (`-ot`) scanning: *none* or *scan*.
- **Tune --n-cpu-moe** — also search how many MoE expert layers to offload to CPU (for Mixture-of-Experts models).
- **Estimate context** — estimate the largest context that fits.
- **Prune batch < ubatch** — skip invalid combinations where batch size is smaller than micro-batch size.
- **No mmap** — disable memory-mapped model loading during benchmarks.

---

## Fit mode

When **Use fit** is enabled, llama.cpp's own `--fit` logic decides the GPU layer count (`ngl`) to pack the model optimally, instead of the tuner searching it. **Fit margin** leaves a safety headroom of VRAM.

> For Mixture-of-Experts models, `--fit` already places experts near-optimally, so the tuner rarely beats it. The tuner adds the most value for **non-fitting**, **dense**, or **prompt-processing-bound** scenarios.

---

## Applying the result

When a run finishes, the **Result** panel shows the improvement over the untuned baseline and the full `llama-server` command.

There is a built-in guard: if the best configuration found is **not actually faster** than the baseline (no tuning), the tool refuses to apply it — it will never make your config slower than doing nothing.

Click **Apply to configuration** to copy the winning settings back into your current profile.
