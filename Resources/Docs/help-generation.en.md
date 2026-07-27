# Generation — model and sampling parameters

The tab has two blocks: **Model parameters** — how the model is loaded into memory, and **Generation parameters** — how it picks the next token.

## Model parameters

- **Context size** (`-c`) — how many tokens the model remembers. The upper bound comes from the selected model's GGUF metadata. Context costs VRAM: the larger it is, the less is left for model layers.
- **Threads** (`-t`) — CPU threads. Your physical core count is a reasonable starting point.
- **GPU layers** (`-ngl`) — how many layers to offload to the GPU. The hint next to the field ("offload all N layers") fills in the layer count read from the GGUF. Fewer layers means less VRAM and markedly lower speed.
- **`--n-cpu-moe`** — for MoE models: how many expert blocks to keep on the CPU. Lets a large MoE model fit into modest VRAM at the cost of speed.
- **Batch size** (`-b`) and **ubatch** (`-ub`) — batch sizes used while processing the prompt. They mostly affect prompt processing speed, not generation. `ubatch` must not exceed `batch`.
- **Seed** (`-s`) — pins randomness so results reproduce.

An empty field means "don't pass the flag" — llama-server falls back to its own default.

## Generation parameters

These are server-side defaults; a client (the WebUI or an API request) can send its own and override them.

- **Temperature** (`--temp`) — spread. Lower is more predictable, higher more varied.
- **Max tokens** (`-n`) — ceiling on response length.
- **Top-K**, **Top-P**, **Min-P** — different ways of cutting off unlikely candidates.
- **Repeat / presence / frequency penalty** — pressure against repetition in the output.

## Benchmarks and optimization

Two buttons sit in the **Model parameters** block header:

- **Optimize…** — automatic tuning of performance parameters (layers, batches, flash attention, cache types, `--n-cpu-moe`) for your model and hardware. That window has its own guide.
- **Benchmarks** — the comparison window for saved runs. A run itself is started elsewhere: the **Start server** button's dropdown → **Run and save benchmark**.
