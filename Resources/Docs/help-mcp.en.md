# MCP

Connect external MCP tools to the server you launch. The server list lives in the profile; before a launch the launcher writes it out as `mcp.json` and hands it to llama-server through `--mcp-servers-config`.

Only the stdio transport is supported: llama-server spawns the command itself as a child process and talks JSON-RPC to it over standard input and output. Nothing has to be started beforehand.

The flag is a recent addition to llama.cpp. If the selected build does not know it, the tab says so and the flag is left out of the command.

## How it works

1. On startup llama-server spawns each server once, asks for its tool list and stops it again.
2. Tools are published as `name_tool` (the server name from the first field plus the tool name) and show up in `GET /tools`, in the Web UI and to the model.
3. On the first tool call the server is spawned again and stays alive for the rest of the llama-server session.

A name that collides with an already registered tool is skipped.

## The list on the tab

Every server takes one row: an "Off / On" switch, the name and the command. Clicking the row opens a separate editor window, so the tab stays short no matter how many servers there are, and a long list scrolls inside its own frame.

## Server settings

Edited in that window (click a row, or "Add server"). Changes are applied by "Save"; "Cancel" leaves the profile untouched.

- **Name** — the key in `mcp.json` and the prefix of the tool names.
- **Command** — the executable. Entries without one are skipped by llama-server. The command is looked up on PATH the same way llama-server does it: the exact name first, then the PATHEXT extensions. That matters on Windows: a Node installation ships an extension-less `npx` (a POSIX script) next to `npx.cmd`, the exact name wins and the spawn fails with "not a valid application". Write `npx.cmd` explicitly in that case.
- **Arguments** — one line, quotes are respected; they are written to the file as an array.
- **Working dir** — optional, the directory of the child process.
- **Timeout** — the limit for a single tool call, 30000 ms by default.
- **Environment** — one `KEY=VALUE` per line, merged over the launcher environment.

The **Test** button in the editor starts the command exactly the way llama-server will, performs the MCP handshake and lists the tools right there. It is the cheap way to find a mistake early: a config llama-server cannot use stops it from starting at all.

The **Query** button on the tab reads `GET /tools` from the running server of this profile. The result arrives as a toast with the tool count, and clicking it opens a window with the list: an MCP section for tools coming from MCP servers, and a built-in section for llama-server's own.

## Things to watch

- Child processes run with the same rights as the launcher. Only add commands you trust.
- An MCP server may start applications of its own, and those outlive the session unless the server stops them itself. So that nothing is left running after llama-server stops, **Settings -> Behavior** has "Stop processes started by the server together with it" (on by default, Windows): the server and everything below it is held in a job object and goes down as a whole.
- With MCP enabled llama-server limits CORS to localhost unless `--cors-origins` is set explicitly. To reach the server from another machine, add that argument on the **Custom arguments** tab.
- Tool calls need jinja templates. The server enables them by default, but `--no-jinja` in custom arguments turns them off.
- Warmup costs up to 10 seconds per server, which is added to the startup time.
- In Docker mode the file is mounted at `/mcp` inside the container, but the commands themselves must exist in the image.
- Import understands the Cursor and Claude Desktop format: an `mcpServers` object with commands, arguments and environment variables. The dropdown next to the button lists the profiles that already have MCP servers, with the server count in brackets - picking one copies its servers into the current profile and leaves the source untouched.
- If the same flag is set by hand on the **Custom arguments** tab, that value wins and the generated file is not passed.
