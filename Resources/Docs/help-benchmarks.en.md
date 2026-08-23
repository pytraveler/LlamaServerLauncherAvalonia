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
- **Prompt run** — ask the model your own questions and keep its answers next to the numbers. See below.
- **Fix seed** — remove variation caused by sampling randomness.
- **Stop after run** — shut the server down once measurements are done. A prompt run turns it on by itself: the point of that mode is to ask, record the answer and free the VRAM.
- **Label** and **Notes** — how this run is titled in the list and the report. A meaningful label ("fa on, ctk q8") saves a lot of time later.

The bottom of the dialog previews the full command that will be executed.

## Prompt run

For when tokens per second are not the whole story and **what** the model actually answered with these settings matters. The launcher starts the server, waits until it is ready, sends your requests as ordinary chat requests (`/v1/chat/completions`), stores the answers with the run and stops the server.

- **System prompt** — sent as the system message before the first request. May be left empty.
- **Requests** — what to ask. A line of three or more dashes (`---`) starts the next request; without one the whole box is a single request. The count of recognised requests is shown on the right.
- **Keep the conversation** — on: every request continues the same conversation and the model sees its own earlier answers. Off: every request starts fresh, with the system prompt alone.
- **Max tokens** — upper limit on the answer length; `0` leaves it to the server.
- **Timeout, s** — how long to wait for one answer before the request counts as failed. A failure does not abort the run: it is written into the report and the launcher moves on to the next request.

Temperature, seed and the rest of the sampling setup come from the launch arguments and are not repeated in the request, so the run measures exactly the profile you assembled.

The model needs a chat template: builds without one answer with an error, and that error lands in the report in place of the answer.

## What gets saved

Every run is written to the data directory, grouped by profile: `benchmarks/<profile>/<runId>/`. It holds the configuration, the command line, the server log, the collected metrics and a ready-made report.

A prompt run adds `prompt-run.md` next to them: the full transcript, with the system prompt, every request, the model's answer (and its reasoning, when the build reports it as a separate field) and per-request timings. A short per-request table also goes into `report.md`, and the run's average speed fills the **Gen tok/s**, **Prompt tok/s** and **TTFT** comparison rows when no standard run was made.

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
