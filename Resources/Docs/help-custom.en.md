# Custom arguments

A free-form argument string appended to the `llama-server` command after everything set through fields on the other tabs. Use it for flags that have no dedicated field in the UI.

## How to use it

Type arguments exactly as you would in a terminal:

```
--override-tensor "blk\.[0-9]+\.ffn_.*=CPU" --no-kv-offload
```

Press **Enter** or move focus away from the field — the string is parsed and a row of toggle buttons appears below it, one per argument.

## Toggles

Every recognized argument becomes a switch:

- **click** — temporarily disable the argument without deleting it (disabled ones are left out of the command);
- **right-click** — remove the argument from the string.

This makes it easy to keep several experimental flags in a profile and enable them one at a time instead of rewriting the line.

## The "+" button

Opens the list of arguments **your** binary actually understands — it is built from `llama-server --help` output. Each argument comes with a description and its accepted values, and the one you pick is appended to the string. The button only appears once the binary's help has been parsed successfully.

## Things to watch

- An argument duplicated here and on another tab lands in the command twice — llama.cpp usually takes the last value, but avoid it anyway.
- The clear icon button wipes the whole string.
- The full resulting command is always visible in the preview at the bottom of the window — check against it.
- `--override-tensor` (`-ot`) has no dedicated field and lives here; the **Optimize** window also writes it here when you apply a result.
