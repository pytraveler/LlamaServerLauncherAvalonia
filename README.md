# LlamaServerLauncher

[Русский](README_ru.md)

![LlamaServerLauncher](docs/images/preview.png)

A cross-platform desktop application for launching and managing [llama.cpp](https://github.com/ggerganov/llama.cpp) server instances with an intuitive graphical interface.

Built with [Avalonia UI](https://avaloniaui.net/) and .NET 8.

## Features

### Server Configuration
- **Executable Path** — Select the `llama-server` binary, or download llama.cpp directly from the app
- **Model Selection** — Choose a specific model file (.gguf), set a models directory, or specify a HuggingFace repo (`--hf-repo`) and file (`--hf-file`)
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

### Multi-Instance Server Management
- Run multiple server instances simultaneously, each with its own profile/configuration
- Per-instance controls: start, stop, restart, unload model, open in browser
- Per-instance auto-restart on crash and log toggle
- Short-lived server error indicator (shows if instance exits within 5 seconds of starting)
- Instance view in system tray menu with full per-instance controls

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
- **Built-in Log Stream Server** — WebSocket-based log streaming with HTTP API endpoints:
  - `/ws` — Real-time WebSocket log streaming with optional token authentication
  - `/api/logs/history` — JSON endpoint for log history
  - `/api/status` — Stream server status
  - Built-in HTML log viewer page with auto-scroll, clear, and reconnect controls

### llama.cpp Integration
- **One-click download** — Download official llama.cpp releases directly from GitHub
- **Update notifications** — Automatically checks for new llama.cpp releases
- **Version management** — Install and switch between different versions
- **PATH integration** — Optionally add llama.cpp directory to PATH
- **Experimental build repositories** — Add custom GitHub release sources (e.g. [llama-cpp-turboquant](https://github.com/pytraveler/llama-cpp-turboquant)) with tag filters and periodic update checks to download experimental builds

### App Updates
- **Auto-update** — Automatically checks for new application releases and supports one-click update with restart

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

Current coverage includes the command-line layer (`CommandLineParser`, `CommandLineBuilder`, `ServerConfiguration`) and the optimization (HPO) engine. Each area is a separate `*Tests.cs` file wired into `Program.cs`, so coverage grows incrementally.

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

### Log Stream Server

The built-in log stream server enables remote log monitoring:

1. Enable and configure the log stream server in settings (port and optional token)
2. Open `http://localhost:<port>/` in a browser for the built-in web viewer
3. Connect via WebSocket at `ws://localhost:<port>/ws?token=<token>` for real-time logs

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
│   └── HelpArgumentInfo               # Help argument metadata for feature detection
├── ViewModels/                        # MVVM view models
│   ├── MainViewModel                  # Main application logic and state (multi-instance, scenarios)
│   ├── ScenarioDialogViewModel        # Scenario creation/editing logic
│   ├── ExperimentalRepoDialogViewModel # Add/edit experimental repository dialog logic
│   ├── DownloadDialogViewModel
│   ├── ArgumentPickerViewModel
│   └── RelayCommand / AsyncRelayCommand
├── Services/                          # Business logic services
│   ├── LlamaServerService             # Process management, HTTP slots/model queries
│   ├── ILlamaServerService            # Service interface
│   ├── ConfigurationService           # Profile and settings persistence (JSON)
│   ├── LlamaCppDownloadService        # Downloads llama.cpp releases from GitHub
│   ├── LlamaHelpParserService         # Parses --help output for feature detection
│   ├── LogService                     # Application and server log management
│   ├── LogStreamService               # WebSocket log streaming server with HTTP API
│   ├── ToastService                   # In-app toast notification system
│   ├── AutoStartService               # System auto-start (Windows/Linux/macOS)
│   ├── SingleInstanceService          # Enforces single instance with IPC activation
│   ├── DockerCliService               # Docker CLI integration
│   ├── AppUpdateService               # Application auto-update via GitHub releases
│   ├── ExperimentalRepoService        # Experimental build repositories (custom GitHub release sources)
│   ├── BrowserDetectionService        # Detects installed browsers for WebUI launch
│   ├── WindowsFileDialogs             # File/folder picker abstractions
│   ├── DialogPositionHelper           # Dialog window position/size persistence
│   └── DataPathResolver               # Data directory resolution and migration
├── Converters/                        # UI value converters
├── Controls/                          # Custom UI controls
│   └── HistoryTextBox                 # TextBox with history navigation
├── Resources/                         # Localization, themes, and assets
│   ├── Strings.resx                   # English localization
│   ├── Strings.ru.resx                # Russian localization
│   ├── LocalizedStrings.cs            # Strongly-typed localization accessor
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
├── DownloadDialogWindow.axaml
├── ArgumentPickerWindow.axaml
├── AboutDialogWindow.axaml
└── App.axaml                          # App entry point, tray icon, culture handling, single-instance
```

## Acknowledgments

Thanks for contributions and moral support — [Methelina](https://github.com/Methelina). Thanks for providing [experimental llama.cpp-turboquant builds](https://github.com/pytraveler/llama-cpp-turboquant).

## License

MIT License - See LICENSE file for details.
