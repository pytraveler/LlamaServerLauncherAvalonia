# Scenarios

A scenario is a list of profiles started one after another at a fixed interval. Handy when you need to run several models in sequence: to compare them, to warm them up, or to hand a series of tasks to different models.

While **Enable scenarios** is unchecked the whole scenario strip is hidden and has no effect on starting the server.

## Building a scenario

1. Check **Enable scenarios**.
2. In the **Edit** button menu choose **New scenario**.
3. Give it a name.
4. Move the profiles you want from the left list to the right one with `»` — the right list *is* the execution order.
5. Reorder with the ▲ ▼ buttons or by dragging.
6. Set the **interval** — how many seconds to wait before switching to the next profile.
7. Save.

## Settings

- **Interval** — the pause between profile switches, in seconds.
- **Auto-start** — run this scenario as soon as the app starts.
- **Clone into scenario** — copy the selected profile and add the copy to the scenario, so you can tweak the copy without touching the original profile.

## Running

The **Run scenario** button on the scenario strip starts the selected scenario from its first profile. The scenario switches profiles by itself; to interrupt it, stop the server.

> Profiles in a scenario run sequentially, not simultaneously. If you need several models in memory at once, start the instances manually, each on its own port.
