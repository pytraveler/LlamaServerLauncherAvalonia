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

### Will it fit in VRAM

Under the fields sits a line answering the question the settings above are really about: whether this model, with these values, fits into the memory the card has free right now. It is read out of the GGUF - the weights of the blocks that would be offloaded, the KV cache for the current context, and the buffers llama.cpp allocates for itself - and it is recomputed as you type, so it always describes what is on the form rather than what was there a minute ago.

The verdict comes in three colours: it fits with room to spare, it fits but only just (the estimate is within a few percent of what is free, and anything unaccounted for will push it over), or it does not fit. The second line breaks the number down into weights, cache, buffers and headroom - the last being a flat allowance for what the backend takes on top of the buffers it reports, the driver context above all. It also says how many blocks would go to the card and how much would be left in system memory. "Rough estimate" appears when the file did not say enough about its own attention layout and a rule of thumb was used for part of the answer.

Once a server is running with the model named on the form, a third line can appear: what llama.cpp says it actually took, in the same breakdown, and - when the settings have not been touched since the launch - by how much the estimate was over or under. The same comparison goes into the application log every time a model finishes loading.

That line only shows up when the server was started with **Verbose Logging** on, on the Options tab, because llama.cpp prints its memory breakdown at no lower verbosity. It is worth turning on for a launch or two to see how close the estimate runs on your own hardware, and worth turning back off afterwards: the same switch also prints a line per tensor and a great deal more besides.

While a server is running with the model named on the form, whatever it holds counts as free again and the line is marked "(after a restart)": applying new settings restarts the server, and that frees the old memory before it takes the new. The three hints do the same, so they stop suggesting that a model already sitting on the card be moved off it. What the running server holds is taken from its own measurement when there is one, and from an estimate of its settings when there is not.

A multimodal setup adds one more part to both lines: the vision projector. The estimate counts the weights of the `-mm` file, since that is what the card has to hold; llama.cpp reserves a graph on top of them and prints its own worst-case figure at load time, which is what the measured line shows. Both parts disappear when **Projector on GPU** is off, because the projector then lives in system memory.

Three hints appear next to the fields themselves, each setting a value when clicked: the largest layer count that fits at the current context, the largest context that fits with the current layer count, and - for MoE models - how many expert blocks to leave on the CPU so that the rest fits. A hint hides itself when what it offers is already what is set.

The estimate knows nothing about a second card or a tensor split, and it says nothing about speed. It is a starting point, not a promise; the Optimize window still measures rather than guesses.


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
