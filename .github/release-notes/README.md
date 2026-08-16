# Release notes extras

Everything the release notes need beyond the changelogs. The release workflow
builds the notes for tag `vX.Y` in this order:

1. the `## vX.Y - YYYY-MM-DD` section of `CHANGELOG.ru.md`, under a
   `## Обновление от DD.MM.YYYY` heading;
2. `---`, then the same section of `CHANGELOG.md` inside a collapsed
   `<details>` block;
3. `vX.Y.md` from this folder, if it exists — the per-release extras, normally
   the `## Интерфейс` block with screenshots;
4. `footer.md`, if it exists — the part that repeats in every release;
5. `**Full Changelog**: <compare link against the previous v* tag>`.

Both changelogs must have a section for the tag, and the tag must agree with
`<Version>` / `<InformationalVersion>` in `LlamaServerLauncher.csproj` —
otherwise the workflow stops before building anything.

The release is created as a draft, so the notes can still be edited on GitHub
before publishing.
