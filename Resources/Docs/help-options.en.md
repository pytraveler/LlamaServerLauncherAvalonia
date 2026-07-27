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
- **Embedding mode** (`--embedding`) — the server returns vectors instead of generating text.
- **Reasoning** (`--reasoning`) and **reasoning budget** — control reasoning mode on models that support it.

## Access and observability

- **WebUI** (`--webui` / `--no-webui`) — the server's built-in web interface.
- **Slots** (`--slots`) — the `/slots` endpoint. Required for the app to show slot occupancy of a running instance.
- **Metrics** (`--metrics`) — the Prometheus-format `/metrics` endpoint. Required if you want to capture benchmarks.
- **API key** (`--api-key`) — mandatory Bearer-token auth. Turn it on if the server is exposed beyond your machine.
- **Alias** (`-a`) — the model name clients see over the API.
- **Log file** (`--log-file`) — write the server log to a file in addition to the window.
