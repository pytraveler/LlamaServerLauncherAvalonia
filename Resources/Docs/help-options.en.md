# Options — additional server flags

Flags that shape server behaviour, memory use, and which endpoints are available.

The switches here are tri-state: **on**, **off**, and **default**. "Default" means the flag is not passed at all and the decision is left to llama-server. Options your binary does not support are marked in the UI — that comes from parsing `llama-server --help`.

## Performance and memory

- **Flash Attention** (`--flash-attn on|off|auto`) — faster and lighter on memory where the build supports it.
- **K / V cache type** (`-ctk`, `-ctv`) — KV cache quantization (e.g. `q8_0`). Cuts VRAM use sharply at long context; many builds require Flash Attention to be on.
- **Continuous batching** (`-cb`) — serve requests from different clients through one batching stream.
- **Parallel slots** (`-np`) — how many requests the server handles at once. Context is divided between slots.
- **mlock** — keep the model from being swapped out.
- **mmap** — load the model by memory-mapping the file. Turning it off (`--no-mmap`) is sometimes suggested for speed, but it is also the most common cause of pinned-memory errors and failed CUDA init — the app recognizes those crashes and offers to switch mmap back on.

## Context and prompt cache

- **Prompt caching** (`--cache-prompt`) — reuse a shared prefix between requests.
- **Context shift** (`--context-shift`) — drop the start of the conversation on overflow instead of erroring out.
- **Timeout** (`-to`) — per-request time limit.

## Multimodality and model role

- **MMProj** (`--mmproj`) — the vision projector file for multimodal models. The main model is set on the **Main** tab; the projector goes here.
- **Projector on GPU** (`--mmproj-offload` / `--no-mmproj-offload`) — where the projector itself is loaded. It defaults to the card, where it can take a gigabyte or two: llama.cpp prints the figure at load time, and the VRAM panel counts it. Turned off, the projector stays in system memory and that VRAM goes to context or layers instead; the price is that image preprocessing then runs on the CPU and takes several times longer. Worth turning off when pictures are rare and context is short.
- **Embedding mode** (`--embedding`) — the server returns vectors instead of generating text.
- **Reasoning** (`--reasoning`) and **reasoning budget** — control reasoning mode on models that support it.
- **Jinja templates** (`--jinja` / `--no-jinja`) — the chat template engine. Recent llama.cpp builds enable it by default, so Auto is normally the right answer and the switch exists for the two cases where it is not. Older builds shipped it disabled, and there tool calls and MCP servers do not work at all until it is **On**. A custom `--chat-template` is the other case: llama.cpp only accepts one of its built-in template names unless jinja was turned on ahead of that flag, and the switch guarantees the order, because custom arguments are appended last. **Off** passes `--no-jinja` and takes tool calls down with it; the MCP tab says so when servers are enabled at the same time.

## Access and observability

- **WebUI** (`--webui` / `--no-webui`) — the server's built-in web interface.
- **Slots** (`--slots`) — the `/slots` endpoint. Required for the app to show slot occupancy of a running instance.
- **Metrics** (`--metrics`) — the Prometheus-format `/metrics` endpoint. Required if you want to capture benchmarks.
- **API key** (`--api-key`) — mandatory Bearer-token auth. Turn it on if the server is exposed beyond your machine.
- **Alias** (`-a`) — the model name clients see over the API.
- **Log file** (`--log-file`) — write the server log to a file in addition to the window.
- **Verbose Logging** (`-v`) — a three-position switch where only "On" does anything: `-v` is a presence-only flag, so "Off" and "Auto" both simply leave it out. Turned on, it makes llama.cpp print its own memory breakdown while loading: what went to weights, cache and buffers, and how much it could not attribute to any of them. The "Will it fit in VRAM" panel on the **Generation** tab checks its estimate against that breakdown, and the same comparison goes into the application log. The price is noise: a line per tensor while loading, and per-request detail afterwards. Sensible to turn on for a couple of launches and then off.
