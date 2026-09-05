# Application settings

Settings for the launcher itself. They are shared across profiles and are not stored in them.

## Appearance

Theme (dark / light), color scheme, UI font size and family. Below that is a custom color block: you can override the window and panel backgrounds, the accent, separators, the command block color, the three toggle positions and the progress bar color, and restore the scheme defaults with the reset button.

**Interface scale** goes from 80 to 160 percent and scales the whole content of every window in the app - text, controls, paddings, icons and fixed sizes alike - where the font size setting only touches text. The button next to the slider returns it to 100 percent. "Resize windows with the scale" decides whether the windows follow their content: with it on, a window opened at a larger scale opens larger, up to what the scale asks for; with it off, only the content is scaled and every window keeps the size it was given. The tray menu is native and keeps the system size.

The download and update progress bars take their color from the app theme rather than the system accent, so they cannot end up an indistinguishable grey. The hardware meters (CPU, RAM, GPU, VRAM, temperature) keep their own colors.

## Updates

How often to check for new releases — separately for the app and for llama.cpp. Checking too often runs into GitHub API rate limits, which the hint under the fields warns about. A found update shows up as a button in the main window header.

"Keep the replaced llama.cpp build for rollback" decides what happens to the build an update replaces: by default it is moved next to the install directory, into `llama.cpp.prev`, instead of being deleted, and the download window offers to roll back to it — instantly and without a network. The rollback swaps the two, so the build it replaces takes the kept slot and the rollback can be undone the same way. Exactly one build is kept, taking as much disk space as the one in use; the download window can delete it. Turn the setting off and an update deletes the old build as it used to.

## Data storage

Where profiles, settings, logs, benchmarks and downloaded llama.cpp builds live. The default is the per-user system data folder; you can point it elsewhere (another drive, for instance). When you change the directory, the app offers to migrate the existing data there.

## Log streaming

A built-in log server so you can watch logs from another device.

- Enable it, set the **port**, and optionally a **token** for access.
- `http://localhost:<port>/` — the built-in viewer page with autoscroll and reconnect.
- `ws://localhost:<port>/ws?token=<token>` — the WebSocket log stream.
- `/api/logs/history` and `/api/status` — history and state as JSON.

Changing the port or token requires restarting the log server; there is a button next to the fields for that.

## On-demand proxy

An OpenAI-compatible reverse proxy: the client asks for the model it wants and the launcher brings up the matching profile.

1. Enable the proxy, set the port and, if needed, an API key.
2. Point your client (Cherry Studio, for example) at `http://<host>:<port>/v1`.
3. Put the **profile name** in the request's `model` field.

The proxy starts that profile, waits until it is ready and proxies the response, streaming included. Starting a profile stops any other running one, so only one model ever sits in VRAM. After the **idle timeout** the server stops by itself (`0` disables auto-unload). Profiles in routing mode (`--models-dir`) are not listed as models.

## Behavior

- **Start with system** — register the app with the OS autostart.
- **Confirm server stop** — ask before stopping a running instance.
- **Skip exit confirmation when a server is running** — automatically stop running instances and exit without asking for confirmation.
- **Hardware monitor** — show CPU / RAM / GPU / VRAM / temperature above the instance list. Polling pauses while a model loads so it does not interfere with CUDA/HIP init.
- **Auto-fit window height** — size the window to its content.
- **Browser** — which browser opens the WebUI: one of the detected ones or a path you provide.
- **ComfyUI** — call ComfyUI's `/free` endpoint before loading any profile so it releases VRAM. Lets you hand GPU memory back and forth between ComfyUI and llama.cpp without manual juggling.
- **Stop processes started by the server together with it** — the server and all of its descendants are held in a Windows job object, so stopping the server, closing the launcher or even crashing it leaves nothing running. It matters for MCP servers: they launch applications of their own (ComfyUI, for one) and a process-tree walk does not always reach them. Turn it off when such an application should keep running after the server stops. Windows only.
- **Minimize to tray** — the minimize button hides the window into the tray, where the icon's menu keeps the running instances at hand. Turn it off and a minimized window stays on the taskbar like any other; the tray icon remains either way.
- **Ask what to import** - an imported file, dropped or picked from the menu, first shows what it would change and lets you check off what to take. Turn it off and an import overwrites the whole form the way it used to.

## Experimental repos

Your own GitHub sources for llama.cpp builds alongside the official one. Add a repository, optionally a tag filter, and its releases appear in the download dialog next to the official ones. The check interval is set here too.
