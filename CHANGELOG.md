# Changelog

[Русская версия](CHANGELOG.ru.md)

The GitHub release notes are built out of this file and its Russian twin. The
release workflow refuses a tag whose version disagrees with
`LlamaServerLauncher.csproj`, or one that neither changelog has a section for;
screenshots and other per-release extras live in `.github/release-notes/`.
Nothing is written by hand at tag time.

## v1.9.1 - 2026-08-25

- **A server that crashes on startup now explains itself in the log** — llama-server dying before it printed anything left a single line with a bare number: `ExitCode=-1073741819`. That number is `0xC0000005`, a native access violation, and it was read again and again as "out of memory" — the one thing it is not. Exit codes are now spelled out (`-1073741819 (0xC0000005, STATUS_ACCESS_VIOLATION)`) along with what actually produces them: a missing or outdated Visual C++ Redistributable, a mismatched GPU driver or CUDA runtime, a half-extracted llama.cpp build, foreign ggml/cudart/cublas DLLs picked up from PATH. A server that died without printing a single line is called out as such — it crashed before the model was even touched, so the model, the context size and the free VRAM have nothing to do with it — and the log gains the versions of `msvcp140.dll`, `vcruntime140.dll` and `vcruntime140_1.dll` found in System32 and next to the executable, since a copy lying next to the binary is loaded instead of the system one. The `--help` probe behind the flag filter reports the same way: "Failed to parse --help output" now names the reason, and a binary that crashes on `--help` is flagged as one that will crash on start too.

- **A binary that answers nothing no longer empties the command line** — when `--help` returned nothing usable, the launcher could end up holding an empty set of supported flags, and the filter then quietly stripped every argument from the command line instead of switching itself off. Such an answer now counts as a failed probe, which is what turns filtering off — the fallback the filter was meant to have all along.

## v1.9 - 2026-08-23

- **MCP servers for the launched model** — recent llama.cpp builds can hand the model external tools over the Model Context Protocol, and the launcher now sets that up per profile. The new "MCP" tab keeps a list of servers: each is a row with an on/off switch, and clicking the row opens an editor window — command (with a browse button and a note that on Windows scripts want their extension, `npx.cmd` rather than `npx`), arguments, environment variables, working directory and timeout, plus a "Test" button that starts the command exactly the way llama-server would and reports the tools it answers with, before any model is loaded. On launch the app writes a Cursor-compatible `mcp.json` for the profile and passes it via `--mcp-servers-config`; the model then calls the tools straight from the llama.cpp WebUI. Servers can be imported from an existing `mcp.json` (Cursor and Claude Desktop keep the same format) or copied from another profile through the import button's menu. "Query" asks a running server `GET /tools` and shows what was actually discovered, grouped by MCP server. Failures that llama-server only mutters into its log — a server it could not spawn, one that died along the way — surface as notifications instead of scrolling past. With Docker the config directory is mounted into the container and the path rewritten. The tab warns about the things worth knowing: MCP servers run as child processes with the launcher's rights, an enabled MCP limits CORS to localhost unless set explicitly, and tool calls need jinja templates. `F1` on the tab opens a dedicated help page.

- **Nothing survives the server it belongs to** — an MCP server is free to start applications of its own (ComfyUI, say), and walking the process tree at shutdown does not always reach them: one intermediate process exiting early is enough to lose the trail, and such orphans kept sitting on the GPU. The server and everything it spawns are now held in a Windows job object, so stopping the server, closing the launcher or even the launcher crashing takes the whole family down. The "Behavior" tab gained a switch for the rare case where a spawned application should outlive the server. Windows only; elsewhere the old process-tree kill keeps working.

- **Prompt run in benchmarks** — the benchmark dialog gained a mode for when tokens per second are not the whole story and what the model actually answers with these settings matters. Set a system prompt and a list of requests (a line of three or more dashes starts the next one), and the launcher starts the server, waits until it is ready, sends the requests one by one as ordinary chat requests — as a conversation, or each from a clean slate — records the answers and stops the server, freeing the VRAM. The run's folder gains `prompt-run.md`: the full transcript with every request, the model's answer, its reasoning where the build reports it separately, and per-request speeds; a short per-request table lands in `report.md`, and the averages fill the comparison rows when the standard workload was not run. Sampling settings come from the launch arguments, so the run measures exactly the profile as assembled; a failed or timed-out request is written down and the run moves on. Answer length cap and per-request timeout are configurable, and routing-mode profiles (`--models-dir`) work too — the request names the model.

- **Progress bars are readable on every machine** — the download and update bars used to inherit the system accent, which on some machines renders an indistinguishable grey on grey. Their colours are now owned by the app theme (teal in dark, blue in light, the scheme accent in Ocean/Forest/Sunset/Ubuntu), and the "Custom" scheme gained a "Progress bar" colour alongside the others. The hardware meters keep their own colours.

- **The window stays inside the screen** — with auto-fit height on, a tall tab could grow the window past the bottom edge, leaving the buttons out of reach. The height is now capped by the working area of the screen and the content scrolls within its tab; a window that grew downwards is pulled back up.

## v1.8.2 - 2026-08-19

- **A custom argument no longer lands in the command twice** — when the custom arguments carried the long form of a flag while the matching field was filled in, the server was handed both: `-ngl 99 --n-gpu-layers 99`. The app writes known arguments in their short form (`-ngl`), did not recognise the long one (`--n-gpu-layers`, `--gpu-layers`) as the same thing, and appended it as a separate entry — the command line ended up carrying the same argument twice, leaving it to guesswork which of the two llama-server would take. The GPU layer count showed it most often, being the flag people usually copy in its long form out of someone else's command. `--flash-attn` next to `-fa`, `--mmproj` next to `-mm`, `--ctx-size` next to `-c` and every other pair of synonyms doubled up the same way. Synonyms of one argument now count as one argument and it reaches the command line exactly once — in the preview, when the server is started, in the docker command and in the benchmarks. The value from the custom arguments still wins over the value in the field.

## v1.8.1 - 2026-08-19

- **The llama.cpp build list no longer stops short on a fresh release** — llama.cpp publishes the tag first and keeps uploading archives for the next couple of minutes, and the app could remember a half-uploaded release: the dialog showed the CPU builds and a single CUDA one, while Vulkan, SYCL, ROCm, OpenVINO and CUDA 13 stayed missing until the half-hour cache expired. Picking a release in the dialog now rereads it by tag with a short freshness window, so the list fills itself in. A build already picked by hand stays picked, and the auto-detection still prefers CUDA 12.4 over the newer 13.3 - the one that runs on an older driver.

## v1.8 - 2026-08-19

- **The toggle positions differ in colour, and the toggles line up on a grid** — the selected position is coloured by meaning: "Off" red, "Auto" blue, "On" green, so a glance across the panel is enough to tell what is switched off, what is left to the server, and what is forced on. All three positions used to light up the same way. Every option now occupies a cell of equal width on a plate of its own: the groups of three line up in columns, and a label stays attached to its own buttons instead of blending into the neighbouring ones. Under the "Custom" colour scheme all three highlight colours are configurable alongside the rest - the appearance section gained "Toggle: Off / Auto / On".

- **The update download is visible** — pressing "Update" now puts a progress bar with a percentage and a "Cancel" button in the header. The app used to pull a file of several tens of megabytes in complete silence: the button was pressed and nothing appeared to happen, leaving nothing to do but wait. When the server does not report a size the bar simply moves, and once the file is down the caption turns into "Restarting the application...". Cancelling puts the update button back and installs nothing.

- **The update check explains itself and never offers a downgrade** — "an update is available" used to mean nothing more than "the published file hashes differently than the local one". That made the check behave differently depending on where the release list came from: the github.com pages the app falls back to when the API quota runs out carry no digests at all, so the decision quietly fell back to comparing versions, while data from the API could offer an "update" to a release older than the one installed. The version decides now: a later release is offered, an older or equal one is not, and the digest is only there to notice a rebuilt binary published under the same tag - and it is computed only when it actually decides, rather than on every check of a fifty-megabyte file. The log now carries a line with the source of the data, the tag found, the local version and the verdict, and a failed check is no longer swallowed in silence. When the list came off the release page or out of the on-disk cache, the "Update" button's tooltip says so.

- **A profile loads once the choice is made, not while the list is being scrolled** — an open profile list now just scrolls: the highlight walks the rows and the profile is picked up when the list closes. The mouse wheel over the closed box still steps through profiles, but the load happens a moment after the wheel stops. Every step used to load another profile, and could interrupt the scrolling with a question about unsaved changes. A list closed on the profile it was opened on does nothing at all.

## v1.7 - 2026-08-16

- **The on-demand proxy address is picked from a list** — instead of the DNS name of the machine, which plenty of clients simply cannot resolve, the "On-Demand Proxy" panel offers a dropdown of this machine's addresses, the ready-made URL underneath it and a "Copy" button. The address of a network with a gateway comes first - the one a device in the same room can actually reach - then the machine itself (`127.0.0.1`) and its network name, while VPN and virtual adapters (WireGuard, VMware, Hyper-V) go last, each labelled with its adapter. Disconnected adapters and self-assigned `169.254.*` addresses are not listed at all. The choice is remembered between sessions, and if that address is gone - a laptop that moved to another network - the best available one takes over. The log streaming hint no longer leans on the machine name either.

- **The three-way option toggles show what is selected** — the "Off / Auto / On" toggles on the "Options" tab no longer depend on the accent colour of the system: the selected position is marked with a border and a caption in the theme colour and reads the same on every machine. Where the Windows accent happens to be close to the button grey nothing was visible at all before, and where that accent is red the selected value looked like an error. Options the current llama-server build does not support now show their value dimmed instead of fully grey - you can still see what is set even when it cannot be changed.

- **The app picks its accent colour, the system no longer does** — under the default colour scheme, checkboxes, switches, sliders, progress bars and the selected tab were painted with the accent colour from the Windows settings, so the app looked different from machine to machine, sometimes to the point of being unreadable. The dark theme now uses teal and the light theme blue, while a chosen colour scheme ("Ocean", "Forest", "Sunset", "Ubuntu") still sets its own accent as before.

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
