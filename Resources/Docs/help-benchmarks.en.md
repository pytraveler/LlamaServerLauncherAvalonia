# Benchmarks

A benchmark captures a profile's performance as a saved run, so you can compare settings honestly later instead of relying on a feeling that things "got faster".

## Where runs come from

The **Benchmarks** button only opens the comparison of **already saved** runs. You need at least one first:

1. In the main window, click the arrow next to **Start server**.
2. Choose **Run and save benchmark**.
3. Adjust the settings in the dialog if needed and press **Run**.
4. Wait for the run to finish. The result is saved into the current profile automatically and shows up in the list on the left of the **Benchmarks** window.

Until you have made a run, the comparison window is empty — that is expected.

## The launch dialog

- **Launch arguments** — the final `llama-server` line for this run. You can edit it here without touching the profile; the toggles below let you switch individual arguments off (right-click removes one).
- **Collect metrics (`--metrics`)** — gather data from the server's `/metrics` endpoint. Requires the binary to support the `--metrics` flag.
- **Standard run** — drive the running server with a built-in, identical HTTP load. This is what makes runs comparable: without it the numbers depend on whatever you happened to send the server by hand.
- **Prompt tokens** / **Generation tokens** — the size of that load: how many tokens to feed in and how many to generate.
- **Repeats** — how many times to repeat the load. More repeats means less influence from random spread.
- **Fix seed** — remove variation caused by sampling randomness.
- **Stop after run** — shut the server down once measurements are done.
- **Label** and **Notes** — how this run is titled in the list and the report. A meaningful label ("fa on, ctk q8") saves a lot of time later.

The bottom of the dialog previews the full command that will be executed.

## What gets saved

Every run is written to the data directory, grouped by profile: `benchmarks/<profile>/<runId>/`. It holds the configuration, the command line, the server log, the collected metrics and a ready-made report.

## The comparison window

- On the left is the run list; the **Profiles** filter narrows it down.
- Tick several runs and a side-by-side table comparison appears on the right.
- The **Rows** filter picks which rows (metrics, launch parameters, environment) end up in the table — handy when you only care about two or three values instead of all three dozen. The choice is remembered between sessions; **Select all** / **Clear** reset it wholesale.
- **Saved comparisons** — name the current selection and save it to come back to it later. The row selection is stored with it, so loading a comparison brings the table back exactly as you left it.
- **Export .md** writes the comparison report as Markdown; **Export ZIP** does the same plus all run files.
- The 📌 icon on a run attaches arbitrary files to it (screenshots, logs); 📁 opens its folder.
- **Delete run** removes the ticked runs from disk permanently.

## Keeping comparisons honest

Change **one** parameter at a time between runs and keep the model, load size and repeat count identical. A run made while games, ComfyUI or a second server instance are working in the background measures something other than what you think.
