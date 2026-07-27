# Speculative decoding

A generation speed-up: a small "draft" model quickly proposes several tokens ahead, and the main model verifies them in a single pass. Matching tokens are accepted as a batch, which beats generating them one at a time. Output quality is unaffected: anything the main model does not confirm is discarded.

The whole section is optional. Leave it empty and the server runs normally.

## When it pays off

- The main model is large and memory-bound, while the draft model is small and shares its architecture/tokenizer.
- The text is predictable (code, structured answers) — guessing lands more often there.

If the draft model misses too often, throughput actually drops: verification costs time. Measuring both ways is worthwhile — see **Benchmarks**.

## General parameters

- **Speculative type** (`--spec-type`) — the mode; accepted values are read from your binary's help output.
- **Draft N-Max / N-Min** (`--spec-draft-n-max`, `--spec-draft-n-min`) — how many tokens the draft model proposes at a time.
- **Draft P-Split / P-Min** (`--spec-draft-p-split`, `--spec-draft-p-min`) — probability thresholds at which a guess is accepted or dropped.

## Draft model

- **Draft model** (`-md`) — path to the draft `.gguf`, or
- **Draft HF repo** (`--hf-repo-draft`) — download it from HuggingFace.
- **Draft GPU layers** (`-ngld`) — how many draft-model layers to offload to the GPU.

> The draft model takes its own share of VRAM. If loading it forces you to lower the main model's `-ngl`, the gain can be wiped out — watch total memory use.
