# Main — paths, model, network

This section holds the minimum needed to launch: what to run it with, what to run, and on which address.

## Quick start

1. **Executable** — point at your `llama-server`, or use the llama.cpp download button in the window header: the app fetches an official GitHub release and preselects the build that matches your GPU (CUDA / Vulkan / HIP / SYCL / CPU).
2. **Model (-m)** — pick a `.gguf` file with **Browse…**, or use **Pick…** for a list with metadata.
3. Press **Start server** at the bottom of the window and watch the log.
4. Once it reports ready, open the WebUI with **Open in browser**.

## Paths

- **llama-server** — the server binary. Which llama.cpp version is currently installed is shown by the tooltip of the **Change llama version** button in the window header (when the build was downloaded through this app and its version is therefore known).
- **Model** — a specific `.gguf` file. A badge below the paths block shows what was read from the GGUF: quantization, parameter count, layers and experts, chat template and vision projector presence. The model's trained context length comes from there too and caps the **Context size** field.
- **Models directory** — routing mode (`--models-dir`): the server picks the model by the name in the request. Fill in either **Model** or **Models directory** — the status line below the fields shows which mode is active.
- **Pick…** — scans a folder (recursively if you want) and shows a filterable list of `.gguf` files with size, metadata and an `mmproj` marker. The last folder is remembered.

> Selecting an `mmproj-*.gguf` projector as the main model raises a warning — that file belongs in the separate **MMProj** field on the **Options** tab.

Path fields keep a history: the **∨** button to the right of a field lists previously entered values, and the bin button next to it clears the field.

## HuggingFace

An alternative to a local file — let llama.cpp fetch the model itself:

- **Repository** (`--hf-repo`) — e.g. `user/model-GGUF`.
- **File** (`--hf-file`) — a specific file inside the repository.
- **Offline** (`--offline`) — use the cache only, never touch the network.

## Network settings

- **Host** — `127.0.0.1` by default (this machine only). Use `0.0.0.0` to expose the server on your LAN.
- **Port** — `8080` by default. Every simultaneously running instance needs its own port.

Invalid values are flagged with a message right below the field.

## Profiles

The top row of the window is the profile bar: a full set of settings under a name. Type a name, press **Save**; picking another entry in the dropdown loads it. The **Save** button menu holds rename, clone, delete, export to JSON / `.bat` / `.sh` / `.command`, and import.

You can also drop `.json`, `.bat`, `.cmd`, `.sh`, `.command`, `.exe` and `.gguf` files straight onto the window.

An import - dropped or picked from the **Import** menu - opens a panel over the window listing what the file would change: field by field, the value on the form against the one in the file, each with a checkbox. Only differences are listed, so a file that matches the form says so instead of opening an empty panel. A profile arrives with everything checked, since a `.json` holds a complete set of values. A `.bat` or `.sh` only speaks about the flags it names, so the fields it does not mention are listed unchecked and keep what the form has - check one to clear the field anyway. Enter applies the checked lines, Escape leaves the form untouched. The panel can be switched off on the **Behavior** tab, and then an import overwrites the whole form as it used to.

The star next to the dropdown pins the selected profile as a favorite. Favorites are kept at the top of the list, above a separator line, and carry the star next to their name, so the profiles you work with every day are the first ones the dropdown offers instead of something to scroll for. The star toggles the pin off again, and the choice is remembered between sessions.

A profile name shown in warning colour in the dropdown points at files that are no longer on disk - models moved to another drive, most often. Hover it to see exactly which paths are gone. The same check runs at every launch, whichever way the server is started, and names the missing path in the log.
