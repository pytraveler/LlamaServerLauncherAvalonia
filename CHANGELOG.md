# Changelog

[Русская версия](CHANGELOG.ru.md)

The GitHub release notes are built out of this file and its Russian twin. The
release workflow refuses a tag whose version disagrees with
`LlamaServerLauncher.csproj`, or one that neither changelog has a section for;
screenshots and other per-release extras live in `.github/release-notes/`.
Nothing is written by hand at tag time.

## v1.6 - 2026-07-27

- **Built-in help** — a button at the bottom of the navigation rail (or the `F1` key) opens a short guide for the section you are currently on: what its fields do, what changing them costs, and where to start. The "Benchmarks", "Benchmark launch", "Scenarios" and "Optimization" windows gained their own help buttons, and `Ctrl+F1` lists the keyboard shortcuts. Help ships inside the executable and works offline, its language follows the UI language, and every page ends with a link to the full README. Empty states became clearer too: with no runs recorded yet, the benchmark comparison window explains where runs come from and offers to start the first one right there; when runs exist but none are selected, it says what to tick.

- **Row filter for benchmark comparisons** — the "Rows" button controls what the table shows: results, server metrics, launch configuration, and environment can be toggled by group or row by row ("Select all" / "Clear all"). The selection is remembered between sessions, stored alongside a named comparison set, and honored when exporting the report to `.md` — so you can narrow the table down to the few numbers you care about and come back to it later.

- **Resilient GitHub releases** — app update checks, llama.cpp build downloads, and experimental repositories now share one caching client. Repeat requests are conditional (ETag-based) and barely touch the GitHub API quota; if the quota does run out, the release list is read straight from github.com pages, and when GitHub is unreachable the on-disk cache is shown instead. Either way the download dialog says so honestly: "GitHub API quota exhausted — release list fetched from github.com pages" or "GitHub unavailable — showing cache from <date>". The app update check now runs right at startup, and the download link is refreshed forcibly before downloading.

## v1.5 - 2026-07-22

- **Benchmarks** — a new profile launch mode that records performance. The start-server button's menu gained a "Run & save benchmark" item: before launching you can tweak the llama-server argument line (with per-argument-group toggles), enable metrics collection from the `/metrics` endpoint and a built-in standard workload (repeat count, prompt/generation sizes, fixed seed). Every run is saved in the data folder together with the configuration, command line, server log, metrics, and a report. The "Benchmarks" button opens the comparison window: selected runs are laid out side by side as Markdown tables, comparison sets can be saved under their own names, and reports export to `.md` — handy for tuning profile settings and comparing llama.cpp builds against each other.

- **Hardware monitor** — a gauge panel in the main window: CPU, RAM and GPU load, VRAM usage, and GPU temperature (multi-GPU supported). NVIDIA is polled via nvidia-smi, AMD via rocm-smi; CPU/RAM metrics work on Windows, Linux, and macOS. Polling pauses automatically while a model is loading so it cannot interfere with CUDA/HIP initialization. Can be disabled on the "Behavior" tab.

- **GGUF model insights** — metadata of the selected model is read directly from the file: a badge next to it shows the architecture, quantization, size, layer/expert count, and a vision-projector flag. The model's maximum (training) context is shown as a "Model max" label and caps the context-size slider. Smart hints were added: a warning when an mmproj file is selected instead of a model, and a clickable "offload all N layers" hint for the GPU-layers field.

- **Model picker window** — the "Pick…" button next to the model path opens a folder scanner: every `.gguf` in the chosen directory (recursively, if desired) is listed with metadata, file size, and an mmproj badge, filterable by name; the last folder is remembered.

- **Model-load indicator** — after start, the app polls `/health` and shows a "Loading model…" status (with an amber instance highlight) until the server is actually ready to serve requests.

- **Live inference stats** — each running instance now shows prompt and generation speeds (tokens per second) and slot usage underneath. The data comes from the server output and `/slots` polling; the polling chatter from health/slots no clutters the log.

- **Copy endpoint & curl command** — a running instance's menu gained a "Copy" submenu: "Server URL", "OpenAI base URL (/v1)", and "curl (chat completion)" — a ready-to-run command to test the server from a terminal or hook up a client.

- **llama.cpp backend auto-detect** — the download dialog detects the GPU vendor (NVIDIA / AMD / Intel / Apple) and pre-selects the best-matching build (CUDA / Vulkan / HIP / SYCL / CPU), showing an "Auto-detected" hint; the choice can still be changed manually.

- **Crash advisor** — if the server crashes during model load with pinned-memory / CUDA-init errors characteristic of `--no-mmap`, the app shows a sticky notification naming the profile and suggesting to disable that option.

- **Miscellaneous** — the app-update dialog now shows the new release's notes as rendered Markdown; small UI refinements (including tri-state on/off/default option selectors); the automated test suite grew substantially (GGUF metadata, metrics parsers, build selection, benchmarks, and more).

## v1.4 - 2026-06-30

**Idle-unload countdown mini-window (for the on-demand proxy)** — when the proxy loads a model into memory, a compact always-on-top window appears with a countdown to the idle unload. The "Cancel unload" button keeps the model resident indefinitely (morphing into "Unload model" for an immediate manual unload) — handy when composing or rewording the next prompt takes longer than the idle timeout allows. The window remembers its on-screen position, closes itself when the countdown ends, and (once in the held state) hides when you close it or switch to the main window. Enabled on the "On-Demand Proxy" tab (off by default).

**Reworked profile selector** — picking a profile from the dropdown now loads it immediately (the separate "Load" button and the in-list name typing are gone). Profile actions are grouped under the "Save" button's menu: "Save As…" (create a new profile), "Rename", "Clone", "Delete", plus "Export" and "Import". "Clear fields" moved to the "⋮" overflow menu.

## v1.3 - 2026-06-25

**On-demand model proxy (OpenAI-compatible)** — a built-in reverse proxy that loads the right launcher profile on demand when an API request arrives. Point a client (e.g. Cherry Studio) at the proxy and pass the profile name in the `model` field — the model loads itself, serves the request (response streaming included), and unloads after an idle timeout. Only one model stays in VRAM: starting a profile evicts any other running one. Supported endpoints are `/v1/chat/completions`, `/v1/completions`, `/v1/embeddings`, `/v1/rerank`, `/infill`, plus `/v1/models` (advertises your profiles as models) and `/health`. It listens on all network interfaces and can be protected with an optional Bearer key. Configured on the "On-Demand Proxy" tab.

**Free ComfyUI VRAM before loading a profile** — optionally calls ComfyUI's `/free` endpoint to unload its models and release GPU memory before any profile starts (manual start or via the proxy). Handy for the "ComfyUI ↔ llama.cpp on a single GPU" workflow: hand the GPU between tools without juggling them by hand. Configured on the "Behavior" tab (toggle + ComfyUI URL).

**App version in the About dialog** — the installed version is now shown (stamped at build time) and, if the periodic update check has already found one, the latest available release — without any extra GitHub API calls.

## v1.2 - 2026-06-20

**Automatic parameter tuning (llama-optimus + Optuna port)** — adds automatic tuning of `llama.cpp` parameters (`-t`, `--batch-size`, `--ubatch-size`, `-ngl`, `--flash-attn`, `--override-tensor`, `--n-cpu-moe`) to maximize throughput. Opened from a dedicated "Optimize parameters" window: the engine runs a series of benchmarks, finds the best configuration for your model and hardware, and lets you apply it to your profile with one click. The logic is ported from [llama-optimus](https://github.com/BrunoArsioli/llama-optimus) and a minimal [Optuna](https://github.com/optuna/optuna) core (TPE / Grid / Random samplers) to C#/.NET — with no Python dependency.

## v1.1 - 2026-06-17

1. **Signed and verifiable releases** — release builds are automated via GitHub Actions. Every binary ships with a build provenance attestation, `SHA256SUMS` checksums, and their GPG signature (`SHA256SUMS.asc`). Anyone can now confirm a file was built from this repository and has not been tampered with.

2. **All-platform CI builds** — releases are automatically built for `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64` (Apple Silicon) through a single reproducible process.

3. **Verification instructions in the README** — a new "Verifying releases" section provides ready-to-use commands for checking provenance (`gh attestation verify`), integrity (`sha256sum`), and the GPG signature. The public key is published in the repository.

**Fixes and improvements**
- README brought up to date with current functionality: speculative decoding, experimental build repositories, WebUI browser selection, offline mode, HF file/draft repo, log rotation
- Project structure in the README extended with the new services and models
- Build examples now include the `osx-arm64` (Apple Silicon) target

> [!NOTE]
> Binaries are not OS code-signed (Authenticode/Apple). The recommended way to trust a download is verifying provenance and checksums (see README → "Verifying releases").
