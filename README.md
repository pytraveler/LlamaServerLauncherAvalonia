# LlamaServerLauncher

[Русский](README_ru.md)

![LlamaServerLauncher](docs/images/preview.png)

A cross-platform desktop application for launching and managing [llama.cpp](https://github.com/ggerganov/llama.cpp) server instances with an intuitive graphical interface.

Built with [Avalonia UI](https://avaloniaui.net/) and .NET 8.

## Features

### Server Configuration
- **Executable Path** — Select the `llama-server` binary, or download llama.cpp directly from the app
- **Model Selection** — Choose a specific model file (.gguf) or set a models directory
- **Network Settings** — Configure host address (default: 127.0.0.1) and port (default: 8080)

### Model Parameters
- Context size (`-c`, `--ctx-size`)
- Number of threads (`-t`, `--threads`)
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

### Logging & Monitoring
- Log file output (`--log-file`)
- Verbose logging (`-v`, `--verbose`)
- Real-time log viewer with auto-scroll
- Server status display with process ID
- Auto-restart on crash

### llama.cpp Integration
- **One-click download** — Download official llama.cpp releases directly from GitHub
- **Update notifications** — Automatically checks for new llama.cpp releases
- **Version management** — Install and switch between different versions
- **PATH integration** — Optionally add llama.cpp directory to PATH

### App Updates
- **Auto-update** — Automatically checks for new application releases and supports one-click update with restart

### Docker Support
- Docker CLI integration for container-based workflows

### Profile Management
- Save, load, rename, and delete configuration profiles
- Export profiles to JSON, Windows batch (.bat), Linux shell (.sh), or macOS script (.command)
- Import profiles from JSON
- Export/import all profiles as a ZIP archive
- Unsaved changes tracking

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
- Tray icon menu with server controls (start, stop, unload model, open in browser)
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

# macOS
dotnet publish LlamaServerLauncher.csproj -c Release -r osx-x64 -o ./publish/osx-x64
```

## Usage

1. Click **Download llama.cpp** to download the binary, or click **Browse** next to **Executable** and select your `llama-server`
2. Click **Browse** next to **Model** and select your model file (.gguf), or set a models directory
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

## Architecture

- **Framework**: Avalonia 12.0.1 (.NET 8.0)
- **Pattern**: MVVM (Model-View-ViewModel)
- **Build**: Self-contained single-file executable

### Project Structure
```
LlamaServerLauncher/
├── Models/                   # Data models and command-line building
│   ├── ServerConfiguration   # All llama-server parameters + KnownArguments mapping
│   ├── CommandLineBuilder    # Constructs full llama-server command line
│   ├── CommandLineParser     # Tokenizes and parses arguments (handles quotes, JSON, arrays)
│   ├── AppSettings           # Persistent application settings
│   ├── ProfileInfo           # Profile metadata
│   └── HelpArgumentInfo      # Help argument metadata for feature detection
├── ViewModels/               # MVVM view models
│   ├── MainViewModel         # Main application logic and state
│   ├── DownloadDialogViewModel
│   ├── ArgumentPickerViewModel
│   ├── RelayCommand          # Custom ICommand implementation
│   └── AsyncRelayCommand
├── Services/                 # Business logic services
│   ├── LlamaServerService    # Process management, HTTP slots/model queries
│   ├── ILlamaServerService   # Service interface
│   ├── ConfigurationService  # Profile and settings persistence (JSON)
│   ├── LlamaCppDownloadService # Downloads llama.cpp releases from GitHub
│   ├── LlamaHelpParserService  # Parses --help output for feature detection
│   ├── LogService            # Application and server log management
│   ├── WindowsFileDialogs    # File/folder picker abstractions
│   ├── AppUpdateService      # Application auto-update via GitHub releases
│   ├── DockerCliService      # Docker CLI integration
│   └── DataPathResolver      # Data directory resolution and migration
├── Converters/               # UI value converters
├── Controls/                 # Custom UI controls
│   └── HistoryTextBox        # TextBox with history navigation
├── Resources/                # Localization, themes, and assets
│   ├── Strings.resx          # English localization
│   ├── Strings.ru.resx       # Russian localization
│   ├── LocalizedStrings.cs   # Strongly-typed localization accessor
│   ├── Themes/
│   │   ├── Dark.xaml         # Dark theme
│   │   ├── Light.xaml        # Light theme
│   │   └── Schemes/          # Color accent schemes
│   │       ├── Default.xaml
│   │       ├── Ocean.xaml
│   │       ├── Forest.xaml
│   │       ├── Sunset.xaml
│   │       └── Ubuntu.xaml
│   └── *.svg                 # Icon assets
├── MainWindow.axaml          # Main window with drag-and-drop support
├── DownloadDialogWindow.axaml
├── ArgumentPickerWindow.axaml
├── AboutDialogWindow.axaml
└── App.axaml                 # App entry point, tray icon, culture handling
```

## Acknowledgments

Thanks for contributions and moral support — [Methelina](https://github.com/Methelina). Thanks for providing [experimental llama.cpp-turboquant builds](https://github.com/pytraveler/llama-cpp-turboquant).

## License

MIT License - See LICENSE file for details.
