# LlamaServerLauncher

[Русский](README_ru.md) | [Changelog](CHANGELOG.md)

![LlamaServerLauncher](docs/images/preview.png)

A cross-platform desktop application for launching and managing [llama.cpp](https://github.com/ggerganov/llama.cpp) server instances with an intuitive graphical interface.

Built with [Avalonia UI](https://avaloniaui.net/) and .NET 8.

## Features

### Server Configuration
- **Executable Path** — Select the `llama-server` binary, or download llama.cpp directly from the app
- **Model Selection** — Choose a specific model file (.gguf), set a models directory, or specify a HuggingFace repo (`--hf-repo`) and file (`--hf-file`)
- **Model Picker** — Scan a folder (optionally recursive) for `.gguf` files and pick from a filterable list with metadata, file size, and an mmproj badge; the last folder is remembered
- **Network Settings** — Configure host address (default: 127.0.0.1) and port (default: 8080)
- **API Key** — Set authentication API key for the server
- **Offline mode** — Force cache-only operation with no network access (`--offline`)
- **Input history** — Path and value fields remember recently used entries for quick reuse

### Model Parameters
- Context size (`-c`, `--ctx-size`)
- Number of threads (`-t`, `--threads`) and batch threads (`-tb`, `--threads-batch`)
- GPU layers (`-ngl`, `--gpu-layers`, `--n-gpu-layers`)
- Batch size (`-b`, `--batch-size`)
- UBatch size (`-ub`, `--ubatch-size`)
- MMProj path (`-mm`, `--mmproj`)
- Cache type K (`-ctk`, `--cache-type-k`)
- Cache type V (`-ctv`, `--cache-type-v`)
- Parallel slots (`-np`, `--parallel`)
- Timeout (`-to`, `--timeout`)
- Seed (`-s`, `--seed`)

### Generation Parameters
- Temperature (`--temp`, `--temperature`)
- Max tokens (`-n`, `--predict`, `--n-predict`)
- Min-P sampling (`--min-p`)
- Top-K sampling (`--top-k`)
- Top-P sampling (`--top-p`)
- Repeat penalty (`--repeat-penalty`)
- Presence penalty (`--presence-penalty`)
- Frequency penalty (`--frequency-penalty`)
- Reasoning mode (`-rea`, `--reasoning`)
- Reasoning budget (`--reasoning-budget`)

### Speculative Decoding
- Speculative decoding type (`--spec-type`)
- Draft model (`-md`, `--spec-draft-model`) or HuggingFace draft repo (`--hf-repo-draft`)
- Draft GPU layers (`-ngld`)
- Draft N-Max / N-Min (`--spec-draft-n-max`, `--spec-draft-n-min`)
- Draft P-Split / P-Min (`--spec-draft-p-split`, `--spec-draft-p-min`)

### Advanced Options
- Flash Attention (`-fa`, `--flash-attn`)
- Continuous batching (`-cb`, `--cont-batching`)
- WebUI (`--webui`, `--no-webui`)
- Embedding mode (`--embedding`, `--embeddings`)
- Slots management (`--slots`, `--no-slots`)
- Metrics endpoint (`--metrics`)
- Cache prompt (`--cache-prompt`, `--no-cache-prompt`)
- Context shift (`--context-shift`, `--no-context-shift`)
- Memory lock (`--mlock`)
- Memory map (`--mmap`, `--no-mmap`)
- API key authentication (`--api-key`)
- Alias (`-a`, `--alias`)
- Custom command-line arguments (with toggleable enable/disable per argument)

### Feature Detection
The app automatically parses `llama-server --help` to detect which flags your binary supports. Unsupported options are visually indicated in the UI.

### Built-in Help
- **Section help** — a button at the bottom of the navigation rail (or the `F1` key) opens a short guide for the section you are currently on: what its fields do, what changing them costs, and where to start
- The **Benchmarks**, **Benchmark launch**, **Scenarios** and **Optimization** windows have their own help buttons
- Help ships inside the executable and works offline; its language follows the UI language. Each page ends with a link to the full README on GitHub
- **Meaningful empty states** — with no runs recorded yet, the benchmark comparison window explains where runs come from and offers to start the first one right there
- `Ctrl+F1` — the keyboard shortcut list

### GGUF Model Insights
- **Model info badge** — architecture, quantization, parameter size, layer/expert count, and vision projector info are read directly from the GGUF file and shown next to the model path
- **Max context detection** — the model's training context length is detected and displayed; the context-size slider is capped to it
- **Smart hints** — a warning when an mmproj projector file is selected as the model, and a clickable "offload all N layers" GPU-layers suggestion

### Multi-Instance Server Management
- Run multiple server instances simultaneously, each with its own profile/configuration
- Per-instance controls: start, stop, restart, unload model, open in browser
- **Model-load indicator** — after start, the app polls `/health` and shows "Loading model…" until the server is actually ready to serve
- **Live inference stats** — prompt/generation tokens per second (parsed from server output) and busy/total slots (polled via `/slots`) shown per running instance
- **Copy menu** — copy the server URL, the OpenAI-compatible base URL (`…/v1`), or a ready-to-run `curl` chat-completion command for any running instance
- **Crash advisor** — recognizes pinned-memory / CUDA-init failures caused by `--no-mmap` and suggests disabling it in a sticky toast
- Per-instance auto-restart on crash and log toggle
- Short-lived server error indicator (shows if instance exits within 5 seconds of starting)
- Instance view in system tray menu with full per-instance controls

### Hardware Monitor
- CPU, RAM, GPU utilization, VRAM, and GPU temperature gauges displayed above the instance list (multi-GPU aware)
- NVIDIA GPUs via `nvidia-smi`, AMD GPUs via `rocm-smi`; CPU/RAM metrics on Windows, Linux, and macOS
- Automatically pauses while a model is loading so GPU polling cannot interfere with CUDA/HIP initialization
- Can be disabled on the **Behavior** tab

### Benchmarks
- **Run & save benchmark** — launch a profile in benchmark mode from the Start-server split-button flyout, with an editable llama-server argument line (with per-argument-group toggles)
- Captures the server's `/metrics` endpoint (Prometheus) and tokens-per-second from the log; optionally drives a built-in standard HTTP workload against the live server
- Each run is stored per profile in the data directory (`benchmarks/<profile>/<runId>/`: config, command line, server log, metrics, report)
- **Comparison window** — compare saved runs side by side as Markdown tables, pick which rows (metrics, launch parameters, environment) the table shows, save named comparison sets together with that row selection, export reports as `.md`, and pin extra files to a run

### On-Demand Model Proxy (OpenAI-compatible)
A built-in reverse proxy that loads the right profile on demand when an API request arrives — point a client like Cherry Studio at it and the matching model is loaded automatically, served, and unloaded when idle.
- **OpenAI-style endpoints** — `/v1/chat/completions`, `/v1/completions`, `/v1/embeddings`, `/v1/rerank`, `/infill`, plus `/v1/models` (advertises your profiles as models) and `/health`
- **Profile = model** — send the profile name in the request's `model` field; the proxy starts that profile, waits until it is healthy, then proxies the request and streams the response back (SSE supported)
- **One model in VRAM** — starting a profile evicts any other running one, so only a single model stays loaded
- **Idle auto-unload** — stops the server after a configurable idle timeout (set to `0` to disable)
- **LAN-accessible** — listens on all interfaces; optional Bearer API key for access control
- Routing-mode profiles (`--models-dir`) are excluded from the advertised model list

### ComfyUI Integration
- **Free ComfyUI VRAM before loading a profile** — optionally calls ComfyUI's `/free` endpoint to unload its models and release GPU memory before any profile starts (manual start or via the on-demand proxy), so you can hand the GPU between ComfyUI and llama.cpp without juggling them by hand. Configured on the **Behavior** tab (toggle + ComfyUI URL)

### Scenarios
- Define sequences of profiles that run in order with configurable time intervals
- Auto-start scenarios on application launch
- Create, edit, rename, and delete scenarios
- Drag-and-drop profile ordering within a scenario
- Clone profile directly into a scenario

### Logging & Monitoring
- Log file output (`--log-file`)
- Verbose logging (`-v`, `--verbose`)
- Real-time log viewer with auto-scroll
- Server status display with process ID
- Auto-restart on crash
- Automatic log file rotation (configurable max file count and size)
- Health/slots polling heartbeats are filtered out of the log view
- **Built-in Log Stream Server** — WebSocket-based log streaming with HTTP API endpoints:
  - `/ws` — Real-time WebSocket log streaming with optional token authentication
  - `/api/logs/history` — JSON endpoint for log history
  - `/api/status` — Stream server status
  - Built-in HTML log viewer page with auto-scroll, clear, and reconnect controls

### llama.cpp Integration
- **One-click download** — Download official llama.cpp releases directly from GitHub
- **Backend auto-detect** — the download dialog detects the GPU vendor (NVIDIA / AMD / Intel / Apple) and pre-selects the best-matching build (CUDA / Vulkan / HIP / SYCL / CPU) with an "Auto-detected" hint; the choice can still be changed manually
- **Update notifications** — Automatically checks for new llama.cpp releases
- **Version management** — Install and switch between different versions
- **PATH integration** — Optionally add llama.cpp directory to PATH
- **Experimental build repositories** — Add custom GitHub release sources (e.g. [llama-cpp-turboquant](https://github.com/pytraveler/llama-cpp-turboquant)) with tag filters and periodic update checks to download experimental builds

### App Updates
- **Auto-update** — Automatically checks for new application releases and supports one-click update with restart
- **Release notes** — the update prompt shows the GitHub release notes rendered as Markdown
- **Version display** — The About dialog shows the installed version (stamped from the build) and, when the periodic check has already found one, the latest available release — without making any extra GitHub API calls

### System Integration
- **Auto-start** — Register the app to start with the operating system (Windows registry, Linux autostart .desktop, macOS LaunchAgent)
- **Single instance** — Enforces only one running instance; launching again activates the existing window
- **Toast notifications** — In-app toast messages for important events and errors
- **Browser selection** — Open the server WebUI in a chosen browser; installed browsers are auto-detected, or set a custom browser path

### Docker Support
- Docker CLI integration for container-based workflows
- Run individual instances in Docker containers

### Profile Management
- Save, load, rename, and delete configuration profiles
- Export profiles to JSON, Windows batch (.bat), Linux shell (.sh), or macOS script (.command)
- Import profiles from JSON
- Export/import all profiles as a ZIP archive
- Unsaved changes tracking
- Clone profiles to quickly create variants

### Drag & Drop
Drop files onto the window to import configurations or set paths:
- `.json` — Profile import
- `.bat` / `.cmd` — Windows batch file parsing
- `.sh` — Linux shell script parsing
- `.command` — macOS script parsing
- `.exe` — Set llama-server executable path
- `.gguf` — Set model path

### System Tray
- Minimize to system tray on window minimize
- Tray icon menu with per-instance server controls (start, stop, restart, auto-restart toggle, log toggle, unload model, open in browser)
- Double-click tray icon to restore window

### Localization
- English
- Russian

### Appearance & Themes
- Dark and Light theme variants
- Multiple color schemes: Default, Ocean, Forest, Sunset, Ubuntu
- Adjustable font size (S, M, L, XL)
- Custom font family selection
- Auto-fit height mode (window auto-sizes to content)
- Collapsible log panel and tab panel
- Window position and size persistence
- Dialog position and size persistence

### Data Management
- Configurable data directory (default or custom location)
- Easy migration of all data (settings, logs, llama.cpp) between directories

## Requirements

- .NET 8.0 Runtime or self-contained build
- [llama.cpp](https://github.com/ggerganov/llama.cpp/releases) server binary (`llama-server`), or download it from within the app

## Installation

1. Download the latest release from the [releases page](https://github.com/pytraveler/LlamaServerLauncherAvalonia/releases) for your platform
2. Put executable file to your desired location
3. Run `LlamaServerLauncher`

## Verifying releases

All release binaries are built by GitHub Actions and ship with a
[build provenance attestation](https://docs.github.com/actions/security-guides/using-artifact-attestations)
plus SHA-256 checksums (the checksum file is GPG-signed). The binaries themselves are
**not** code-signed, so verifying provenance/checksums is the recommended way to trust a download.

### 1. Verify provenance (proves the binary was built from this repository)

Requires the [GitHub CLI](https://cli.github.com/):

```bash
gh attestation verify LlamaServerLauncher_win_x64.exe \
  --repo pytraveler/LlamaServerLauncherAvalonia
```

### 2. Verify integrity (checksums)

```bash
# Linux / macOS
sha256sum -c SHA256SUMS

# Windows PowerShell — compare against the value in SHA256SUMS
(Get-FileHash LlamaServerLauncher_win_x64.exe -Algorithm SHA256).Hash
```

### 3. Verify the checksums signature (optional, GPG)

The public key lives in this repository at
[`LlamaServerLauncherAvalonia-public.asc`](LlamaServerLauncherAvalonia-public.asc)
(also attached to each release). Fetching it from the repository over HTTPS is the
stronger trust anchor.

```bash
gpg --import LlamaServerLauncherAvalonia-public.asc
gpg --verify SHA256SUMS.asc SHA256SUMS
```

Signing key fingerprint (verify it matches after import):

```
7CE2 D333 77DD 11F2 2748  DC40 2B4E E046 8C62 EBA1
```

## Build from Source

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build Commands

```bash
# Debug build
dotnet build LlamaServerLauncher.csproj

# Linux
dotnet publish LlamaServerLauncher.csproj -c Release -r linux-x64 -o ./publish/linux-x64

# Windows
dotnet publish LlamaServerLauncher.csproj -c Release -r win-x64 -o ./publish/win-x64

# macOS (Intel)
dotnet publish LlamaServerLauncher.csproj -c Release -r osx-x64 -o ./publish/osx-x64

# macOS (Apple Silicon)
dotnet publish LlamaServerLauncher.csproj -c Release -r osx-arm64 -o ./publish/osx-arm64
```

## Tests

The project uses lightweight, standalone console test harnesses rather than a test framework — no xUnit/NUnit, in keeping with the project's no-third-party-dependencies policy. Each harness links the relevant app sources directly, is excluded from the main application build, and returns exit code `0` when every check passes.

```bash
cd tests
dotnet run -c Release   # exit code 0 = all checks passed
```

Current coverage includes the command-line layer (`CommandLineParser`, `CommandLineBuilder`, `ServerConfiguration`), the optimization (HPO) engine, the on-demand proxy protocol helpers (`ProxyProtocol`), GGUF metadata reading, model folder scanning, the inference/GPU/CPU stats parsers (NVIDIA + AMD), endpoint/curl snippet building, llama.cpp backend asset selection, server log filtering, the crash advisor, and the benchmark metrics/report pipeline. Each area is a separate `*Tests.cs` file wired into `Program.cs`, so coverage grows incrementally.

## Usage

1. Click **Download llama.cpp** to download the binary, or click **Browse** next to **Executable** and select your `llama-server`
2. Click **Browse** next to **Model** and select your model file (.gguf), or set a models directory, or enter a HuggingFace repo
3. Configure additional parameters as needed
4. Click **Start Server** to launch llama-server
5. Monitor logs in the **Log Output** section
6. Use **Open in Browser** to open the llama-server WebUI

### Managing Profiles

To save current settings as a profile:
1. Enter a name in the profile input field or select an existing profile from the dropdown
2. Click **Save**

To load a saved profile:
1. Select the profile from the dropdown
2. Click **Load**

To export configurations:
- Use **Export** to save as JSON, batch file (.bat), shell script (.sh), or macOS script (.command)
- Use **Export All** to save all profiles as a ZIP archive
- Use **Import** to load a single profile from JSON
- Use **Import All** to load all profiles from a ZIP archive
- Drag and drop `.json`, `.bat`, `.cmd`, `.sh`, or `.command` files onto the window

### Working with Scenarios

Scenarios allow you to run multiple profiles in sequence with timed transitions:

1. Click **Scenarios** to open the scenario management interface
2. Click **New Scenario** to create a scenario
3. Add profiles to the scenario in the desired order
4. Set the interval (in seconds) between profile switches
5. Optionally enable **Auto-start** to launch the scenario on application startup
6. Save the scenario

### Running Benchmarks

Benchmark mode captures performance metrics for a profile so different settings can be compared later:

1. Open the **Start Server** split-button flyout and choose **Run & save benchmark**
2. Optionally edit the llama-server argument line and choose what to collect (`/metrics` scrape, built-in standard workload, repeats)
3. Run the benchmark; when the server stops, the run is saved automatically under the profile
4. Click **Benchmarks** to open the comparison window: select runs, compare them side by side as Markdown tables, narrow the table down with the **Rows** filter, save named comparison sets (runs + row selection), and export reports as `.md`

### Log Stream Server

The built-in log stream server enables remote log monitoring:

1. Enable and configure the log stream server in settings (port and optional token)
2. Open `http://localhost:<port>/` in a browser for the built-in web viewer
3. Connect via WebSocket at `ws://localhost:<port>/ws?token=<token>` for real-time logs

### On-Demand Model Proxy

Let an OpenAI-compatible client load models on demand instead of switching profiles by hand:

1. Enable and configure the proxy in settings (**On-Demand Proxy** tab): port, idle-unload timeout, optional API key
2. Point your client (e.g. Cherry Studio) at `http://<host>:<port>/v1` and use the **profile name** as the model name
3. On each request the proxy starts the matching profile (stopping any other running one), waits until it is ready, and proxies the response — streaming included
4. After the idle timeout with no requests, the server is stopped automatically

Optionally, on the **Behavior** tab, enable **Free ComfyUI VRAM before loading a profile** and set the ComfyUI URL so GPU memory is released before each model loads.

## Architecture

- **Framework**: Avalonia 12.0.1 (.NET 8.0)
- **Pattern**: MVVM (Model-View-ViewModel)
- **Build**: Self-contained single-file executable

### Project Structure
```
LlamaServerLauncher/
├── Models/                            # Data models and command-line building
│   ├── ServerConfiguration            # All llama-server parameters + KnownArguments mapping
│   ├── CommandLineBuilder             # Constructs full llama-server command line
│   ├── CommandLineParser              # Tokenizes and parses arguments (handles quotes, JSON, arrays)
│   ├── LlamaArgumentDefinition        # Structured argument metadata (flag, aliases, descriptions, defaults)
│   ├── LlamaArgumentRegistry          # Complete registry of known llama-server arguments with EN/RU docs
│   ├── ServerInstance                 # Per-instance server lifecycle management
│   ├── ScenarioInfo                   # Scenario definition (profile sequence, interval, auto-start)
│   ├── AppSettings                    # Persistent application settings (including dialog geometry)
│   ├── ProfileInfo                    # Profile metadata
│   ├── ExperimentalRepoInfo           # Experimental repository definition + cached releases
│   ├── BrowserInfo                    # Detected browser (name + executable path)
│   ├── HelpArgumentInfo               # Help argument metadata for feature detection
│   ├── ProxyProtocol                  # HTTP/JSON helpers for the on-demand proxy (request parsing, model matching)
│   ├── InferenceStatsParser           # Tokens-per-second parsing from server output
│   ├── GpuStatsParser / AmdGpuParser  # nvidia-smi / rocm-smi output parsing
│   ├── CpuUsage / HardwareSnapshot    # CPU usage math + combined hardware metrics snapshot
│   ├── ServerLogFilter                # Filters health/slots polling noise out of the log view
│   ├── ServerCrashAdvisor             # Detects --no-mmap pinned-memory crash signatures
│   ├── ModelScanEntry                 # Model picker entry + GGUF metadata formatting
│   ├── EndpointSnippets               # Endpoint URL / curl snippet builders
│   ├── BackendAssetSelector           # Picks the best llama.cpp build for the detected GPU vendor
│   ├── Benchmarking/                  # BenchmarkRun, BenchmarkMetrics, BenchmarkComparisonSet
│   └── AppInfo                        # Application version accessor (reflection)
├── ViewModels/                        # MVVM view models
│   ├── MainViewModel                  # Main application logic and state (multi-instance, scenarios)
│   ├── ScenarioDialogViewModel        # Scenario creation/editing logic
│   ├── ExperimentalRepoDialogViewModel # Add/edit experimental repository dialog logic
│   ├── ModelPickerViewModel           # Model folder scan/pick dialog logic
│   ├── BenchmarkLaunchViewModel       # Benchmark run configuration dialog logic
│   ├── BenchmarkComparisonViewModel   # Benchmark comparison window logic
│   ├── DownloadDialogViewModel
│   ├── ArgumentPickerViewModel
│   ├── IOnDemandProxyHost             # Bridge for the proxy to drive profile start/stop
│   └── RelayCommand / AsyncRelayCommand
├── Services/                          # Business logic services
│   ├── LlamaServerService             # Process management, HTTP slots/model queries
│   ├── ILlamaServerService            # Service interface
│   ├── ConfigurationService           # Profile and settings persistence (JSON)
│   ├── LlamaCppDownloadService        # Downloads llama.cpp releases from GitHub
│   ├── LlamaHelpParserService         # Parses --help output for feature detection
│   ├── LogService                     # Application and server log management
│   ├── LogStreamService               # WebSocket log streaming server with HTTP API
│   ├── OnDemandProxyService           # OpenAI-compatible on-demand proxy (auto-loads profiles, idle unload, ComfyUI free)
│   ├── ToastService                   # In-app toast notification system
│   ├── AutoStartService               # System auto-start (Windows/Linux/macOS)
│   ├── SingleInstanceService          # Enforces single instance with IPC activation
│   ├── DockerCliService               # Docker CLI integration
│   ├── AppUpdateService               # Application auto-update via GitHub releases
│   ├── ExperimentalRepoService        # Experimental build repositories (custom GitHub release sources)
│   ├── BrowserDetectionService        # Detects installed browsers for WebUI launch
│   ├── GgufMetadataService            # Reads GGUF metadata (context, layers, quant, vision projector)
│   ├── ModelScanService               # Recursive .gguf folder scan for the model picker
│   ├── HardwareMonitorService         # CPU/RAM/GPU/VRAM polling (nvidia-smi + rocm-smi providers)
│   ├── SystemMetrics                  # OS-level CPU/RAM metrics (Windows/Linux/macOS)
│   ├── GpuVendorDetector              # One-shot GPU vendor probe for backend auto-detect
│   ├── Benchmarking/                  # PrometheusMetricsParser, BenchmarkReportBuilder/Localizer,
│   │                                  #   BenchmarkStorageService, BenchmarkRunController, ShellHelper
│   ├── WindowsFileDialogs             # File/folder picker abstractions
│   ├── HelpService                    # Loads and shows the built-in per-section help
│   ├── DialogPositionHelper           # Dialog window position/size persistence
│   └── DataPathResolver               # Data directory resolution and migration
├── Converters/                        # UI value converters
├── Controls/                          # Custom UI controls
│   ├── HistoryTextBox                 # TextBox with history navigation
│   ├── TriStateSelector               # Tri-state (on/off/default) option selector
│   └── MarkdownRenderer               # Lightweight Markdown renderer (incl. GFM tables)
├── Resources/                         # Localization, themes, and assets
│   ├── Strings.resx                   # English localization
│   ├── Strings.ru.resx                # Russian localization
│   ├── LocalizedStrings.cs            # Strongly-typed localization accessor
│   ├── Docs/                          # Built-in help pages (Markdown, en + ru)
│   ├── Themes/
│   │   ├── Dark.xaml                  # Dark theme
│   │   ├── Light.xaml                 # Light theme
│   │   └── Schemes/                   # Color accent schemes
│   │       ├── Default.xaml
│   │       ├── Ocean.xaml
│   │       ├── Forest.xaml
│   │       ├── Sunset.xaml
│   │       └── Ubuntu.xaml
│   └── *.svg                          # Icon assets
├── MainWindow.axaml                   # Main window with drag-and-drop support
├── ScenarioDialogWindow.axaml         # Scenario creation and editing dialog
├── ExperimentalRepoDialogWindow.axaml # Add/edit experimental repository dialog
├── ModelPickerWindow.axaml            # GGUF model picker (folder scan + metadata list)
├── BenchmarkLaunchWindow.axaml        # Benchmark run configuration dialog
├── BenchmarkComparisonWindow.axaml    # Benchmark comparison window (Markdown tables)
├── MarkdownViewerWindow.axaml         # Markdown viewer (release notes, reports)
├── DownloadDialogWindow.axaml
├── ArgumentPickerWindow.axaml
├── AboutDialogWindow.axaml
└── App.axaml                          # App entry point, tray icon, culture handling, single-instance
```

## Acknowledgments

Thanks for contributions and moral support — [Methelina](https://github.com/Methelina). Thanks for providing [experimental llama.cpp-turboquant builds](https://github.com/pytraveler/llama-cpp-turboquant).

## License

MIT License - See LICENSE file for details.
